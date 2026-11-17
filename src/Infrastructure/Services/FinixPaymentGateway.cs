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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Plain-HTTP implementation of the Finix API. The contract is pinned to the
/// Finix-Version documented at docs.finix.com (2022-02-01) and uses Basic auth.
/// </summary>
public sealed class FinixPaymentGateway : IFinixPaymentGateway
{
    private const string FinixApiVersion = "2022-02-01";
    private const string SandboxBaseUrl = "https://finix.sandbox-payments-api.com";
    private const string LiveBaseUrl = "https://finix.live-payments-api.com";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly FinixOptions _options;

    public FinixPaymentGateway(FinixOptions options)
    {
        _options = options;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public async Task<string> CreateIdentityAsync(CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Post, "identities",
            new { type = "PERSONAL" }, cancellationToken);
        return RequiredString(document.RootElement, "id");
    }

    public async Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(
        string finixIdentityId, CardDetails card, CancellationToken cancellationToken = default)
    {
        var body = new
        {
            identity = finixIdentityId,
            type = "PAYMENT_CARD",
            number = OnlyDigits(card.Number),
            expiration_month = card.ExpirationMonth,
            expiration_year = card.ExpirationYear,
            security_code = card.SecurityCode,
            name = card.Name,
            address = card.BillingAddress is null ? null : new
            {
                line1 = card.BillingAddress.Line1,
                line2 = card.BillingAddress.Line2,
                city = card.BillingAddress.City,
                region = card.BillingAddress.Region,
                postal_code = card.BillingAddress.PostalCode,
                country = card.BillingAddress.Country
            }
        };

        using var document = await SendAsync(HttpMethod.Post, "payment_instruments", body, cancellationToken);
        var root = document.RootElement;
        return new FinixPaymentInstrument(
            RequiredString(root, "id"),
            String(root, "brand") ?? "UNKNOWN",
            String(root, "last_four") ?? LastFour(OnlyDigits(card.Number)),
            Int(root, "expiration_month"),
            Int(root, "expiration_year"),
            Bool(root, "enabled") ?? true);
    }

    public async Task DisablePaymentInstrumentAsync(
        string paymentInstrumentId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(HttpMethod.Put,
            $"payment_instruments/{Uri.EscapeDataString(paymentInstrumentId)}",
            new { enabled = false }, cancellationToken);
    }

    public async Task<FinixAuthorization> CreateAuthorizationAsync(
        string paymentInstrumentId, long amountMinor, string currency,
        string orderReference, string idempotencyKey, string? securityCode,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            amount = amountMinor,
            currency,
            merchant = _options.MerchantId,
            source = paymentInstrumentId,
            idempotency_id = idempotencyKey,
            security_code = securityCode,
            tags = new Dictionary<string, string>
            {
                ["order_id"] = orderReference,
                ["source"] = "eshoponweb"
            }
        };

        try
        {
            using var document = await SendAsync(HttpMethod.Post, "authorizations", body, cancellationToken);
            return ParseAuthorization(document.RootElement);
        }
        catch (FinixGatewayException exception) when (IsDuplicate(exception))
        {
            var existing = await FindAuthorizationByIdempotencyIdAsync(idempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    public async Task<FinixAuthorization> GetAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}", null, cancellationToken);
        return ParseAuthorization(document.RootElement);
    }

