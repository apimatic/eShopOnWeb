using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Banking;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThePlaidApi;
using ThePlaidApi.Core.ErrorResponse;
using ThePlaidApi.Core.Exceptions;
using ThePlaidApi.Servers;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>
/// A failure the app reports to the caller: a status code (normally 4xx) and a reason.
/// </summary>
public class BankingException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public BankingException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static BankingException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static BankingException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    public static BankingException Conflict(string message) => new(HttpStatusCode.Conflict, message);
    public static BankingException Unprocessable(string message) => new((HttpStatusCode)422, message);
}

/// <summary>Plaid settings, read from the "Plaid:" configuration section.</summary>
public class PlaidSettings
{
    public const string SectionName = "Plaid";
    /// <summary>Version of the Plaid API the SDK is generated for (sent as the Plaid-Version header).</summary>
    public const string ApiVersion = "2020-09-14";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }

    public static PlaidSettings From(IConfiguration configuration) => new()
    {
        ClientId = configuration[$"{SectionName}:ClientId"],
        Secret = configuration[$"{SectionName}:Secret"],
        Environment = configuration[$"{SectionName}:Environment"],
        BaseUrl = configuration[$"{SectionName}:BaseUrl"],
    };
}

public static class PlaidClientFactory
{
    public static ThePlaidApiClient Create(PlaidSettings settings, HttpClient httpClient)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.Secret))
        {
            throw new BankingException(HttpStatusCode.ServiceUnavailable,
                "Pay by bank is unavailable: the Plaid:ClientId and Plaid:Secret settings are not configured.");
        }

        var options = new ThePlaidApiClientOptions
        {
            ClientId = settings.ClientId,
            Secret = settings.Secret,
            PlaidVersion = PlaidSettings.ApiVersion,
        };

        var hasBaseUrl = !string.IsNullOrWhiteSpace(settings.BaseUrl);
        if (hasBaseUrl)
        {
            // An explicit base address is used verbatim, whatever the environment says.
            options.Server.Default.Production.BaseUrl = settings.BaseUrl!;
            options.Server.Default.Environment2.BaseUrl = settings.BaseUrl!;
        }

        var environment = settings.Environment?.Trim();
        if (string.IsNullOrEmpty(environment))
        {
            if (!hasBaseUrl)
            {
                throw new BankingException(HttpStatusCode.ServiceUnavailable,
                    "Pay by bank is unavailable: the Plaid:Environment setting is not configured.");
            }
            options.Environment = ServerEnvironment.Environment2;
        }
        else if (string.Equals(environment, "sandbox", StringComparison.OrdinalIgnoreCase))
        {
            options.Environment = ServerEnvironment.Environment2;
        }
        else if (string.Equals(environment, "production", StringComparison.OrdinalIgnoreCase))
        {
            options.Environment = ServerEnvironment.Production;
        }
        else if (hasBaseUrl)
        {
            options.Environment = ServerEnvironment.Environment2;
        }
        else
        {
            throw new BankingException(HttpStatusCode.ServiceUnavailable,
                $"Pay by bank is unavailable: unsupported Plaid:Environment '{environment}' (use sandbox or production).");
        }

        return new ThePlaidApiClient(httpClient, options);
    }
}

public static class PlaidErrorTranslator
{
    private sealed record PlaidError(string? ErrorType, string? ErrorCode, string? ErrorMessage, string? DisplayMessage);

    public static PlaidErrorInfo Parse(RawError raw)
    {
        PlaidError? parsed = null;
        try
        {
            using var doc = JsonDocument.Parse(raw.ReadAsBytes());
            var root = doc.RootElement;
            string? Get(string name) =>
                root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() : null;
            parsed = new PlaidError(Get("error_type"), Get("error_code"), Get("error_message"), Get("display_message"));
        }
        catch (JsonException)
        {
        }

        return new PlaidErrorInfo(raw.StatusCode, parsed?.ErrorType, parsed?.ErrorCode,
            parsed?.DisplayMessage ?? parsed?.ErrorMessage ?? "Plaid rejected the request.");
    }

    public static PlaidErrorInfo Parse(ApiException<RawError> exception) => Parse(exception.Error);

