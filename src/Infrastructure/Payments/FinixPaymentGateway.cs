using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Hand-written client for the Finix API. Every request shape and endpoint in this class
/// comes from finix-spec/finix-openapi.yaml.
/// </summary>
public class FinixPaymentGateway : IFinixPaymentGateway
{
    private const string FinixApiVersion = "2022-02-01";
    private const int MaxPages = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly FinixOptions _options;

    public FinixPaymentGateway(HttpClient httpClient, IOptions<FinixOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public Task<FinixIdentityResult> CreateBuyerIdentityAsync(FinixBuyerIdentityRequest request, CancellationToken cancellationToken = default)
    {
        var entity = new Dictionary<string, object?>();
        AddIfPresent(entity, "first_name", request.FirstName);
        AddIfPresent(entity, "last_name", request.LastName);
        AddIfPresent(entity, "email", request.Email);
        AddIfPresent(entity, "phone", request.Phone);
        if (request.Address is not null)
        {
            entity["personal_address"] = ToAddressBody(request.Address);
        }

        var body = new Dictionary<string, object?>
        {
            ["type"] = "PERSONAL",
            ["identity_roles"] = new[] { "BUYER" },
            ["entity"] = entity
        };
        AddTags(body, request.Tags);

        return SendAsync<FinixIdentityResult>(HttpMethod.Post, "/identities", body, cancellationToken);
    }

    public Task<FinixPaymentInstrumentResult> CreatePaymentInstrumentAsync(FinixCreatePaymentInstrumentRequest request, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["type"] = "PAYMENT_CARD",
            ["identity"] = request.IdentityId,
            ["number"] = request.Number,
            ["expiration_month"] = request.ExpirationMonth,
            ["expiration_year"] = request.ExpirationYear,
            ["name"] = request.Name
        };

        AddIfPresent(body, "security_code", request.SecurityCode);
        if (request.Address is not null)
        {
            body["address"] = ToAddressBody(request.Address);
        }
        AddTags(body, request.Tags);

        return SendAsync<FinixPaymentInstrumentResult>(HttpMethod.Post, "/payment_instruments", body, cancellationToken);
    }

