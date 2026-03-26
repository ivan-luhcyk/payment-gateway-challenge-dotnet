using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Models.Requests;

/// <summary>Card and payment details required to process a payment.</summary>
public class PostPaymentRequest : IValidatableObject
{
    /// <summary>Full card number (14–19 digits).</summary>
    /// <example>2222405343248877</example>
    [Required]
    [RegularExpression(@"^\d{14,19}$", ErrorMessage = "Card number must be 14–19 digits.")]
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>Card expiry month (1–12).</summary>
    /// <example>4</example>
    [Range(1, 12, ErrorMessage = "Expiry month must be between 1 and 12.")]
    public int ExpiryMonth { get; set; }

    /// <summary>Card expiry year (4-digit).</summary>
    /// <example>2030</example>
    public int ExpiryYear { get; set; }

    /// <summary>ISO 4217 currency code (3 letters).</summary>
    /// <example>GBP</example>
    [Required]
    [RegularExpression(@"^[A-Za-z]{3}$", ErrorMessage = "Currency must be a 3-letter ISO code.")]
    public string Currency { get; set; } = string.Empty;

    /// <summary>Payment amount in minor currency units (e.g. 1 = $0.01, 1050 = $10.50 for USD).</summary>
    /// <example>1050</example>
    [Range(1, long.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
    public long Amount { get; set; }

    /// <summary>Card CVV (3–4 digits).</summary>
    /// <example>123</example>
    [Required]
    [RegularExpression(@"^\d{3,4}$", ErrorMessage = "CVV must be 3 or 4 digits.")]
    public string Cvv { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var now = DateTime.UtcNow;
        if (ExpiryYear < now.Year || (ExpiryYear == now.Year && ExpiryMonth < now.Month))
            yield return new ValidationResult("Card has expired.", [nameof(ExpiryMonth), nameof(ExpiryYear)]);
    }
}
