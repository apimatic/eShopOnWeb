using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single implementation of <see cref="IPayPalGateway"/> over the PayPal Server SDK. It builds
/// SDK request models, reads response envelopes one level down, and translates every SDK/transport
/// failure into <see cref="PayPalGatewayException"/> so callers see one failure type. Amounts are
/// formatted to the cent in the configured currency. Card numbers pass straight through to PayPal
/// and are never logged or persisted.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(40);

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalGateway> _logger;
    private readonly string _currency;

    public PayPalGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options, ILogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
        _currency = options.Value.Currency;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(
        decimal amount, string orderReference, string invoiceId, CardPaymentInput card, string idempotencyKeyBase, CancellationToken ct)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = _currency, Value = Format(amount) },
                    CustomId = orderReference,
                    InvoiceId = invoiceId,
                    Description = $"eShopOnWeb order {orderReference}"
                }
            },
            PaymentSource = new PaymentSource { Card = BuildCardRequest(card) }
        };

        Order order;
        try
        {
            order = await Bounded(c => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"{idempotencyKeyBase}-ord",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: c), ct);
        }
        catch (SdkException<CreateOrderError> ex) { throw FromTyped(ex.Error.TryGetError(out var er) ? er : null, ex.Error, "create order"); }
        catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "create order"); }
        catch (JsonException ex) { throw FromJson(ex, "create order"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "create order", ct); }

        EnsureNoChallenge(order.Status?.Value, order.Id);

        var authorization = ExtractAuthorization(order.PurchaseUnits);
        var orderStatus = order.Status?.Value ?? "CREATED";

        if (authorization is null)
        {
            // Fall back to the explicit authorize step (payment source carried in the request).
            var authBody = new OrderAuthorizeRequest
            {
                PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = BuildCardRequest(card) }
            };
            OrderAuthorizeResponse authResponse;
            try
            {
                authResponse = await Bounded(c => _client.Orders.AuthorizeOrder(
                    order.Id!,
                    payPalMockResponse: null,
                    payPalRequestId: $"{idempotencyKeyBase}-auth",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: authBody,
                    prefer: "return=representation",
                    ct: c), ct);
            }
            catch (SdkException<AuthorizeOrderError> ex) { throw FromTyped(ex.Error.TryGetError(out var er) ? er : null, ex.Error, "authorize order"); }
            catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "authorize order"); }
            catch (JsonException ex) { throw FromJson(ex, "authorize order"); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "authorize order", ct); }

            EnsureNoChallenge(authResponse.Status?.Value, order.Id);
            authorization = ExtractAuthorization(authResponse.PurchaseUnits);
            orderStatus = authResponse.Status?.Value ?? orderStatus;
        }

        if (authorization?.Id is null)
        {
            throw new PayPalGatewayException(
                $"PayPal accepted order {order.Id} (status {orderStatus}) but returned no authorization to act on.",
                statusCode: 502);
        }

        _logger.LogInformation(
            "PayPal authorized order {OrderReference}: paypalOrderId={PayPalOrderId} authorizationId={AuthorizationId} status={Status}",
            orderReference, order.Id, authorization.Id, authorization.Status?.Value);

        return new PayPalAuthorizationResult(order.Id!, authorization.Id, authorization.Status?.Value ?? "CREATED");
    }

    public async Task<string> GetAuthorizationStatusAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var auth = await Bounded(c => _client.Payments.GetAuthorizedPayment(
                authorizationId, payPalMockResponse: null, payPalAuthAssertion: null, ct: c), ct);
            return auth.Status?.Value ?? "UNKNOWN";
        }
        catch (SdkException<GetAuthorizedPaymentError> ex) { throw FromTyped(ex.Error.TryGetError(out var er) ? er : null, ex.Error, "get authorization"); }
        catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "get authorization"); }
        catch (JsonException ex) { throw FromJson(ex, "get authorization"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "get authorization", ct); }
    }

    public async Task<PayPalAuthorizationRef> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        var body = new ReauthorizeRequest { Amount = new Money { CurrencyCode = _currency, Value = Format(amount) } };
        try
        {
            var auth = await Bounded(c => _client.Payments.ReauthorizePayment(
                authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: c), ct);
            _logger.LogInformation("PayPal reauthorized {Old} -> {New} status={Status}", authorizationId, auth.Id, auth.Status?.Value);
            return new PayPalAuthorizationRef(auth.Id ?? authorizationId, auth.Status?.Value ?? "CREATED");
        }
        catch (SdkException<ReauthorizePaymentError> ex) { throw FromTyped(ex.Error.TryGetError(out var er) ? er : null, ex.Error, "reauthorize"); }
        catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "reauthorize"); }
        catch (JsonException ex) { throw FromJson(ex, "reauthorize"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "reauthorize", ct); }
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        var body = new CaptureRequest
        {
            FinalCapture = true,
            Amount = amount.HasValue ? new Money { CurrencyCode = _currency, Value = Format(amount.Value) } : null
        };
        try
        {
            var captured = await Bounded(c => _client.Payments.CaptureAuthorizedPayment(
                authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: c), ct);

            var breakdown = captured.SellerReceivableBreakdown;
            var capturedAmount = ParseMoney(captured.Amount) ?? amount ?? 0m;
            _logger.LogInformation(
                "PayPal captured authorization {AuthorizationId}: captureId={CaptureId} status={Status} gross={Gross} fee={Fee} net={Net}",
                authorizationId, captured.Id, captured.Status?.Value, capturedAmount,
                ParseMoney(breakdown?.PaypalFee), ParseMoney(breakdown?.NetAmount));

            return new PayPalCaptureResult(
                captured.Id!,
                captured.Status?.Value ?? "COMPLETED",
                capturedAmount,
                ParseMoney(breakdown?.PaypalFee),
                ParseMoney(breakdown?.NetAmount));
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw FromTyped(ex.Error.TryGetError(out var er) ? er : null, ex.Error, "capture"); }
        catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "capture"); }
        catch (JsonException ex) { throw FromJson(ex, "capture"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "capture", ct); }
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            await Bounded(c => _client.Payments.VoidPayment(
                authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=minimal",
                ct: c), ct);
            _logger.LogInformation("PayPal voided authorization {AuthorizationId}", authorizationId);
        }
        catch (JsonException)
        {
            // A minimal void returns 204 No Content; the SDK's deserialization of an empty body can
            // throw even though the void succeeded. The 2xx status is the outcome — treat as success.
            _logger.LogInformation("PayPal voided authorization {AuthorizationId} (empty body)", authorizationId);
        }
        catch (SdkException<VoidPaymentError> ex) { throw FromTyped(ex.Error.TryGetError(out var er) ? er : null, ex.Error, "void"); }
        catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "void"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "void", ct); }
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        // Omitting the body refunds in full; an Amount refunds partially.
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = _currency, Value = Format(amount.Value) } }
            : null;
        try
        {
            var refund = await Bounded(c => _client.Payments.RefundCapturedPayment(
                captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: c), ct);
            _logger.LogInformation("PayPal refunded capture {CaptureId}: refundId={RefundId} status={Status} amount={Amount}",
                captureId, refund.Id, refund.Status?.Value, ParseMoney(refund.Amount));
            return new PayPalRefundResult(refund.Id!, refund.Status?.Value ?? "COMPLETED", ParseMoney(refund.Amount) ?? amount ?? 0m);
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw FromTyped(ex.Error.TryGetError(out var er) ? er : null, ex.Error, "refund"); }
        catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "refund"); }
        catch (JsonException ex) { throw FromJson(ex, "refund"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "refund", ct); }
    }

    public async Task<PayPalVaultResult> VaultCardAsync(
        CardPaymentInput card, string buyerReference, string? existingCustomerId, string idempotencyKey, CancellationToken ct)
    {
        // Two-step vault: create a setup token from the raw card, then exchange it for a permanent
        // payment token. (Vaulting a raw card directly via CreatePaymentToken is not supported for
        // server-side card processing.)
        var setupBody = new SetupTokenRequest
        {
            Customer = new Customer
            {
                Id = string.IsNullOrEmpty(existingCustomerId) ? null : existingCustomerId,
                MerchantCustomerId = SanitizeCustomerId(buyerReference)
            },
            PaymentSource = new SetupTokenRequestPaymentSource
            {
                Card = new SetupTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName
                }
            }
        };

        SetupTokenResponse setup;
        try
        {
            setup = await Bounded(c => _client.Vault.CreateSetupToken(
                payPalRequestId: $"{idempotencyKey}-setup", body: setupBody, ct: c), ct);
        }
        catch (SdkException<CreateSetupTokenError> ex) { throw FromTyped(ex.Error.TryGetError(out var er) ? er : null, ex.Error, "create setup token"); }
        catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "create setup token"); }
        catch (JsonException ex) { throw FromJson(ex, "create setup token"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "create setup token", ct); }

        if (string.IsNullOrEmpty(setup.Id))
        {
            throw new PayPalGatewayException("PayPal did not return a setup token id.", statusCode: 502);
        }
        var customerId = setup.Customer?.Id ?? existingCustomerId;

        var tokenBody = new PaymentTokenRequest
        {
            Customer = string.IsNullOrEmpty(customerId) ? null : new Customer { Id = customerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Token = new VaultTokenRequest { Id = setup.Id, Type = VaultTokenRequestType.SetupToken }
            }
        };

        try
        {
            var response = await Bounded(c => _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey, body: tokenBody, ct: c), ct);

            var cardEntity = response.PaymentSource?.Card;
            _logger.LogInformation("PayPal vaulted a card: tokenId={TokenId} customerId={CustomerId} brand={Brand} last4={Last4}",
                response.Id, response.Customer?.Id, cardEntity?.Brand?.Value, cardEntity?.LastDigits);

            return new PayPalVaultResult(
                response.Id!,
                response.Customer?.Id ?? customerId,
                cardEntity?.Brand?.Value,
                cardEntity?.LastDigits,
                cardEntity?.Expiry,
                cardEntity?.Name);
        }
        catch (SdkException<CreatePaymentTokenError> ex) { throw FromTyped(ex.Error.TryGetError(out var er) ? er : null, ex.Error, "vault card"); }
        catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "vault card"); }
        catch (JsonException ex) { throw FromJson(ex, "vault card"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "vault card", ct); }
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct)
    {
        try
        {
            await Bounded(c => _client.Vault.DeletePaymentToken(vaultTokenId, ct: c), ct);
            _logger.LogInformation("PayPal deleted vault token {TokenId}", vaultTokenId);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            var translated = FromTyped(ex.Error.TryGetError(out var er) ? er : null, ex.Error, "delete vault card");
            if (translated.StatusCode == 404)
            {
                // Already gone — deletion is idempotent.
                _logger.LogInformation("PayPal vault token {TokenId} was already absent.", vaultTokenId);
                return;
            }
            throw translated;
        }
        catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "delete vault card"); }
        catch (JsonException ex) { throw FromJson(ex, "delete vault card"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "delete vault card", ct); }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<PayPalTransaction>();
        var start = FormatDate(from);
        var end = FormatDate(to);

        const int maxPages = 1000; // provider-independent backstop against an unbounded page loop
        int page = 1;
        int totalPages;

        do
        {
            var currentPage = page;
            SearchResponse response;
            try
            {
                response = await Bounded(c => _client.TransactionSearch.SearchTransactions(
                    startDate: start,
                    endDate: end,
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
                    pageSize: 100,
                    page: currentPage,
                    ct: c), ct);
            }
            catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, "transaction search"); }
            catch (JsonException ex) { throw FromJson(ex, "transaction search"); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex, "transaction search", ct); }

            totalPages = response.TotalPages ?? 1;
            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    results.Add(new PayPalTransaction(
                        info?.TransactionId,
                        info?.TransactionStatus,
                        ParseMoney(info?.TransactionAmount),
                        info?.TransactionAmount?.CurrencyCode,
                        info?.InvoiceId,
                        ParseDate(info?.TransactionInitiationDate)));
                }
            }

            page++;
        }
        while (page <= totalPages && page <= maxPages);

        return results;
    }

    // --- call budget: the CancellationToken deadline is the only thing that bounds a whole call ---

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        await call(cts.Token);
    }

    // --- request/response helpers ---

    private static CardRequest BuildCardRequest(CardPaymentInput card) =>
        card.IsVaulted
            ? new CardRequest { VaultId = card.VaultId }
            : new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName
            };

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .Select(pu => pu.Payments?.Authorizations)
            .Where(a => a is { Count: > 0 })
            .SelectMany(a => a!)
            .FirstOrDefault(a => a.Id is not null);

    private void EnsureNoChallenge(string? statusValue, string? orderId)
    {
        if (string.Equals(statusValue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalGatewayException(
                $"PayPal requires the shopper to approve payment in a browser (3DS / PAYER_ACTION_REQUIRED) for order {orderId}. " +
                "This integration does not support a browser approval round-trip.",
                issue: "PAYER_ACTION_REQUIRED",
                statusCode: 409);
        }
    }

    private string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money?.Value is { Length: > 0 } v && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        !string.IsNullOrEmpty(value) &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : null;

    // PayPal documents merchant_customer_id as ^[0-9a-zA-Z-_.^*$@#]+$, but the sandbox returns a
    // 500 when it actually contains '@' or '.', so restrict to the safe alphanumeric/underscore/hyphen
    // subset. Deterministic, so a shopper's cards still group under the same customer. Max 64.
    private static string SanitizeCustomerId(string reference)
    {
        const string allowed = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_-";
        var cleaned = new string(reference.Where(allowed.Contains).ToArray());
        if (cleaned.Length == 0) cleaned = "shopper";
        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }

    // --- error translation (Case A typed, Case B raw, JSON drift, transport) ---

    // Called from each typed catch with the typed Error already extracted (only the concrete
    // {Operation}Error exposes TryGetError). Falls back to the raw body when no typed error is present.
    private PayPalGatewayException FromTyped(Error? error, ApiError apiError, string context)
    {
        RawError? raw = null;
        if (error is null && apiError.TryGetRawError(out var r)) raw = r;
        return Build(error, raw, context);
    }

    private PayPalGatewayException FromRaw(RawError raw, string context) => Build(null, raw, context);

    private PayPalGatewayException Build(Error? error, RawError? raw, string context)
    {
        if (error is not null)
        {
            var issue = error.Details is { Count: > 0 } ? error.Details[0].Issue : error.Name;
            var status = MapStatus(error.Name, raw?.StatusCode);
            _logger.LogWarning("PayPal {Context} failed: name={Name} issue={Issue} debug_id={DebugId}",
                context, error.Name, issue, error.DebugId);
            return new PayPalGatewayException(
                $"PayPal {context} failed: {error.Name} - {error.Message}", issue, error.DebugId, status);
        }
        if (raw is not null)
        {
            var status = (int)raw.StatusCode;
            _logger.LogWarning("PayPal {Context} failed with HTTP {Status}: {Body}", context, status, SafeReadRaw(raw));
            return new PayPalGatewayException(
                $"PayPal {context} failed with HTTP {status}.", issue: null, debugId: null, statusCode: MapStatus(null, raw.StatusCode));
        }
        _logger.LogWarning("PayPal {Context} failed with an unrecognised error shape.", context);
        return new PayPalGatewayException($"PayPal {context} failed with an unrecognised error.", statusCode: 502);
    }

    private PayPalGatewayException FromJson(JsonException ex, string context)
    {
        // A JsonException here is either a drifted 2xx body or an error body that did not match the
        // operation's generated error shape (which destroys the status). Either way the detail is
        // unusable; surface a caller-safe message and never leak System.Text.Json internals.
        _logger.LogWarning(ex, "PayPal {Context}: response could not be processed.", context);
        return new PayPalGatewayException($"PayPal {context}: the response could not be processed.", statusCode: 502);
    }

    private PayPalGatewayException Unreachable(Exception ex, string context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return new PayPalGatewayException($"PayPal {context} was cancelled.", statusCode: 504);
        }
        _logger.LogWarning(ex, "PayPal {Context}: provider unreachable or timed out.", context);
        return new PayPalGatewayException($"PayPal {context}: the provider is unreachable.", statusCode: 502);
    }

    private static int MapStatus(string? name, HttpStatusCode? httpStatus)
    {
        if (httpStatus is { } code)
        {
            var s = (int)code;
            if (s == 401 || s == 403) return 502; // our credentials/permissions — not the caller's fault
            if (s == 429) return 503;             // our quota
            if (s is >= 400 and < 500) return s;  // caller-fixable (400/404/409/422)
            return 502;
        }

        // Typed error without an HTTP status: infer from PayPal's error name.
        return name switch
        {
            "INVALID_REQUEST" => 400,
            "VALIDATION_ERROR" => 400,
            "RESOURCE_NOT_FOUND" => 404,
            "UNPROCESSABLE_ENTITY" => 422,
            "NOT_AUTHORIZED" or "PERMISSION_DENIED" or "AUTHENTICATION_FAILURE" => 502,
            _ => 422 // most business/card declines are caller-actionable
        };
    }

    private static string SafeReadRaw(RawError raw)
    {
        try { return raw.ReadAsString(); }
        catch { return "<unreadable>"; }
    }
}
