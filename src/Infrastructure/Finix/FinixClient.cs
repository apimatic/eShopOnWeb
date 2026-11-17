using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public class FinixClient : IFinixClient
{
    private const string FinixVersion = "2022-02-01";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixClient> _logger;
    private readonly string _basicAuth;

    public FinixClient(HttpClient httpClient, IOptions<FinixOptions> options, ILogger<FinixClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
        {
            throw new InvalidOperationException("Finix credentials are not configured. Set FINIX_USERNAME and FINIX_PASSWORD.");
        }

        if (string.IsNullOrWhiteSpace(_options.MerchantId))
        {
            throw new InvalidOperationException("Finix merchant is not configured. Set FINIX_MERCHANT_ID.");
        }

        if (string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new InvalidOperationException("Finix currency is not configured. Set FINIX_CURRENCY.");
        }

        _httpClient.BaseAddress = new Uri(_options.ResolveBaseUrl().TrimEnd('/') + "/", UriKind.Absolute);
        _basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
    }

    public async Task<FinixIdentityResult> CreateBuyerIdentityAsync(
        string firstName,
        string lastName,
        string? email,
        string? phone,
        FinixAddress? address,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            type = "PERSONAL",
            identity_roles = new[] { "BUYER" },
            entity = new
            {
                first_name = firstName,
                last_name = lastName,
                email,
                phone,
                personal_address = ToAddress(address ?? DefaultAddress)
            }
        };

        var response = await SendAsync<IdentityResponse>(HttpMethod.Post, "identities", payload, cancellationToken);
        return new FinixIdentityResult(response.Id);
    }

    public async Task<FinixPaymentInstrumentResult> CreatePaymentInstrumentAsync(
        string identityId,
        FinixCard card,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            type = "PAYMENT_CARD",
            identity = identityId,
            name = card.Name,
            number = NormalizeCardNumber(card.Number),
            expiration_month = card.ExpirationMonth,
            expiration_year = card.ExpirationYear,
            security_code = card.SecurityCode,
            address = card.Address == null ? null : ToAddress(card.Address)
        };

        var response = await SendAsync<PaymentInstrumentResponse>(HttpMethod.Post, "payment_instruments", payload, cancellationToken);
        return MapPaymentInstrument(response);
    }

    public async Task<FinixPaymentInstrumentResult> GetPaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<PaymentInstrumentResponse>(
            HttpMethod.Get,
            $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            null,
            cancellationToken);
        return MapPaymentInstrument(response);
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default)
    {
        await SendAsync<PaymentInstrumentResponse>(
            HttpMethod.Put,
            $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            new { enabled = false },
            cancellationToken);
    }

    public async Task<FinixAuthorizationResult> CreateAuthorizationAsync(
        int amountCents,
        string currency,
        string sourcePaymentInstrumentId,
        string idempotencyId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            amount = amountCents,
            currency,
            merchant = _options.MerchantId,
            source = sourcePaymentInstrumentId,
            idempotency_id = idempotencyId,
            tags
        };

        var response = await SendAsync<AuthorizationResponse>(HttpMethod.Post, "authorizations", payload, cancellationToken);
        return MapAuthorization(response);
    }

    public async Task<FinixAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AuthorizationResponse>(
            HttpMethod.Get,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            null,
            cancellationToken);
        return MapAuthorization(response);
    }

    public async Task<FinixAuthorizationResult?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        var path = $"authorizations?limit=1&idempotency_id={Uri.EscapeDataString(idempotencyId)}";
        var response = await SendAsync<AuthorizationListResponse>(HttpMethod.Get, path, null, cancellationToken);
        var authorization = response.Embedded?.Authorizations?.FirstOrDefault();
        return authorization == null ? null : MapAuthorization(authorization);
    }

    public async Task<FinixAuthorizationResult> CaptureAuthorizationAsync(
        string authorizationId,
        int amountCents,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AuthorizationResponse>(
            HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new { capture_amount = amountCents },
            cancellationToken);
        return MapAuthorization(response);
    }

    public async Task<FinixAuthorizationResult> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<AuthorizationResponse>(
            HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new { void_me = true },
            cancellationToken);
        return MapAuthorization(response);
    }

    public async Task<FinixTransferResult> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<TransferResponse>(
            HttpMethod.Get,
            $"transfers/{Uri.EscapeDataString(transferId)}",
            null,
            cancellationToken);
        return MapTransfer(response);
    }

    public async Task<FinixTransferResult> CreateReversalAsync(
        string transferId,
        int amountCents,
        string idempotencyId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync<TransferResponse>(
            HttpMethod.Post,
            $"transfers/{Uri.EscapeDataString(transferId)}/reversals",
            new { refund_amount = amountCents, idempotency_id = idempotencyId },
            cancellationToken);
        return MapTransfer(response);
    }

    public async Task<IReadOnlyList<FinixTransferResult>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FinixTransferResult>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        for (var page = 0; page < 1000; page++)
        {
            var path = "transfers?limit=100"
                       + "&created_at.gte=" + Uri.EscapeDataString(FormatTimestamp(from))
                       + "&created_at.lte=" + Uri.EscapeDataString(FormatTimestamp(to));

            if (!string.IsNullOrWhiteSpace(cursor))
            {
                path += "&after_cursor=" + Uri.EscapeDataString(cursor);
            }

            var response = await SendAsync<TransferListResponse>(HttpMethod.Get, path, null, cancellationToken);
            var transfers = response.Embedded?.Transfers ?? new List<TransferResponse>();
            results.AddRange(transfers.Select(MapTransfer));

            var next = response.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(next) || transfers.Count == 0 || !seenCursors.Add(next))
            {
                break;
            }

            cursor = next;
        }

        return results;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicAuth);
        request.Headers.TryAddWithoutValidation("Finix-Version", FinixVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        _logger.LogDebug("Calling Finix {Method} {Path}", method.Method, path);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions)
               ?? throw new FinixApiException(response.StatusCode, "INVALID_RESPONSE", "Finix returned an empty or unreadable response.");
    }

    private static FinixApiException BuildException(HttpStatusCode statusCode, string content)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ErrorResponse>(content, JsonOptions);
            var first = error?.Embedded?.Errors?.FirstOrDefault();
            if (first != null)
            {
                return new FinixApiException(statusCode, first.Code, first.Message ?? "Finix request failed.");
            }
        }
        catch (JsonException)
        {
        }

        var message = string.IsNullOrWhiteSpace(content)
            ? $"Finix request failed with HTTP {(int)statusCode}."
            : $"Finix request failed with HTTP {(int)statusCode}: {Truncate(content, 500)}";
        return new FinixApiException(statusCode, null, message);
    }

    private static FinixAuthorizationResult MapAuthorization(AuthorizationResponse response) =>
        new(
            response.Id,
            response.State,
            response.Amount,
            response.Currency,
            response.ExpiresAt,
            response.IsVoid,
            response.Transfer,
            response.FailureCode,
            response.FailureMessage);

    private static FinixTransferResult MapTransfer(TransferResponse response) =>
        new(
            response.Id,
            response.Type,
            response.State,
            response.Amount,
            response.Fee,
            response.ParentTransfer,
            response.OperationKey,
            response.CreatedAt,
            response.Currency,
            response.FailureCode,
            response.FailureMessage);

    private static FinixPaymentInstrumentResult MapPaymentInstrument(PaymentInstrumentResponse response) =>
        new(
            response.Id,
            response.Brand,
            response.LastFour,
            response.ExpirationMonth,
            response.ExpirationYear,
            response.Enabled);

    private static object ToAddress(FinixAddress address) => new
    {
        line1 = address.Line1,
        line2 = address.Line2,
        city = address.City,
        region = address.Region,
        postal_code = address.PostalCode,
        country = NormalizeCountry(address.Country)
    };

    private static string NormalizeCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return "USA";
        }

        var value = country.Trim().ToUpperInvariant();
        return value switch
        {
            "US" => "USA",
            "CA" => "CAN",
            "GB" => "GBR",
            _ => value
        };
    }

    private static string NormalizeCardNumber(string number) =>
        new(number.Where(char.IsDigit).ToArray());

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);

    private static readonly FinixAddress DefaultAddress = new(
        "123 Main St",
        null,
        "San Francisco",
        "CA",
        "94105",
        "USA");

    private sealed class IdentityResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    }

    private sealed class PaymentInstrumentResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("brand")] public string? Brand { get; set; }
        [JsonPropertyName("last_four")] public string? LastFour { get; set; }
        [JsonPropertyName("expiration_month")] public int ExpirationMonth { get; set; }
        [JsonPropertyName("expiration_year")] public int ExpirationYear { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    }

    private sealed class AuthorizationResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
        [JsonPropertyName("amount")] public int Amount { get; set; }
        [JsonPropertyName("currency")] public string Currency { get; set; } = string.Empty;
        [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
        [JsonPropertyName("is_void")] public bool IsVoid { get; set; }
        [JsonPropertyName("transfer")] public string? Transfer { get; set; }
        [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
        [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    }

    private sealed class TransferResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
        [JsonPropertyName("amount")] public int Amount { get; set; }
        [JsonPropertyName("fee")] public int Fee { get; set; }
        [JsonPropertyName("parent_transfer")] public string? ParentTransfer { get; set; }
        [JsonPropertyName("operation_key")] public string? OperationKey { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
        [JsonPropertyName("currency")] public string? Currency { get; set; }
        [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
        [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    }

    private sealed class TransferListResponse
    {
        [JsonPropertyName("page")] public PageInfo? Page { get; set; }
        [JsonPropertyName("_embedded")] public TransferEmbedded? Embedded { get; set; }
    }

    private sealed class TransferEmbedded
    {
        [JsonPropertyName("transfers")] public List<TransferResponse>? Transfers { get; set; }
    }

    private sealed class AuthorizationListResponse
    {
        [JsonPropertyName("page")] public PageInfo? Page { get; set; }
        [JsonPropertyName("_embedded")] public AuthorizationEmbedded? Embedded { get; set; }
    }

    private sealed class AuthorizationEmbedded
    {
        [JsonPropertyName("authorizations")] public List<AuthorizationResponse>? Authorizations { get; set; }
    }

    private sealed class PageInfo
    {
        [JsonPropertyName("next_cursor")] public string? NextCursor { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("_embedded")] public ErrorEmbedded? Embedded { get; set; }
    }

    private sealed class ErrorEmbedded
    {
        [JsonPropertyName("errors")] public List<ErrorItem>? Errors { get; set; }
    }

    private sealed class ErrorItem
    {
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("logref")] public string? LogRef { get; set; }
    }
}