    public Task<FinixPaymentInstrumentResult> DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["enabled"] = false };
        return SendAsync<FinixPaymentInstrumentResult>(HttpMethod.Put, $"/payment_instruments/{Encode(paymentInstrumentId)}", body, cancellationToken);
    }

    public Task<FinixAuthorizationResult> AuthorizeAsync(FinixAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = request.AmountCents,
            ["currency"] = request.Currency,
            ["merchant"] = request.MerchantId,
            ["source"] = request.Source,
            ["idempotency_id"] = request.IdempotencyId
        };
        AddTags(body, request.Tags);

        return SendAsync<FinixAuthorizationResult>(HttpMethod.Post, "/authorizations", body, cancellationToken);
    }

    public Task<FinixAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixAuthorizationResult>(HttpMethod.Get, $"/authorizations/{Encode(authorizationId)}", null, cancellationToken);
    }

    public Task<FinixAuthorizationResult> CaptureAuthorizationAsync(string authorizationId, long captureAmountCents, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["capture_amount"] = captureAmountCents };
        return SendAsync<FinixAuthorizationResult>(HttpMethod.Put, $"/authorizations/{Encode(authorizationId)}", body, cancellationToken);
    }

    public Task<FinixAuthorizationResult> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["void_me"] = true };
        return SendAsync<FinixAuthorizationResult>(HttpMethod.Put, $"/authorizations/{Encode(authorizationId)}", body, cancellationToken);
    }

    public Task<FinixTransferResult> GetTransferAsync(string transferId, CancellationToken cancellationToken = default)
    {
        return SendAsync<FinixTransferResult>(HttpMethod.Get, $"/transfers/{Encode(transferId)}", null, cancellationToken);
    }

    public Task<FinixTransferResult> RefundTransferAsync(string transferId, long refundAmountCents, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["refund_amount"] = refundAmountCents };
        return SendAsync<FinixTransferResult>(HttpMethod.Post, $"/transfers/{Encode(transferId)}/reversals", body, cancellationToken);
    }

    public Task<FinixTransferResult> UpdateTransferTagsAsync(string transferId, IReadOnlyDictionary<string, string> tags, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["tags"] = tags };
        return SendAsync<FinixTransferResult>(HttpMethod.Put, $"/transfers/{Encode(transferId)}", body, cancellationToken);
    }

    public async Task<IReadOnlyList<FinixAuthorizationResult>> ListAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        return await ListAllAsync<FinixAuthorizationResult>("/authorizations", from, to, envelope => envelope.Embedded?.Authorizations, cancellationToken);
    }

    public async Task<IReadOnlyList<FinixTransferResult>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        return await ListAllAsync<FinixTransferResult>("/transfers", from, to, envelope => envelope.Embedded?.Transfers, cancellationToken);
    }

    private async Task<List<T>> ListAllAsync<T>(string path, DateTimeOffset from, DateTimeOffset to,
        Func<FinixListEnvelope<T>, List<T>?> selector, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        string? cursor = null;

        for (var pageNumber = 0; pageNumber < MaxPages; pageNumber++)
        {
            var query = new List<string>
            {
                $"created_at.gte={Uri.EscapeDataString(FormatFilterDate(from))}",
                $"created_at.lte={Uri.EscapeDataString(FormatFilterDate(to))}",
                "limit=100"
            };

            if (!string.IsNullOrWhiteSpace(cursor))
            {
                query.Add($"after_cursor={Uri.EscapeDataString(cursor!)}");
            }

            var envelope = await SendAsync<FinixListEnvelope<T>>(HttpMethod.Get, $"{path}?{string.Join("&", query)}", null, cancellationToken);
            var items = selector(envelope);
            if (items is not null)
            {
                results.AddRange(items);
            }

            cursor = envelope.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                cursor = ExtractAfterCursor(envelope.Links?.Next?.Href);
            }

            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return results;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var requestUri = $"{_options.ResolveBaseUrl()}{path}";
        using var request = new HttpRequestMessage(method, requestUri);
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.TryAddWithoutValidation("Finix-Version", FinixApiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, responseText);
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            return default!;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(responseText, JsonOptions)
                ?? throw new FinixApiException((int)response.StatusCode, "INVALID_RESPONSE", "Finix returned an empty response.");
        }
        catch (JsonException)
        {
            throw new FinixApiException((int)response.StatusCode, "INVALID_RESPONSE", "Finix returned a response that could not be read.");
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.Username)
            || string.IsNullOrWhiteSpace(_options.Password)
            || string.IsNullOrWhiteSpace(_options.MerchantId))
        {
            throw new FinixApiException((int)HttpStatusCode.ServiceUnavailable, "NOT_CONFIGURED",
                "Finix is not configured. Set Finix:Username, Finix:Password and Finix:MerchantId.");
        }
    }

    private static FinixApiException BuildException(HttpStatusCode statusCode, string responseText)
    {
        var code = "UNKNOWN";
        var message = $"Finix request failed with HTTP {(int)statusCode}.";

        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("_embedded", out var embedded)
                && embedded.TryGetProperty("errors", out var errors)
                && errors.ValueKind == JsonValueKind.Array
                && errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                if (first.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String)
                {
                    code = codeElement.GetString() ?? code;
                }
                if (first.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
                {
                    message = messageElement.GetString() ?? message;
                }
            }
        }
        catch (JsonException)
        {
            // Keep the generic message if Finix did not return its documented error envelope.
        }

        return new FinixApiException((int)statusCode, code, message);
    }

    private static Dictionary<string, object?> ToAddressBody(FinixAddress address)
    {
        var body = new Dictionary<string, object?>();
        AddIfPresent(body, "line1", address.Line1);
        AddIfPresent(body, "line2", address.Line2);
        AddIfPresent(body, "city", address.City);
        AddIfPresent(body, "region", address.Region);
        AddIfPresent(body, "postal_code", address.PostalCode);
        AddIfPresent(body, "country", address.Country);
        return body;
    }

    private static void AddIfPresent(Dictionary<string, object?> body, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            body[key] = value;
        }
    }

    private static void AddTags(Dictionary<string, object?> body, IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is not null && tags.Count > 0)
        {
            body["tags"] = tags;
        }
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);

    private static string FormatFilterDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

    private static string? ExtractAfterCursor(string? nextHref)
    {
        if (string.IsNullOrWhiteSpace(nextHref))
        {
            return null;
        }

        const string marker = "after_cursor=";
        var index = nextHref.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var value = nextHref.Substring(index + marker.Length);
        var ampersand = value.IndexOf('&');
        if (ampersand >= 0)
        {
            value = value.Substring(0, ampersand);
        }

        return Uri.UnescapeDataString(value);
    }
}
