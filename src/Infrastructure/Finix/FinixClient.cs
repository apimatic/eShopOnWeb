using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

/// <summary>
/// Hand-written client for the Finix API. The OpenAPI document in
/// finix-spec/finix-openapi.yaml is the authoritative contract for every
/// endpoint, parameter, request body and response shape used here.
/// </summary>
public class FinixClient : IFinixClient
{
    private const string FinixVersion = "2022-02-01";
    private const string SandboxBaseUrl = "https://finix.sandbox-payments-api.com";
    private const string LiveBaseUrl = "https://finix.live-payments-api.com";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly FinixSettings _settings;
    private readonly ILogger<FinixClient> _logger;

    public FinixClient(HttpClient httpClient, IOptions<FinixSettings> settings, ILogger<FinixClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    private string BaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                return _settings.BaseUrl.TrimEnd('/');
            }

            return string.Equals(_settings.Environment, "live", StringComparison.OrdinalIgnoreCase)
                ? LiveBaseUrl
                : SandboxBaseUrl;
        }
    }

    public Task<FinixIdentity> CreateBuyerIdentityAsync(
        string buyerId,
        string? email,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken)
    {
        var entity = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(firstName)) entity["first_name"] = firstName;
        if (!string.IsNullOrWhiteSpace(lastName)) entity["last_name"] = lastName;
        if (!string.IsNullOrWhiteSpace(email)) entity["email"] = email;

        var body = new Dictionary<string, object?>
        {
            ["entity"] = entity,
            ["identity_roles"] = new[] { "BUYER" },
            ["type"] = "PERSONAL",
            ["tags"] = new Dictionary<string, string> { ["eshop_buyer_id"] = buyerId }
        };

        return SendAsync<FinixIdentity>(HttpMethod.Post, "/identities", body, cancellationToken);
    }

    public Task<FinixPaymentInstrument> CreateCardPaymentInstrumentAsync(
        string identityId,
        CardInput card,
        string buyerId,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = "PAYMENT_CARD",
            ["identity"] = identityId,
            ["name"] = card.Name,
            ["number"] = card.Number,
            ["expiration_month"] = card.ExpirationMonth,
            ["expiration_year"] = card.ExpirationYear,
            ["tags"] = new Dictionary<string, string> { ["eshop_buyer_id"] = buyerId }
        };

        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            body["security_code"] = card.SecurityCode;
        }

        if (card.BillingAddress != null)
        {
            body["address"] = ToAddressBody(card.BillingAddress);
        }

        return SendAsync<FinixPaymentInstrument>(
            HttpMethod.Post,
            "/payment_instruments",
            body,
            cancellationToken);
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?> { ["enabled"] = false };
        await SendAsync<FinixPaymentInstrument>(
            HttpMethod.Put,
            $"/payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            body,
            cancellationToken);
    }

    public Task<FinixAuthorization> CreateAuthorizationAsync(
        long amountInCents,
        string currency,
        string source,
        string idempotencyId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = amountInCents,
            ["currency"] = currency,
            ["merchant"] = _settings.MerchantId,
            ["source"] = source,
            ["idempotency_id"] = idempotencyId,
            ["partial_authorization"] = false
        };

        if (tags != null && tags.Count > 0)
        {
            body["tags"] = tags;
        }

        return SendAsync<FinixAuthorization>(HttpMethod.Post, "/authorizations", body, cancellationToken);
    }

    public Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        return SendAsync<FinixAuthorization>(
            HttpMethod.Get,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            cancellationToken);
    }

    public Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long amountInCents,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?> { ["capture_amount"] = amountInCents };
        return SendAsync<FinixAuthorization>(
            HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body,
            cancellationToken);
    }

    public Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?> { ["void_me"] = true };
        return SendAsync<FinixAuthorization>(
            HttpMethod.Put,
            $"/authorizations/{Uri.EscapeDataString(authorizationId)}",
            body,
            cancellationToken);
    }

    public Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        return SendAsync<FinixTransfer>(
            HttpMethod.Get,
            $"/transfers/{Uri.EscapeDataString(transferId)}",
            null,
            cancellationToken);
    }

    public Task<FinixTransfer> CreateReversalAsync(
        string transferId,
        long amountInCents,
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["refund_amount"] = amountInCents,
            ["idempotency_id"] = idempotencyId
        };

        return SendAsync<FinixTransfer>(
            HttpMethod.Post,
            $"/transfers/{Uri.EscapeDataString(transferId)}/reversals",
            body,
            cancellationToken);
    }

    public async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<FinixTransfer>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var pageCount = 0;

        do
        {
            var query = new StringBuilder();
            query.Append("/transfers?limit=100");
            query.Append("&created_at.gte=").Append(Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss")));
            query.Append("&created_at.lte=").Append(Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss")));
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                query.Append("&after_cursor=").Append(Uri.EscapeDataString(cursor));
            }

            var page = await SendAsync<FinixListResponse<FinixTransfer>>(
                HttpMethod.Get,
                query.ToString(),
                null,
                cancellationToken);

            if (page.Embedded?.Transfers != null)
            {
                results.AddRange(page.Embedded.Transfers);
            }

            cursor = page.Page?.NextCursor;
            pageCount++;

            if (pageCount > 1000)
            {
                _logger.LogWarning("Finix transfer pagination exceeded 1000 pages; stopping to avoid an unbounded loop.");
                break;
            }
        }
        while (!string.IsNullOrWhiteSpace(cursor) && seenCursors.Add(cursor));

        return results;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(method, BaseUrl + path);
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_settings.Username}:{_settings.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.TryAddWithoutValidation("Finix-Version", FinixVersion);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, responseBody);
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
               ?? throw new FinixApiException(
                   (int)response.StatusCode,
                   "Finix returned an empty or unreadable response.");
    }

    private FinixApiException BuildException(System.Net.HttpStatusCode statusCode, string responseBody)
    {
        FinixError? error = null;
        try
        {
            var document = JsonSerializer.Deserialize<FinixErrorResponse>(responseBody, JsonOptions);
            error = document?.Embedded?.Errors?.FirstOrDefault();
        }
        catch (JsonException)
        {
            // Finix occasionally returns a non-JSON error body; fall back to the status code.
        }

        var message = error?.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"Finix request failed with HTTP status {(int)statusCode}.";
        }

        _logger.LogWarning(
            "Finix request failed with status {StatusCode}, code {Code}, logref {Logref}.",
            (int)statusCode,
            error?.Code,
            error?.Logref);

        return new FinixApiException((int)statusCode, message, error?.Code, error?.Logref, error?.Transfer);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.Username)
            || string.IsNullOrWhiteSpace(_settings.Password)
            || string.IsNullOrWhiteSpace(_settings.MerchantId))
        {
            throw new FinixApiException(
                503,
                "Finix is not configured. Set Finix:Username, Finix:Password and Finix:MerchantId from the FINIX_USERNAME, FINIX_PASSWORD and FINIX_MERCHANT_ID environment variables or user-secrets.");
        }
    }

    private static Dictionary<string, object?> ToAddressBody(FinixAddress address)
    {
        var body = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(address.Line1)) body["line1"] = address.Line1;
        if (!string.IsNullOrWhiteSpace(address.Line2)) body["line2"] = address.Line2;
        if (!string.IsNullOrWhiteSpace(address.City)) body["city"] = address.City;
        if (!string.IsNullOrWhiteSpace(address.Region)) body["region"] = address.Region;
        if (!string.IsNullOrWhiteSpace(address.PostalCode)) body["postal_code"] = address.PostalCode;
        if (!string.IsNullOrWhiteSpace(address.Country)) body["country"] = address.Country;
        return body;
    }
}
