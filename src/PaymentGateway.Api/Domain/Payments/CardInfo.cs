namespace PaymentGateway.Api.Domain.Payments;

/// <summary>Masked card identity stored with a payment — last four digits and expiry.</summary>
public record CardInfo(int LastFour, int ExpiryMonth, int ExpiryYear);
