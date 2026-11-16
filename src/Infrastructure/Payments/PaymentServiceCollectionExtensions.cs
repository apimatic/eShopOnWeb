using System;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public static class PaymentServiceCollectionExtensions
{
    private const string SandboxBaseUrl = "https://finix.sandbox-payments-api.com";
    private const string LiveBaseUrl = "https://finix.live-payments-api.com";

    public static IServiceCollection AddFinixPayments(this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(FinixOptions.SectionName);
        var options = section.Get<FinixOptions>() ?? new FinixOptions();

        var missing = new System.Collections.Generic.List<string>();
        if (string.IsNullOrWhiteSpace(options.Username)) missing.Add($"{FinixOptions.SectionName}:Username");
        if (string.IsNullOrWhiteSpace(options.Password)) missing.Add($"{FinixOptions.SectionName}:Password");
        if (string.IsNullOrWhiteSpace(options.MerchantId)) missing.Add($"{FinixOptions.SectionName}:MerchantId");
        if (string.IsNullOrWhiteSpace(options.Currency)) missing.Add($"{FinixOptions.SectionName}:Currency");
        if (string.IsNullOrWhiteSpace(options.Environment)) missing.Add($"{FinixOptions.SectionName}:Environment");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Finix configuration is incomplete. Missing: {string.Join(", ", missing)}.");
        }

        var currency = options.Currency.Trim().ToUpperInvariant();
        if (currency is not ("USD" or "CAD"))
        {
            throw new InvalidOperationException(
                $"Unsupported Finix currency '{options.Currency}'. Supported values are USD and CAD.");
        }

        var environment = options.Environment.Trim().ToLowerInvariant();
        if (environment is not ("sandbox" or "live"))
        {
            throw new InvalidOperationException(
                $"Unsupported Finix environment '{options.Environment}'. Supported values are sandbox and live.");
        }

        services.Configure<FinixOptions>(section);
        services.AddScoped<IPaymentApplicationService, PaymentApplicationService>();

        services.AddFinixApiReferenceClient(clientOptions =>
        {
            clientOptions.BasicAuth = new BasicAuthCredentials
            {
                Username = options.Username,
                Password = options.Password
            };

            clientOptions.Server.Default.Production.BaseUrl =
                string.IsNullOrWhiteSpace(options.BaseUrl)
                    ? environment == "live" ? LiveBaseUrl : SandboxBaseUrl
                    : options.BaseUrl;

            clientOptions.Logging = new LoggingOptions
            {
                LogRequestHeaders = false,
                LogRequestBody = false,
                LogResponseHeaders = false
            };
        });

        return services;
    }
}
