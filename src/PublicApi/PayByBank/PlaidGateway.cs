using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;
using ThePlaidApi;
using ThePlaidApi.Core.Configuration;
using ThePlaidApi.Core.ErrorResponse;
using ThePlaidApi.Core.Exceptions;
using ThePlaidApi.Core.Hooks;
using ThePlaidApi.Servers;

namespace Microsoft.eShopWeb.PublicApi.PayByBank;

/// <summary>
/// Single entry point to the Plaid SDK: builds the client from the <c>Plaid:</c> settings and
/// translates every SDK failure into a <see cref="PayByBankException"/> with a 4xx/5xx status and a reason.
/// </summary>
public sealed class PlaidGateway
{
    /// <summary>The Plaid API version the SDK was generated for.</summary>
    private const string PlaidApiVersion = "2020-09-14";

    private readonly IOptions<PlaidOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private ThePlaidApiClient? _client;

    public PlaidGateway(IOptions<PlaidOptions> options, IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
    }

    private ThePlaidApiClient Client
    {
        get
        {
            if (_client != null) return _client;
            lock (_gate)
            {
                return _client ??= Build(_options.Value);
            }
        }
    }

    private ThePlaidApiClient Build(PlaidOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.ClientId) || string.IsNullOrWhiteSpace(o.Secret))
            throw new PayByBankException(HttpStatusCode.ServiceUnavailable, "Pay by bank is not configured: Plaid:ClientId and Plaid:Secret are required.");

        var baseUrl = string.IsNullOrWhiteSpace(o.BaseUrl) ? null : o.BaseUrl!.Trim();
        ServerEnvironment environment;
        var envName = o.Environment?.Trim().ToLowerInvariant();
        if (envName == "sandbox") environment = ServerEnvironment.Environment2;
        else if (envName == "production") environment = ServerEnvironment.Production;
        else if (baseUrl != null) environment = ServerEnvironment.Environment2; // the override decides where we call
        else
            throw new PayByBankException(HttpStatusCode.ServiceUnavailable,
                "Pay by bank is not configured: Plaid:Environment must be 'sandbox' or 'production' (or Plaid:BaseUrl must be set).");

        var clientOptions = new ThePlaidApiClientOptions
        {
            Environment = environment,
            ClientId = o.ClientId,
            Secret = o.Secret,
            PlaidVersion = PlaidApiVersion,
            TimeProvider = _timeProvider,
            Hooks = [DateOnlyFieldsHook],
            // Plaid's API is POST-only; the default policy would never retry a rate-limited call.
            Retry = RetryOptions.Default() with
            {
                HttpMethodsToRetry = [HttpMethod.Get, HttpMethod.Head, HttpMethod.Put, HttpMethod.Options, HttpMethod.Post],
                StatusCodesToRetry =
                [
                    HttpStatusCode.RequestTimeout,
                    (HttpStatusCode)429,
                    HttpStatusCode.BadGateway,
                    HttpStatusCode.ServiceUnavailable,
                    HttpStatusCode.GatewayTimeout
                ],
            }
        };
        if (baseUrl != null)
        {
            clientOptions.Server.Default.Production.BaseUrl = baseUrl;
            clientOptions.Server.Default.Environment2.BaseUrl = baseUrl;
        }

        return new ThePlaidApiClient(_httpClientFactory.CreateClient(), clientOptions);
    }

    private static readonly Regex TimestampedDate =
        new(@"(""(?:start_date|end_date)""\s*:\s*"")(\d{4}-\d{2}-\d{2})T[^""]*""", RegexOptions.Compiled);

    /// <summary>
    /// The SDK writes its date-typed request fields (e.g. /transactions/get start_date and end_date) as full
    /// timestamps, but Plaid only accepts <c>YYYY-MM-DD</c>. Trim them on the way out.
    /// </summary>
    private static readonly SdkHook DateOnlyFieldsHook = SdkHook.OnRequest(async (request, _, _) =>
    {
        if (request.Content is null) return;
        var body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
        var fixedBody = TimestampedDate.Replace(body, "$1$2\"");
        if (fixedBody != body)
            request.Content = new StringContent(fixedBody, Encoding.UTF8, "application/json");
    });

    public async Task<T> Call<T>(Func<ThePlaidApiClient, Task<T>> action)
    {
        var client = Client;
        try
        {
            return await action(client).ConfigureAwait(false);
        }
        catch (ApiException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new PayByBankException(HttpStatusCode.GatewayTimeout, "Plaid did not answer in time; try again shortly.", ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new PayByBankException(HttpStatusCode.BadGateway, "Plaid could not be reached; try again shortly.", ex);
        }
        catch (SdkException ex)
        {
            throw new PayByBankException(HttpStatusCode.BadGateway, "Plaid returned an unexpected answer: " + ex.Message, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PayByBankException(HttpStatusCode.BadGateway, "Plaid could not be reached; try again shortly.", ex);
        }
    }

    private sealed class PlaidErrorBody
    {
        [JsonPropertyName("error_type")] public string? ErrorType { get; set; }
        [JsonPropertyName("error_code")] public string? ErrorCode { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
        [JsonPropertyName("display_message")] public string? DisplayMessage { get; set; }
    }

    private static PlaidApiException Translate(ApiException<RawError> ex)
    {
        PlaidErrorBody? body = null;
        try { body = ex.Error.ReadAsJson<PlaidErrorBody>(); } catch { /* not JSON */ }

        var code = body?.ErrorCode;
        var type = body?.ErrorType;
        var detail = body?.DisplayMessage ?? body?.ErrorMessage ?? ex.Message;
        var http = (int)ex.StatusCode;

        HttpStatusCode status;
        string reason;
        if (http == 429 || type == "RATE_LIMIT_EXCEEDED" || code == "RATE_LIMIT_EXCEEDED")
        {
            status = (HttpStatusCode)429;
            reason = "Plaid is rate limiting requests; try again shortly.";
        }
        else if (http >= 500)
        {
            status = HttpStatusCode.BadGateway;
            reason = "Plaid had a problem handling the request; try again shortly.";
        }
        else if (http == 401 || http == 403 || code is "INVALID_API_KEYS" or "INVALID_CLIENT_ID" or "INVALID_SECRET" or "UNAUTHORIZED_ENVIRONMENT")
        {
            status = HttpStatusCode.BadGateway;
            reason = "Plaid rejected the shop's credentials.";
        }
        else
        {
            switch (code)
            {
                case "ITEM_LOGIN_REQUIRED":
                    status = HttpStatusCode.Conflict;
                    reason = "The bank requires the shopper to sign in again; reconnect the bank.";
                    break;
                case "PRODUCT_NOT_READY":
                    status = HttpStatusCode.Conflict;
                    reason = "The bank's data is not ready yet; try again shortly.";
                    break;
                case "ITEM_NOT_FOUND":
                case "INVALID_ACCESS_TOKEN":
                    status = HttpStatusCode.Conflict;
                    reason = "Plaid no longer knows this bank connection; reconnect the bank.";
                    break;
                default:
                    status = type is "TRANSFER_ERROR" or "ITEM_ERROR" or "INSTITUTION_ERROR"
                        ? HttpStatusCode.UnprocessableEntity
                        : HttpStatusCode.BadRequest;
                    reason = detail;
                    break;
            }
        }

        var message = code is null ? reason : $"{reason} (Plaid error {code})";
        return new PlaidApiException(status, message, code, type, ex);
    }
}
