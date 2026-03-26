using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Infrastructure.Messaging;

public class RabbitMqOptions
{
    public const string Section = "MassTransit:RabbitMq";

    [Required]
    public string Host { get; set; } = string.Empty;

    public string VirtualHost { get; set; } = "/";

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
