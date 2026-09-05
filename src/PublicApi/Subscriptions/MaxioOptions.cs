using System;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Settings for the server-to-server Maxio Advanced Billing API connection.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Subdomain { get; init; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>Optional full Maxio API base URL. When empty, it is derived from <see cref="Subdomain"/>.</summary>
    public string? BaseUrl { get; init; }

    internal Uri GetApiBaseUri()
    {
        var address = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com/"
            : BaseUrl;

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new OptionsValidationException(SectionName, typeof(MaxioOptions), new[]
            {
                "Maxio:BaseUrl must be an absolute HTTPS URL."
            });
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }
}

public static class MaxioServiceCollectionExtensions
{
    public static IServiceCollection AddMaxioAdvancedBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MaxioOptions>()
            .Bind(configuration.GetRequiredSection(MaxioOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => Uri.TryCreate(
                    string.IsNullOrWhiteSpace(options.BaseUrl)
                        ? $"https://{options.Subdomain}.chargify.com/"
                        : options.BaseUrl,
                    UriKind.Absolute,
                    out var uri) && uri.Scheme == Uri.UriSchemeHttps, "Maxio:BaseUrl must be an absolute HTTPS URL.")
            .ValidateOnStart();

        services.AddHttpClient<IMaxioAdvancedBillingClient, MaxioAdvancedBillingClient>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<MaxioOptions>>().Value;
            client.BaseAddress = options.GetApiBaseUri();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.ApiKey}:"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        return services;
    }
}