    public async Task<FinixAuthorization?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var relative = $"authorizations?idempotency_id={Uri.EscapeDataString(idempotencyKey)}" +
                       $"&merchant={Uri.EscapeDataString(_options.MerchantId)}&limit=1";
        using var document = await SendAsync(HttpMethod.Get, relative, null, cancellationToken);
        var items = EmbeddedArray(document.RootElement, "authorizations");
        return items.Count == 0 ? null : ParseAuthorization(items[0]);
    }

    public async Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId, long captureAmountMinor,
        CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new { capture_amount = captureAmountMinor }, cancellationToken);
        return ParseAuthorization(document.RootElement);
    }

    public async Task<FinixAuthorization> VoidAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Put,
            $"authorizations/{Uri.EscapeDataString(authorizationId)}",
            new { void_me = true }, cancellationToken);
        return ParseAuthorization(document.RootElement);
    }

    public async Task<FinixTransfer> GetTransferAsync(
        string transferId, CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"transfers/{Uri.EscapeDataString(transferId)}", null, cancellationToken);
        return ParseTransfer(document.RootElement);
    }

    public async Task<FinixFeeSummary> GetTransferFeesAsync(
        string transferId, CancellationToken cancellationToken = default)
    {
        decimal total = 0m;
        string? currency = null;
        string? cursor = null;

        do
        {
            var relative = $"fees?linked_to={Uri.EscapeDataString(transferId)}&limit=100";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                relative += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            using var document = await SendAsync(HttpMethod.Get, relative, null, cancellationToken);
            var fees = EmbeddedArray(document.RootElement, "fees");
            foreach (var fee in fees)
            {
                total += Decimal(fee, "amount");
                currency ??= String(fee, "currency");
            }

            cursor = PageCursor(document.RootElement);
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return new FinixFeeSummary(total, currency ?? _options.Currency);
    }

    public async Task<FinixRefund> CreateRefundAsync(
        string transferId, long amountMinor, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var body = new
        {
            refund_amount = amountMinor,
            idempotency_id = idempotencyKey
        };

        try
        {
            using var document = await SendAsync(HttpMethod.Post,
                $"transfers/{Uri.EscapeDataString(transferId)}/reversals", body, cancellationToken);
            return ParseRefund(document.RootElement, idempotentReplay: false);
        }
        catch (FinixGatewayException exception) when (
            !string.IsNullOrWhiteSpace(exception.RelatedTransferId) &&
            exception.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
        {
            var existing = await GetTransferAsync(exception.RelatedTransferId!, cancellationToken);
            return new FinixRefund(existing.Id, existing.State, existing.AmountMinor,
                existing.Currency, existing.ParentTransferId, existing.FailureCode,
                existing.FailureMessage, IdempotentReplay: true);
        }
    }

    public async Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<FinixTransfer>();
        string? cursor = null;
        var fromValue = FormatDate(from);
        var toValue = FormatDate(to);

        do
        {
            var relative = $"transfers?merchant={Uri.EscapeDataString(_options.MerchantId)}" +
                           $"&created_at.gte={Uri.EscapeDataString(fromValue)}" +
                           $"&created_at.lte={Uri.EscapeDataString(toValue)}&limit=100";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                relative += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            using var document = await SendAsync(HttpMethod.Get, relative, null, cancellationToken);
            results.AddRange(EmbeddedArray(document.RootElement, "transfers").Select(ParseTransfer));

            var next = PageCursor(document.RootElement);
            if (string.IsNullOrWhiteSpace(next) || string.Equals(next, cursor, StringComparison.Ordinal))
            {
                break;
            }

            cursor = next;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    public async Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<FinixAuthorization>();
        string? cursor = null;
        var fromValue = FormatDate(from);
        var toValue = FormatDate(to);

        do
        {
            var relative = $"authorizations?merchant={Uri.EscapeDataString(_options.MerchantId)}" +
                           $"&created_at.gte={Uri.EscapeDataString(fromValue)}" +
                           $"&created_at.lte={Uri.EscapeDataString(toValue)}&limit=100";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                relative += $"&after_cursor={Uri.EscapeDataString(cursor)}";
            }

            using var document = await SendAsync(HttpMethod.Get, relative, null, cancellationToken);
            results.AddRange(EmbeddedArray(document.RootElement, "authorizations")
                .Select(ParseAuthorization));

            var next = PageCursor(document.RootElement);
            if (string.IsNullOrWhiteSpace(next) || string.Equals(next, cursor, StringComparison.Ordinal))
            {
                break;
            }

            cursor = next;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string relative, object? body,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = BuildRequest(method, relative, body);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FinixGatewayException("Finix did not respond before the request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new FinixGatewayException($"Could not reach Finix: {exception.Message}");
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException(response.StatusCode, payload);
            }

            try
            {
                return JsonDocument.Parse(payload);
            }
            catch (JsonException)
            {
                throw new FinixGatewayException("Finix returned a response that was not valid JSON.");
            }
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string relative, object? body)
    {
        var baseUrl = ResolveBaseUrl();
        var request = new HttpRequestMessage(method,
            new Uri(baseUrl.TrimEnd('/') + "/" + relative.TrimStart('/')));

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.TryAddWithoutValidation("Finix-Version", FinixApiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8, "application/json");
        }

        return request;
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.Username) ||
            string.IsNullOrWhiteSpace(_options.Password) ||
            string.IsNullOrWhiteSpace(_options.MerchantId))
        {
            throw new FinixGatewayException(
                "Finix credentials are not configured. Set FINIX_USERNAME, FINIX_PASSWORD and " +
                "FINIX_MERCHANT_ID (or the matching Finix user-secrets).");
        }

        if (string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new FinixGatewayException("Finix:Currency is not configured.");
        }
    }

    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return _options.BaseUrl;
        }

        return string.Equals(_options.Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? LiveBaseUrl
            : SandboxBaseUrl;
    }

    private static FinixGatewayException BuildApiException(HttpStatusCode statusCode, string payload)
    {
        string message = $"Finix returned HTTP {(int)statusCode}.";
        string? code = null;
        string? relatedTransferId = null;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var errors = EmbeddedArray(document.RootElement, "errors");
            if (errors.Count > 0)
            {
                var first = errors[0];
                message = String(first, "message") ?? message;
                code = String(first, "code");
                relatedTransferId = String(first, "transfer");
            }
        }
        catch (JsonException)
        {
            // Keep the generic message; never echo a raw response body.
        }

        return new FinixGatewayException(message, code, (int)statusCode, relatedTransferId);
    }

    private static bool IsDuplicate(FinixGatewayException exception) =>
        exception.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);

    private static FinixAuthorization ParseAuthorization(JsonElement root) =>
        new(
            RequiredString(root, "id"),
            String(root, "state") ?? "UNKNOWN",
            Long(root, "amount"),
            String(root, "currency") ?? "USD",
            String(root, "transfer"),
            String(root, "void_state"),
            Bool(root, "is_void") ?? false,
            String(root, "failure_code"),
            String(root, "failure_message"),
            DateTimeOffset(root, "created_at"));

    private static FinixTransfer ParseTransfer(JsonElement root) =>
        new(
            RequiredString(root, "id"),
            String(root, "state") ?? "UNKNOWN",
            String(root, "type") ?? "UNKNOWN",
            Long(root, "amount"),
            String(root, "currency") ?? "USD",
            String(root, "parent_transfer"),
            String(root, "failure_code"),
            String(root, "failure_message"),
            DateTimeOffset(root, "created_at"),
            TagValue(root, "order_id"));

    private static FinixRefund ParseRefund(JsonElement root, bool idempotentReplay) =>
        new(
            RequiredString(root, "id"),
            String(root, "state") ?? "UNKNOWN",
            Long(root, "amount"),
            String(root, "currency") ?? "USD",
            String(root, "parent_transfer"),
            String(root, "failure_code"),
            String(root, "failure_message"),
            idempotentReplay);

    private static IReadOnlyList<JsonElement> EmbeddedArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty("_embedded", out var embedded) ||
            embedded.ValueKind != JsonValueKind.Object ||
            !embedded.TryGetProperty(name, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<JsonElement>();
        }

        return array.EnumerateArray().Select(element => element.Clone()).ToList();
    }

    private static string? PageCursor(JsonElement root)
    {
        if (root.TryGetProperty("page", out var page) && page.ValueKind == JsonValueKind.Object)
        {
            return String(page, "next_cursor");
        }

        return null;
    }

    private static string RequiredString(JsonElement element, string name) =>
        String(element, name) ?? throw new FinixGatewayException(
            $"Finix response did not contain the required field '{name}'.");

    private static string? String(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static long Long(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        return value.TryGetInt64(out var number) ? number : (long)value.GetDecimal();
    }

    private static int Int(JsonElement element, string name) => (int)Long(element, name);

    private static decimal Decimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return 0m;
        }

        return value.GetDecimal();
    }

    private static bool? Bool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static DateTimeOffset? DateTimeOffset(JsonElement element, string name)
    {
        var value = String(element, name);
        return System.DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? TagValue(JsonElement element, string tagName)
    {
        if (!element.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return String(tags, tagName);
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string OnlyDigits(string value) =>
        new(value.Where(char.IsDigit).ToArray());

    private static string LastFour(string value) =>
        value.Length <= 4 ? value : value[^4..];
}
