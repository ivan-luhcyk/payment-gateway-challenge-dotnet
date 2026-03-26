using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Presentation;

public class IdempotencyOptions
{
    public const string Section = "Idempotency";

    [Required]
    public string HeaderName { get; set; } = "Idempotency-Key";
}
