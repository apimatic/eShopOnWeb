using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThePlaidApi;
using ThePlaidApi.Core.Configuration;
using ThePlaidApi.Servers;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>
/// The <c>Plaid:</c> configuration section.
/// </summary>
public class PlaidSettings
{
    public const string SECTION = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional override. When set it is used verbatim as the API's base address.</summary>
    public string? BaseUrl { get; set; }

    public static PlaidSettings From(IConfiguration configuration)
    {
        var settings = configuration.GetSection(SECTION).Get<PlaidSettings>() ?? new PlaidSettings();

        // Fall back to the sandbox credential environment variables when the section doesn't carry them.
        settings.ClientId = FirstNonEmpty(settings.ClientId, System.Environment.GetEnvironmentVariable("PLAID_CLIENT_ID"));
        settings.Secret = FirstNonEmpty(settings.Secret, System.Environment.GetEnvironmentVariable("PLAID_SECRET"));
        settings.Environment = FirstNonEmpty(settings.Environment, System.Environment.GetEnvironmentVariable("PLAID_ENV"));
        settings.BaseUrl = FirstNonEmpty(settings.BaseUrl);
        return settings;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value!.Trim();
        }
        return null;
    }
}

public static class PlaidServiceCollectionExtensions
{
    /// <summary>The Plaid API version the SDK is generated against.</summary>
    private const string PLAID_VERSION = "2020-09-14";

    public static IServiceCollection AddPlaidBanking(this IServiceCollection services)
    {
        services.AddDataProtection();
        services.AddSingleton<ThePlaidApiClient>(sp => CreateClient(PlaidSettings.From(sp.GetRequiredService<IConfiguration>())));
        services.AddSingleton<ConnectionLocks>();
        services.AddSingleton<AccessTokenProtector>();
        services.AddSingleton<InstitutionDirectory>();
        services.AddScoped<BankConnectionService>();
        services.AddScoped<BankPaymentService>();
        services.AddScoped<BankActivityService>();
        services.AddScoped<PortfolioService>();
        return services;
    }

    public static ThePlaidApiClient CreateClient(PlaidSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.Secret))
        {
            throw new BankingException(503, "Plaid is not configured: set Plaid:ClientId and Plaid:Secret.");
        }

        var options = new ThePlaidApiClientOptions
        {
            ClientId = settings.ClientId,
            Secret = settings.Secret,
            PlaidVersion = PLAID_VERSION,
        };

        switch (settings.Environment?.ToLowerInvariant())
        {
            case "sandbox":
                options.Environment = ServerEnvironment.Environment2;
                break;
            case "production":
                options.Environment = ServerEnvironment.Production;
                break;
            default:
                if (string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    throw new BankingException(503,
                        $"Plaid is not configured: Plaid:Environment must be 'sandbox' or 'production' (was '{settings.Environment}').");
                }
                options.Environment = ServerEnvironment.Environment2;
                break;
        }

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            options.Server.Default.Environment2.BaseUrl = settings.BaseUrl;
        }

        // Plaid calls are POSTs; the SDK sends one idempotency key per call and reuses it across retries,
        // so retrying them on 429/5xx is safe.
        var retry = RetryOptions.Default();
        options.Retry = retry with
        {
            HttpMethodsToRetry = new List<HttpMethod>(retry.HttpMethodsToRetry) { HttpMethod.Post },
        };

        var http = new HttpClient(new PlaidDateFormatHandler(new SocketsHttpHandler()))
        {
            Timeout = TimeSpan.FromMinutes(2),
        };
        return new ThePlaidApiClient(http, options);
    }
}

/// <summary>
/// The SDK models Plaid's date-only request fields (<c>start_date</c>, <c>end_date</c>) as
/// <see cref="DateTimeOffset"/> and serializes them as full ISO date-times, which Plaid rejects
/// ("must be a string of the format 'YYYY-MM-DD'"). This handler trims those two fields to their date part.
/// </summary>
public sealed class PlaidDateFormatHandler : DelegatingHandler
{
    private static readonly Regex IsoDateTime = new(@"^(\d{4}-\d{2}-\d{2})T", RegexOptions.Compiled);

    public PlaidDateFormatHandler(HttpMessageHandler inner) : base(inner)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content is not null && request.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (body.Contains("\"start_date\"") || body.Contains("\"end_date\""))
            {
                try
                {
                    if (JsonNode.Parse(body) is JsonObject json)
                    {
                        var changed = false;
                        foreach (var name in new[] { "start_date", "end_date" })
                        {
                            if (json[name] is JsonValue value && value.TryGetValue<string>(out var text))
                            {
                                var match = IsoDateTime.Match(text);
                                if (match.Success)
                                {
                                    json[name] = match.Groups[1].Value;
                                    changed = true;
                                }
                            }
                        }
                        if (changed)
                        {
                            request.Content = new StringContent(json.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
                        }
                    }
                }
                catch (JsonException)
                {
                    // Not our shape; send it untouched.
                }
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
