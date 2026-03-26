using PaymentGateway.Api.Domain.Enums;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Tests.Integration.Fixtures;

namespace PaymentGateway.Api.Tests.Integration;

/// <summary>
/// Integration tests for POST /api/payments.
/// The factory uses an in-memory MassTransit transport and a fake bank client,
/// so no external infrastructure (Redis, RabbitMQ, bank simulator) is required.
/// </summary>
public class PostPaymentTests : IClassFixture<PaymentGatewayFactory>
{
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // Card numbers chosen to exercise every bank simulator rule.
    private const string AuthorizedCard = "2222405343248877"; // ends in 7 (odd)  → Authorized
    private const string DeclinedCard   = "2222405343248112"; // ends in 2 (even) → Declined
    private const string BankDownCard   = "2222405343248110"; // ends in 0        → Rejected

    public PostPaymentTests(PaymentGatewayFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Validation rejections ────────────────────────────────────────────────

    [Fact]
    public async Task Post_MissingIdempotencyKeyHeader_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/payments", ValidBody());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_BlankIdempotencyKeyHeader_Returns400(string headerValue)
    {
        var request = BuildRequest(ValidBody(), headerValue);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("123")]            // too short (< 14 digits)
    [InlineData("12345678901234567890")] // too long  (> 19 digits)
    [InlineData("abcd1234567890ab")]     // non-numeric
    public async Task Post_InvalidCardNumber_Returns400(string cardNumber)
    {
        var body = ValidBody(cardNumber: cardNumber);
        var response = await _client.SendAsync(BuildRequest(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public async Task Post_InvalidExpiryMonth_Returns400(int month)
    {
        var body = ValidBody(expiryMonth: month);
        var response = await _client.SendAsync(BuildRequest(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_ExpiredCard_Returns400()
    {
        // Expired: year in the past
        var body = ValidBody(expiryMonth: 1, expiryYear: 2020);
        var response = await _client.SendAsync(BuildRequest(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("GB")]    // too short
    [InlineData("GBPP")] // too long
    [InlineData("123")]   // digits, not letters
    public async Task Post_InvalidCurrency_Returns400(string currency)
    {
        var body = ValidBody(currency: currency);
        var response = await _client.SendAsync(BuildRequest(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("12")]     // too short
    [InlineData("12345")] // too long
    [InlineData("abc")]    // non-numeric
    public async Task Post_InvalidCvv_Returns400(string cvv)
    {
        var body = ValidBody(cvv: cvv);
        var response = await _client.SendAsync(BuildRequest(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_ZeroAmount_Returns400()
    {
        var body = ValidBody(amount: 0);
        var response = await _client.SendAsync(BuildRequest(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_NegativeAmount_Returns400()
    {
        var body = ValidBody(amount: -1);
        var response = await _client.SendAsync(BuildRequest(body));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Successful acceptance ────────────────────────────────────────────────

    [Fact]
    public async Task Post_ValidRequest_ReturnsSuccessStatusCode()
    {
        var response = await _client.SendAsync(BuildRequest(ValidBody()));
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Post_ValidRequest_ReturnsProcessingStatus()
    {
        var response = await _client.SendAsync(BuildRequest(ValidBody()));
        var payment = await DeserializeAsync<PaymentResponse>(response);

        Assert.Equal(PaymentStatus.Processing, payment!.Status);
    }

    [Fact]
    public async Task Post_ValidRequest_ReturnsNonEmptyPaymentId()
    {
        var response = await _client.SendAsync(BuildRequest(ValidBody()));
        var payment = await DeserializeAsync<PaymentResponse>(response);

        Assert.NotEqual(Guid.Empty, payment!.Id);
    }

    [Fact]
    public async Task Post_ValidRequest_ReturnsLastFourOfCard()
    {
        // AuthorizedCard = "2222405343248877" → last four = 8877
        var response = await _client.SendAsync(BuildRequest(ValidBody(cardNumber: AuthorizedCard)));
        var payment = await DeserializeAsync<PaymentResponse>(response);

        Assert.Equal(8877, payment!.CardNumberLastFour);
    }

    [Fact]
    public async Task Post_ValidRequest_NeverReturnsFullCardNumber()
    {
        var response = await _client.SendAsync(BuildRequest(ValidBody(cardNumber: AuthorizedCard)));
        var rawBody = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(AuthorizedCard, rawBody);
    }

    [Fact]
    public async Task Post_ValidRequest_ReturnsCorrectExpiryAndCurrencyAndAmount()
    {
        var response = await _client.SendAsync(BuildRequest(ValidBody()));
        var payment = await DeserializeAsync<PaymentResponse>(response);

        Assert.Equal(4, payment!.ExpiryMonth);
        Assert.Equal(2030, payment.ExpiryYear);
        Assert.Equal("GBP", payment.Currency);
        Assert.Equal(1050L, payment.Amount);
    }

    // ── Idempotency ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_SameIdempotencyKey_ReturnsSamePaymentId()
    {
        var key = Guid.NewGuid().ToString();

        var r1 = await _client.SendAsync(BuildRequest(ValidBody(), idempotencyKey: key));
        var r2 = await _client.SendAsync(BuildRequest(ValidBody(), idempotencyKey: key));

        var p1 = await DeserializeAsync<PaymentResponse>(r1);
        var p2 = await DeserializeAsync<PaymentResponse>(r2);

        Assert.Equal(p1!.Id, p2!.Id);
    }

    [Fact]
    public async Task Post_DifferentIdempotencyKeys_ReturnDifferentPaymentIds()
    {
        var r1 = await _client.SendAsync(BuildRequest(ValidBody(), idempotencyKey: Guid.NewGuid().ToString()));
        var r2 = await _client.SendAsync(BuildRequest(ValidBody(), idempotencyKey: Guid.NewGuid().ToString()));

        var p1 = await DeserializeAsync<PaymentResponse>(r1);
        var p2 = await DeserializeAsync<PaymentResponse>(r2);

        Assert.NotEqual(p1!.Id, p2!.Id);
    }

    // ── Final status after async bank processing ─────────────────────────────

    [Fact]
    public async Task Post_CardWithOddLastDigit_EventuallyBecomesAuthorized()
    {
        var response = await _client.SendAsync(BuildRequest(ValidBody(cardNumber: AuthorizedCard)));
        var payment  = await DeserializeAsync<PaymentResponse>(response);

        var final = await WaitForFinalStatusAsync(payment!.Id);

        Assert.Equal(PaymentStatus.Authorized, final?.Status);
    }

    [Fact]
    public async Task Post_CardWithEvenLastDigit_EventuallyBecomesDeclined()
    {
        var response = await _client.SendAsync(BuildRequest(ValidBody(cardNumber: DeclinedCard)));
        var payment  = await DeserializeAsync<PaymentResponse>(response);

        var final = await WaitForFinalStatusAsync(payment!.Id);

        Assert.Equal(PaymentStatus.Declined, final?.Status);
    }

    [Fact]
    public async Task Post_CardWithZeroLastDigit_EventuallyBecomesRejected()
    {
        var response = await _client.SendAsync(BuildRequest(ValidBody(cardNumber: BankDownCard)));
        var payment  = await DeserializeAsync<PaymentResponse>(response);

        var final = await WaitForFinalStatusAsync(payment!.Id);

        Assert.Equal(PaymentStatus.Rejected, final?.Status);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static object ValidBody(
        string cardNumber  = AuthorizedCard,
        int expiryMonth    = 4,
        int expiryYear     = 2030,
        string currency    = "GBP",
        long amount        = 1050,
        string cvv         = "123") => new
    {
        cardNumber,
        expiryMonth,
        expiryYear,
        currency,
        amount,
        cvv
    };

    private static HttpRequestMessage BuildRequest(
        object body,
        string? idempotencyKey = null)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, "/api/payments")
        {
            Content = JsonContent.Create(body)
        };

        var key = idempotencyKey ?? Guid.NewGuid().ToString();
        if (!string.IsNullOrWhiteSpace(key))
            msg.Headers.Add("Idempotency-Key", key);

        return msg;
    }

    private async Task<PaymentResponse?> WaitForFinalStatusAsync(Guid id, int maxAttempts = 40)
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            var response = await _client.GetAsync($"/api/payments/{id}");
            if (!response.IsSuccessStatusCode) return null;

            var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>(JsonOptions);
            if (payment?.Status != PaymentStatus.Processing) return payment;

            await Task.Delay(50);
        }

        return null;
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<T>(JsonOptions);
}
