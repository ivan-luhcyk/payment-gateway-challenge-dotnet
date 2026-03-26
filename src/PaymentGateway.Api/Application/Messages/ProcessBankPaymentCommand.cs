using PaymentGateway.Api.Domain.Payments;

namespace PaymentGateway.Api.Application.Messages;

public record ProcessBankPaymentCommand(Guid PaymentId, CardDetails CardDetails, Money Money);
