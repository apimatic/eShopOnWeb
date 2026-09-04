using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Integrations.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Plain-HTTPS implementation of <see cref="IPayPalProvider"/> against the PayPal
/// REST APIs (Orders v2, Payments v2, Vault v3 payment tokens, Transaction Search v1).
///
/// Contracts verified against the official PayPal API references (developer.paypal.com):
///  - POST /v1/oauth2/token                          (client credentials)
///  - POST /v2/checkout/orders + /{id}/authorize     (hold = authorization)
///  - POST /v2/payments/authorizations/{id}/capture  (take the money)
///  - POST /v2/payments/authorizations/{id}/reauthorize
///  - POST /v2/payments/authorizations/{id}/void     (release the hold)
///  - POST /v2/payments/captures/{id}/refund         (full/partial; PayPal-Request-Id idempotent)
///  - POST /v3/vault/setup-tokens -> /v3/vault/payment-tokens (save card)
///  - DELETE /v3/vault/payment-tokens/{id}           (remove saved card)
///  - GET  /v1/reporting/transactions                (transaction report, paged)
///
/// Card data is only ever used to build a request body and is never logged or stored.
/// </summary>
public class PayPalProvider : IPayPalProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalProvider> _logger;

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

    public PayPalProvider(HttpClient httpClient, PayPalSettings settings, ILogger<PayPalProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    private string BaseUrl => _settings.ResolvedBaseUrl;

    // ---------------------------------------------------------------- token

    private async Task<string> GetAccessTokenAsync()
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow.AddSeconds(30) < _accessTokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync();
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow.AddSeconds(30) < _accessTokenExpiresAt)
            {
                return _accessToken;
            }

            _settings.ValidateForPayments();

            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials"
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}")));

            var response = await _httpClient.SendAsync(request);
            var body = await ParseJsonAsync(response, "oauth2/token");
            if (!response.IsSuccessStatusCode)
            {
                ThrowForPayPalError(body, "PayPal authentication failed - check PayPal:ClientId / PayPal:ClientSecret.");
            }

            var token = body.Root.GetProperty("access_token").GetString()
                        ?? throw new PaymentGatewayException("PayPal token response did not contain an access_token.");
            var expiresIn = body.Root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds)
                ? seconds
                : 300;

            _accessToken = token;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            _logger.LogInformation("Obtained a new PayPal access token (expires in {ExpiresIn}s).", expiresIn);
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    // ---------------------------------------------------------------- helpers

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(HttpMethod method, string path, string what, HttpContent? content = null, string? requestId = null)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetAccessTokenAsync();
            var request = new HttpRequestMessage(method, $"{BaseUrl}{path}")
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.TryAddWithoutValidation("Accept-Language", "en_US");
            // Ask PayPal to return the full resource (amount, seller_receivable_breakdown, refunds...)
            // rather than the default minimal {id,status,links} body on POST operations.
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            if (!string.IsNullOrEmpty(requestId))
            {
                request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request);
            }
            catch (Exception ex)
            {
                throw new PaymentGatewayException($"Could not reach PayPal at {BaseUrl} ({what}): {ex.Message}", ex);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Cached token expired: drop it, force re-authentication, retry once.
                _accessToken = null;
                _accessTokenExpiresAt = DateTimeOffset.MinValue;
                response.Dispose();
                if (attempt == 0)
                {
                    continue;
                }
            }

            return response;
        }

        throw new PaymentGatewayException($"PayPal authentication failed for {what}.");
    }

    private static async Task<JsonResponse> ParseJsonAsync(HttpResponseMessage response, string what)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new JsonResponse(response.StatusCode, null);
        }

        try
        {
            return new JsonResponse(response.StatusCode, JsonDocument.Parse(text));
        }
        catch (JsonException)
        {
            throw new PaymentGatewayException($"PayPal returned a non-JSON response for {what} (HTTP {(int)response.StatusCode}).");
        }
    }

    private sealed record JsonResponse(HttpStatusCode StatusCode, JsonDocument? Body)
    {
        public JsonElement Root => Body?.RootElement ?? default;
        public bool HasBody => Body is not null;
    }

    private static string? TryGetDebugId(JsonResponse response) =>
        response.HasBody && response.Root.TryGetProperty("debug_id", out var d) ? d.GetString() : null;

    private static StringContent Json(object body) =>
        new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

    /// <summary>
    /// Extracts a PayPal error issue code from a problem response and throws a
    /// PaymentDeclinedException carrying only the issue + description (never request bodies).
    /// </summary>
    private static void ThrowForPayPalError(JsonResponse response, string fallbackMessage)
    {
        var issue = "UNKNOWN";
        var description = fallbackMessage;
        string? debugId = null;

        if (response.HasBody && response.Root.TryGetProperty("details", out var details) &&
            details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
        {
            var first = details[0];
            issue = first.TryGetProperty("issue", out var i) ? i.GetString() ?? issue : issue;
            description = first.TryGetProperty("description", out var d) ? d.GetString() ?? description : description;
        }
        else if (response.HasBody && response.Root.TryGetProperty("message", out var m))
        {
            description = m.GetString() ?? description;
        }
        if (response.HasBody && response.Root.TryGetProperty("debug_id", out var dbg))
        {
            debugId = dbg.GetString();
        }

        throw new PaymentDeclinedException(issue, $"{fallbackMessage} [{issue}: {description}]", debugId);
    }

    private static DateTimeOffset? ParseDate(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    private static decimal? ParseMoneyValue(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var money) && money.ValueKind == JsonValueKind.Object &&
            money.TryGetProperty("value", out var value) &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return amount;
        }
        return null;
    }

    private static string? ParseMoneyCurrency(JsonElement parent, string property)
    {
        if (parent.TryGetProperty(property, out var money) && money.ValueKind == JsonValueKind.Object &&
            money.TryGetProperty("currency_code", out var value))
        {
            return value.GetString();
        }
        return null;
    }

    // ---------------------------------------------------------------- authorize

    /// <inheritdoc />
    public async Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount,
        string currency,
        CardDetails? card,
        string? vaultId,
        string? invoiceId,
        string? customId,
        string requestId,
        bool storeCardInVault = false)
    {
        var paymentSource = BuildCardPaymentSource(card, vaultId, storeCardInVault);

        var purchaseUnit = new Dictionary<string, object?>
        {
            ["reference_id"] = "default",
            ["amount"] = new Dictionary<string, string>
            {
                ["currency_code"] = currency,
                ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
            }
        };
        if (!string.IsNullOrEmpty(invoiceId)) purchaseUnit["invoice_id"] = invoiceId;
        if (!string.IsNullOrEmpty(customId)) purchaseUnit["custom_id"] = customId;

        var createBody = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new List<object> { purchaseUnit },
            ["payment_source"] = paymentSource
        };

        var createResponse = await SendAuthenticatedAsync(
            HttpMethod.Post, "/v2/checkout/orders", "create order",
            Json(createBody), requestId);

        var created = await ParseJsonAsync(createResponse, "create order");
        if (created.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
        {
            ThrowForPayPalError(created, "PayPal rejected the payment (could not create the order).");
        }

        var payPalOrderId = created.Root.GetProperty("id").GetString()
                            ?? throw new PaymentGatewayException("PayPal order response had no id.");
        var status = created.Root.TryGetProperty("status", out var s) ? s.GetString() : null;

        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            // The card requires an in-browser challenge (e.g. 3D Secure). This app processes
            // direct card payments without a browser round-trip, so this is surfaced as a
            // decline with an explicit issue code rather than worked around.
            throw new PaymentDeclinedException(
                "PAYER_ACTION_REQUIRED",
                "PayPal requires the cardholder to approve this payment in a browser (3D Secure challenge); server-side processing of this card was declined.",
                TryGetDebugId(created));
        }

        // Some responses already carry the authorization; otherwise authorize the order now.
        if (TryReadAuthorization(created.Root, out var existingAuthId, out var existingAuthStatus, out var existingExpiration))
        {
            return new PayPalAuthorizationResult
            {
                PayPalOrderId = payPalOrderId,
                AuthorizationId = existingAuthId!,
                Status = existingAuthStatus ?? "CREATED",
                ExpirationTime = existingExpiration
            };
        }

        var authorizeResponse = await SendAuthenticatedAsync(
            HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize", "authorize order",
            Json(paymentSource), $"{requestId}-auth");

        var authorized = await ParseJsonAsync(authorizeResponse, "authorize order");
        if (authorized.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
        {
            ThrowForPayPalError(authorized, "PayPal declined the authorization for this order.");
        }

        if (!TryReadAuthorization(authorized.Root, out var authId, out var authStatus, out var expiration))
        {
            throw new PaymentGatewayException("PayPal authorize response did not contain an authorization.");
        }

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authId!,
            Status = authStatus ?? "CREATED",
            ExpirationTime = expiration
        };
    }

    private Dictionary<string, object?> BuildCardPaymentSource(CardDetails? card, string? vaultId, bool storeCardInVault)
    {
        var cardSource = new Dictionary<string, object?>();

        if (!string.IsNullOrEmpty(vaultId))
        {
            cardSource["vault_id"] = vaultId;
        }
        else if (card is not null)
        {
            cardSource["number"] = card.Number;
            cardSource["expiry"] = card.Expiry;
            if (!string.IsNullOrEmpty(card.Cvv)) cardSource["security_code"] = card.Cvv;
            if (!string.IsNullOrEmpty(card.CardHolderName)) cardSource["name"] = card.CardHolderName;

            var billing = card.BillingAddress;
            if (billing is not null && (!string.IsNullOrEmpty(billing.Street) || !string.IsNullOrEmpty(billing.PostalCode) || !string.IsNullOrEmpty(billing.CountryCode)))
            {
                var address = new Dictionary<string, string?>();
                if (!string.IsNullOrEmpty(billing.Street)) address["address_line_1"] = billing.Street;
                if (!string.IsNullOrEmpty(billing.City)) address["admin_area_2"] = billing.City;
                if (!string.IsNullOrEmpty(billing.State)) address["admin_area_1"] = billing.State;
                if (!string.IsNullOrEmpty(billing.PostalCode)) address["postal_code"] = billing.PostalCode;
                address["country_code"] = string.IsNullOrEmpty(billing.CountryCode) ? "US" : billing.CountryCode.ToUpperInvariant();
                cardSource["billing_address"] = address;
            }

            if (storeCardInVault)
            {
                cardSource["attributes"] = new Dictionary<string, object?>
                {
                    ["vault"] = new Dictionary<string, object?>
                    {
                        ["store_in_vault"] = "ON_SUCCESS"
                    }
                };
            }
        }
        else
        {
            throw new DomainValidationException("A payment requires either card details or a saved card.");
        }

        return new Dictionary<string, object?> { ["card"] = cardSource };
    }

    private static bool TryReadAuthorization(JsonElement orderRoot, out string? authorizationId, out string? status, out DateTimeOffset? expiration)
    {
        authorizationId = null;
        status = null;
        expiration = null;

        if (!orderRoot.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments) ||
                !payments.TryGetProperty("authorizations", out var auths) ||
                auths.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var auth in auths.EnumerateArray())
            {
                authorizationId = auth.TryGetProperty("id", out var id) ? id.GetString() : null;
                status = auth.TryGetProperty("status", out var st) ? st.GetString() : null;
                expiration = ParseDate(auth, "expiration_time");
                if (!string.IsNullOrEmpty(authorizationId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- status / capture / void

    /// <inheritdoc />
    public async Task<PayPalAuthorizationStatus?> GetAuthorizationStatusAsync(string authorizationId)
    {
        var response = await SendAuthenticatedAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}", "get authorization");
        var parsed = await ParseJsonAsync(response, "get authorization");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            ThrowForPayPalError(parsed, "Could not read the authorization status from PayPal.");
        }

        return new PayPalAuthorizationStatus
        {
            Id = parsed.Root.TryGetProperty("id", out var id) ? id.GetString() ?? authorizationId : authorizationId,
            Status = parsed.Root.TryGetProperty("status", out var s) ? s.GetString() ?? "UNKNOWN" : "UNKNOWN",
            ExpirationTime = ParseDate(parsed.Root, "expiration_time")
        };
    }

    /// <inheritdoc />
    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency, string requestId, bool finalCapture = true)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new Dictionary<string, string>
            {
                ["currency_code"] = currency,
                ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
            },
            ["final_capture"] = finalCapture
        };

        var response = await SendAuthenticatedAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture", "capture authorization",
            Json(body), requestId);

        var parsed = await ParseJsonAsync(response, "capture authorization");
        if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
        {
            ThrowForPayPalError(parsed, "PayPal declined the capture of the authorized payment.");
        }

        var captureAmount = ParseMoneyValue(parsed.Root, "amount") ?? amount;
        var captureCurrency = ParseMoneyCurrency(parsed.Root, "amount") ?? currency;
        decimal? fee = null;
        decimal? net = null;
        if (parsed.Root.TryGetProperty("seller_receivable_breakdown", out var breakdown))
        {
            fee = ParseMoneyValue(breakdown, "paypal_fee");
            net = ParseMoneyValue(breakdown, "net_amount");
        }

        return new PayPalCaptureResult
        {
            CaptureId = parsed.Root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Status = parsed.Root.TryGetProperty("status", out var s) ? s.GetString() ?? "PENDING" : "PENDING",
            CapturedAmount = captureAmount,
            FeeAmount = fee,
            NetAmount = net,
            Currency = captureCurrency
        };
    }

    /// <inheritdoc />
    public async Task<PayPalVoidResult> VoidAuthorizationAsync(string authorizationId, string requestId)
    {
        var response = await SendAuthenticatedAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", "void authorization",
            Json(new Dictionary<string, object?>()), requestId);

        var parsed = await ParseJsonAsync(response, "void authorization");
        if (!response.IsSuccessStatusCode)
        {
            if (parsed.HasBody && parsed.Root.TryGetProperty("details", out var details) &&
                details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0 &&
                details[0].TryGetProperty("issue", out var issueEl) &&
                string.Equals(issueEl.GetString(), "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase))
            {
                // Already voided: an idempotent cancel can proceed.
                return new PayPalVoidResult { AuthorizationId = authorizationId, Status = "VOIDED" };
            }
            ThrowForPayPalError(parsed, "PayPal rejected the void of the authorization.");
        }

        return new PayPalVoidResult
        {
            AuthorizationId = authorizationId,
            Status = parsed.Root.TryGetProperty("status", out var s) ? s.GetString() ?? "VOIDED" : "VOIDED"
        };
    }

    // ---------------------------------------------------------------- reauthorize

    /// <inheritdoc />
    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string staleAuthorizationId, decimal amount, string currency, string requestId)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new Dictionary<string, string>
            {
                ["currency_code"] = currency,
                ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
            }
        };

        var response = await SendAuthenticatedAsync(
            HttpMethod.Post, $"/v2/payments/authorizations/{staleAuthorizationId}/reauthorize", "reauthorize",
            Json(body), requestId);

        var parsed = await ParseJsonAsync(response, "reauthorize");
        if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
        {
            ThrowForPayPalError(parsed, "PayPal could not reauthorize the expired authorization.");
        }

        // The response is an authorization resource; the PayPal checkout order id is unchanged.
        return new PayPalAuthorizationResult
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = parsed.Root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Status = parsed.Root.TryGetProperty("status", out var s) ? s.GetString() ?? "CREATED" : "CREATED",
            ExpirationTime = ParseDate(parsed.Root, "expiration_time")
        };
    }

    // ---------------------------------------------------------------- refund

    /// <inheritdoc />
    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal amount, string currency, string requestId, string? noteToPayer = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = new Dictionary<string, string>
            {
                ["currency_code"] = currency,
                ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
            }
        };
        if (!string.IsNullOrEmpty(noteToPayer))
        {
            body["note_to_payer"] = noteToPayer;
        }

        var response = await SendAuthenticatedAsync(
            HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", "refund capture",
            Json(body), requestId);

        var parsed = await ParseJsonAsync(response, "refund capture");
        if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
        {
            ThrowForPayPalError(parsed, "PayPal rejected the refund.");
        }

        return new PayPalRefundResult
        {
            RefundId = parsed.Root.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            Status = parsed.Root.TryGetProperty("status", out var s) ? s.GetString() ?? "PENDING" : "PENDING",
            Amount = ParseMoneyValue(parsed.Root, "amount") ?? amount,
            Currency = ParseMoneyCurrency(parsed.Root, "amount") ?? currency
        };
    }

    // ---------------------------------------------------------------- vault

    /// <inheritdoc />
    public async Task<PayPalVaultResult> VaultCardAsync(CardDetails card, string customerId, string requestId)
    {
        var cardSource = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };
        if (!string.IsNullOrEmpty(card.Cvv)) cardSource["security_code"] = card.Cvv;
        if (!string.IsNullOrEmpty(card.CardHolderName)) cardSource["name"] = card.CardHolderName;

        var billing = card.BillingAddress;
        if (billing is not null && (!string.IsNullOrEmpty(billing.Street) || !string.IsNullOrEmpty(billing.PostalCode)))
        {
            var address = new Dictionary<string, string?>();
            if (!string.IsNullOrEmpty(billing.Street)) address["address_line_1"] = billing.Street;
            if (!string.IsNullOrEmpty(billing.City)) address["admin_area_2"] = billing.City;
            if (!string.IsNullOrEmpty(billing.State)) address["admin_area_1"] = billing.State;
            if (!string.IsNullOrEmpty(billing.PostalCode)) address["postal_code"] = billing.PostalCode;
            address["country_code"] = string.IsNullOrEmpty(billing.CountryCode) ? "US" : billing.CountryCode.ToUpperInvariant();
            cardSource["billing_address"] = address;
        }

        var setupBody = new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, string> { ["id"] = customerId },
            ["payment_source"] = new Dictionary<string, object?> { ["card"] = cardSource }
        };

        var setupResponse = await SendAuthenticatedAsync(
            HttpMethod.Post, "/v3/vault/setup-tokens", "create setup token",
            Json(setupBody), requestId);

        var setup = await ParseJsonAsync(setupResponse, "create setup token");
        if (!setupResponse.IsSuccessStatusCode)
        {
            ThrowForPayPalError(setup, "PayPal rejected the request to save this card.");
        }

        var setupStatus = setup.Root.TryGetProperty("status", out var st) ? st.GetString() : null;
        var setupTokenId = setup.Root.TryGetProperty("id", out var sid) ? sid.GetString() : null;
        if (!string.Equals(setupStatus, "APPROVED", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(setupTokenId))
        {
            throw new PaymentDeclinedException(
                setupStatus ?? "SETUP_TOKEN_NOT_APPROVED",
                "PayPal did not approve the card for saving without further action (the vault flow requires customer approval in a browser).");
        }

        var confirmBody = new Dictionary<string, object?>
        {
            ["customer"] = new Dictionary<string, string> { ["id"] = customerId },
            ["payment_source"] = new Dictionary<string, object?>
            {
                ["token"] = new Dictionary<string, string>
                {
                    ["id"] = setupTokenId,
                    ["type"] = "SETUP_TOKEN"
                }
            }
        };

        var confirmResponse = await SendAuthenticatedAsync(
            HttpMethod.Post, "/v3/vault/payment-tokens", "confirm payment token",
            Json(confirmBody), $"{requestId}-confirm");

        var confirm = await ParseJsonAsync(confirmResponse, "confirm payment token");
        if (!confirmResponse.IsSuccessStatusCode)
        {
            ThrowForPayPalError(confirm, "PayPal could not create the saved-card token.");
        }

        string? brand = null;
        string? last4 = null;
        string? expiry = null;
        if (confirm.Root.TryGetProperty("payment_source", out var ps) &&
            ps.TryGetProperty("card", out var cardEl))
        {
            brand = cardEl.TryGetProperty("brand", out var b) ? b.GetString() : null;
            last4 = cardEl.TryGetProperty("last_digits", out var l) ? l.GetString() : null;
            expiry = cardEl.TryGetProperty("expiry", out var x) ? x.GetString() : null;
        }

        return new PayPalVaultResult
        {
            VaultId = confirm.Root.TryGetProperty("id", out var vid) ? vid.GetString() ?? string.Empty : string.Empty,
            Brand = brand,
            Last4 = last4,
            Expiry = expiry
        };
    }

    /// <inheritdoc />
    public async Task DeleteVaultCardAsync(string vaultId)
    {
        var response = await SendAuthenticatedAsync(
            HttpMethod.Delete, $"/v3/vault/payment-tokens/{Uri.EscapeDataString(vaultId)}", "delete payment token");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return; // already gone; deletion is idempotent
        }

        if (!response.IsSuccessStatusCode)
        {
            var parsed = await ParseJsonAsync(response, "delete payment token");
            ThrowForPayPalError(parsed, "PayPal could not delete the saved card.");
        }
    }

    // ---------------------------------------------------------------- reporting

    /// <inheritdoc />
    public async Task<PayPalTransactionPage> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, int page, int pageSize)
    {
        var fromParam = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toParam = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var path = $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(fromParam)}&end_date={Uri.EscapeDataString(toParam)}&fields=all&page={page}&page_size={pageSize}";

        var response = await SendAuthenticatedAsync(HttpMethod.Get, path, "list transactions");
        var parsed = await ParseJsonAsync(response, "list transactions");
        if (!response.IsSuccessStatusCode)
        {
            ThrowForPayPalError(parsed, "PayPal could not produce the transaction report for this range.");
        }

        var transactions = new List<PayPalTransactionRecord>();
        if (parsed.Root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
        {
            foreach (var detail in details.EnumerateArray())
            {
                if (!detail.TryGetProperty("transaction_info", out var info))
                {
                    continue;
                }

                string? payerEmail = null;
                if (detail.TryGetProperty("payer_info", out var pi) && pi.TryGetProperty("payer_email", out var pe))
                {
                    payerEmail = pe.GetString();
                }

                transactions.Add(new PayPalTransactionRecord
                {
                    TransactionId = info.TryGetProperty("transaction_id", out var tid) ? tid.GetString() ?? string.Empty : string.Empty,
                    PayPalReferenceId = info.TryGetProperty("paypal_reference_id", out var rid) ? rid.GetString() : null,
                    PayPalReferenceIdType = info.TryGetProperty("paypal_reference_id_type", out var rt) ? rt.GetString() : null,
                    TransactionEventCode = info.TryGetProperty("transaction_event_code", out var ec) ? ec.GetString() : null,
                    TransactionStatus = info.TryGetProperty("transaction_status", out var ts) ? ts.GetString() : null,
                    Amount = ParseMoneyValue(info, "transaction_amount") ?? 0m,
                    FeeAmount = ParseMoneyValue(info, "fee_amount"),
                    Currency = ParseMoneyCurrency(info, "transaction_amount"),
                    InitiationDate = ParseDate(info, "transaction_initiation_date"),
                    InvoiceId = info.TryGetProperty("invoice_id", out var inv) ? inv.GetString() : null,
                    PayerEmail = payerEmail,
                    TransactionSubject = info.TryGetProperty("transaction_subject", out var subj) ? subj.GetString() : null
                });
            }
        }

        return new PayPalTransactionPage
        {
            Transactions = transactions,
            Page = parsed.Root.TryGetProperty("page", out var pageEl) && pageEl.TryGetInt32(out var pageNum) ? pageNum : page,
            TotalPages = parsed.Root.TryGetProperty("total_pages", out var tp) && tp.TryGetInt32(out var tpi) ? tpi : 0,
            TotalItems = parsed.Root.TryGetProperty("total_items", out var ti) && ti.TryGetInt32(out var tiInt) ? tiInt : transactions.Count
        };
    }
}
