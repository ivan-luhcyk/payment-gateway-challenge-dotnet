using MassTransit;
using Microsoft.Extensions.Logging;
using PaymentGateway.Api.Application.Messages;
using PaymentGateway.Api.Domain.Interfaces;
using PaymentGateway.Api.Domain.Payments;
using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Application.Handlers;

public class PostPaymentCommandHandler : IConsumer<PostPaymentCommand>
{
    private readonly IPublishEndpoint   _publishEndpoint;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IIdempotencyStore  _idempotencyStore;
    private readonly ILogger<PostPaymentCommandHandler> _logger;

    public PostPaymentCommandHandler(
        IPublishEndpoint publishEndpoint,
        IPaymentRepository paymentRepository,
        IIdempotencyStore idempotencyStore,
        ILogger<PostPaymentCommandHandler> logger)
    {
        _publishEndpoint   = publishEndpoint;
        _paymentRepository = paymentRepository;
        _idempotencyStore  = idempotencyStore;
        _logger            = logger;
    }

    public async Task Consume(ConsumeContext<PostPaymentCommand> context)
    {
        var cmd               = context.Message;
        var cancellationToken = context.CancellationToken;

        _logger.LogInformation("PostPaymentCommand received. IdempotencyKey={IdempotencyKey}", cmd.IdempotencyKey);

        var existingId = await _idempotencyStore.GetPaymentIdAsync(cmd.IdempotencyKey, cancellationToken);
        if (existingId.HasValue)
        {
            var existing = await _paymentRepository.GetByIdAsync(existingId.Value, cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Idempotency hit. IdempotencyKey={IdempotencyKey} PaymentId={PaymentId}", cmd.IdempotencyKey, existing.Id);
                await context.RespondAsync(PaymentResponse.From(existing));
                return;
            }
        }

        var card    = new CardInfo(LastFour(cmd.CardNumber), cmd.ExpiryMonth, cmd.ExpiryYear);
        var money   = new Money(cmd.Amount, cmd.Currency);
        var payment = Payment.Create(card, money);

        await _paymentRepository.SaveAsync(payment, cancellationToken);
        _logger.LogInformation("Payment created. PaymentId={PaymentId}", payment.Id);

        var registered = await _idempotencyStore.TryRegisterAsync(cmd.IdempotencyKey, payment.Id, cancellationToken);
        if (!registered)
        {
            existingId = await _idempotencyStore.GetPaymentIdAsync(cmd.IdempotencyKey, cancellationToken);
            if (existingId.HasValue)
            {
                var existing = await _paymentRepository.GetByIdAsync(existingId.Value, cancellationToken);
                if (existing is not null)
                {
                    _logger.LogInformation("Idempotency hit (race). IdempotencyKey={IdempotencyKey} PaymentId={PaymentId}", cmd.IdempotencyKey, existing.Id);
                    await context.RespondAsync(PaymentResponse.From(existing));
                    return;
                }
            }
        }

        await _publishEndpoint.Publish(
            new ProcessBankPaymentCommand(payment.Id, new CardDetails(cmd.CardNumber, cmd.ExpiryMonth, cmd.ExpiryYear, cmd.Cvv), money),
            cancellationToken);

        _logger.LogInformation("ProcessBankPaymentCommand published. PaymentId={PaymentId}", payment.Id);
        await context.RespondAsync(PaymentResponse.From(payment));
    }

    private static int LastFour(string cardNumber)
        => cardNumber.Length >= 4 && int.TryParse(cardNumber[^4..], out var last) ? last : 0;
}
