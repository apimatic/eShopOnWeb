using System;
using System.Net.Http;
using CyberSourceMergedSpec;
using CyberSourceMergedSpec.Core.Configuration;
using CyberSourceMergedSpec.Core.Experimental.VisaHttpSignature;
using CyberSourceMergedSpec.Servers;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Invoicing;

/// <summary>
/// Wires the Visa/CyberSource invoicing client and gateway into the service container.
/// </summary>
public static class VisaInvoicingServiceCollectionExtensions
{
    private const string VisaHttpClientName = "visa-cybersource";

    public static IServiceCollection AddVisaInvoicing(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind + validate the settings. ValidateOnStart surfaces a missing credential at startup rather
        // than on the first request.
        services.AddOptions<VisaSettings>()
            .Bind(configuration.GetSection(VisaSettings.SectionName))
            .Validate(s => !string.IsNullOrWhiteSpace(s.BaseUrl), "Visa:BaseUrl must be configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.MerchantId), "Visa:MerchantId must be configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.KeyId), "Visa:KeyId must be configured.")
            .Validate(s => !string.IsNullOrWhiteSpace(s.SecretKey), "Visa:SecretKey must be configured.")
            .Validate(s => s.RequestTimeoutSeconds is >= 1 and <= 600, "Visa:RequestTimeoutSeconds must be between 1 and 600.")
            .ValidateOnStart();

        var settings = configuration.GetSection(VisaSettings.SectionName).Get<VisaSettings>() ?? new VisaSettings();

        // A named, isolated HttpClient so this SDK's timeout/handler pipeline never touches the shared
        // default client. PooledConnectionLifetime keeps DNS fresh behind the long-lived (singleton) client.
        var wireLog = configuration.GetValue<bool>($"{VisaSettings.SectionName}:WireLog");
        if (wireLog)
            services.AddTransient<VisaWireLoggingHandler>();

        var httpClientBuilder = services.AddHttpClient(VisaHttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds + 5);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        if (wireLog)
            httpClientBuilder.AddHttpMessageHandler<VisaWireLoggingHandler>();

        // The SDK client is long-lived; construct it once. The HTTP-Signature auth hook is built from
        // configuration (not process env vars) and injected via options.Hooks — the only signing path.
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<VisaSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(VisaHttpClientName);

            var signatureConfig = new VisaHttpSignatureConfig
            {
                MerchantId = opts.MerchantId,
                KeyId = opts.KeyId,
                SecretKey = opts.SecretKey,
            };

            var clientOptions = new CyberSourceMergedSpecClientOptions
            {
                Environment = ServerEnvironment.Production,
                Hooks = new[] { new VisaHttpSignatureHook(signatureConfig) },
                Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(opts.RequestTimeoutSeconds) },
            };

            // Route EVERY call through the configured base URL, verbatim. The single environment member is
            // "Production" but its default host is the sandbox, so the base URL must be pinned explicitly.
            clientOptions.Server.Default.Production.BaseUrl = opts.BaseUrl;

            return new CyberSourceMergedSpecClient(httpClient, clientOptions);
        });

        services.AddScoped<IInvoiceProviderGateway, CyberSourceInvoiceGateway>();

        // A stable tag identifying this deployment's bills on the shared provider account.
        var tag = string.IsNullOrWhiteSpace(settings.MerchantReferenceTag)
            ? Guid.NewGuid().ToString("N").Substring(0, 8)
            : settings.MerchantReferenceTag!.Trim();
        services.AddSingleton<IInvoicingInstance>(new InvoicingInstance(tag));

        return services;
    }
}
