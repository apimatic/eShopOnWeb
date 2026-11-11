using System;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ThePlaidApi;
using ThePlaidApi.Core.ErrorResponse;
using ThePlaidApi.Core.Exceptions;
using ThePlaidApi.Core.Hooks;
using ThePlaidApi.Servers;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>The "Plaid:" configuration section.</summary>
public sealed class PlaidSettings
{
    public const string SectionName = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }
    public string? Environment { get; set; }
    /// <summary>Optional override. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>Shop-side settings of paying by bank ("BankPayment:" section).</summary>
public sealed class BankPaymentOptions
{
    public const string SectionName = "BankPayment";

    /// <summary>Fee the shopper pays for the express (same business day) speed.</summary>
    public decimal ExpressFee { get; set; } = 5.00m;
}

/// <summary>The app can't carry out a request: answered with a 4xx status and the reason.</summary>
public class BankingException : Exception
{
    public int StatusCode { get; }

    public BankingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

/// <summary>An error response from Plaid.</summary>
public sealed class PlaidApiException : Exception
{
    public int HttpStatus { get; }
    public string ErrorType { get; }
    public string ErrorCode { get; }

    public PlaidApiException(int httpStatus, string errorType, string errorCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        HttpStatus = httpStatus;
        ErrorType = errorType;
        ErrorCode = errorCode;
    }

    /// <summary>The status to answer our own caller with.</summary>
    public int ResponseStatus => HttpStatus switch
    {
        429 => 429,
        >= 400 and < 500 => 422,
        _ => 502
    };
}

public static class PlaidServiceCollectionExtensions
{
    public static IServiceCollection AddPlaidBanking(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlaidSettings>(configuration.GetSection(PlaidSettings.SectionName));
        services.Configure<BankPaymentOptions>(configuration.GetSection(BankPaymentOptions.SectionName));
        services.AddHttpClient();
        services.AddSingleton<PlaidGateway>();
        services.AddScoped<ShopperResolver>();
        services.AddSingleton<BankDirectoryService>();
        services.AddScoped<BankConnectionService>();
        services.AddScoped<BankPaymentService>();
        services.AddScoped<BankActivityService>();
        services.AddScoped<PortfolioService>();
        return services;
    }
}

/// <summary>Owns the SDK client, built from the Plaid settings, and translates its errors.</summary>
public sealed class PlaidGateway
{
    private readonly Lazy<ThePlaidApiClient> _client;

    public PlaidGateway(IOptions<PlaidSettings> settings, IHttpClientFactory httpClientFactory)
    {
        _client = new Lazy<ThePlaidApiClient>(() => Create(settings.Value, httpClientFactory));
    }

    private static ThePlaidApiClient Create(PlaidSettings s, IHttpClientFactory httpClientFactory)
    {
        if (string.IsNullOrWhiteSpace(s.ClientId) || string.IsNullOrWhiteSpace(s.Secret))
            throw new InvalidOperationException("Plaid is not configured: set Plaid:ClientId and Plaid:Secret.");

        // 2020-09-14 is the Plaid API version this SDK was generated for.
        var options = new ThePlaidApiClientOptions { ClientId = s.ClientId, Secret = s.Secret, PlaidVersion = "2020-09-14" };

        options.Hooks = new[] { DateOnlyRequestFields };

        if (!string.IsNullOrWhiteSpace(s.BaseUrl))
        {
            // An explicit base address is used verbatim, whatever the environment.
            options.Server.Default.Production.BaseUrl = s.BaseUrl;
            options.Server.Default.Environment2.BaseUrl = s.BaseUrl;
            options.Environment = ServerEnvironment.Environment2;
        }
        else
        {
            options.Environment = (s.Environment ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "sandbox" => ServerEnvironment.Environment2,
                "production" => ServerEnvironment.Production,
                _ => throw new InvalidOperationException(
                    "Plaid:Environment must be 'sandbox' or 'production' (or set Plaid:BaseUrl).")
            };
        }

        return new ThePlaidApiClient(httpClientFactory.CreateClient(), options);
    }

    /// <summary>
    /// The SDK writes the date-only fields of /transactions/get (start_date, end_date) as full timestamps,
    /// which Plaid refuses: send them as YYYY-MM-DD.
    /// </summary>
    private static readonly SdkHook DateOnlyRequestFields = SdkHook.OnRequest(async (request, context, cancellationToken) =>
    {
        if (request.Content is null || !context.Uri.AbsolutePath.EndsWith("/transactions/get", StringComparison.Ordinal))
            return;
        var body = JsonNode.Parse(await request.Content.ReadAsStringAsync()) as JsonObject;
        if (body is null) return;
        foreach (var field in new[] { "start_date", "end_date" })
        {
            if (body[field] is JsonValue v && v.TryGetValue<string>(out var text) && text.Length > 10)
                body[field] = text.Substring(0, 10);
        }
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
    });

    public async Task<T> CallAsync<T>(Func<ThePlaidApiClient, Task<T>> call)
    {
        try
        {
            return await call(_client.Value);
        }
        catch (ApiException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (SdkException ex) when (ex is not ApiException)
        {
            throw new PlaidApiException(502, "SDK_ERROR", "PLAID_UNREACHABLE", "Plaid could not be reached: " + ex.Message, ex);
        }
    }

    private static PlaidApiException Translate(ApiException<RawError> ex)
    {
        var status = (int)ex.StatusCode;
        string type = "API_ERROR", code = "UNKNOWN", message = "Plaid answered with an error.";
        try
        {
            using var doc = JsonDocument.Parse(ex.Error.ReadAsBytes());
            var root = doc.RootElement;
            string? Get(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            type = Get("error_type") ?? type;
            code = Get("error_code") ?? code;
            message = Get("display_message") ?? Get("error_message") ?? message;
            if (Get("error_message") is { } em && Get("display_message") is null) message = em;
        }
        catch (JsonException)
        {
        }

        return new PlaidApiException(status, type, code, $"Plaid: {message} ({code})", ex);
    }
}

/// <summary>Resolves the caller from the JWT into the canonical Identity user.</summary>
public sealed class ShopperResolver
{
    private readonly UserManager<ApplicationUser> _users;

    public ShopperResolver(UserManager<ApplicationUser> users)
    {
        _users = users;
    }

    public async Task<ApplicationUser> RequireAsync(ClaimsPrincipal principal)
    {
        var name = principal.FindFirst(ClaimTypes.Name)?.Value ?? principal.Identity?.Name;
        if (string.IsNullOrEmpty(name))
            throw new BankingException(401, "The token does not identify a shopper.");
        var user = await _users.FindByNameAsync(name);
        return user ?? throw new BankingException(401, "The shopper in the token does not exist.");
    }

    public async Task<ApplicationUser> FindByEmailAsync(string email)
    {
        var user = await _users.FindByEmailAsync(email) ?? await _users.FindByNameAsync(email);
        return user ?? throw new BankingException(404, $"No shopper with email '{email}'.");
    }
}

public static class Money
{
    public static string ToPlaid(decimal amount) =>
        amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>True when the amount has no more than two decimals.</summary>
    public static bool IsCents(decimal amount) => decimal.Round(amount, 2) == amount;
}
