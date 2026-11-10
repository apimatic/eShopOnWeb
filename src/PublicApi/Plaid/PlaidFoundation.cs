using System;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThePlaidApi;
using ThePlaidApi.Core.Exceptions;
using ThePlaidApi.Core.ErrorResponse;
using ThePlaidApi.Core.Hooks;
using ThePlaidApi.Servers;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>
/// The <c>Plaid:</c> configuration section.
/// </summary>
public class PlaidSettings
{
    public const string SectionName = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional. When set it is used verbatim as the Plaid API's base address.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// Bank payment settings that belong to the shop rather than to Plaid (<c>BankPayments:</c> section).
/// </summary>
public class BankPaymentSettings
{
    public const string SectionName = "BankPayments";

    /// <summary>The flat fee a shopper pays for express (same business day) delivery of the money.</summary>
    public decimal ExpressFee { get; set; } = 5.00m;
}

/// <summary>
/// An error the API reports to its caller: a status code and the reason the request can't be carried out.
/// </summary>
public class ApiProblemException : Exception
{
    public ApiProblemException(int statusCode, string reason, Exception? inner = null) : base(reason, inner)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }

    public static ApiProblemException BadRequest(string reason) => new(400, reason);
    public static ApiProblemException NotFound(string reason) => new(404, reason);
    public static ApiProblemException Conflict(string reason) => new(409, reason);
    public static ApiProblemException Unprocessable(string reason) => new(422, reason);
}

/// <summary>
/// An error returned by Plaid itself, translated into something the API can report.
/// </summary>
public class PlaidApiException : ApiProblemException
{
    public PlaidApiException(int statusCode, string reason, string? errorCode, Exception inner)
        : base(statusCode, reason, inner)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Plaid's <c>error_code</c>, e.g. <c>ITEM_NOT_FOUND</c>.</summary>
    public string? ErrorCode { get; }
}

public static class PlaidCall
{
    /// <summary>
    /// Runs a call to Plaid and translates every failure into an <see cref="ApiProblemException"/>.
    /// </summary>
    public static async Task<T> RunAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (ApiException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new ApiProblemException(503, "Plaid credentials are not configured: " + ex.Message, ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new ApiProblemException(504, "Plaid did not answer in time.", ex);
        }
        catch (SdkException ex)
        {
            throw new ApiProblemException(502, "Plaid could not be reached or answered unexpectedly: " + ex.Message, ex);
        }
    }

    public static async Task RunAsync(Func<Task> call)
    {
        await RunAsync(async () =>
        {
            await call().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
    }

    private static PlaidApiException Translate(ApiException<RawError> ex)
    {
        string? code = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(ex.Error.ReadAsBytes());
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("error_code", out var c) && c.ValueKind == JsonValueKind.String)
                    code = c.GetString();
                if (doc.RootElement.TryGetProperty("display_message", out var d) && d.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(d.GetString()))
                    message = d.GetString();
                else if (doc.RootElement.TryGetProperty("error_message", out var m) && m.ValueKind == JsonValueKind.String)
                    message = m.GetString();
            }
        }
        catch (JsonException)
        {
            // keep the generic reason
        }

        var http = (int)ex.StatusCode;
        var status = http switch
        {
            429 => 429,
            >= 500 => 502,
            401 or 403 => 502, // our own Plaid credentials are wrong - not the shopper's fault
            _ => 422
        };
        var reason = message ?? $"Plaid answered with status {http}.";
        if (code != null)
            reason += $" ({code})";
        return new PlaidApiException(status, reason, code, ex);
    }

    public static bool IsCode(Exception ex, string errorCode) =>
        ex is PlaidApiException p && string.Equals(p.ErrorCode, errorCode, StringComparison.Ordinal);
}

public static class CurrentUser
{
    /// <summary>The shopper's name as it is in the JWT. Identity always comes from the token.</summary>
    public static string Name(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new ApiProblemException(401, "The token does not identify a shopper.");
        return name;
    }

