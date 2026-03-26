namespace PaymentGateway.Api.Domain.Payments;

/// <summary>Full card data required to call the acquiring bank. Never persisted.</summary>
public record CardDetails(string CardNumber, int ExpiryMonth, int ExpiryYear, string Cvv);
