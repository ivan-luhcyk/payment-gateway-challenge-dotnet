using MassTransit;
using Microsoft.Extensions.Logging;
using PaymentGateway.Api.Application.Handlers;
using PaymentGateway.Api.Application.Messages;
using PaymentGateway.Api.Domain.Interfaces;
using PaymentGateway.Api.Domain.Payments;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Tests.Unit.Application;

public class GetPaymentQueryHandlerTests
{
    private readonly IPaymentRepository _repository = Substitute.For<IPaymentRepository>();
    private readonly GetPaymentQueryHandler _handler;

    public GetPaymentQueryHandlerTests()
    {
        _handler = new GetPaymentQueryHandler(_repository, Substitute.For<ILogger<GetPaymentQueryHandler>>());
    }

    private static ConsumeContext<GetPaymentQuery> BuildContext(Guid id)
    {
        var ctx = Substitute.For<ConsumeContext<GetPaymentQuery>>();
        ctx.Message.Returns(new GetPaymentQuery(id));
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task Consume_PaymentExists_RespondsWithPaymentResponse()
    {
        var payment = Payment.Create(new CardInfo(8877, 4, 2030), new Money(1050, "GBP"));
        _repository.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);

        var ctx = BuildContext(payment.Id);
        await _handler.Consume(ctx);

        await ctx.Received(1).RespondAsync(Arg.Any<PaymentResponse>());
        await ctx.DidNotReceive().RespondAsync(Arg.Any<PaymentNotFound>());
    }

    [Fact]
    public async Task Consume_PaymentExists_ResponseContainsCorrectPaymentId()
    {
        var payment = Payment.Create(new CardInfo(8877, 4, 2030), new Money(1050, "GBP"));
        _repository.GetByIdAsync(payment.Id, Arg.Any<CancellationToken>()).Returns(payment);

        PaymentResponse? captured = null;
        var ctx = BuildContext(payment.Id);
        ctx.When(x => x.RespondAsync(Arg.Any<PaymentResponse>()))
            .Do(ci => captured = ci.Arg<PaymentResponse>());

        await _handler.Consume(ctx);

        Assert.NotNull(captured);
        Assert.Equal(payment.Id, captured!.Id);
        Assert.Equal(8877, captured.CardNumberLastFour);
        Assert.Equal(1050, captured.Amount);
        Assert.Equal("GBP", captured.Currency);
    }

    [Fact]
    public async Task Consume_PaymentNotFound_RespondsWithPaymentNotFound()
    {
        var unknownId = Guid.NewGuid();
        _repository.GetByIdAsync(unknownId, Arg.Any<CancellationToken>()).Returns((Payment?)null);

        var ctx = BuildContext(unknownId);
        await _handler.Consume(ctx);

        await ctx.Received(1).RespondAsync(Arg.Any<PaymentNotFound>());
        await ctx.DidNotReceive().RespondAsync(Arg.Any<PaymentResponse>());
    }

    [Fact]
    public async Task Consume_PaymentNotFound_ResponseContainsCorrectId()
    {
        var unknownId = Guid.NewGuid();
        _repository.GetByIdAsync(unknownId, Arg.Any<CancellationToken>()).Returns((Payment?)null);

        PaymentNotFound? captured = null;
        var ctx = BuildContext(unknownId);
        ctx.When(x => x.RespondAsync(Arg.Any<PaymentNotFound>()))
            .Do(ci => captured = ci.Arg<PaymentNotFound>());

        await _handler.Consume(ctx);

        Assert.NotNull(captured);
        Assert.Equal(unknownId, captured!.PaymentId);
    }
}
