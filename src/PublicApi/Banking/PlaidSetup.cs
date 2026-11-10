using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThePlaidApi;
using ThePlaidApi.Core.Hooks;
using ThePlaidApi.Servers;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>Bound from the <c>Plaid:</c> configuration section.</summary>
public class PlaidSettings
{
    public const string SectionName = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional override. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>Settings of the pay-by-bank checkout itself (not Plaid credentials).</summary>
public class BankPaymentSettings
{
    public const string SectionName = "BankPayment";

    /// <summary>What the shopper pays on top of the order for the express (same business day) speed.</summary>
    public decimal ExpressFee { get; set; } = 5.00m;
}

public static class PlaidSetup
{
    // Plaid API version header the SDK sends; it is part of the API contract, not a setting.
    private const string PlaidApiVersion = "2020-09-14";

    public static IServiceCollection AddPlaidBanking(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaidSettings>(configuration.GetSection(PlaidSettings.SectionName));
        services.Configure<BankPaymentSettings>(configuration.GetSection(BankPaymentSettings.SectionName));

        // The client is built lazily on first use, so the app starts even when Plaid is not configured.
        services.AddThePlaidApiClient(options =>
        {
            var settings = configuration.GetSection(PlaidSettings.SectionName).Get<PlaidSettings>() ?? new PlaidSettings();
            Apply(settings, options);
        });

        services.AddMemoryCache();
        services.AddSingleton<BankDirectory>();
        services.AddScoped<BankConnectionService>();
        services.AddScoped<BankTransactionSync>();
        services.AddScoped<BankPaymentService>();
        services.AddScoped<BankReportingService>();
        return services;
    }

    internal static void Apply(PlaidSettings settings, ThePlaidApiClientOptions options)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.Secret))
            throw new InvalidOperationException(
                "Plaid is not configured: set Plaid:ClientId and Plaid:Secret (for example with dotnet user-secrets).");

        options.ClientId = settings.ClientId;
        options.Secret = settings.Secret;
        options.PlaidVersion = PlaidApiVersion;

        var environment = (settings.Environment ?? "sandbox").Trim().ToLowerInvariant();
        options.Environment = environment switch
        {
            "sandbox" => ServerEnvironment.Environment2,
            "production" => ServerEnvironment.Production,
            _ when !string.IsNullOrWhiteSpace(settings.BaseUrl) => ServerEnvironment.Environment2,
            _ => throw new InvalidOperationException(
                $"Unsupported Plaid:Environment '{settings.Environment}'. Use 'sandbox' or 'production', or set Plaid:BaseUrl.")
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Used verbatim, whatever the environment says.
            options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            options.Server.Default.Environment2.BaseUrl = settings.BaseUrl;
        }

        options.Hooks = [new DateOnlyRequestHook()];
    }
}

/// <summary>
/// The SDK serialises the <c>start_date</c>/<c>end_date</c> of /transactions/get as full date-times, but Plaid
/// only accepts <c>YYYY-MM-DD</c>. This hook trims them on the wire.
/// </summary>
internal sealed class DateOnlyRequestHook : SdkHook
{
    private static readonly string[] DateFields = ["start_date", "end_date"];

    public override async ValueTask BeforeRequest(HttpRequestMessage request, HookContext context,
        CancellationToken cancellationToken)
    {
        var path = context.Uri.AbsolutePath;
        if (request.Content is null || !(path.EndsWith("/transactions/get", StringComparison.Ordinal)
                                        || path.EndsWith("/investments/transactions/get", StringComparison.Ordinal)))
            return;

        var body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (JsonNode.Parse(body) is not JsonObject json)
            return;

        var changed = false;
        foreach (var field in DateFields)
        {
            if (json[field] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 10
                && DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.AssumeUniversal
                                                       | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                json[field] = parsed.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                changed = true;
            }
        }

        if (!changed)
            return;

        request.Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json");
    }
}
