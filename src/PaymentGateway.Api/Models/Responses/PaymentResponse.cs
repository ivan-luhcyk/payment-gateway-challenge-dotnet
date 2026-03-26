using System.Text.Json.Serialization;
using PaymentGateway.Api.Domain.Enums;
using PaymentGateway.Api.Domain.Payments;

namespace PaymentGateway.Api.Models.Responses;

/// <summary>Result of a processed payment.</summary>
public class PaymentResponse
{
    /// <summary>Unique identifier for this payment.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Outcome of the payment:
    /// <list type="bullet">
    ///   <item><description><b>Processing</b> - accepted and waiting for bank result.</description></item>
    ///   <item><description><b>Authorized</b> — bank approved the payment.</description></item>
    ///   <item><description><b>Declined</b> — bank refused the payment.</description></item>
    ///   <item><description><b>Rejected</b> — request failed validation, or bank was unreachable/returned an error.</description></item>
    /// </list>
    /// </summary>
    public PaymentStatus Status { get; set; }

    /// <summary>Last four digits of the card number.</summary>
    /// <example>8877</example>
    public int CardNumberLastFour { get; set; }

    /// <summary>Card expiry month (1–12).</summary>
    /// <example>4</example>
    public int ExpiryMonth { get; set; }

    /// <summary>Card expiry year (4-digit).</summary>
    /// <example>2030</example>
    public int ExpiryYear { get; set; }

    /// <summary>ISO 4217 currency code.</summary>
    /// <example>GBP</example>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Payment amount in minor currency units (e.g. 1 = $0.01, 1050 = $10.50 for USD).</summary>
    /// <example>1050</example>
    public long Amount { get; set; }

    /// <summary>Maps a <see cref="Payment"/> aggregate to its response DTO.</summary>
    public static PaymentResponse From(Payment payment) => new()
    {
        Id                 = payment.Id,
        Status             = payment.Status,
        CardNumberLastFour = payment.Card.LastFour,
        ExpiryMonth        = payment.Card.ExpiryMonth,
        ExpiryYear         = payment.Card.ExpiryYear,
        Currency           = payment.Money.Currency,
        Amount             = payment.Money.Amount
    };
}