    /// <summary>Maps an error returned by Plaid to the status and reason the caller gets.</summary>
    public static BankingException Translate(ApiException<RawError> exception)
    {
        var info = Parse(exception);
        var reason = $"Plaid: {info.Message}" + (info.ErrorCode is null ? "" : $" ({info.ErrorCode})");

        if ((int)info.StatusCode == 429 || info.ErrorType == "RATE_LIMIT_EXCEEDED")
            return new BankingException((HttpStatusCode)429, reason);

        switch (info.ErrorType)
        {
            case "INVALID_REQUEST":
            case "INVALID_INPUT":
                // Credential / account problems on our side are not the caller's fault.
                if (info.ErrorCode is "INVALID_API_KEYS" or "UNAUTHORIZED_ENVIRONMENT" or "INVALID_PRODUCT"
                    or "UNAUTHORIZED_PRODUCT" or "PRODUCTS_NOT_ENABLED")
                    return new BankingException(HttpStatusCode.BadGateway, reason);
                return new BankingException(HttpStatusCode.BadRequest, reason);
            case "ITEM_ERROR":
                return new BankingException(HttpStatusCode.Conflict, reason);
            case "TRANSFER_ERROR":
            case "TRANSFER_REFUND_ERROR":
                return new BankingException((HttpStatusCode)422, reason);
            case "API_ERROR":
            case "INSTITUTION_ERROR":
            case "ASSET_REPORT_ERROR":
                return new BankingException(HttpStatusCode.BadGateway, reason);
        }

        return (int)info.StatusCode is >= 400 and < 500
            ? new BankingException(HttpStatusCode.BadRequest, reason)
            : new BankingException(HttpStatusCode.BadGateway, reason);
    }
}

public record PlaidErrorInfo(HttpStatusCode StatusCode, string? ErrorType, string? ErrorCode, string Message);

public static class PlaidCalls
{
    /// <summary>Runs a Plaid call, translating Plaid errors into <see cref="BankingException"/>s.</summary>
    public static async Task<T> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiException<RawError> ex)
        {
            throw PlaidErrorTranslator.Translate(ex);
        }
        catch (HttpRequestException ex)
        {
            throw new BankingException(HttpStatusCode.BadGateway, $"Plaid could not be reached: {ex.Message}");
        }
    }

    public static bool IsPlaidError(Exception ex, string errorCode) =>
        ex is ApiException<RawError> api && PlaidErrorTranslator.Parse(api).ErrorCode == errorCode;

    /// <summary>
    /// Plaid prepares a product (transactions, investments) in the background after linking; until then it
    /// answers PRODUCT_NOT_READY. Retries for a bounded time.
    /// </summary>
    public static async Task<T> WhenProductReady<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        const int attempts = 12;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (ApiException<RawError> ex) when (PlaidErrorTranslator.Parse(ex).ErrorCode == "PRODUCT_NOT_READY")
            {
                if (attempt >= attempts)
                    throw PlaidErrorTranslator.Translate(ex);
                await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
            }
            catch (ApiException<RawError> ex)
            {
                throw PlaidErrorTranslator.Translate(ex);
            }
            catch (HttpRequestException ex)
            {
                throw new BankingException(HttpStatusCode.BadGateway, $"Plaid could not be reached: {ex.Message}");
            }
        }
    }
}

/// <summary>Serialises work per key (one process).</summary>
public class KeyedLock
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}

public static class UserExtensions
{
    /// <summary>The shopper's identity from the JWT: the e-mail address, normalised.</summary>
    public static string ShopperId(this ClaimsPrincipal user)
    {
        var name = user.Identity?.Name
                   ?? user.FindFirstValue(ClaimTypes.Name)
                   ?? user.FindFirstValue(ClaimTypes.Email)
                   ?? user.FindFirstValue("name");
        if (string.IsNullOrWhiteSpace(name))
            throw new BankingException(HttpStatusCode.Unauthorized, "The token does not identify a shopper.");
        return NormalizeShopper(name);
    }

    public static string NormalizeShopper(string email) => email.Trim().ToLowerInvariant();
}

public static class Money
{
    public const int MaxDecimals = 2;

    public static string ToPlaid(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    public static decimal FromPlaid(string amount) => decimal.Parse(amount, NumberStyles.Number, CultureInfo.InvariantCulture);

    public static bool HasCentsOnly(decimal amount) => decimal.Round(amount, MaxDecimals) == amount;
}

public static class BankingServiceCollectionExtensions
{
    public static IServiceCollection AddBanking(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddDataProtection();
        services.AddMemoryCache();
        services.AddSingleton(sp => PlaidSettings.From(sp.GetRequiredService<IConfiguration>()));
        services.AddSingleton(sp => PlaidClientFactory.Create(
            sp.GetRequiredService<PlaidSettings>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient()));
        services.AddSingleton<KeyedLock>();
        services.AddSingleton<InstitutionService>();
        services.AddScoped<ConnectionService>();
        services.AddScoped<CheckoutService>();
        services.AddScoped<OrderCancellationService>();
        services.AddScoped<BankActivityService>();
        services.AddScoped<PortfolioService>();
        return services;
    }
}
