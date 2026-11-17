using System;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PaymentsDependencies
{
    public static IServiceCollection AddFinixPayments(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(FinixOptions.SectionName);
        services.Configure<FinixOptions>(options =>
        {
            options.Username = section[nameof(FinixOptions.Username)] ?? string.Empty;
            options.Password = section[nameof(FinixOptions.Password)] ?? string.Empty;
            options.MerchantId = section[nameof(FinixOptions.MerchantId)] ?? string.Empty;
            options.Environment = section[nameof(FinixOptions.Environment)] ?? "sandbox";
            options.Currency = string.IsNullOrWhiteSpace(section[nameof(FinixOptions.Currency)])
                ? "USD"
                : section[nameof(FinixOptions.Currency)]!.Trim().ToUpperInvariant();
            options.BaseUrl = section[nameof(FinixOptions.BaseUrl)];
        });

        services.AddScoped<IPaymentService, PaymentService>();
        return services;
    }

    public static string ResolveBaseUrl(FinixOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return options.BaseUrl.TrimEnd('/');
        }

        return options.Environment?.Trim().ToLowerInvariant() switch
        {
            "live" => "https://finix.live-payments-api.com",
            "sandbox" or null or "" => "https://finix.sandbox-payments-api.com",
            _ => throw new InvalidOperationException(
                $"Unsupported Finix environment '{options.Environment}'. Expected 'sandbox' or 'live'.")
        };
    }
}
