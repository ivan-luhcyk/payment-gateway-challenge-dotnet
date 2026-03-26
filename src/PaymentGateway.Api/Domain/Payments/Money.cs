namespace PaymentGateway.Api.Domain.Payments;

/// <summary>An amount in a specific currency (minor units).</summary>
public record Money(long Amount, string Currency);
