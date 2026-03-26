namespace PaymentGateway.Api.Domain.Interfaces;

public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically registers <paramref name="paymentId"/> under <paramref name="idempotencyKey"/>.
    /// Returns <c>true</c> if the key was newly registered, <c>false</c> if it already existed.
    /// </summary>
    Task<bool> TryRegisterAsync(string idempotencyKey, Guid paymentId, CancellationToken cancellationToken = default);

    Task<Guid?> GetPaymentIdAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}
