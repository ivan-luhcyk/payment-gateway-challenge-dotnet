using PaymentGateway.Api.Domain.Payments;

namespace PaymentGateway.Api.Domain.Interfaces;

public interface IPaymentRepository
{
    Task SaveAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}
