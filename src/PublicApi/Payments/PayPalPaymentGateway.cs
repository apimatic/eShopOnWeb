using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.Hooks;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// PayPal-backed implementation of <see cref="IPayPalPaymentGateway"/>. Every PayPal SDK call goes
/// through <see cref="InvokeAsync{T}"/>, which bounds the whole call with a deadline, captures the HTTP
/// status via a response hook, and translates SDK/transport/parse failures into
/// <see cref="PaymentGatewayException"/> so callers see one failure type.
/// </summary>
public sealed class PayPalPaymentGateway : IPayPalPaymentGateway
{
    // Full representation is required so authorize returns the authorization id and capture returns the
    // seller-receivable breakdown (gross/fee/net), and so void returns a body instead of a 204.
    private const string PreferRepresentation = "return=representation";

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalPaymentGateway> _logger;
    private readonly TimeSpan _timeout;

    public PayPalPaymentGateway(
        PayPalServerSdkClient client,
        IOptions<PayPalOptions> options,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);
    }

    private string Currency => _options.Currency;

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeCommand command, CancellationToken ct)
    {
        if (command.Card is null && string.IsNullOrEmpty(command.VaultId))
            throw new PaymentGatewayException("A card or a saved payment method is required to authorize a payment.", 400);

        var value = MoneyFormatter.Format(command.Amount, Currency);
        var reference = OrderReference(command.OrderId);

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = "default",
                    // custom_id is the stable reconciliation key (survives across runs); invoice_id must be
                    // unique per merchant, so it carries a unique suffix while still embedding the order ref.
                    CustomId = reference,
                    InvoiceId = $"{reference}-{DateTime.UtcNow.Ticks}",
                    Amount = new AmountWithBreakdown { CurrencyCode = Currency, Value = value }
                }
            }
        };

        var order = await InvokeAsync(
            (ro, token) => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: command.IdempotencyKey + "-create",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=minimal",
                requestOptions: ro,
                ct: token),
            "create order",
            (ex, status) => ex is SdkException<CreateOrderError> e
                ? MakeError("create order", e.Error.TryGetError(out var err) ? err : null, status)
                : null,
            ct);

        var payPalOrderId = order.Id
            ?? throw new PaymentGatewayException("PayPal did not return an order id when creating the order.", 502);

        var card = string.IsNullOrEmpty(command.VaultId)
            ? BuildCardRequest(command.Card!)
            : new CardRequest { VaultId = command.VaultId };

        var authorizeRequest = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = card }
        };

        var authorizeResponse = await InvokeAsync(
            (ro, token) => _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: command.IdempotencyKey + "-auth",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: authorizeRequest,
                prefer: PreferRepresentation,
                requestOptions: ro,
                ct: token),
            "authorize order",
            (ex, status) => ex is SdkException<AuthorizeOrderError> e
                ? MakeError("authorize order", e.Error.TryGetError(out var err) ? err : null, status)
                : null,
            ct);

        var authorization = authorizeResponse.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        if (authorization?.Id is null)
        {
            var orderStatus = authorizeResponse.Status?.Value;
            var payerActionRequired =
                string.Equals(orderStatus, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                (authorizeResponse.Links?.Any(l =>
                    string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase)) ?? false);

            if (payerActionRequired)
                throw new PaymentChallengeRequiredException(
                    "PayPal requires the shopper to approve this card payment in a browser (a challenge/3DS step). " +
                    "This integration does not perform a browser approval round-trip.");

            throw new PaymentGatewayException(
                $"PayPal did not create an authorization (order status: {orderStatus ?? "unknown"}).", 502);
        }

        var authStatus = authorization.Status?.Value ?? string.Empty;
        if (string.Equals(authStatus, "DENIED", StringComparison.OrdinalIgnoreCase))
            throw new PaymentGatewayException("The card authorization was declined by PayPal.", 402);

        _logger.LogInformation(
            "PayPal authorization {AuthorizationId} created for order {OrderId} (PayPal order {PayPalOrderId}, status {Status}).",
            authorization.Id, command.OrderId, payPalOrderId, authStatus);

        return new AuthorizationResult(payPalOrderId, authorization.Id, authStatus, command.Amount);
    }

    public async Task<CaptureResult> CaptureAsync(CaptureCommand command, CancellationToken ct)
    {
        var effectiveAuthId = command.AuthorizationId;

        // If the authorization has gone stale before fulfilment, renew it rather than failing outright.
        var currentStatus = await GetAuthorizationStatusAsync(effectiveAuthId, ct);
        if (string.Equals(currentStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Authorization {AuthorizationId} has expired; attempting to reauthorize.", effectiveAuthId);
            effectiveAuthId = await ReauthorizeAsync(effectiveAuthId, command.Amount, ct);
        }

        var captureRequest = new CaptureRequest
        {
            FinalCapture = true,
            Amount = new Money { CurrencyCode = Currency, Value = MoneyFormatter.Format(command.Amount, Currency) }
        };

        var capture = await InvokeAsync(
            (ro, token) => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: effectiveAuthId,
                payPalMockResponse: null,
                payPalRequestId: command.IdempotencyKey + "-capture",
                payPalAuthAssertion: null,
                body: captureRequest,
                prefer: PreferRepresentation,
                requestOptions: ro,
                ct: token),
            "capture payment",
            (ex, status) => ex is SdkException<CaptureAuthorizedPaymentError> e
                ? MakeError("capture payment", e.Error.TryGetError(out var err) ? err : null, status)
                : null,
            ct);

        var captureId = capture.Id
            ?? throw new PaymentGatewayException("PayPal did not return a capture id.", 502);

        var breakdown = capture.SellerReceivableBreakdown;
        var gross = MoneyFormatter.Parse(breakdown?.GrossAmount?.Value)
            ?? MoneyFormatter.Parse(capture.Amount?.Value)
            ?? command.Amount;
        var fee = MoneyFormatter.Parse(breakdown?.PaypalFee?.Value);
        var net = MoneyFormatter.Parse(breakdown?.NetAmount?.Value);

        _logger.LogInformation(
            "Captured payment {CaptureId} for authorization {AuthorizationId} (gross {Gross}, fee {Fee}, net {Net}).",
            captureId, effectiveAuthId, gross, fee, net);

        return new CaptureResult(captureId, effectiveAuthId, capture.Status?.Value ?? string.Empty, gross, fee, net);
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            await InvokeAsync(
                (ro, token) => _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey + "-void",
                    prefer: PreferRepresentation,
                    requestOptions: ro,
                    ct: token),
                "void authorization",
                (ex, status) => ex is SdkException<VoidPaymentError> e
                    ? MakeError("void authorization", e.Error.TryGetError(out var err) ? err : null, status)
                    : null,
                ct);
        }
        catch (PaymentGatewayException ex) when (ex.InnerException is JsonException)
        {
            // A successful void can legitimately return an empty (204) body, which the JSON response
            // mapper cannot deserialize. That is success, not a failure.
            _logger.LogInformation("Void of authorization {AuthorizationId} returned no content (treated as success).", authorizationId);
        }

        _logger.LogInformation("Voided authorization {AuthorizationId}.", authorizationId);
    }

    public async Task<RefundResult> RefundAsync(RefundCommand command, CancellationToken ct)
    {
        var refundRequest = new RefundRequest
        {
            NoteToPayer = command.NoteToPayer,
            Amount = command.Amount is { } amount
                ? new Money { CurrencyCode = Currency, Value = MoneyFormatter.Format(amount, Currency) }
                : null
        };

        var refund = await InvokeAsync(
            (ro, token) => _client.Payments.RefundCapturedPayment(
                captureId: command.CaptureId,
                payPalMockResponse: null,
                payPalRequestId: command.IdempotencyKey,
                payPalAuthAssertion: null,
                body: refundRequest,
                prefer: PreferRepresentation,
                requestOptions: ro,
                ct: token),
            "refund payment",
            (ex, status) => ex is SdkException<RefundCapturedPaymentError> e
                ? MakeError("refund payment", e.Error.TryGetError(out var err) ? err : null, status)
                : null,
            ct);

        var refundId = refund.Id
            ?? throw new PaymentGatewayException("PayPal did not return a refund id.", 502);
        var amountRefunded = MoneyFormatter.Parse(refund.Amount?.Value) ?? command.Amount ?? 0m;

        _logger.LogInformation("Refund {RefundId} issued for capture {CaptureId} (amount {Amount}, status {Status}).",
            refundId, command.CaptureId, amountRefunded, refund.Status?.Value);

        return new RefundResult(refundId, refund.Status?.Value ?? string.Empty, amountRefunded);
    }

    public async Task<VaultedCardResult> VaultCardAsync(VaultCardCommand command, CancellationToken ct)
    {
        // Step 1: create a setup token holding the raw card (short-lived, never stored by us).
        var setupTokenRequest = new SetupTokenRequest
        {
            Customer = string.IsNullOrEmpty(command.ExistingCustomerId)
                ? null
                : new Customer { Id = command.ExistingCustomerId },
            PaymentSource = new SetupTokenRequestPaymentSource
            {
                Card = new SetupTokenRequestCard
                {
                    Number = command.Card.Number,
                    Expiry = command.Card.Expiry,
                    SecurityCode = command.Card.SecurityCode,
                    Name = command.Card.CardholderName,
                    BillingAddress = BuildAddress(command.Card.BillingAddress)
                }
            }
        };

        var setupToken = await InvokeAsync(
            (ro, token) => _client.Vault.CreateSetupToken(
                payPalRequestId: command.IdempotencyKey + "-setup",
                body: setupTokenRequest,
                requestOptions: ro,
                ct: token),
            "create setup token",
            (ex, status) => ex is SdkException<CreateSetupTokenError> e
                ? MakeError("create setup token", e.Error.TryGetError(out var err) ? err : null, status)
                : null,
            ct);

        var setupTokenId = setupToken.Id
            ?? throw new PaymentGatewayException("PayPal did not return a setup token id.", 502);

        // Step 2: exchange the setup token for a permanent vault (payment) token.
        var paymentTokenRequest = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Token = new VaultTokenRequest
                {
                    Id = setupTokenId,
                    Type = VaultTokenRequestType.SetupToken
                }
            }
        };

        var paymentToken = await InvokeAsync(
            (ro, token) => _client.Vault.CreatePaymentToken(
                payPalRequestId: command.IdempotencyKey + "-token",
                body: paymentTokenRequest,
                requestOptions: ro,
                ct: token),
            "create payment token",
            (ex, status) => ex is SdkException<CreatePaymentTokenError> e
                ? MakeError("create payment token", e.Error.TryGetError(out var err) ? err : null, status)
                : null,
            ct);

        var vaultId = paymentToken.Id
            ?? throw new PaymentGatewayException("PayPal did not return a vault id.", 502);
        var customerId = paymentToken.Customer?.Id
            ?? throw new PaymentGatewayException("PayPal did not return a customer id for the vaulted card.", 502);

        var cardEntity = paymentToken.PaymentSource?.Card;
        var summary = new CardSummary(cardEntity?.Brand?.Value, cardEntity?.LastDigits, cardEntity?.Expiry);

        _logger.LogInformation("Vaulted card {VaultId} for customer {CustomerId} ({Brand} ****{Last4}).",
            vaultId, customerId, summary.Brand, summary.Last4);

        return new VaultedCardResult(vaultId, customerId, summary);
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct) =>
        InvokeAsync(
            async (ro, token) =>
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, requestOptions: ro, ct: token);
                return true;
            },
            "delete payment token",
            (ex, status) => ex is SdkException<DeletePaymentTokenError> e
                ? MakeError("delete payment token", e.Error.TryGetError(out var err) ? err : null, status)
                : null,
            ct);

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<PayPalTransaction>();
        var startDate = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        var endDate = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        const int pageSize = 100;
        const int maxPages = 1000; // hard backstop so the loop never depends solely on the provider
        int page = 1;

        while (true)
        {
            var currentPage = page;
            SearchResponse response;
            try
            {
                response = await InvokeAsync(
                (ro, token) => _client.TransactionSearch.SearchTransactions(
                    startDate: startDate,
                    endDate: endDate,
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: pageSize,
                    page: currentPage,
                    requestOptions: ro,
                    ct: token),
                "search transactions",
                // TransactionSearch is Case B (RawError) — no typed accessors.
                (ex, status) => ex is SdkException<RawError> e
                    ? new PaymentGatewayException(
                        $"PayPal transaction search failed: HTTP {(int)e.Error.StatusCode}: {SafeBody(e.Error)}",
                        (int)e.Error.StatusCode)
                    : null,
                ct);
            }
            catch (PaymentGatewayException ex) when (ex.StatusCode == 404)
            {
                // PayPal transaction reporting lags live activity: a recent range legitimately has no
                // data yet ("Data for the given start date is not available."). That is an expected empty
                // result, not a failure — stop and return whatever was already collected.
                _logger.LogInformation(
                    "Transaction search returned no data for the requested range (reporting lag): {Message}", ex.Message);
                break;
            }

            foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info is null)
                    continue;

                results.Add(new PayPalTransaction(
                    info.TransactionId,
                    info.TransactionStatus,
                    MoneyFormatter.Parse(info.TransactionAmount?.Value),
                    info.TransactionAmount?.CurrencyCode,
                    MoneyFormatter.Parse(info.FeeAmount?.Value),
                    info.InvoiceId,
                    info.CustomField,
                    ParseDate(info.TransactionInitiationDate)));
            }

            var totalPages = response.TotalPages ?? 1;
            if (page >= totalPages || page >= maxPages)
                break;
            page++;
        }

        return results;
    }

    public async Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct)
    {
        var authorization = await InvokeAsync(
            (ro, token) => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: ro,
                ct: token),
            "get authorization",
            (ex, status) => ex is SdkException<GetAuthorizedPaymentError> e
                ? MakeError("get authorization", e.Error.TryGetError(out var err) ? err : null, status)
                : null,
            ct);

        return authorization.Status?.Value ?? string.Empty;
    }

    private async Task<string> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken ct)
    {
        try
        {
            var request = new ReauthorizeRequest
            {
                Amount = new Money { CurrencyCode = Currency, Value = MoneyFormatter.Format(amount, Currency) }
            };

            var reauthorized = await InvokeAsync(
                (ro, token) => _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: null,
                    payPalAuthAssertion: null,
                    body: request,
                    prefer: PreferRepresentation,
                    requestOptions: ro,
                    ct: token),
                "reauthorize payment",
                (ex, status) => ex is SdkException<ReauthorizePaymentError> e
                    ? MakeError("reauthorize payment", e.Error.TryGetError(out var err) ? err : null, status)
                    : null,
                ct);

            return reauthorized.Id ?? authorizationId;
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentReauthorizationException(
                "The payment authorization has expired and could not be renewed. Collect a new payment from the shopper.",
                ex.DebugId, ex);
        }
    }

    private CardRequest BuildCardRequest(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = BuildAddress(card.BillingAddress)
    };

    private static Address? BuildAddress(CardBillingAddress? address)
    {
        // The SDK's Address requires a country code, so an address is only sent when one is present.
        if (address?.CountryCode is null || address.CountryCode.Trim().Length != 2)
            return null;

        return new Address
        {
            CountryCode = address.CountryCode.Trim().ToUpperInvariant(),
            AddressLine1 = address.AddressLine1,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.PostalCode
        };
    }

    private static string OrderReference(int orderId) => $"eshop-order-{orderId}";

    private static (string? Name, string? Message, string? DebugId) ParseError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return (null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string? Get(string key) => root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
            return (Get("name"), Get("message"), Get("debug_id"));
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static string SafeBody(RawError error)
    {
        try
        {
            var body = error.ReadAsString();
            return string.IsNullOrWhiteSpace(body) ? "(empty body)" : body;
        }
        catch
        {
            return "(unreadable body)";
        }
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static PaymentGatewayException MakeError(string operation, Error? error, int? status)
    {
        var name = error?.Name;
        var message = error?.Message ?? "PayPal rejected the request.";
        var text = name is null
            ? $"PayPal {operation} failed: {message}"
            : $"PayPal {operation} failed ({name}): {message}";
        return new PaymentGatewayException(text, status, error?.DebugId);
    }

    // Bounded retry for transient failures. Safe because every write goes out with a stable
    // PayPal-Request-Id (idempotency), so a resend is deduplicated by PayPal rather than duplicated.
    private const int MaxAttempts = 3;

    /// <summary>
    /// Runs an SDK call bounded by the whole-call deadline, captures the response status/body via a
    /// per-call hook, converts SDK/transport/parse failures into <see cref="PaymentGatewayException"/>,
    /// and retries transient failures (transport, 5xx, and the sandbox's intermittent 403 NOT_AUTHORIZED
    /// on vault operations) with backoff.
    /// </summary>
    private async Task<T> InvokeAsync<T>(
        Func<RequestOptions, CancellationToken, Task<T>> call,
        string operation,
        Func<Exception, int?, PaymentGatewayException?> mapSdkError,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);

            int? lastStatus = null;
            string? lastErrorBody = null;
            var requestOptions = new RequestOptions
            {
                Hooks = new List<SdkHook>
                {
                    SdkHook.OnResponse((response, _) =>
                    {
                        lastStatus = (int)response.StatusCode;
                        // On an error status, buffer the body so we can still report it even when it does
                        // not match the operation's generated error schema (which surfaces as a
                        // JsonException, destroying the typed SdkException). Success bodies are left for the
                        // SDK to read.
                        if (response.StatusCode >= System.Net.HttpStatusCode.BadRequest && response.Content is not null)
                        {
                            try { lastErrorBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult(); }
                            catch { /* best-effort diagnostics only */ }
                        }
                    })
                }
            };

            try
            {
                return await call(requestOptions, cts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // the caller cancelled — propagate as cancellation, not a gateway failure
            }
            catch (Exception ex)
            {
                var mapped = Translate(ex, operation, lastStatus, lastErrorBody, mapSdkError);
                var transient = IsTransient(mapped, lastErrorBody, ex, ct);

                if (transient && attempt < MaxAttempts)
                {
                    var delay = TimeSpan.FromMilliseconds(300 * attempt * attempt); // 300ms, 1200ms
                    _logger.LogWarning(
                        "PayPal {Operation} transient failure (attempt {Attempt}/{Max}, status {Status}): {Message}; retrying in {Delay}ms.",
                        operation, attempt, MaxAttempts, mapped.StatusCode, mapped.Message, delay.TotalMilliseconds);
                    await Task.Delay(delay, ct);
                    continue;
                }

                _logger.LogError(ex, "PayPal {Operation} failed (status {Status}, debug_id {DebugId}): {Message}",
                    operation, mapped.StatusCode, mapped.DebugId, mapped.Message);
                throw mapped;
            }
        }
    }

    private PaymentGatewayException Translate(
        Exception ex, string operation, int? status, string? errorBody,
        Func<Exception, int?, PaymentGatewayException?> mapSdkError)
    {
        if (ex is JsonException)
        {
            // A 2xx body that no longer matches its model, OR a non-2xx body that did not match the
            // operation's generated error shape (which replaces the SdkException). Recover the provider's
            // own name/message/debug_id from the buffered body when possible.
            var (name, message, debugId) = ParseError(errorBody);
            var text = name is null
                ? $"PayPal {operation} failed with status {status?.ToString() ?? "unknown"}."
                : $"PayPal {operation} failed ({name}): {message}";
            return new PaymentGatewayException(text, status, debugId, ex);
        }

        if (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            return new PaymentGatewayException($"PayPal was unreachable or timed out for {operation}.", 504, inner: ex);

        return mapSdkError(ex, status)
            ?? new PaymentGatewayException($"PayPal {operation} failed unexpectedly.", status, inner: ex);
    }

    // Whether a mapped failure is worth retrying.
    private static bool IsTransient(PaymentGatewayException mapped, string? errorBody, Exception ex, CancellationToken ct)
    {
        if (ex is HttpRequestException)
            return true;
        if (ex is TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
            return true;
        if (mapped.StatusCode is 500 or 502 or 503 or 504)
            return true;
        // The sandbox's limited-release Vault surface intermittently answers 403 NOT_AUTHORIZED.
        if (mapped.StatusCode == 403 && (errorBody?.Contains("NOT_AUTHORIZED") ?? false))
            return true;
        return false;
    }
}
