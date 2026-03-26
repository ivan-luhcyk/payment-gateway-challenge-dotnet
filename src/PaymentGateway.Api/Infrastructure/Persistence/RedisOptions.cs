using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Infrastructure.Persistence;

public class RedisOptions
{
    public const string Section = "Redis";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
