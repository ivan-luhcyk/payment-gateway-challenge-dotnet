using System.Text.Json;
using System.Text.Json.Serialization;
using PaymentGateway.Api.Domain.Interfaces;
using PaymentGateway.Api.Domain.Payments;
using StackExchange.Redis;

namespace PaymentGateway.Api.Infrastructure.Persistence;

public class PaymentsRepository : IPaymentRepository, IIdempotencyStore
{
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IDatabase _database;

    public PaymentsRepository(IConnectionMultiplexer connectionMultiplexer)
    {
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task SaveAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        var value = JsonSerializer.Serialize(payment, _serializerOptions);
        await _database.StringSetAsync(PaymentKey(payment.Id), value);
    }

    public async Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var value = await _database.StringGetAsync(PaymentKey(id));
        if (!value.HasValue) return null;
        return JsonSerializer.Deserialize<Payment>(value!, _serializerOptions);
    }

    public async Task<bool> TryRegisterAsync(
        string idempotencyKey,
        Guid paymentId,
        CancellationToken cancellationToken = default)
        => await _database.StringSetAsync(
            IdempotencyKey(idempotencyKey),
            paymentId.ToString(),
            when: When.NotExists);

    public async Task<Guid?> GetPaymentIdAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var value = await _database.StringGetAsync(IdempotencyKey(idempotencyKey));
        if (!value.HasValue) return null;
        return Guid.TryParse(value!, out var id) ? id : null;
    }

    private string PaymentKey(Guid id)        => $"payment:{id}";
    private string IdempotencyKey(string key) => $"idempotency:{key}";
}
