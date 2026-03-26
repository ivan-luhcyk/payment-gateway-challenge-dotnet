using PaymentGateway.Api.Domain.Bank;
using PaymentGateway.Api.Domain.Interfaces;
using PaymentGateway.Api.Domain.Payments;

namespace PaymentGateway.Api.Tests.Integration.Fixtures;

/// <summary>
/// Simulates the bank simulator rules without an HTTP call.
/// Last digit of card number:
///   Odd (1,3,5,7,9) → authorized: true
///   Even (2,4,6,8)  → authorized: false
///   0               → null (bank unavailable → Rejected)
/// </summary>
public class FakeBankClient : IBankClient
{
    public Task<BankAuthorizationResult?> AuthorizeAsync(
        CardDetails card,
        Money money,
        CancellationToken cancellationToken = default)
    {
        var lastDigit = card.CardNumber[^1] - '0';

        BankAuthorizationResult? result = lastDigit switch
        {
            0                          => null,
            _ when lastDigit % 2 != 0 => new BankAuthorizationResult(true),
            _                          => new BankAuthorizationResult(false)
        };

        return Task.FromResult(result);
    }
}
