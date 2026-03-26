using MassTransit;
using Microsoft.Extensions.Logging;
using PaymentGateway.Api.Application.Messages;
using PaymentGateway.Api.Domain.Interfaces;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Application.Handlers;

public class GetPaymentQueryHandler : IConsumer<GetPaymentQuery>
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ILogger<GetPaymentQueryHandler> _logger;

    public GetPaymentQueryHandler(IPaymentRepository paymentRepository, ILogger<GetPaymentQueryHandler> logger)
    {
        _paymentRepository = paymentRepository;
        _logger            = logger;
    }

    public async Task Consume(ConsumeContext<GetPaymentQuery> context)
    {
        var paymentId = context.Message.PaymentId;
        var payment   = await _paymentRepository.GetByIdAsync(paymentId, context.CancellationToken);

        if (payment is null)
        {
            _logger.LogWarning("Payment not found. PaymentId={PaymentId}", paymentId);
            await context.RespondAsync(new PaymentNotFound(paymentId));
            return;
        }

        _logger.LogInformation("Payment retrieved. PaymentId={PaymentId} Status={Status}", paymentId, payment.Status);
        await context.RespondAsync(PaymentResponse.From(payment));
    }
}
