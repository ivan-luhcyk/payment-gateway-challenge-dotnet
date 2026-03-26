using MassTransit;
using Microsoft.Extensions.Logging;
using PaymentGateway.Api.Application.Handlers;
using PaymentGateway.Api.Application.Messages;
using PaymentGateway.Api.Domain.Enums;
using PaymentGateway.Api.Domain.Interfaces;
using PaymentGateway.Api.Domain.Payments;

namespace PaymentGateway.Api.Tests.Unit.Application;

public class PostPaymentCommandHandlerTests
{
    private readonly IPublishEndpoint _publishEndpoint = Substitute.For<IPublishEndpoint>();
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();
    private readonly IIdempotencyStore _idempotencyStore = Substitute.For<IIdempotencyStore>();
    private readonly PostPaymentCommandHandler _handler;

    private static readonly PostPaymentCommand ValidCommand = new(
        IdempotencyKey: "key-123",
        CardNumber: "2222405343248877",
        ExpiryMonth: 4,
        ExpiryYear: 2030,
        Currency: "GBP",
        Amount: 1050,
        Cvv: "123");

    public PostPaymentCommandHandlerTests()
    {
        _handler = new PostPaymentCommandHandler(_publishEndpoint, _repository, _idempotencyStore, Substitute.For<ILogger<PostPaymentCommandHandler>>());
    }

    private ConsumeContext<PostPaymentCommand> BuildContext(PostPaymentCommand? command = null)
    {
        var ctx = Substitute.For<ConsumeContext<PostPaymentCommand>>();
        ctx.Message.Returns(command ?? ValidCommand);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private void SetupNewPayment()
    {
        _idempotencyStore.GetPaymentIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
        _idempotencyStore.TryRegisterAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    [Fact]
    public async Task Consume_NewPayment_SavesPaymentWithProcessingStatus()
    {
        SetupNewPayment();

        await _handler.Consume(BuildContext());

        await _repository.Received(1).SaveAsync(
            Arg.Is<Payment>(p => p.Status == PaymentStatus.Processing),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_NewPayment_SavesPaymentWithCorrectCardAndMoney()
    {
        SetupNewPayment();

        await _handler.Consume(BuildContext());

        await _repository.Received(1).SaveAsync(
            Arg.Is<Payment>(p =>
                p.Card.LastFour == 8877 &&
                p.Card.ExpiryMonth == 4 &&
                p.Card.ExpiryYear == 2030 &&
                p.Money.Amount == 1050 &&
                p.Money.Currency == "GBP"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_NewPayment_PublishesBankCommandWithFullCardDetails()
    {
        SetupNewPayment();

        await _handler.Consume(BuildContext());

        await _publishEndpoint.Received(1).Publish(
            Arg.Is<ProcessBankPaymentCommand>(cmd =>
                cmd.CardDetails.CardNumber == "2222405343248877" &&
                cmd.CardDetails.ExpiryMonth == 4 &&
                cmd.CardDetails.ExpiryYear == 2030 &&
                cmd.CardDetails.Cvv == "123" &&
                cmd.Money.Currency == "GBP" &&
                cmd.Money.Amount == 1050),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_NewPayment_RegistersIdempotencyKey()
    {
        SetupNewPayment();

        await _handler.Consume(BuildContext(ValidCommand with { IdempotencyKey = "my-key" }));

        await _idempotencyStore.Received(1).TryRegisterAsync(
            "my-key",
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_DuplicateIdempotencyKey_ReturnsExistingPaymentWithoutSavingOrPublishing()
    {
        var existing = Payment.Create(new CardInfo(8877, 4, 2030), new Money(1050, "GBP"));
        existing.Authorize();

        _idempotencyStore.GetPaymentIdAsync("dup-key", Arg.Any<CancellationToken>())
            .Returns(existing.Id);
        _repository.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>())
            .Returns(existing);

        await _handler.Consume(BuildContext(ValidCommand with { IdempotencyKey = "dup-key" }));

        await _repository.DidNotReceive().SaveAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
        await _publishEndpoint.DidNotReceive().Publish(Arg.Any<ProcessBankPaymentCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_RaceConditionOnIdempotencyKey_ReturnsExistingPaymentWithoutPublishing()
    {
        _idempotencyStore.GetPaymentIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null);
        _idempotencyStore.TryRegisterAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var existing = Payment.Create(new CardInfo(8877, 4, 2030), new Money(1050, "GBP"));
        _idempotencyStore.GetPaymentIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Guid?)null, existing.Id);
        _repository.GetByIdAsync(existing.Id, Arg.Any<CancellationToken>())
            .Returns(existing);

        await _handler.Consume(BuildContext());

        await _publishEndpoint.DidNotReceive().Publish(Arg.Any<ProcessBankPaymentCommand>(), Arg.Any<CancellationToken>());
    }
}
