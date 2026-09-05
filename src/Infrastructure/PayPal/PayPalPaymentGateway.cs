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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal REST adapter. Each capability was verified against PayPal's API reference and confirmed
/// against the sandbox before it was written down here:
/// <list type="bullet">
/// <item>OAuth2 client credentials: <c>POST /v1/oauth2/token</c> with the REST app credentials.</item>
/// <item>Hold at checkout: <c>POST /v2/checkout/orders</c> with <c>intent=AUTHORIZE</c> and a card
/// payment source, or <c>payment_source.card.vault_id</c> for a saved card. PayPal authorizes such an
/// order itself, so the hold is read from the response; when it leaves the order <c>CREATED</c> the
/// hold is placed with <c>POST /v2/checkout/orders/{id}/authorize</c> instead.</item>
/// <item>Take the money at fulfilment: <c>POST /v2/payments/authorizations/{id}/capture</c>, which
/// answers with <c>seller_receivable_breakdown</c> (gross, <c>paypal_fee</c>, net).</item>
/// <item>Renew a stale hold: <c>POST /v2/payments/authorizations/{id}/reauthorize</c>.</item>
/// <item>Release a hold: <c>POST /v2/payments/authorizations/{id}/void</c> (204, read back for status).</item>
/// <item>Return money: <c>POST /v2/payments/captures/{id}/refund</c> with an amount for a partial return.</item>
/// <item>Save a card: <c>POST /v3/vault/setup-tokens</c> then <c>POST /v3/vault/payment-tokens</c>;
/// forget it with <c>DELETE /v3/vault/payment-tokens/{id}</c>. Both need the <c>Customer-Context</c> header.</item>
/// <item>Reconcile: <c>GET /v1/reporting/transactions</c>, walked page by page.</item>
/// </list>
/// Card data is only ever put on the wire: no request body is logged, and failures keep to PayPal's
/// problem details (name, details[].issue, debug_id).
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    /// <summary>Named client with the longer timeout a money movement needs.</summary>
    public const string HTTP_CLIENT_NAME = "paypal-payments";

    private const int REPORT_PAGE_SIZE = 100;
    private const int MAX_REPORT_PAGES = 200;

    private static readonly JsonSerializerOptions _serializeOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayPalAccessTokenProvider _tokenProvider;
    private readonly IOptions<PayPalSettings> _settings;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(IHttpClientFactory httpClientFactory, PayPalAccessTokenProvider tokenProvider,
        IOptions<PayPalSettings> settings, IAppLogger<PayPalPaymentGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _settings = settings;
        _logger = logger;
    }

    public string Currency => _settings.Value.Currency;

    public async Task<PaymentAuthorization> AuthorizeAsync(AuthorizePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var paymentSource = BuildCardPaymentSource(request);

        var createOrder = new Dictionary<string, object?>
        {
            ["intent"] = "AUTHORIZE",
            ["purchase_units"] = new List<object?>
            {
                new Dictionary<string, object?>
                {
                    ["reference_id"] = "eshop-order",
                    ["invoice_id"] = request.InvoiceId,
                    ["custom_id"] = request.CustomId,
                    ["description"] = request.Description,
                    ["amount"] = Money(request.Amount, request.Currency)
                }
            },
            ["payment_source"] = paymentSource
        };

        var order = await SendAsync(HttpMethod.Post, "/v2/checkout/orders", createOrder, request.RequestId, null,
            cancellationToken).ConfigureAwait(false);

        var authorization = FindAuthorization(order);
        if (authorization is null)
        {
            if (RequiresPayerAction(order))
            {
                throw ActionRequired();
            }

            // Some stored-card payments come back CREATED with an authorize link rather than a hold.
            var payPalOrderId = order.Text("id") ?? throw new PaymentProcessorException(
                "The payment processor did not return an order id for the payment.");

            order = await SendAsync(HttpMethod.Post, $"/v2/checkout/orders/{payPalOrderId}/authorize",
                new Dictionary<string, object?> { ["payment_source"] = paymentSource },
                $"{request.RequestId}-authorize", null, cancellationToken).ConfigureAwait(false);

            authorization = FindAuthorization(order);
        }

        if (authorization is null)
        {
            if (RequiresPayerAction(order))
            {
                throw ActionRequired();
            }

            throw new PaymentProcessorException(
                "The payment processor accepted the payment but returned no hold to capture.");
        }

        var result = ToAuthorization(order, authorization.Value);

        if (!IsHoldAccepted(result.Status))
        {
            _logger.LogWarning($"PayPal refused the card for hold {result.AuthorizationId} (status {result.Status}).");
            throw new CardDeclinedException(DescribeDecline(result));
        }

        return result;
    }

    public async Task<PaymentAuthorization?> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(authorizationId))
        {
            return null;
        }

        try
        {
            var authorization = await SendAsync(HttpMethod.Get, $"/v2/payments/authorizations/{authorizationId}",
                null, null, null, cancellationToken).ConfigureAwait(false);

            return new PaymentAuthorization
            {
                PayPalOrderId = string.Empty,
                AuthorizationId = authorizationId,
                Status = authorization.Text("status") ?? string.Empty,
                ExpirationTime = authorization.Instant("expiration_time"),
                Amount = authorization.MoneyValue(),
                Currency = authorization.MoneyCurrency()
            };
        }
        catch (PaymentProcessorException exception) when (exception.HttpStatus == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<CapturedPayment> CaptureAsync(string authorizationId, decimal amount, string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Money(amount, Currency),
            ["final_capture"] = true
        };

        var capture = await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/capture",
            body, requestId, null, cancellationToken).ConfigureAwait(false);

        var breakdown = capture.Prop("seller_receivable_breakdown");
        return new CapturedPayment
        {
            CaptureId = capture.Text("id") ?? throw new PaymentProcessorException(
                "The payment processor took the money but returned no capture id."),
            Status = capture.Text("status") ?? string.Empty,
            GrossAmount = capture.MoneyValue(),
            FeeAmount = breakdown.MoneyValue("paypal_fee"),
            NetAmount = breakdown.MoneyValue("net_amount"),
            Currency = capture.MoneyCurrency() ?? Currency
        };
    }

    public async Task<PaymentAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string requestId,
        CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?> { ["amount"] = Money(amount, Currency) };

        var authorization = await SendAsync(HttpMethod.Post,
            $"/v2/payments/authorizations/{authorizationId}/reauthorize", body, requestId, null, cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrEmpty(authorization.Text("id")))
        {
            throw new PaymentProcessorException("The payment processor renewed the hold but returned no id for it.");
        }

        var result = new PaymentAuthorization
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = authorization.Text("id")!,
            Status = authorization.Text("status") ?? string.Empty,
            ExpirationTime = authorization.Instant("expiration_time"),
            Amount = authorization.MoneyValue(),
            Currency = authorization.MoneyCurrency()
        };

        if (!IsHoldAccepted(result.Status))
        {
            throw new CardDeclinedException(DescribeDecline(result));
        }

        return result;
    }

    public async Task<PaymentAuthorization> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken = default)
    {
        // A void answers 204 with no body, so the hold is read back to report its new state.
        await SendAsync(HttpMethod.Post, $"/v2/payments/authorizations/{authorizationId}/void", null, requestId,
            null, cancellationToken).ConfigureAwait(false);

        var authorization = await GetAuthorizationAsync(authorizationId, cancellationToken).ConfigureAwait(false);
        return authorization ?? new PaymentAuthorization
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = authorizationId,
            Status = "VOIDED"
        };
    }

    public async Task<RefundedPayment> RefundAsync(string captureId, decimal amount, string requestId,
        string? noteToPayer, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["amount"] = Money(amount, Currency)
        };

        if (!string.IsNullOrWhiteSpace(noteToPayer))
        {
            body["note_to_payer"] = noteToPayer;
        }

        var refund = await SendAsync(HttpMethod.Post, $"/v2/payments/captures/{captureId}/refund", body, requestId,
            null, cancellationToken).ConfigureAwait(false);

        var breakdown = refund.Prop("seller_payable_breakdown");
        return new RefundedPayment
        {
            RefundId = refund.Text("id") ?? throw new PaymentProcessorException(
                "The payment processor refunded the payment but returned no refund id."),
            Status = refund.Text("status") ?? string.Empty,
            Amount = refund.MoneyValue(),
            FeeReturned = breakdown.MoneyValue("paypal_fee"),
            NetAmount = breakdown.MoneyValue("net_amount"),
            TotalRefunded = breakdown.Prop("total_refunded_amount") is null ? amount : breakdown.MoneyValue("total_refunded_amount"),
            Currency = refund.MoneyCurrency() ?? Currency
        };
    }

    public async Task<SavedCardToken> SaveCardAsync(CardDetails card, string shopperKey,
        CancellationToken cancellationToken = default)
    {
        var setupToken = await SendAsync(HttpMethod.Post, "/v3/vault/setup-tokens",
            new Dictionary<string, object?>
            {
                ["payment_source"] = new Dictionary<string, object?> { ["card"] = BuildCardBody(card) }
            }, null, shopperKey, cancellationToken).ConfigureAwait(false);

        var setupTokenId = setupToken.Text("id") ?? throw new PaymentProcessorException(
            "The payment processor created a card set-up token but returned no id for it.");
        var customerId = setupToken.Prop("customer").Text("id");

        // PayPal approves a card set-up token itself when no verification method is asked for. Any
        // other status would mean the cardholder has to approve the card in a browser, which this
        // integration does not support.
        var status = setupToken.Text("status");
        if (status is not (null or "APPROVED"))
        {
            throw new PaymentProcessorException(
                "The payment processor will not save this card without the cardholder approving it. " +
                "Pay with the card for this order instead.", status);
        }

        if (string.IsNullOrEmpty(customerId))
        {
            throw new PaymentProcessorException("The payment processor saved the card without a customer record.");
        }

        var paymentToken = await SendAsync(HttpMethod.Post, "/v3/vault/payment-tokens",
            new Dictionary<string, object?>
            {
                ["payment_source"] = new Dictionary<string, object?>
                {
                    ["token"] = new Dictionary<string, object?> { ["id"] = setupTokenId, ["type"] = "SETUP_TOKEN" }
                }
            }, null, customerId, cancellationToken).ConfigureAwait(false);

        var vaultId = paymentToken.Text("id") ?? throw new PaymentProcessorException(
            "The payment processor saved the card but returned no token for it.");
        var saved = paymentToken.Prop("payment_source").Prop("card");

        return new SavedCardToken
        {
            VaultId = vaultId,
            PayPalCustomerId = customerId,
            Brand = saved.Text("brand"),
            Last4 = saved.Text("last_digits"),
            Expiry = saved.Text("expiry"),
            CardHolderName = saved.Text("name"),
            BillingCountry = saved.Prop("billing_address").Text("country_code")
        };
    }

    public async Task DeleteSavedCardAsync(string vaultId, string payPalCustomerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await SendAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", null, null, payPalCustomerId,
                cancellationToken).ConfigureAwait(false);
        }
        catch (PaymentProcessorException exception) when (exception.HttpStatus == (int)HttpStatusCode.NotFound)
        {
            // Already gone at the processor, which is what the caller wanted.
        }
    }

    public async Task<IReadOnlyList<ProcessorTransactionLine>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var lines = new List<ProcessorTransactionLine>();

        for (var page = 1; page <= MAX_REPORT_PAGES; page++)
        {
            var query = "?start_date=" + Uri.EscapeDataString(ReportTimestamp(from))
                + "&end_date=" + Uri.EscapeDataString(ReportTimestamp(to))
                + "&fields=all&balance_affecting_records_only=N"
                + $"&page_size={REPORT_PAGE_SIZE}&page={page}";

            var response = await SendAsync(HttpMethod.Get, "/v1/reporting/transactions" + query, null, null, null,
                cancellationToken).ConfigureAwait(false);

            var transactions = response.Prop("transaction_details");
            var pageCount = 0;
            foreach (var entry in transactions.ArrayOrEmpty())
            {
                var info = entry.Prop("transaction_info");
                if (info is not { ValueKind: JsonValueKind.Object })
                {
                    continue;
                }

                pageCount++;
                var fee = info.Prop("fee_amount");
                lines.Add(new ProcessorTransactionLine
                {
                    TransactionId = info.Text("transaction_id") ?? string.Empty,
                    ReferenceId = info.Text("paypal_reference_id"),
                    ReferenceIdType = info.Text("paypal_reference_id_type"),
                    EventCode = info.Text("transaction_event_code"),
                    Status = info.Text("transaction_status"),
                    Amount = info.MoneyValue("transaction_amount"),
                    Currency = info.MoneyCurrency("transaction_amount") ?? string.Empty,
                    FeeAmount = fee is null ? null : fee.Value.MoneyValueHere(),
                    InvoiceId = info.Text("invoice_id"),
                    CustomField = info.Text("custom_field"),
                    TransactionDate = info.Instant("transaction_initiation_date") ?? DateTimeOffset.MinValue
                });
            }

            var totalPages = response.Prop("total_pages") is { } element && element.TryGetInt32(out var parsed)
                ? parsed
                : 1;

            if (pageCount == 0 || page >= totalPages)
            {
                break;
            }
        }

        return lines;
    }

    /// <summary>PayPal's transaction search wants RFC-3339 timestamps in UTC.</summary>
    private static string ReportTimestamp(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static PaymentAuthorization ToAuthorization(JsonElement order, JsonElement authorization)
        => new()
        {
            PayPalOrderId = order.Text("id") ?? string.Empty,
            AuthorizationId = authorization.Text("id") ?? throw new PaymentProcessorException(
                "The payment processor returned a hold without an id."),
            Status = authorization.Text("status") ?? string.Empty,
            ExpirationTime = authorization.Instant("expiration_time"),
            Amount = authorization.MoneyValue(),
            Currency = authorization.MoneyCurrency(),
            DeclineCode = authorization.Prop("status_details").Text("reason")
        };

    private static Dictionary<string, object?> BuildCardPaymentSource(AuthorizePaymentRequest request)
    {
        if (request.SavedCard is not null)
        {
            return new Dictionary<string, object?>
            {
                ["card"] = new Dictionary<string, object?> { ["vault_id"] = request.SavedCard.VaultId }
            };
        }

        if (request.Card is null)
        {
            throw new ArgumentException("A payment needs either a card or a saved card.", nameof(request));
        }

        return new Dictionary<string, object?> { ["card"] = BuildCardBody(request.Card) };
    }

    private static Dictionary<string, object?> BuildCardBody(CardDetails card)
    {
        var body = new Dictionary<string, object?>
        {
            ["number"] = card.Number,
            ["expiry"] = card.Expiry,
            ["name"] = card.CardHolderName
        };

        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            body["security_code"] = card.SecurityCode;
        }

        var address = BuildAddress(card);
        if (address.Count > 0)
        {
            body["billing_address"] = address;
        }

        return body;
    }

    private static Dictionary<string, object?> BuildAddress(CardDetails card)
    {
        var address = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(card.Street))
        {
            address["address_line_1"] = card.Street;
        }
        if (!string.IsNullOrWhiteSpace(card.City))
        {
            address["admin_area_2"] = card.City;
        }
        if (!string.IsNullOrWhiteSpace(card.Region))
        {
            address["admin_area_1"] = card.Region;
        }
        if (!string.IsNullOrWhiteSpace(card.PostalCode))
        {
            address["postal_code"] = card.PostalCode;
        }
        if (!string.IsNullOrWhiteSpace(card.CountryCode))
        {
            address["country_code"] = card.CountryCode;
        }

        return address;
    }

    private static Dictionary<string, object?> Money(decimal amount, string currency) => new()
    {
        ["currency_code"] = currency,
        ["value"] = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    /// <summary>
    /// A hold the processor has actually put on the money. PENDING is accepted: it is a hold that is
    /// still settling, and capturing it is exactly what fulfilment does.
    /// </summary>
    private static bool IsHoldAccepted(string status)
        => status is "CREATED" or "PENDING" or "PROCESSED" or "COMPLETED" or "PARTIALLY_CAPTURED";

    private static string DescribeDecline(PaymentAuthorization authorization)
        => $"The payment was refused (status {authorization.Status}" +
           $"{(string.IsNullOrEmpty(authorization.DeclineCode) ? string.Empty : $", reason {authorization.DeclineCode}")}). " +
           "No money has been taken. Ask the shopper to pay with another card.";

    private static PaymentProcessorException ActionRequired() => new(
        "The payment processor asked for the cardholder to approve this payment in a browser before it " +
        "could be taken. This integration only supports direct card payments, so it cannot continue.",
        "PAYER_ACTION_REQUIRED");

    private static JsonElement? FindAuthorization(JsonElement order)
    {
        foreach (var purchaseUnit in order.Prop("purchase_units").ArrayOrEmpty())
        {
            foreach (var authorization in purchaseUnit.Prop("payments").Prop("authorizations").ArrayOrEmpty())
            {
                return authorization;
            }
        }

        return null;
    }

    private static bool RequiresPayerAction(JsonElement order)
    {
        if (string.Equals(order.Text("status"), "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return order.Prop("links").ArrayOrEmpty()
            .Any(link => string.Equals(link.Text("rel"), "payer-action", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, string? requestId,
        string? customerContext, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, $"{_settings.Value.BaseAddress}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }

        if (!string.IsNullOrWhiteSpace(customerContext))
        {
            request.Headers.TryAddWithoutValidation("Customer-Context", customerContext);
        }

        if (body is not null)
        {
            request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
            request.Content = new StringContent(JsonSerializer.Serialize(body, _serializeOptions), Encoding.UTF8,
                "application/json");
        }

        HttpResponseMessage response;
        using var client = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);
        try
        {
            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogError($"Could not reach the payment processor: {exception.Message}");
            throw new PaymentProcessorException("The payment processor could not be reached. Try again shortly.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError($"The payment processor did not answer {method.Method} {path} in time.");
            throw new PaymentProcessorException("The payment processor did not answer in time. Try again shortly.");
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            JsonElement? document = null;
            if (!string.IsNullOrWhiteSpace(payload))
            {
                try
                {
                    document = JsonDocument.Parse(payload).RootElement.Clone();
                }
                catch (JsonException)
                {
                    _logger.LogWarning($"The payment processor answered {path} with a body that is not JSON.");
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                throw BuildProblem(method, path, response.StatusCode, document);
            }

            _logger.LogInformation($"PayPal {method.Method} {path} succeeded with {(int)response.StatusCode}.");
            return document ?? JsonDocument.Parse("{}").RootElement.Clone();
        }
    }

    private PaymentProcessorException BuildProblem(HttpMethod method, string path, HttpStatusCode statusCode,
        JsonElement? problem)
    {
        var name = problem.Text("name");
        var message = problem.Text("message");
        var debugId = problem.Text("debug_id");

        var issues = new List<string>();
        foreach (var detail in problem.Prop("details").ArrayOrEmpty())
        {
            var issue = detail.Text("issue");
            if (!string.IsNullOrEmpty(issue))
            {
                issues.Add(issue);
            }
        }

        _logger.LogWarning($"PayPal {method.Method} {path} failed with {(int)statusCode}: " +
                           $"{name} {string.Join(",", issues)} debug_id={debugId}");

        var summary = $"The payment processor refused the request" +
                      $"{(string.IsNullOrEmpty(name) ? string.Empty : $" ({name})")}";

        return new PaymentProcessorException(string.IsNullOrEmpty(message) ? summary : $"{summary}: {message}",
            name, (int)statusCode, issues, debugId);
    }
}
