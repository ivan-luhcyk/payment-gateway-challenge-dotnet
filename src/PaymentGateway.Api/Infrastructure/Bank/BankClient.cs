using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using PaymentGateway.Api.Domain.Bank;
using PaymentGateway.Api.Domain.Interfaces;
using PaymentGateway.Api.Domain.Payments;

namespace PaymentGateway.Api.Infrastructure.Bank;

public class BankClient : IBankClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BankClient> _logger;

    public BankClient(HttpClient httpClient, ILogger<BankClient> logger)
    {
        _httpClient = httpClient;
        _logger     = logger;
    }

    public async Task<BankAuthorizationResult?> AuthorizeAsync(
        CardDetails card,
        Money money,
        CancellationToken cancellationToken = default)
    {
        var request = new BankPaymentRequest
        {
            CardNumber = card.CardNumber,
            ExpiryDate = $"{card.ExpiryMonth:D2}/{card.ExpiryYear:D4}",
            Currency   = money.Currency,
            Amount     = money.Amount,
            Cvv        = card.Cvv
        };

        var response = await _httpClient.PostAsJsonAsync("/payments", request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Bank returned non-success status {StatusCode}", (int)response.StatusCode);
            return null;
        }

        var bankResponse = await response.Content.ReadFromJsonAsync<BankPaymentResponse>(cancellationToken);
        return bankResponse is null ? null : new BankAuthorizationResult(bankResponse.Authorized);
    }
}
