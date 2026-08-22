using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public class PayPalGateway : IPayPalGateway
{
    private readonly HttpClient _httpClient;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly IOptions<PayPalOptions> _options;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(
        HttpClient httpClient,
        PayPalAccessTokenProvider tokenProvider,
        IOptions<PayPalOptions> options,
        ILogger<PayPalGateway> logger)
    {
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _options = options;
        _logger = logger;
        _httpClient.Timeout = TimeSpan.FromSeconds(45);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken cancellationToken = default)
    {
        var payload = BuildCreateOrderPayload(request);
        using var created = await SendAsync(
            HttpMethod.Post,
            "/v2/checkout/orders",
            payload,
            request.IdempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        ThrowIfPayerActionRequired(created.RootElement);

        var authorizations = FindAuthorizations(created.RootElement);
        if (authorizations.Count == 0)
        {
            var orderId = GetString(created.RootElement, "id") ??
                          throw new PayPalApiException(502, "PayPal create-order response did not include an order id.");

            using var authorized = await SendAsync(
                HttpMethod.Post,
                $"/v2/checkout/orders/{orderId}/authorize",
                new JsonObject(),
                $"{request.IdempotencyKey}-authorize",
                preferRepresentation: true,
                cancellationToken);

            ThrowIfPayerActionRequired(authorized.RootElement);
            return MapAuthorization(authorized.RootElement, orderId);
        }

        return MapAuthorization(created.RootElement, GetString(created.RootElement, "id") ?? string.Empty);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        using var document = await SendAsync(
            HttpMethod.Get,
            $"/v2/payments/authorizations/{authorizationId}",
            body: null,
            requestId: null,
            preferRepresentation: true,
            cancellationToken);

        return MapAuthorizationResource(document.RootElement, paypalOrderId: string.Empty);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["amount"] = Money(currency, amount)
        };

        using var document = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize",
            payload,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        return MapAuthorizationResource(document.RootElement, paypalOrderId: string.Empty);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["amount"] = Money(currency, amount),
            ["invoice_id"] = invoiceId,
            ["final_capture"] = true
        };

        using var document = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/capture",
            payload,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        var root = document.RootElement;
        var breakdown = root.TryGetProperty("seller_receivable_breakdown", out var br) ? br : default;
        return new PayPalCaptureResult
        {
            CaptureId = GetString(root, "id") ?? throw new PayPalApiException(502, "PayPal capture response did not include an id."),
            Status = GetString(root, "status") ?? "UNKNOWN",
            CapturedAmount = GetMoney(root, "amount"),
            Currency = GetCurrency(root, "amount", currency),
            PaypalFee = TryGetMoney(breakdown, "paypal_fee"),
            NetProceeds = TryGetMoney(breakdown, "net_amount")
        };
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/void",
            body: null,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        JsonNode payload = new JsonObject();
        if (amount.HasValue)
        {
            payload = new JsonObject
            {
                ["amount"] = Money(currency, amount.Value)
            };
        }

        using var document = await SendAsync(
            HttpMethod.Post,
            $"/v2/payments/captures/{captureId}/refund",
            payload,
            idempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        var root = document.RootElement;
        return new PayPalRefundResult
        {
            RefundId = GetString(root, "id") ?? throw new PayPalApiException(502, "PayPal refund response did not include an id."),
            Status = GetString(root, "status") ?? "UNKNOWN",
            Amount = GetMoney(root, "amount"),
            Currency = GetCurrency(root, "amount", currency)
        };
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(PayPalVaultCardRequest request, CancellationToken cancellationToken = default)
    {
        var customer = new JsonObject
        {
            ["merchant_customer_id"] = request.MerchantCustomerId
        };
        if (!string.IsNullOrWhiteSpace(request.PayPalCustomerId))
        {
            customer["id"] = request.PayPalCustomerId;
        }

        var payload = new JsonObject
        {
            ["customer"] = customer,
            ["payment_source"] = new JsonObject
            {
                ["card"] = BuildCardNode(request.Card, includeStoredCredential: false)
            }
        };

        using var document = await SendAsync(
            HttpMethod.Post,
            "/v3/vault/payment-tokens",
            payload,
            request.IdempotencyKey,
            preferRepresentation: true,
            cancellationToken);

        ThrowIfPayerActionRequired(document.RootElement);

        var root = document.RootElement;
        var card = root.TryGetProperty("payment_source", out var source) && source.TryGetProperty("card", out var cardEl)
            ? cardEl
            : default;

        var lastDigits = GetString(card, "last_digits");
        if (string.IsNullOrEmpty(lastDigits) && request.Card.Number.Length >= 4)
        {
            lastDigits = request.Card.Number[^4..];
        }

        var customerId = root.TryGetProperty("customer", out var customerEl) ? GetString(customerEl, "id") : null;

        return new PayPalVaultedCard
        {
            VaultId = GetString(root, "id") ?? throw new PayPalApiException(502, "PayPal vault response did not include a token id."),
            LastDigits = lastDigits ?? "0000",
            Brand = GetString(card, "brand") ?? "UNKNOWN",
            Expiry = GetString(card, "expiry") ?? request.Card.Expiry,
            CardholderName = GetString(card, "name") ?? request.Card.Name,
            PayPalCustomerId = customerId
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        using var _ = await SendAsync(
            HttpMethod.Delete,
            $"/v3/vault/payment-tokens/{vaultId}",
            body: null,
            requestId: null,
            preferRepresentation: false,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> ListAllTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalReportedTransaction>();
        var windowStart = from;
        while (windowStart <= to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            int totalPages;
            do
            {
                var path =
                    $"/v1/reporting/transactions?start_date={Uri.EscapeDataString(FormatTime(windowStart))}&end_date={Uri.EscapeDataString(FormatTime(windowEnd))}&page={page}&page_size=100&fields=transaction_info";

                using var document = await SendAsync(
                    HttpMethod.Get,
                    path,
                    body: null,
                    requestId: null,
                    preferRepresentation: false,
                    cancellationToken);

                var root = document.RootElement;
                totalPages = root.TryGetProperty("total_pages", out var pagesEl) && pagesEl.TryGetInt32(out var pages)
                    ? pages
                    : 1;

                if (root.TryGetProperty("transaction_details", out var details) && details.ValueKind == JsonValueKind.Array)
                {
                    foreach (var detail in details.EnumerateArray())
                    {
                        var info = detail.TryGetProperty("transaction_info", out var tx) ? tx : detail;
                        results.Add(new PayPalReportedTransaction
                        {
                            TransactionId = GetString(info, "transaction_id") ?? string.Empty,
                            ReferenceId = GetString(info, "paypal_reference_id"),
                            CustomField = GetString(info, "custom_field"),
                            InvoiceId = GetString(info, "invoice_id"),
                            EventCode = GetString(info, "transaction_event_code"),
                            Status = GetString(info, "transaction_status"),
                            Amount = TryGetMoney(info, "transaction_amount"),
                            Currency = info.TryGetProperty("transaction_amount", out var amt)
                                ? GetString(amt, "currency_code")
                                : null,
                            InitiationDate = TryGetTime(info, "transaction_initiation_date"),
                            UpdatedDate = TryGetTime(info, "transaction_updated_date")
                        });
                    }
                }

                page++;
            } while (page <= totalPages);

            if (windowEnd == to)
            {
                break;
            }

            windowStart = windowEnd.AddSeconds(1);
        }

        return results;
    }

    private JsonObject BuildCreateOrderPayload(PayPalAuthorizeRequest request)
    {
        var items = new JsonArray();
        decimal itemTotal = 0m;
        foreach (var item in request.Items)
        {
            var unit = PayPalMoney.Round(item.UnitPrice, request.Currency);
            itemTotal += unit * item.Quantity;
            items.Add(new JsonObject
            {
                ["name"] = item.Name,
                ["quantity"] = item.Quantity.ToString(CultureInfo.InvariantCulture),
                ["unit_amount"] = Money(request.Currency, unit)
            });
        }

        var purchaseUnit = new JsonObject
        {
            ["custom_id"] = request.CustomId,
            ["invoice_id"] = request.InvoiceId,
            ["amount"] = new JsonObject
            {
                ["currency_code"] = request.Currency,
                ["value"] = PayPalMoney.Format(request.Amount, request.Currency),
                ["breakdown"] = new JsonObject
                {
                    ["item_total"] = Money(request.Currency, itemTotal)
                }
            },
            ["items"] = items
        };

        var paymentSource = new JsonObject();
        if (!string.IsNullOrWhiteSpace(request.VaultId))
        {
            paymentSource["card"] = new JsonObject
            {
                ["vault_id"] = request.VaultId,
                ["stored_credential"] = new JsonObject
                {
                    ["payment_initiator"] = "CUSTOMER",
                    ["payment_type"] = "UNSCHEDULED",
                    ["usage"] = "SUBSEQUENT"
                }
            };
        }
        else if (request.Card is not null)
        {
            paymentSource["card"] = BuildCardNode(request.Card, includeStoredCredential: false);
        }
        else
        {
            throw new CheckoutException(400, "A card or saved payment method is required to pay.");
        }

        return new JsonObject
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new JsonArray { purchaseUnit },
            ["payment_source"] = paymentSource
        };
    }

    private static JsonObject BuildCardNode(PayPalCardDetails card, bool includeStoredCredential)
    {
        var node = new JsonObject
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry
        };

        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            node["security_code"] = card.SecurityCode;
        }

        if (!string.IsNullOrWhiteSpace(card.Name))
        {
            node["name"] = card.Name;
        }

        if (card.BillingAddress is not null)
        {
            var address = new JsonObject
            {
                ["country_code"] = card.BillingAddress.CountryCode
            };
            if (!string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1))
            {
                address["address_line_1"] = card.BillingAddress.AddressLine1;
            }

            if (!string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine2))
            {
                address["address_line_2"] = card.BillingAddress.AddressLine2;
            }

            if (!string.IsNullOrWhiteSpace(card.BillingAddress.AdminArea2))
            {
                address["admin_area_2"] = card.BillingAddress.AdminArea2;
            }

            if (!string.IsNullOrWhiteSpace(card.BillingAddress.AdminArea1))
            {
                address["admin_area_1"] = card.BillingAddress.AdminArea1;
            }

            if (!string.IsNullOrWhiteSpace(card.BillingAddress.PostalCode))
            {
                address["postal_code"] = card.BillingAddress.PostalCode;
            }

            node["billing_address"] = address;
        }

        if (includeStoredCredential)
        {
            node["stored_credential"] = new JsonObject
            {
                ["payment_initiator"] = "CUSTOMER",
                ["payment_type"] = "UNSCHEDULED",
                ["usage"] = "FIRST"
            };
        }

        return node;
    }

    private async Task<JsonDocument> SendAsync(
        HttpMethod method,
        string relativePath,
        JsonNode? body,
        string? requestId,
        bool preferRepresentation,
        CancellationToken cancellationToken,
        bool retryOnUnauthorized = true)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, PayPalUrl.Combine(PayPalUrl.ResolveBase(_options.Value), relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (preferRepresentation)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (body is not null && method != HttpMethod.Get && method != HttpMethod.Delete)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        else if (method == HttpMethod.Post && body is null)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && retryOnUnauthorized)
        {
            _tokenProvider.Invalidate();
            return await SendAsync(method, relativePath, body, requestId, preferRepresentation, cancellationToken, retryOnUnauthorized: false);
        }

        _logger.LogInformation("PayPal {Method} {Path} returned {StatusCode}.", method.Method, SanitizePath(relativePath), (int)response.StatusCode);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent || string.IsNullOrWhiteSpace(responseBody))
        {
            if (response.IsSuccessStatusCode)
            {
                return JsonDocument.Parse("{}");
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw ParseError((int)response.StatusCode, responseBody);
        }

        return JsonDocument.Parse(string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody);
    }

    private static PayPalApiException ParseError(int statusCode, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var name = GetString(root, "name");
            var message = GetString(root, "message") ?? "PayPal request failed.";
            var debugId = GetString(root, "debug_id");
            string? issue = null;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                var first = details.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object)
                {
                    issue = GetString(first, "issue");
                    var description = GetString(first, "description");
                    if (!string.IsNullOrEmpty(description))
                    {
                        message = $"{message} {description}";
                    }
                }
            }

            if (!string.IsNullOrEmpty(name))
            {
                message = $"{name}: {message}";
            }

            return new PayPalApiException(statusCode, message.Trim(), debugId, issue);
        }
        catch (JsonException)
        {
            return new PayPalApiException(statusCode, "PayPal request failed.");
        }
    }

    private static void ThrowIfPayerActionRequired(JsonElement root)
    {
        var status = GetString(root, "status");
        if (!string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string? href = null;
        if (root.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
        {
            foreach (var link in links.EnumerateArray())
            {
                if (string.Equals(GetString(link, "rel"), "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    href = GetString(link, "href");
                    break;
                }
            }
        }

        throw new PayPalPayerActionRequiredException(
            "PayPal required a shopper to complete a browser challenge (for example 3-D Secure). This integration does not implement an approval round-trip.",
            href);
    }

    private static PayPalAuthorizationResult MapAuthorization(JsonElement order, string paypalOrderId)
    {
        var authorizations = FindAuthorizations(order);
        if (authorizations.Count == 0)
        {
            throw new PayPalApiException(502, "PayPal did not return an authorization for this order.");
        }

        var auth = authorizations[0];
        var mapped = MapAuthorizationResource(auth, string.IsNullOrEmpty(paypalOrderId) ? GetString(order, "id") ?? string.Empty : paypalOrderId);
        if (string.Equals(mapped.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalApiException(402, "PayPal denied the authorization.");
        }

        return mapped;
    }

    private static PayPalAuthorizationResult MapAuthorizationResource(JsonElement auth, string paypalOrderId)
    {
        var id = GetString(auth, "id") ?? throw new PayPalApiException(502, "PayPal authorization is missing an id.");
        return new PayPalAuthorizationResult
        {
            PayPalOrderId = paypalOrderId,
            AuthorizationId = id,
            Status = GetString(auth, "status") ?? "UNKNOWN",
            Amount = GetMoney(auth, "amount"),
            Currency = GetCurrency(auth, "amount", "USD"),
            ExpirationTime = TryGetTime(auth, "expiration_time"),
            CreateTime = TryGetTime(auth, "create_time")
        };
    }

    private static List<JsonElement> FindAuthorizations(JsonElement order)
    {
        var found = new List<JsonElement>();
        if (!order.TryGetProperty("purchase_units", out var units) || units.ValueKind != JsonValueKind.Array)
        {
            return found;
        }

        foreach (var unit in units.EnumerateArray())
        {
            if (!unit.TryGetProperty("payments", out var payments))
            {
                continue;
            }

            if (!payments.TryGetProperty("authorizations", out var auths) || auths.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            found.AddRange(auths.EnumerateArray());
        }

        return found;
    }

    private static JsonObject Money(string currency, decimal amount) => new()
    {
        ["currency_code"] = currency,
        ["value"] = PayPalMoney.Format(amount, currency)
    };

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static decimal GetMoney(JsonElement parent, string name)
    {
        return TryGetMoney(parent, name) ?? 0m;
    }

    private static decimal? TryGetMoney(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var money))
        {
            return null;
        }

        if (money.ValueKind == JsonValueKind.Object)
        {
            var value = GetString(money, "value");
            return string.IsNullOrEmpty(value) ? null : PayPalMoney.Parse(value);
        }

        return null;
    }

    private static string GetCurrency(JsonElement parent, string name, string fallback)
    {
        if (parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(name, out var money) &&
            money.ValueKind == JsonValueKind.Object)
        {
            return GetString(money, "currency_code") ?? fallback;
        }

        return fallback;
    }

    private static DateTimeOffset? TryGetTime(JsonElement parent, string name)
    {
        var raw = GetString(parent, name);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string SanitizePath(string path)
    {
        var query = path.IndexOf('?', StringComparison.Ordinal);
        return query < 0 ? path : path[..query];
    }
}
