using PaymentGateway.Api.Domain.Enums;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Tests.Integration.Fixtures;

namespace PaymentGateway.Api.Tests.Integration;

/// <summary>
/// Integration tests for GET /api/payments/{id}.
/// </summary>
public class GetPaymentTests : IClassFixture<PaymentGatewayFactory>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public GetPaymentTests(PaymentGatewayFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/payments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_KnownId_Returns200()
    {
        var id = await CreatePaymentIdAsync();
        var response = await _client.GetAsync($"/api/payments/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_KnownId_ReturnsCorrectPaymentId()
    {
        var id = await CreatePaymentIdAsync();
        var response = await _client.GetAsync($"/api/payments/{id}");
        var payment = await DeserializeAsync<PaymentResponse>(response);

        Assert.Equal(id, payment!.Id);
    }

    [Fact]
    public async Task Get_KnownId_ReturnsPaymentWithExpectedFields()
    {
        var id = await CreatePaymentIdAsync(
            cardNumber: "2222405343248877", // last four = 8877
            expiryMonth: 4,
            expiryYear: 2030,
            currency: "GBP",
            amount: 1050);

        var response = await _client.GetAsync($"/api/payments/{id}");
        var payment = await DeserializeAsync<PaymentResponse>(response);

        Assert.Equal(8877, payment!.CardNumberLastFour);
        Assert.Equal(4, payment.ExpiryMonth);
        Assert.Equal(2030, payment.ExpiryYear);
        Assert.Equal("GBP", payment.Currency);
        Assert.Equal(1050L, payment.Amount);
    }

    [Fact]
    public async Task Get_KnownId_NeverExposesFullCardNumber()
    {
        const string fullCard = "2222405343248877";
        var id = await CreatePaymentIdAsync(cardNumber: fullCard);

        var response = await _client.GetAsync($"/api/payments/{id}");
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(fullCard, rawBody);
    }

    [Fact]
    public async Task Get_InvalidGuidFormat_Returns400OrNotFound()
    {
        var response = await _client.GetAsync("/api/payments/not-a-guid");
        // ASP.NET Core route constraint rejects non-Guid values with 400
        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest ||
            response.StatusCode == HttpStatusCode.NotFound);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> CreatePaymentIdAsync(
        string cardNumber  = "2222405343248877",
        int expiryMonth    = 4,
        int expiryYear     = 2030,
        string currency    = "GBP",
        long amount        = 1050,
        string cvv         = "123")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
        {
            Content = JsonContent.Create(new { cardNumber, expiryMonth, expiryYear, currency, amount, cvv })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var payment = await DeserializeAsync<PaymentResponse>(response);
        return payment!.Id;
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<T>(JsonOptions);
}
