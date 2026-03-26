namespace PaymentGateway.Api.Domain.Bank;

/// <summary>The outcome of an authorization request to the acquiring bank.</summary>
public record BankAuthorizationResult(bool Authorized);
