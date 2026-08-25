using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalAuthorizationResult
{
    public string OrderId { get; init; } = "";
    public string AuthorizationId { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
}

public class PayPalCaptureResult
{
    public string CaptureId { get; init; } = "";
    public decimal GrossAmount { get; init; }
    public decimal FeeAmount { get; init; }
    public decimal NetAmount { get; init; }
}

public class PayPalRefundResult
{
    public string RefundId { get; init; } = "";
}

public class PayPalReauthorizeResult
{
    public string NewAuthorizationId { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
}

public class PayPalVaultResult
{
    public string VaultId { get; init; } = "";
    public string CustomerId { get; init; } = "";
    public string Last4 { get; init; } = "";
    public string Brand { get; init; } = "";
    public string Expiry { get; init; } = "";
}

public class PayPalSavedCard
{
    public string VaultId { get; init; } = "";
    public string Last4 { get; init; } = "";
    public string Brand { get; init; } = "";
    public string Expiry { get; init; } = "";
}

public class PayPalTransaction
{
    public string TransactionId { get; init; } = "";
    public string Status { get; init; } = "";
    public decimal Amount { get; init; }
    public decimal Fee { get; init; }
    public string CurrencyCode { get; init; } = "";
    public DateTimeOffset TransactionDate { get; init; }
}

public class PayPalException : Exception
{
    public string PayPalName { get; }
    public int HttpStatus { get; }

    public PayPalException(string message, string paypalName, int httpStatus)
        : base(message)
    {
        PayPalName = paypalName;
        HttpStatus = httpStatus;
    }
}

public class PayerActionRequiredException : Exception
{
    public PayerActionRequiredException()
        : base("PayPal returned PAYER_ACTION_REQUIRED: the card requires browser-based 3DS authentication which is not supported in this headless API.") { }
}

public class PayPalClient
{
    private readonly PayPalSettings _settings;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public PayPalClient(IOptions<PayPalSettings> options)
    {
        _settings = options.Value;
        _http = new HttpClient
        {
            BaseAddress = new Uri(_settings.GetBaseUrl() + "/")
        };
    }

    // ── Auth ───────────────────────────────────────────────────────────────

    private async Task<string> GetAccessTokenAsync()
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _accessToken;

        await _tokenLock.WaitAsync();
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiry)
                return _accessToken;

            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));

            var req = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                })
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();

            var doc = JsonNode.Parse(body)!;
            _accessToken = doc["access_token"]!.GetValue<string>();
            var expiresIn = doc["expires_in"]!.GetValue<int>();
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 300);

            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpRequestMessage> BuildRequestAsync(HttpMethod method, string path,
        object? body = null, string? idempotencyKey = null)
    {
        var token = await GetAccessTokenAsync();
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (idempotencyKey != null)
            req.Headers.TryAddWithoutValidation("PayPal-Request-Id", idempotencyKey);

        if (body != null)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return req;
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path,
        object? body = null, string? idempotencyKey = null, bool allowEmpty = false)
    {
        var req = await BuildRequestAsync(method, path, body, idempotencyKey);
        var resp = await _http.SendAsync(req);
        var raw = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            string name = "UNKNOWN";
            string message = raw;
            if (!string.IsNullOrEmpty(raw))
            {
                try
                {
                    var err = JsonNode.Parse(raw);
                    name = err?["name"]?.GetValue<string>() ?? "UNKNOWN";
                    message = err?["message"]?.GetValue<string>() ?? raw;
                }
                catch { }
            }
            throw new PayPalException(message, name, (int)resp.StatusCode);
        }

        if (allowEmpty && string.IsNullOrWhiteSpace(raw))
            return JsonNode.Parse("{}")!;

        return JsonNode.Parse(raw)!;
    }

    // ── Orders / Authorize ────────────────────────────────────────────────

    public async Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currency,
        string cardNumber, string expiry, string cvv,
        string cardholderName, string street, string city,
        string state, string country, string zipCode,
        string idempotencyKey)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new { amount = new { currency_code = currency, value = FormatAmount(amount) } }
            },
            payment_source = new
            {
                card = new
                {
                    number = cardNumber,
                    expiry,
                    security_code = cvv,
                    name = cardholderName,
                    billing_address = new
                    {
                        address_line_1 = street,
                        admin_area_2 = city,
                        admin_area_1 = state,
                        postal_code = zipCode,
                        country_code = country
                    }
                }
            }
        };

        JsonNode resp;
        try
        {
            resp = await SendAsync(HttpMethod.Post, "v2/checkout/orders", body, idempotencyKey);
        }
        catch (PayPalException)
        {
            throw;
        }

        var status = resp["status"]?.GetValue<string>() ?? "";
        if (status == "PAYER_ACTION_REQUIRED")
            throw new PayerActionRequiredException();

        return ExtractAuthorization(resp);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeWithVaultAsync(
        decimal amount, string currency, string vaultId, string idempotencyKey)
    {
        var body = new
        {
            intent = "AUTHORIZE",
            purchase_units = new[]
            {
                new { amount = new { currency_code = currency, value = FormatAmount(amount) } }
            },
            payment_source = new
            {
                card = new { vault_id = vaultId }
            }
        };

        var resp = await SendAsync(HttpMethod.Post, "v2/checkout/orders", body, idempotencyKey);

        var status = resp["status"]?.GetValue<string>() ?? "";
        if (status == "PAYER_ACTION_REQUIRED")
            throw new PayerActionRequiredException();

        return ExtractAuthorization(resp);
    }

    private static PayPalAuthorizationResult ExtractAuthorization(JsonNode resp)
    {
        var orderId = resp["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Missing order id in PayPal response");
        var auth = resp["purchase_units"]?[0]?["payments"]?["authorizations"]?[0]
            ?? throw new InvalidOperationException("No authorization in PayPal response");

        var authId = auth["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Missing authorization id");
        var expiresStr = auth["expiration_time"]?.GetValue<string>();
        var expiresAt = expiresStr != null
            ? DateTimeOffset.Parse(expiresStr)
            : DateTimeOffset.UtcNow.AddDays(29);

        return new PayPalAuthorizationResult
        {
            OrderId = orderId,
            AuthorizationId = authId,
            ExpiresAt = expiresAt
        };
    }

    // ── Capture ───────────────────────────────────────────────────────────

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency)
    {
        var body = new
        {
            amount = new { currency_code = currency, value = FormatAmount(amount) },
            final_capture = true
        };

        var resp = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/capture", body);

        return ExtractCapture(resp);
    }

    private static PayPalCaptureResult ExtractCapture(JsonNode resp)
    {
        var captureId = resp["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Missing capture id");
        var breakdown = resp["seller_receivable_breakdown"];

        decimal gross = ParseAmount(resp["amount"]);
        decimal fee = 0m;
        decimal net = gross;

        if (breakdown != null)
        {
            gross = ParseAmount(breakdown["gross_amount"]);
            fee = Math.Abs(ParseAmount(breakdown["paypal_fee"]));
            net = ParseAmount(breakdown["net_amount"]);
        }

        return new PayPalCaptureResult
        {
            CaptureId = captureId,
            GrossAmount = gross,
            FeeAmount = fee,
            NetAmount = net
        };
    }

    // ── Void ──────────────────────────────────────────────────────────────

    public async Task VoidAuthorizationAsync(string authorizationId)
    {
        await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/void",
            allowEmpty: true);
    }

    // ── Reauthorize ───────────────────────────────────────────────────────

    public async Task<PayPalReauthorizeResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency)
    {
        var body = new { amount = new { value = FormatAmount(amount), currency_code = currency } };

        var resp = await SendAsync(HttpMethod.Post,
            $"v2/payments/authorizations/{authorizationId}/reauthorize", body);

        var newAuthId = resp["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Missing authorization id in reauthorize response");
        var expiresStr = resp["expiration_time"]?.GetValue<string>();
        var expiresAt = expiresStr != null
            ? DateTimeOffset.Parse(expiresStr)
            : DateTimeOffset.UtcNow.AddDays(3);

        return new PayPalReauthorizeResult { NewAuthorizationId = newAuthId, ExpiresAt = expiresAt };
    }

    // ── Refund ────────────────────────────────────────────────────────────

    public async Task<PayPalRefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey)
    {
        object body = amount.HasValue
            ? new { amount = new { currency_code = currency, value = FormatAmount(amount.Value) } }
            : new { };

        var resp = await SendAsync(HttpMethod.Post,
            $"v2/payments/captures/{captureId}/refund", body, idempotencyKey);

        var refundId = resp["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Missing refund id");

        return new PayPalRefundResult { RefundId = refundId };
    }

    // ── Vault ─────────────────────────────────────────────────────────────

    public async Task<PayPalVaultResult> VaultCardAsync(
        string cardNumber, string expiry, string cardholderName,
        string street, string city, string state, string country, string zipCode)
    {
        // Step 1: create setup token
        var setupBody = new
        {
            payment_source = new
            {
                card = new
                {
                    number = cardNumber,
                    expiry,
                    name = cardholderName,
                    billing_address = new
                    {
                        address_line_1 = street,
                        admin_area_2 = city,
                        admin_area_1 = state,
                        postal_code = zipCode,
                        country_code = country
                    }
                }
            }
        };

        var setupResp = await SendAsync(HttpMethod.Post, "v3/vault/setup-tokens", setupBody,
            idempotencyKey: Guid.NewGuid().ToString());

        var setupTokenId = setupResp["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Missing setup token id");
        var customerId = setupResp["customer"]?["id"]?.GetValue<string>() ?? "";
        var cardInfo = setupResp["payment_source"]?["card"];

        // Step 2: create payment token from setup token
        var tokenBody = new
        {
            payment_source = new
            {
                token = new { id = setupTokenId, type = "SETUP_TOKEN" }
            }
        };

        var tokenResp = await SendAsync(HttpMethod.Post, "v3/vault/payment-tokens", tokenBody,
            idempotencyKey: Guid.NewGuid().ToString());

        var vaultId = tokenResp["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Missing vault token id");
        var finalCustomerId = tokenResp["customer"]?["id"]?.GetValue<string>()
            ?? customerId;
        var finalCard = tokenResp["payment_source"]?["card"];

        return new PayPalVaultResult
        {
            VaultId = vaultId,
            CustomerId = finalCustomerId,
            Last4 = finalCard?["last_digits"]?.GetValue<string>()
                ?? cardInfo?["last_digits"]?.GetValue<string>() ?? "****",
            Brand = finalCard?["brand"]?.GetValue<string>()
                ?? cardInfo?["brand"]?.GetValue<string>() ?? "UNKNOWN",
            Expiry = finalCard?["expiry"]?.GetValue<string>()
                ?? cardInfo?["expiry"]?.GetValue<string>() ?? expiry
        };
    }

    public async Task<List<PayPalSavedCard>> ListSavedCardsAsync(string customerId)
    {
        var token = await GetAccessTokenAsync();
        var req = new HttpRequestMessage(HttpMethod.Get,
            $"v3/vault/payment-tokens?customer_id={Uri.EscapeDataString(customerId)}&page_size=20");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _http.SendAsync(req);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return new List<PayPalSavedCard>();

        var raw = await resp.Content.ReadAsStringAsync();
        resp.EnsureSuccessStatusCode();

        var doc = JsonNode.Parse(raw)!;
        var tokens = doc["payment_tokens"]?.AsArray();
        var result = new List<PayPalSavedCard>();

        if (tokens == null) return result;

        foreach (var t in tokens)
        {
            if (t == null) continue;
            var card = t["payment_source"]?["card"];
            result.Add(new PayPalSavedCard
            {
                VaultId = t["id"]?.GetValue<string>() ?? "",
                Last4 = card?["last_digits"]?.GetValue<string>() ?? "****",
                Brand = card?["brand"]?.GetValue<string>() ?? "UNKNOWN",
                Expiry = card?["expiry"]?.GetValue<string>() ?? ""
            });
        }

        return result;
    }

    public async Task DeleteVaultTokenAsync(string vaultId)
    {
        await SendAsync(HttpMethod.Delete, $"v3/vault/payment-tokens/{vaultId}", allowEmpty: true);
    }

    // ── Reporting ─────────────────────────────────────────────────────────

    public async Task<List<PayPalTransaction>> GetTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to)
    {
        var all = new List<PayPalTransaction>();
        int page = 1;
        int totalPages = 1;

        do
        {
            // PayPal requires RFC 3339 with seconds and timezone offset, e.g. 2024-01-01T00:00:00+00:00
            var startDate = Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz"));
            var endDate = Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz"));
            var url = $"v1/reporting/transactions?start_date={startDate}&end_date={endDate}" +
                      $"&page_size=500&page={page}&fields=all";

            var token = await GetAccessTokenAsync();
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                string name = "UNKNOWN";
                string message = raw;
                if (!string.IsNullOrEmpty(raw))
                {
                    try
                    {
                        var err = JsonNode.Parse(raw);
                        name = err?["name"]?.GetValue<string>() ?? "UNKNOWN";
                        message = err?["message"]?.GetValue<string>() ?? raw;
                    }
                    catch { }
                }
                throw new PayPalException(message, name, (int)resp.StatusCode);
            }

            var doc = JsonNode.Parse(raw)!;
            totalPages = doc["total_pages"]?.GetValue<int>() ?? 1;

            var details = doc["transaction_details"]?.AsArray();
            if (details != null)
            {
                foreach (var d in details)
                {
                    if (d == null) continue;
                    var info = d["transaction_info"];
                    if (info == null) continue;

                    var dateStr = info["transaction_initiation_date"]?.GetValue<string>();
                    DateTimeOffset txDate = DateTimeOffset.UtcNow;
                    if (dateStr != null)
                        DateTimeOffset.TryParse(dateStr, out txDate);

                    var amountNode = info["transaction_amount"];
                    var feeNode = info["fee_amount"];

                    all.Add(new PayPalTransaction
                    {
                        TransactionId = info["transaction_id"]?.GetValue<string>() ?? "",
                        Status = info["transaction_status"]?.GetValue<string>() ?? "",
                        Amount = ParseAmount(amountNode),
                        Fee = ParseAmount(feeNode),
                        CurrencyCode = amountNode?["currency_code"]?.GetValue<string>() ?? "",
                        TransactionDate = txDate
                    });
                }
            }

            page++;
        } while (page <= totalPages);

        return all;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string FormatAmount(decimal amount) =>
        amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    private static decimal ParseAmount(JsonNode? node)
    {
        var val = node?["value"]?.GetValue<string>();
        if (val == null) return 0m;
        return decimal.TryParse(val, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }
}
