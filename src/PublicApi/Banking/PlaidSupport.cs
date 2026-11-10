using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ThePlaidApi;
using ThePlaidApi.Core.ErrorResponse;
using ThePlaidApi.Core.Exceptions;
using ThePlaidApi.Core.Hooks;
using ThePlaidApi.Servers;

namespace Microsoft.eShopWeb.PublicApi.Banking;

/// <summary>
/// The app can't carry out a request: answered with <see cref="StatusCode"/> and the message as the reason.
/// </summary>
public class ApiRequestException : Exception
{
    public ApiRequestException(HttpStatusCode statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

/// <summary>A Plaid error body, as returned by the Plaid API.</summary>
public sealed record PlaidErrorInfo(string? ErrorType, string? ErrorCode, string? Message, string? DisplayMessage);

public class PlaidSettings
{
    public const string SectionName = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }
    /// <summary>Optional: the Plaid-Version header (the API version the SDK was generated for by default).</summary>
    public string? Version { get; set; }

    public static PlaidSettings From(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);
        return new PlaidSettings
        {
            ClientId = section["ClientId"],
            Secret = section["Secret"],
            Environment = section["Environment"],
            BaseUrl = section["BaseUrl"],
            Version = section["Version"]
        };
    }
}

/// <summary>Creates the Plaid SDK client from the <c>Plaid:</c> configuration section.</summary>
public class PlaidClientProvider
{
    private const string DefaultPlaidVersion = "2020-09-14";

    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly object _lock = new();
    private ThePlaidApiClient? _client;

    public PlaidClientProvider(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
    }

    public ThePlaidApiClient Client
    {
        get
        {
            if (_client is not null)
            {
                return _client;
            }

            lock (_lock)
            {
                return _client ??= Create();
            }
        }
    }

    /// <summary>
    /// Plaid wants <c>start_date</c> and <c>end_date</c> as YYYY-MM-DD, but the SDK writes its DateTimeOffset
    /// properties as full date-times (its date-only converter is not applied to them). Cut them down to the date.
    /// </summary>
    private static SdkHook DateOnlyFieldsHook() => SdkHook.OnRequest(async (request, _, _) =>
    {
        if (request.Content is null)
        {
            return;
        }

        var body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!body.Contains("start_date") && !body.Contains("end_date"))
        {
            return;
        }

        var node = JsonNode.Parse(body) as JsonObject;
        if (node is null)
        {
            return;
        }

        var changed = false;
        foreach (var name in new[] { "start_date", "end_date" })
        {
            if (node[name] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 10 && text[10] == 'T')
            {
                node[name] = text.Substring(0, 10);
                changed = true;
            }
        }

        if (changed)
        {
            request.Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json");
        }
    });

    private ThePlaidApiClient Create()
    {
        var settings = PlaidSettings.From(_configuration);
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.Secret))
        {
            throw new ApiRequestException(HttpStatusCode.ServiceUnavailable,
                "Pay by bank is not available: Plaid:ClientId and Plaid:Secret are not configured.");
        }

        var options = new ThePlaidApiClientOptions
        {
            Hooks = new[] { DateOnlyFieldsHook() },
            ClientId = settings.ClientId,
            Secret = settings.Secret,
            PlaidVersion = string.IsNullOrWhiteSpace(settings.Version) ? DefaultPlaidVersion : settings.Version
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // The override is used verbatim as the API's base address.
            options.Server.Default.Production.BaseUrl = settings.BaseUrl!;
            options.Server.Default.Environment2.BaseUrl = settings.BaseUrl!;
        }
        else
        {
            options.Environment = (settings.Environment ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "sandbox" => ServerEnvironment.Environment2,
                "production" => ServerEnvironment.Production,
                _ => throw new ApiRequestException(HttpStatusCode.ServiceUnavailable,
                    "Pay by bank is not available: Plaid:Environment must be 'sandbox' or 'production'.")
            };
        }

        return new ThePlaidApiClient(_httpClientFactory.CreateClient(), options);
    }
}

/// <summary>Runs Plaid calls and turns Plaid failures into <see cref="ApiRequestException"/>s.</summary>
public class PlaidApi
{
    private readonly PlaidClientProvider _provider;

    public PlaidApi(PlaidClientProvider provider)
    {
        _provider = provider;
    }

    public async Task<T> CallAsync<T>(Func<ThePlaidApiClient, Task<T>> call)
    {
        try
        {
            return await call(_provider.Client);
        }
        catch (ApiRequestException)
        {
            throw;
        }
        catch (ApiException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new ApiRequestException(HttpStatusCode.GatewayTimeout, "Plaid did not answer in time.", ex);
        }
        catch (SdkException ex)
        {
            throw new ApiRequestException(HttpStatusCode.BadGateway, "Plaid could not be reached: " + ex.Message, ex);
        }
    }

    public static PlaidErrorInfo? ReadError(ApiException<RawError> ex)
    {
        try
        {
            using var doc = JsonDocument.Parse(ex.Error.ReadAsBytes());
            string? Get(string name) =>
                doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetString()
                    : null;
            return new PlaidErrorInfo(Get("error_type"), Get("error_code"), Get("error_message"), Get("display_message"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ApiRequestException Translate(ApiException<RawError> ex)
    {
        var info = ReadError(ex);
        var reason = info?.DisplayMessage ?? info?.Message ?? "Plaid rejected the request.";
        var code = info?.ErrorCode;
        var text = code is null ? reason : $"{reason} ({code})";
        var status = (int)ex.StatusCode;

        if (code is "ITEM_LOGIN_REQUIRED" or "PRODUCT_NOT_READY" or "ITEM_NOT_FOUND" or "ADDITIONAL_CONSENT_REQUIRED"
            or "NO_ACCOUNTS" or "INVALID_PRODUCT" or "PRODUCTS_NOT_SUPPORTED" or "ACCESS_NOT_GRANTED"
            or "NO_INVESTMENT_ACCOUNTS")
        {
            return new ApiRequestException(HttpStatusCode.Conflict, text, ex);
        }

        if (status == 429 || info?.ErrorType == "RATE_LIMIT_EXCEEDED")
        {
            return new ApiRequestException((HttpStatusCode)429, "Plaid is rate limiting requests, try again shortly. " + text, ex);
        }

        var configurationProblem = code is "INVALID_API_KEYS" or "INVALID_CLIENT_ID" or "INVALID_SECRET"
            or "UNAUTHORIZED_ENVIRONMENT" or "INVALID_ACCESS_TOKEN";
        if (status == 401 || status == 403 || status >= 500 || info?.ErrorType == "API_ERROR" || configurationProblem)
        {
            return new ApiRequestException(HttpStatusCode.BadGateway, "Plaid failed the request: " + text, ex);
        }

        return new ApiRequestException(HttpStatusCode.UnprocessableEntity, text, ex);
    }

    public static string? CodeOf(Exception ex) =>
        ex is ApiRequestException { InnerException: ApiException<RawError> inner } ? ReadError(inner)?.ErrorCode : null;
}
