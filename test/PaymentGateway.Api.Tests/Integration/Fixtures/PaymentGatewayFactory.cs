using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PaymentGateway.Api.Application.Handlers;
using PaymentGateway.Api.Application.Messages;
using PaymentGateway.Api.Domain.Interfaces;
using PaymentGateway.Api.Infrastructure.Persistence;
using StackExchange.Redis;

namespace PaymentGateway.Api.Tests.Integration.Fixtures;

public class PaymentGatewayFactory : WebApplicationFactory<Program>
{
    private readonly InMemoryPaymentsStore _store = new();
    private readonly FakeBankClient _bankClient = new();

    /// <summary>Exposes the shared in-memory store so tests can inspect or pre-seed state.</summary>
    public InMemoryPaymentsStore Store => _store;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // ── Remove Redis and real repository registrations ─────────────
            services.RemoveAll<IConnectionMultiplexer>();
            services.RemoveAll<PaymentsRepository>();
            services.RemoveAll<IPaymentRepository>();
            services.RemoveAll<IIdempotencyStore>();

            // ── Remove real bank client (registered via AddHttpClient) ─────
            services.RemoveAll<IBankClient>();

            // ── Remove ALL MassTransit service registrations ───────────────
            var massTransitDescriptors = services
                .Where(d =>
                    IsFromMassTransit(d.ServiceType) ||
                    IsFromMassTransit(d.ImplementationType) ||
                    (d.ImplementationInstance != null && IsFromMassTransit(d.ImplementationInstance.GetType())))
                .ToList();

            foreach (var d in massTransitDescriptors)
                services.Remove(d);

            // ── Register test doubles ──────────────────────────────────────
            services.AddSingleton(_store);
            services.AddSingleton<IPaymentRepository>(_ => _store);
            services.AddSingleton<IIdempotencyStore>(_ => _store);
            services.AddSingleton<IBankClient>(_ => _bankClient);

            // ── Register MassTransit with in-memory transport ──────────────
            services.AddMassTransit(cfg =>
            {
                cfg.AddConsumer<PostPaymentCommandHandler>();
                cfg.AddConsumer<ProcessBankPaymentCommandHandler>();
                cfg.AddConsumer<GetPaymentQueryHandler>();

                cfg.AddRequestClient<PostPaymentCommand>();
                cfg.AddRequestClient<GetPaymentQuery>();

                cfg.UsingInMemory((context, inMemoryCfg) =>
                {
                    inMemoryCfg.ConfigureEndpoints(context);
                });
            });
        });
    }

    private bool IsFromMassTransit(Type? type)
        => type?.Namespace?.StartsWith("MassTransit") == true ||
           type?.FullName?.StartsWith("MassTransit") == true;
}
