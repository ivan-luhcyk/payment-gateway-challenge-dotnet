using System.Collections.Concurrent;
using PaymentGateway.Api.Domain.Interfaces;
using PaymentGateway.Api.Domain.Payments;

namespace PaymentGateway.Api.Tests.Integration.Fixtures;

/// <summary>
/// Thread-safe in-memory store used in integration tests in place of Redis.
/// Implements both IPaymentRepository and IIdempotencyStore so a single instance
/// can be registered for both interfaces — mirroring the production singleton.
/// </summary>
public class InMemoryPaymentsStore : IPaymentRepository, IIdempotencyStore
{
    private readonly ConcurrentDictionary<Guid, Payment> _payments = new();
    private readonly ConcurrentDictionary<string, Guid> _idempotencyKeys = new();

    public Task SaveAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        _payments[payment.Id] = payment;
        return Task.CompletedTask;
    }

    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _payments.TryGetValue(id, out var payment);
        return Task.FromResult(payment);
    }

    public Task<bool> TryRegisterAsync(string idempotencyKey, Guid paymentId, CancellationToken cancellationToken = default)
        => Task.FromResult(_idempotencyKeys.TryAdd(idempotencyKey, paymentId));

    public Task<Guid?> GetPaymentIdAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        _idempotencyKeys.TryGetValue(idempotencyKey, out var id);
        return Task.FromResult(id == Guid.Empty ? null : (Guid?)id);
    }
}
