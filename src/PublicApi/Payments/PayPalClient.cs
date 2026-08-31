using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalClient : IPayPalClient
{
    private const int TransactionPageSize = 500;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public PayPalClient(HttpClient httpClient, IOptions<PayPalOptions> options,
        ILogger<PayPalClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency => _options.Currency.Trim().ToUpperInvariant();

    public async Task<PayPalAuthorization> AuthorizeAsync(int orderId, string paymentReference, decimal amount,
        PayPalCard? card, string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        if ((card is null) == string.IsNullOrWhiteSpace(vaultId))
        {
            throw new ArgumentException("Supply either a card or a vault ID, but not both.");
        }

        object cardSource = card is not null
            ? CardPayload(card)
            : new
            {
                vault_id = vaultId,
                stored_credential = new
                {
                    payment_initiator = "CUSTOMER",
                    payment_type = "ONE_TIME",
                    usage = "SUBSEQUENT"
                }
            };

        var value = Money(amount);
        var payload = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new
                {
                    reference_id = $"eshop-{paymentReference}",
                    custom_id = paymentReference,
                    invoice_id = $"eshop-{paymentReference}",
                    amount = new { currency_code = Currency, value }
                }
            },
            payment_source = new { card = cardSource }
        };

        using var document = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", payload,
            requestId, cancellationToken);
        ThrowIfPayerActionRequired(document.RootElement);

        var root = document.RootElement;
        var authorization = root.GetProperty("purchase_units")[0]
            .GetProperty("payments").GetProperty("authorizations")[0];
        return ParseAuthorization(root.GetProperty("id").GetString()!, authorization);
    }

    public async Task<PayPalAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken cancellationToken)
    {
        var payload = new { amount = new { currency_code = Currency, value = Money(amount) } };
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/reauthorize",
            payload, requestId, cancellationToken);
        return ParseAuthorization(string.Empty, document.RootElement);
    }

    public async Task<PayPalCapture> CaptureAsync(string authorizationId, decimal amount,
        string requestId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            amount = new { currency_code = Currency, value = Money(amount) },
            final_capture = true
        };
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/capture",
            payload, requestId, cancellationToken);
        return ParseCapture(document.RootElement);
    }

    public async Task<PayPalCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        using var document = await SendAsync(HttpMethod.Get,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}", null, null, cancellationToken);
        return ParseCapture(document.RootElement);
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{Uri.EscapeDataString(authorizationId)}/void",
            null, requestId, cancellationToken);
        return "VOIDED";
    }

    public async Task<PayPalRefund> RefundAsync(string captureId, decimal amount,
        string requestId, CancellationToken cancellationToken)
    {
        var payload = new { amount = new { currency_code = Currency, value = Money(amount) } };
        using var document = await SendAsync(HttpMethod.Post,
            $"/v2/payments/captures/{Uri.EscapeDataString(captureId)}/refund",
            payload, requestId, cancellationToken);
        var root = document.RootElement;
        return new PayPalRefund(
            root.GetProperty("id").GetString()!,
            root.GetProperty("status").GetString()!,
            ParseMoney(root.GetProperty("amount")),
            ReadDate(root, "create_time"));
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(string buyerId, PayPalCard card,
        string requestId, CancellationToken cancellationToken)
    {
        var payload = new
        {
            customer = new { merchant_customer_id = buyerId },
            payment_source = new { card = CardPayload(card) }
        };
        using var document = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            payload, requestId, cancellationToken);
        ThrowIfPayerActionRequired(document.RootElement);

        var root = document.RootElement;
        var cardResponse = root.GetProperty("payment_source").GetProperty("card");
        return new PayPalVaultedCard(
            root.GetProperty("id").GetString()!,
            cardResponse.GetProperty("brand").GetString() ?? "UNKNOWN",
            cardResponse.GetProperty("last_digits").GetString()!,
            cardResponse.GetProperty("expiry").GetString() ?? card.Expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        await SendAsync(HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", null, null, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to <= from)
        {
            throw new ArgumentException("The reconciliation end must be after its start.");
        }

        var result = new List<PayPalTransaction>();
        for (var chunkStart = from; chunkStart < to;)
        {
            var chunkEnd = chunkStart.AddDays(30);
            if (chunkEnd > to) chunkEnd = to;

            for (var page = 1; ; page++)
            {
                var path = "/v1/reporting/transactions" +
                    $"?start_date={Uri.EscapeDataString(Rfc3339(chunkStart))}" +
                    $"&end_date={Uri.EscapeDataString(Rfc3339(chunkEnd))}" +
                    "&fields=transaction_info&balance_affecting_records_only=N" +
                    $"&page_size={TransactionPageSize}&page={page}";
                using var document = await SendAsync(HttpMethod.Get, path, null, null, cancellationToken);
                var root = document.RootElement;
                var count = 0;
                if (root.TryGetProperty("transaction_details", out var details))
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        count++;
                        result.Add(ParseTransaction(detail.GetProperty("transaction_info")));
                    }
                }

                var totalPages = root.TryGetProperty("total_pages", out var pagesElement)
                    ? pagesElement.GetInt32()
                    : page;
                if (page >= totalPages || count < TransactionPageSize) break;
            }

            chunkStart = chunkEnd;
        }

        return result
            .GroupBy(x => new { x.TransactionId, x.EventCode, x.InitiatedAt, x.Amount, x.Currency })
            .Select(x => x.First())
            .ToList();
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        string? requestId, CancellationToken cancellationToken)
    {
        _options.Validate();
        string? serializedBody = body is null ? null : JsonSerializer.Serialize(body, JsonOptions);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var accessToken = await GetAccessTokenAsync(cancellationToken);
            using var request = new HttpRequestMessage(method, _options.GetBaseUrl() + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (requestId is not null)
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }
            if (serializedBody is not null)
            {
                request.Content = new StringContent(serializedBody, Encoding.UTF8, "application/json");
            }

            using var response = await _httpClient.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 1)
            {
                _accessToken = null;
                continue;
            }

            if ((response.StatusCode == HttpStatusCode.TooManyRequests ||
                 (int)response.StatusCode >= 500) && attempt < 3)
            {
                var delay = response.Headers.RetryAfter?.Delta
                    ?? TimeSpan.FromMilliseconds(150 * attempt * attempt + Random.Shared.Next(25, 125));
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    return JsonDocument.Parse("{}");
                }
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }

            var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
            var (name, message, debugId) = ParseError(errorText);
            _logger.LogError("PayPal request {Method} {Path} failed with {Status}; name={Name}; error={Error}; debug_id={DebugId}",
                method.Method, path, (int)response.StatusCode, name, message, debugId);
            if (string.Equals(name, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PayPalPayerActionRequiredException();
            }
            throw new PayPalApiException(response.StatusCode, name, message, debugId);
        }

        throw new InvalidOperationException("PayPal request retry loop ended unexpectedly.");
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken is not null && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return _accessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post,
                _options.GetBaseUrl() + "/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                var (name, message, debugId) = ParseError(error);
                _logger.LogError("PayPal OAuth failed with {Status}; name={Name}; debug_id={DebugId}",
                    (int)response.StatusCode, name, debugId);
                throw new PayPalApiException(response.StatusCode, name, message, debugId);
            }

            var token = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, cancellationToken);
            _accessToken = token.GetProperty("access_token").GetString()!;
            var expiresIn = token.TryGetProperty("expires_in", out var expiry) ? expiry.GetInt32() : 300;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private object CardPayload(PayPalCard card) => new
    {
        name = card.Name,
        number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
        expiry = card.Expiry,
        security_code = card.SecurityCode,
        billing_address = new
        {
            country_code = card.CountryCode.ToUpperInvariant(),
            address_line_1 = card.AddressLine1,
            address_line_2 = card.AddressLine2,
            admin_area_2 = card.City,
            admin_area_1 = card.State,
            postal_code = card.PostalCode
        }
    };

    private static PayPalAuthorization ParseAuthorization(string orderId, JsonElement element) => new(
        orderId,
        element.GetProperty("id").GetString()!,
        element.GetProperty("status").GetString()!,
        ParseMoney(element.GetProperty("amount")),
        element.GetProperty("amount").GetProperty("currency_code").GetString()!,
        ReadDate(element, "create_time") ?? DateTimeOffset.UtcNow,
        ReadDate(element, "expiration_time"));

    private static PayPalCapture ParseCapture(JsonElement root)
    {
        decimal? fee = null;
        decimal? net = null;
        if (root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            if (breakdown.TryGetProperty("paypal_fee", out var feeElement)) fee = ParseMoney(feeElement);
            if (breakdown.TryGetProperty("net_amount", out var netElement)) net = ParseMoney(netElement);
        }

        return new PayPalCapture(
            root.GetProperty("id").GetString()!,
            root.GetProperty("status").GetString()!,
            ParseMoney(root.GetProperty("amount")),
            fee,
            net,
            ReadDate(root, "create_time"));
    }

    private static PayPalTransaction ParseTransaction(JsonElement info)
    {
        decimal? amount = null;
        string? currency = null;
        decimal? fee = null;
        if (info.TryGetProperty("transaction_amount", out var amountElement))
        {
            amount = ParseMoney(amountElement);
            currency = amountElement.GetProperty("currency_code").GetString();
        }
        if (info.TryGetProperty("fee_amount", out var feeElement)) fee = ParseMoney(feeElement);

        return new PayPalTransaction(
            ReadString(info, "transaction_id")!,
            ReadString(info, "paypal_reference_id"),
            ReadString(info, "transaction_event_code"),
            ReadString(info, "transaction_status"),
            ReadDate(info, "transaction_initiation_date"),
            amount,
            currency,
            fee,
            ReadString(info, "invoice_id"),
            ReadString(info, "custom_field"));
    }

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        if ((root.TryGetProperty("status", out var status) &&
             string.Equals(status.GetString(), "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)) ||
            (root.TryGetProperty("links", out var links) && links.EnumerateArray().Any(link =>
                link.TryGetProperty("rel", out var rel) &&
                string.Equals(rel.GetString(), "payer-action", StringComparison.OrdinalIgnoreCase))))
        {
            throw new PayPalPayerActionRequiredException();
        }
    }

    private static (string Name, string Message, string? DebugId) ParseError(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var message = ReadString(root, "message") ?? "PayPal rejected the operation.";
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                var safeDetails = details.EnumerateArray().Select(detail =>
                    $"{ReadString(detail, "issue")}: {ReadString(detail, "description")}")
                    .Where(detail => !string.IsNullOrWhiteSpace(detail))
                    .ToArray();
                if (safeDetails.Length > 0) message += " " + string.Join(" ", safeDetails);
            }
            return (ReadString(root, "name") ?? "PAYPAL_ERROR", message, ReadString(root, "debug_id"));
        }
        catch (JsonException)
        {
            return ("PAYPAL_ERROR", "PayPal rejected the operation.", null);
        }
    }

    private static decimal ParseMoney(JsonElement money) => decimal.Parse(
        money.GetProperty("value").GetString()!, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static string Money(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Rfc3339(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    private static DateTimeOffset? ReadDate(JsonElement element, string name) =>
        DateTimeOffset.TryParse(ReadString(element, name), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var value) ? value : null;
}
