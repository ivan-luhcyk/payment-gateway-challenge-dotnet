using MassTransit;
using Microsoft.Extensions.Logging;
using PaymentGateway.Api.Application.Messages;
using PaymentGateway.Api.Domain.Interfaces;

namespace PaymentGateway.Api.Application.Handlers;

public class ProcessBankPaymentCommandHandler : IConsumer<ProcessBankPaymentCommand>
{
    private readonly IBankClient        _bankClient;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ILogger<ProcessBankPaymentCommandHandler> _logger;

    public ProcessBankPaymentCommandHandler(
        IBankClient bankClient,
        IPaymentRepository paymentRepository,
        ILogger<ProcessBankPaymentCommandHandler> logger)
    {
        _bankClient        = bankClient;
        _paymentRepository = paymentRepository;
        _logger            = logger;
    }

    public async Task Consume(ConsumeContext<ProcessBankPaymentCommand> context)
    {
        var command           = context.Message;
        var cancellationToken = context.CancellationToken;

        var payment = await _paymentRepository.GetByIdAsync(command.PaymentId, cancellationToken);
        if (payment is null)
        {
            _logger.LogWarning("ProcessBankPaymentCommand: payment not found. PaymentId={PaymentId}", command.PaymentId);
            return;
        }

        _logger.LogInformation("Authorizing payment with bank. PaymentId={PaymentId}", command.PaymentId);

        try
        {
            var result = await _bankClient.AuthorizeAsync(command.CardDetails, command.Money, cancellationToken);
            if (result is null)
            {
                _logger.LogWarning("Bank returned no result (failure). PaymentId={PaymentId}", command.PaymentId);
                payment.Fail();
            }
            else if (result.Authorized)
            {
                _logger.LogInformation("Payment authorized. PaymentId={PaymentId}", command.PaymentId);
                payment.Authorize();
            }
            else
            {
                _logger.LogInformation("Payment declined. PaymentId={PaymentId}", command.PaymentId);
                payment.Decline();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bank authorization threw an exception. PaymentId={PaymentId}", command.PaymentId);
            payment.Fail();
        }

        await _paymentRepository.SaveAsync(payment, cancellationToken);
    }
}
