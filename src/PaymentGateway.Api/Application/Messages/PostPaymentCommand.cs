namespace PaymentGateway.Api.Application.Messages;

public record PostPaymentCommand(
    string IdempotencyKey,
    string CardNumber,
    int ExpiryMonth,
    int ExpiryYear,
    string Currency,
    long Amount,
    string Cvv);
