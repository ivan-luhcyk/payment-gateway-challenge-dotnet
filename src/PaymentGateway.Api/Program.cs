using System.Net;
using System.Reflection;
using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Polly;
using PaymentGateway.Api.Application.Handlers;
using PaymentGateway.Api.Application.Messages;
using PaymentGateway.Api.Domain.Interfaces;
using PaymentGateway.Api.Infrastructure.Bank;
using PaymentGateway.Api.Infrastructure.Messaging;
using PaymentGateway.Api.Infrastructure.Persistence;
using PaymentGateway.Api.Presentation;
using PaymentGateway.Api.Presentation.Swagger;
using StackExchange.Redis;
using Polly.Timeout;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(options => options.SuppressModelStateInvalidFilter = true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Payment Gateway API",
        Version = "v1",
        Description = "Processes card payments through an acquiring bank and stores the result."
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    options.IncludeXmlComments(xmlPath);
    options.OperationFilter<IdempotencyHeaderOperationFilter>();
});

// ── Options ───────────────────────────────────────────────────────────────────

builder.Services
    .AddOptions<IdempotencyOptions>()
    .BindConfiguration(IdempotencyOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<RedisOptions>()
    .BindConfiguration(RedisOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<RabbitMqOptions>()
    .BindConfiguration(RabbitMqOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<BankSimulatorOptions>()
    .BindConfiguration(BankSimulatorOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── Infrastructure ────────────────────────────────────────────────────────────

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
    return ConnectionMultiplexer.Connect(opts.ConnectionString);
});

builder.Services.AddSingleton<PaymentsRepository>();
builder.Services.AddSingleton<IPaymentRepository>(sp  => sp.GetRequiredService<PaymentsRepository>());
builder.Services.AddSingleton<IIdempotencyStore>(sp   => sp.GetRequiredService<PaymentsRepository>());

builder.Services.AddMassTransit(cfg =>
{
    cfg.AddConsumer<PostPaymentCommandHandler>();
    cfg.AddConsumer<ProcessBankPaymentCommandHandler>();
    cfg.AddConsumer<GetPaymentQueryHandler>();

    cfg.AddRequestClient<PostPaymentCommand>();
    cfg.AddRequestClient<GetPaymentQuery>();

    cfg.UsingRabbitMq((context, rabbitCfg) =>
    {
        var opts = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
        rabbitCfg.Host(opts.Host, opts.VirtualHost, h =>
        {
            h.Username(opts.Username);
            h.Password(opts.Password);
        });

        rabbitCfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddHttpClient<IBankClient, BankClient>((sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<BankSimulatorOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
})
.AddResilienceHandler("bank-client", (pipeline, context) =>
{
    var opts = context.ServiceProvider.GetRequiredService<IOptions<BankSimulatorOptions>>().Value;

    pipeline.AddTimeout(TimeSpan.FromSeconds(opts.TimeoutSeconds));

    pipeline.AddRetry(new HttpRetryStrategyOptions
    {
        MaxRetryAttempts = opts.RetryCount,
        Delay            = TimeSpan.FromMilliseconds(opts.RetryDelayMs),
        BackoffType      = DelayBackoffType.Exponential,
        UseJitter        = true,
        ShouldHandle     = args => ValueTask.FromResult(
            args.Outcome.Result?.StatusCode == HttpStatusCode.ServiceUnavailable ||
            args.Outcome.Exception is HttpRequestException or TimeoutRejectedException)
    });

    pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
    {
        MinimumThroughput = opts.CircuitBreakerMinimumThroughput,
        BreakDuration     = TimeSpan.FromSeconds(opts.CircuitBreakerBreakSeconds),
        FailureRatio      = 0.5,
        SamplingDuration  = TimeSpan.FromSeconds(opts.CircuitBreakerBreakSeconds)
    });
});

// ── Pipeline ──────────────────────────────────────────────────────────────────

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Payment Gateway API v1"));
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
