using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Api.Infrastructure.Bank;

public class BankSimulatorOptions
{
    public const string Section = "BankSimulator";

    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Per-attempt timeout in seconds.</summary>
    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Number of retry attempts on transient failures (503, network errors).</summary>
    [Range(0, 5)]
    public int RetryCount { get; set; } = 2;

    /// <summary>Base delay in milliseconds between retries (exponential back-off).</summary>
    [Range(0, 10000)]
    public int RetryDelayMs { get; set; } = 300;

    /// <summary>Number of failures within the sampling window required to open the circuit.</summary>
    [Range(1, 100)]
    public int CircuitBreakerMinimumThroughput { get; set; } = 5;

    /// <summary>How long in seconds to keep the circuit open before attempting a probe.</summary>
    [Range(1, 300)]
    public int CircuitBreakerBreakSeconds { get; set; } = 30;
}
