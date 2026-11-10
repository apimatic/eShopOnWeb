using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThePlaidApi;
using ThePlaidApi.Servers;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>The <c>Plaid:</c> configuration section.</summary>
public class PlaidSettings
{
    public const string CONFIG_NAME = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }

    /// <summary><c>sandbox</c> or <c>production</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API's base address.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>The API version the SDK was generated against.</summary>
internal static class PlaidApi
{
    public const string Version = "2020-09-14";
    public const string HttpClientName = "plaid";
}

public static class PlaidServiceCollectionExtensions
{
    /// <summary>Registers the Plaid SDK client, configured from the <c>Plaid:</c> section.</summary>
    public static IServiceCollection AddPlaid(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaidSettings>(configuration.GetSection(PlaidSettings.CONFIG_NAME));
        services.AddTransient<PlaidDateFormatHandler>();
        services.AddHttpClient(PlaidApi.HttpClientName).AddHttpMessageHandler<PlaidDateFormatHandler>();

        // Built on first use, so an app without Plaid settings still starts; Plaid features then answer 503.
        services.AddSingleton(sp =>
        {
            var settings = configuration.GetSection(PlaidSettings.CONFIG_NAME).Get<PlaidSettings>() ?? new PlaidSettings();
            return CreateClient(settings, sp.GetRequiredService<IHttpClientFactory>().CreateClient(PlaidApi.HttpClientName));
        });
        return services;
    }

    internal static ThePlaidApiClient CreateClient(PlaidSettings settings, HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.Secret))
        {
            throw new RequestFailedException(503, "Plaid is not configured: set Plaid:ClientId and Plaid:Secret.");
        }

        var options = new ThePlaidApiClientOptions
        {
            ClientId = settings.ClientId,
            Secret = settings.Secret,
            PlaidVersion = PlaidApi.Version,
            Environment = ParseEnvironment(settings.Environment),
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            options.Server.Default.Environment2.BaseUrl = settings.BaseUrl;
        }

        return new ThePlaidApiClient(httpClient, options);
    }

    private static ServerEnvironment ParseEnvironment(string? environment)
    {
        // The SDK defaults to production; never end up there unless it was asked for.
        if (string.IsNullOrWhiteSpace(environment) || environment.Trim().Equals("sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Environment2;
        }

        if (environment.Trim().Equals("production", StringComparison.OrdinalIgnoreCase))
        {
            return ServerEnvironment.Production;
        }

        throw new RequestFailedException(503, $"Plaid:Environment must be 'sandbox' or 'production', not '{environment}'.");
    }
}

/// <summary>
/// The SDK serializes <c>start_date</c>/<c>end_date</c> as full timestamps, but Plaid only accepts
/// <c>YYYY-MM-DD</c>. This trims them on the way out.
/// </summary>
public class PlaidDateFormatHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null && request.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var body = await request.Content.ReadAsStringAsync(cancellationToken);
            if (body.Contains("_date\"", StringComparison.Ordinal) && JsonNode.Parse(body) is JsonObject json)
            {
                var changed = false;
                foreach (var key in new[] { "start_date", "end_date" })
                {
                    if (json[key] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 10 && text[10] == 'T')
                    {
                        json[key] = text.Substring(0, 10);
                        changed = true;
                    }
                }

                if (changed)
                {
                    request.Content = new StringContent(json.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
