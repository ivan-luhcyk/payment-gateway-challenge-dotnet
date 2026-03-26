using MassTransit;
using Microsoft.Extensions.Logging;
using PaymentGateway.Api.Application.Handlers;
using PaymentGateway.Api.Application.Messages;
using PaymentGateway.Api.Domain.Bank;
using PaymentGateway.Api.Domain.Enums;
using PaymentGateway.Api.Domain.Interfaces;
using PaymentGateway.Api.Domain.Payments;

namespace PaymentGateway.Api.Tests.Unit.Application;

public class ProcessBankPaymentCommandHandlerTests
{
    private readonly IBankClient _bankClient = Substitute.For<IBankClient>();
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();
    private readonly ProcessBankPaymentCommandHandler _handler;

    private static readonly CardDetails TestCardDetails = new("2222405343248877", 4, 2030, "123");
    private static readonly Money TestMoney = new(1050, "GBP");

    public ProcessBankPaymentCommandHandlerTests()
    {
        _handler = new ProcessBankPaymentCommandHandler(_bankClient, _repository, Substitute.For<ILogger<ProcessBankPaymentCommandHandler>>());
    }

    private (Payment payment, ConsumeContext<ProcessBankPaymentCommand> context) BuildScenario()
    {
        var payment = Payment.Create(new CardInfo(8877, 4, 2030), TestMoney);
        _repository.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);

        var ctx = Substitute.For<ConsumeContext<ProcessBankPaymentCommand>>();
        ctx.Message.Returns(new ProcessBankPaymentCommand(payment.Id, TestCardDetails, TestMoney));
        ctx.CancellationToken.Returns(CancellationToken.None);

        return (payment, ctx);
    }

    [Fact]
    public async Task Consume_BankAuthorizes_SetsAuthorizedStatus()
    {
        var (payment, ctx) = BuildScenario();
        _bankClient.AuthorizeAsync(Arg.Any<CardDetails>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(new BankAuthorizationResult(true));

        await _handler.Consume(ctx);

        Assert.Equal(PaymentStatus.Authorized, payment.Status);
    }

    [Fact]
    public async Task Consume_BankDeclines_SetsDeclinedStatus()
    {
        var (payment, ctx) = BuildScenario();
        _bankClient.AuthorizeAsync(Arg.Any<CardDetails>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(new BankAuthorizationResult(false));

        await _handler.Consume(ctx);

        Assert.Equal(PaymentStatus.Declined, payment.Status);
    }

    [Fact]
    public async Task Consume_BankReturnsNull_SetsRejectedStatus()
    {
        var (payment, ctx) = BuildScenario();
        _bankClient.AuthorizeAsync(Arg.Any<CardDetails>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns((BankAuthorizationResult?)null);

        await _handler.Consume(ctx);

        Assert.Equal(PaymentStatus.Rejected, payment.Status);
    }

    [Fact]
    public async Task Consume_BankThrows_SetsRejectedStatus()
    {
        var (payment, ctx) = BuildScenario();
        _bankClient.AuthorizeAsync(Arg.Any<CardDetails>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BankAuthorizationResult?>(new HttpRequestException("connection refused")));

        await _handler.Consume(ctx);

        Assert.Equal(PaymentStatus.Rejected, payment.Status);
    }

    [Fact]
    public async Task Consume_AfterProcessing_SavesUpdatedPayment()
    {
        var (payment, ctx) = BuildScenario();
        _bankClient.AuthorizeAsync(Arg.Any<CardDetails>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(new BankAuthorizationResult(true));

        await _handler.Consume(ctx);

        await _repository.Received(1).SaveAsync(payment, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_PaymentNotFound_DoesNotCallBank()
    {
        var ctx = Substitute.For<ConsumeContext<ProcessBankPaymentCommand>>();
        ctx.Message.Returns(new ProcessBankPaymentCommand(Guid.NewGuid(), TestCardDetails, TestMoney));
        ctx.CancellationToken.Returns(CancellationToken.None);
        _repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Payment?)null);

        await _handler.Consume(ctx);

        await _bankClient.DidNotReceive().AuthorizeAsync(
            Arg.Any<CardDetails>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_PassesCorrectCardDetailsAndMoneyToBank()
    {
        var (_, ctx) = BuildScenario();
        _bankClient.AuthorizeAsync(Arg.Any<CardDetails>(), Arg.Any<Money>(), Arg.Any<CancellationToken>())
            .Returns(new BankAuthorizationResult(true));

        await _handler.Consume(ctx);

        await _bankClient.Received(1).AuthorizeAsync(
            Arg.Is<CardDetails>(c =>
                c.CardNumber == "2222405343248877" &&
                c.ExpiryMonth == 4 &&
                c.ExpiryYear == 2030 &&
                c.Cvv == "123"),
            Arg.Is<Money>(m => m.Amount == 1050 && m.Currency == "GBP"),
            Arg.Any<CancellationToken>());
    }
}
