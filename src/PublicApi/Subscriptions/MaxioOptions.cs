using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the "Maxio" configuration
/// section (user-secrets / environment variables). No values are hard-coded so the same build
/// can target a different Maxio site or catalog.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// Maxio Advanced Billing API key (Basic-auth username; password is the literal "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain, e.g. "cp-exp-2" for https://cp-exp-2.chargify.com.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Maxio environment of the site (e.g. "US"). Used for diagnostics only;
    /// <see cref="BaseUrl"/> overrides the derived address when present.
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the Maxio product family that holds the subscription plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address override. When set it is used verbatim
    /// instead of the address derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: neither 'Maxio:BaseUrl' nor 'Maxio:Subdomain' is set.");
        }

        return $"https://{Subdomain}.chargify.com";
    }
}

public static class MaxioOptionsRegistration
{
    public const string HttpClientName = "Maxio";

    public static IServiceCollection AddMaxio(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(MaxioOptions.SectionName);

        var optionsBuilder = services.AddOptions<MaxioOptions>()
            .Bind(section)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Maxio:ApiKey is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ProductFamilyHandle), "Maxio:ProductFamilyHandle is required.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.BaseUrl) || !string.IsNullOrWhiteSpace(o.Subdomain),
                "Either 'Maxio:BaseUrl' or 'Maxio:Subdomain' must be configured.");

        // Fail fast in real deployments where the Maxio section is configured, but keep
        // hosts that do not use the subscription capability (e.g. integration tests)
        // working without a Maxio section.
        if (section.Exists())
        {
            optionsBuilder.ValidateOnStart();
        }

        services.AddHttpClient(HttpClientName, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<MaxioOptions>>().Value;

            client.BaseAddress = new Uri(options.ResolveBaseUrl() + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Maxio Advanced Billing uses Basic auth: the API key as username, "x" as password.
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:x"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        });

        services.AddSingleton<MaxioClient>();

        return services;
    }
}