    public static string Key(string userName) => userName.Trim().ToUpperInvariant();
}

/// <summary>
/// Protects Plaid access tokens at rest.
/// </summary>
public class AccessTokenProtector
{
    private readonly IDataProtector _protector;

    public AccessTokenProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("Plaid.AccessToken.v1");
    }

    public string Protect(string accessToken) => _protector.Protect(accessToken);

    public string Unprotect(string protectedToken)
    {
        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new ApiProblemException(409,
                "The saved bank connection can no longer be read; please connect the bank again.", ex);
        }
    }
}

public static class PlaidServiceCollectionExtensions
{
    // The version of the Plaid API the SDK is generated for. The SDK insists on it as a header.
    private const string PlaidApiVersion = "2020-09-14";

    public static IServiceCollection AddPlaidPayByBank(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaidSettings>(configuration.GetSection(PlaidSettings.SectionName));
        services.Configure<BankPaymentSettings>(configuration.GetSection(BankPaymentSettings.SectionName));
        services.AddDataProtection();
        services.AddSingleton<AccessTokenProtector>();

        services.AddHttpClient();
        services.AddSingleton(sp => CreateClient(sp, configuration));

        services.AddSingleton<InstitutionService>();
        services.AddSingleton<PerKeyLock>();
        services.AddScoped<BankConnectionService>();
        services.AddScoped<BankPaymentService>();
        services.AddScoped<BankActivityService>();
        services.AddScoped<PortfolioService>();
        return services;
    }

    private static ThePlaidApiClient CreateClient(IServiceProvider sp, IConfiguration configuration)
    {
        var settings = configuration.GetSection(PlaidSettings.SectionName).Get<PlaidSettings>() ?? new PlaidSettings();
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.Secret))
            throw new ApiProblemException(503, "Plaid is not configured: set Plaid:ClientId and Plaid:Secret.");

        var options = new ThePlaidApiClientOptions
        {
            ClientId = settings.ClientId,
            Secret = settings.Secret,
            PlaidVersion = PlaidApiVersion,
            Hooks = [DateOnlyRequestDates()]
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // The override is used verbatim, whatever the environment says.
            options.Server.Default.Production.BaseUrl = settings.BaseUrl;
            options.Server.Default.Environment2.BaseUrl = settings.BaseUrl;
            options.Environment = ServerEnvironment.Environment2;
        }
        else
        {
            options.Environment = settings.Environment?.Trim().ToLowerInvariant() switch
            {
                "sandbox" => ServerEnvironment.Environment2,
                "production" => ServerEnvironment.Production,
                _ => throw new ApiProblemException(503,
                    "Plaid is not configured: Plaid:Environment must be 'sandbox' or 'production' (or set Plaid:BaseUrl).")
            };
        }

        var httpClient = sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient("plaid");
        return new ThePlaidApiClient(httpClient, options);
    }

    /// <summary>
    /// The SDK writes the <c>start_date</c>/<c>end_date</c> of <c>/transactions/get</c> as full timestamps, which
    /// Plaid rejects: it wants plain <c>YYYY-MM-DD</c> dates. This hook rewrites them before the request is sent.
    /// </summary>
    private static SdkHook DateOnlyRequestDates() =>
        SdkHook.OnRequest(async (request, context, cancellationToken) =>
        {
            if (request.Content == null || !context.Uri.AbsolutePath.EndsWith("/transactions/get", StringComparison.Ordinal))
                return;

#if NET5_0_OR_GREATER
            var body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
            var body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
            if (JsonNode.Parse(body) is not JsonObject json)
                return;

            foreach (var field in new[] { "start_date", "end_date" })
            {
                if (json[field] is JsonValue v && v.TryGetValue<string>(out var text) && text.Length > 10)
                    json[field] = text.Substring(0, 10);
            }

            request.Content = new System.Net.Http.StringContent(json.ToJsonString(), Encoding.UTF8, "application/json");
        });
}
