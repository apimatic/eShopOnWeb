using System;
using System.Collections.Generic;
using System.Net.Http;
using CyberSourceMergedSpec;
using CyberSourceMergedSpec.Core.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

public static class VisaInvoicingServiceCollectionExtensions
{
    /// <summary>
    /// The environment-variable switch that turns on the SDK's HTTP-Signature hook. Without it every
    /// request would go out unsigned and be rejected at the provider. Not a secret.
    /// </summary>
    private const string HttpSignatureSwitch = "APIMATIC_EXPERIMENTAL_VISA_HTTP_SIGNATURE";

    /// <summary>
    /// Wires the Visa/CyberSource billing integration: validated settings, the signed SDK client with
    /// its base address bound from configuration, and the <see cref="IInvoiceService"/> implementation.
    /// </summary>
    public static IServiceCollection AddVisaInvoicing(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(VisaSettings.CONFIG_SECTION);
        services.Configure<VisaSettings>(section);

        var settings = section.Get<VisaSettings>() ?? new VisaSettings();

        // Fail fast at startup (not on the first request, and not with an unsigned client) if a
        // credential is missing. Name what is missing; never echo a value.
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(settings.MerchantId)) missing.Add($"{VisaSettings.CONFIG_SECTION}:MerchantId");
        if (string.IsNullOrWhiteSpace(settings.KeyId)) missing.Add($"{VisaSettings.CONFIG_SECTION}:KeyId");
        if (string.IsNullOrWhiteSpace(settings.SecretKey)) missing.Add($"{VisaSettings.CONFIG_SECTION}:SecretKey");
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "Visa billing is not configured. Missing required setting(s): " + string.Join(", ", missing) +
                ". Load them into user-secrets from the VISA_MERCHANT_ID / VISA_KEY_ID / VISA_SECRET_KEY environment variables.");
        }

        // The SDK reads its credentials from process environment variables inside the client constructor,
        // so they must be present before the client is first resolved. Load the values from configuration
        // (user-secrets / environment) into those variables now — without a value ever touching source.
        // Existing environment values are only overwritten when configuration actually supplies one.
        SetIfPresent("VISA_MERCHANT_ID", settings.MerchantId);
        SetIfPresent("VISA_KEY_ID", settings.KeyId);
        SetIfPresent("VISA_SECRET_KEY", settings.SecretKey);
        Environment.SetEnvironmentVariable(HttpSignatureSwitch, "true");

        services.AddCyberSourceMergedSpecClient(options =>
        {
            // Route every provider call through the configured base address, used verbatim, when set.
            // Left unset, the SDK's built-in (sandbox) default applies. No host is hard-coded here.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            }

            // Bound each attempt so a hung provider cannot pin a request for the SDK's 100s default.
            options.Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(20) };
        });

        // The SDK resolves the default (unnamed) HttpClient. Keep DNS fresh behind the singleton client,
        // and attach opt-in wire diagnostics when enabled.
        services.AddHttpClient(Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        if (settings.LogRequests)
        {
            services.AddTransient<VisaWireLoggingHandler>();
            services.AddHttpClient(Options.DefaultName).AddHttpMessageHandler<VisaWireLoggingHandler>();
        }

        services.AddScoped<IInvoiceService, VisaInvoiceService>();

        return services;
    }

    private static void SetIfPresent(string variable, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Environment.SetEnvironmentVariable(variable, value);
        }
    }
}
