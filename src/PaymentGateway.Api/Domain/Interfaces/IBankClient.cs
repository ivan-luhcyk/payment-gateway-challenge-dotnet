using PaymentGateway.Api.Domain.Bank;
using PaymentGateway.Api.Domain.Payments;

namespace PaymentGateway.Api.Domain.Interfaces;

public interface IBankClient
{
    Task<BankAuthorizationResult?> AuthorizeAsync(
        CardDetails card,
        Money money,
        CancellationToken cancellationToken = default);
}
