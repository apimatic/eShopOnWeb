using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PayByBank;

public static class PayByBankServiceCollectionExtensions
{
    public static IServiceCollection AddPayByBank(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaidOptions>(configuration.GetSection(PlaidOptions.SectionName));
        // The sandbox credentials are handed to the process as environment variables; fall back to them
        // when the Plaid: section (user-secrets, appsettings, ...) leaves a value unset.
        services.PostConfigure<PlaidOptions>(o =>
        {
            if (string.IsNullOrWhiteSpace(o.ClientId)) o.ClientId = Environment.GetEnvironmentVariable("PLAID_CLIENT_ID");
            if (string.IsNullOrWhiteSpace(o.Secret)) o.Secret = Environment.GetEnvironmentVariable("PLAID_SECRET");
            if (string.IsNullOrWhiteSpace(o.Environment)) o.Environment = Environment.GetEnvironmentVariable("PLAID_ENV");
        });
        services.Configure<PayByBankOptions>(configuration.GetSection(PayByBankOptions.SectionName));

        services.AddDataProtection();
        services.AddHttpClient();
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<PlaidGateway>();
        services.AddSingleton<BankTokenProtector>();
        services.AddSingleton<PaymentSpeedPolicy>();
        services.AddScoped<BankDirectoryService>();
        services.AddScoped<BankConnectionService>();
        services.AddScoped<BankPaymentService>();
        services.AddScoped<BankActivityService>();
        services.AddScoped<PortfolioService>();
        return services;
    }
}
