using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The only place in the application that talks to the PayPal SDK. Raw card data flows in for a
/// single call and is handed straight to the SDK; it is NEVER logged, persisted, or placed into an
/// exception message.
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalGateway> _logger;
    private readonly string _defaultCurrency;

    public PayPalGateway(
        PayPalServerSdkClient client,
        IAppLogger<PayPalGateway> logger,
        IOptions<PayPalSettings> settings)
    {
        _client = client;
        _logger = logger;
        _defaultCurrency = string.IsNullOrWhiteSpace(settings.Value.Currency) ? "USD" : settings.Value.Currency;
    }

    // ---------------------------------------------------------------------------------------------
    // 1 + 3. Create order (intent=AUTHORIZE) with raw or vaulted card, then place the hold.
    // ---------------------------------------------------------------------------------------------
    public async Task<PayPalAuthorization> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken ct = default)
    {
        var hasCard = request.Card is not null;
        var hasVault = !string.IsNullOrWhiteSpace(request.VaultId);
        if (hasCard == hasVault)
        {
            throw new ArgumentException(
                "Exactly one of raw Card details or a saved VaultId must be supplied.", nameof(request));
        }

        var currency = string.IsNullOrWhiteSpace(request.Currency) ? _defaultCurrency : request.Currency;

        CardRequest card = hasCard
            ? new CardRequest
            {
                Name = request.Card!.CardholderName,
                Number = request.Card.Number,
                Expiry = request.Card.ToPayPalExpiry(),
                SecurityCode = request.Card.SecurityCode,
                BillingAddress = BuildBillingAddress(request.Card)
            }
            : new CardRequest
            {
                VaultId = request.VaultId
            };

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits =
            [
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = FormatAmount(request.Amount)
                    }
                }
            ],
            PaymentSource = new PaymentSource { Card = card }
        };

        Order created;
        try
        {
            created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: request.IdempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateOrders(ex, "create order");
        }
        catch (JsonException ex) { throw Unreadable("create order", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable("create order", ex); }

        // Stop rather than build a browser/3DS round-trip. This gate runs BEFORE we decide the flow.
        if (RequiresChallenge(created))
        {
            var status = created.Status?.Value ?? "unknown";
            throw new PayPalChallengeRequiredException(
                $"PayPal requires shopper approval (order status '{status}') before this card payment can be " +
                $"authorized; the integration does not perform browser/3DS approval. PayPal order id: {created.Id}.");
        }

        var orderId = created.Id ?? throw new PayPalGatewayException("PayPal did not return an order id.");

        // Advanced Card Processing (raw/vaulted card in payment_source at create time) executes the
        // AUTHORIZE intent INLINE during CreateOrder, so the authorization is already present in the
        // create response. Use it and do NOT call AuthorizeOrder — a second authorize on an
        // already-authorized order fails HTTP 422. AuthorizeOrder applies only to the payer-approval
        // (redirect) flow, where the create response carries no authorization yet.
        var authorization = ExtractAuthorization(created.PurchaseUnits);

        if (authorization is null)
        {
            OrderAuthorizeResponse authResp;
            try
            {
                authResp = await _client.Orders.AuthorizeOrder(
                    id: orderId,
                    payPalMockResponse: null,
                    payPalRequestId: request.IdempotencyKey + ":authorize",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw TranslateOrders(ex, "authorize order");
            }
            catch (JsonException ex) { throw Unreadable("authorize order", ex); }
            catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable("authorize order", ex); }

            authorization = ExtractAuthorization(authResp.PurchaseUnits);
            orderId = authResp.Id ?? orderId;
        }

        if (authorization is null)
        {
            throw new PayPalGatewayException(
                $"PayPal order '{orderId}' produced no authorization record.");
        }

        return new PayPalAuthorization
        {
            PayPalOrderId = orderId,
            AuthorizationId = authorization.Id!,
            Status = authorization.Status?.Value ?? "UNKNOWN",
            Amount = ParseAmount(authorization.Amount?.Value) ?? request.Amount,
            Currency = authorization.Amount?.CurrencyCode ?? currency,
            ExpiresAt = ParseDate(authorization.ExpirationTime)
        };
    }

    // ---------------------------------------------------------------------------------------------
    // 5. Capture an authorization.
    // ---------------------------------------------------------------------------------------------
    public async Task<PayPalCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null, // null body = full capture
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw TranslatePayments(ex.Error, "capture", ex);
        }
        catch (JsonException ex) { throw Unreadable("capture", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable("capture", ex); }

        var breakdown = captured.SellerReceivableBreakdown;
        var currency = breakdown?.GrossAmount?.CurrencyCode
                       ?? captured.Amount?.CurrencyCode
                       ?? _defaultCurrency;

        return new PayPalCapture
        {
            CaptureId = captured.Id ?? string.Empty,
            Status = captured.Status?.Value ?? "UNKNOWN",
            GrossAmount = ParseAmount(breakdown?.GrossAmount?.Value) ?? 0m,
            PayPalFee = ParseAmount(breakdown?.PaypalFee?.Value) ?? 0m,
            NetAmount = ParseAmount(breakdown?.NetAmount?.Value) ?? 0m,
            Currency = currency
        };
    }

    // ---------------------------------------------------------------------------------------------
    // 6. Reauthorize a stale hold.
    // ---------------------------------------------------------------------------------------------
    public async Task<PayPalAuthorization> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        var effectiveCurrency = string.IsNullOrWhiteSpace(currency) ? _defaultCurrency : currency;

        PaymentAuthorization reauth;
        try
        {
            reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = effectiveCurrency, Value = FormatAmount(amount) }
                },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            // A typed error carrying issue details on reauthorize means PayPal refused to renew the
            // hold as requested (e.g. honor window elapsed / max reauthorizations reached). Surface
            // an operator-actionable reason built from PayPal's own issue codes + descriptions.
            if (ex.Error.TryGetError(out var e) && e.Details is { Count: > 0 })
            {
                var reason = string.Join("; ", e.Details.Select(d =>
                    string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"));
                var debug = string.IsNullOrEmpty(e.DebugId) ? "" : $" (PayPal debug_id: {e.DebugId})";
                _logger.LogWarning(
                    $"PayPal reauthorize rejected for authorization {authorizationId}. Reason: {reason}{debug}");
                throw new AuthorizationNotRenewableException(
                    $"PayPal cannot reauthorize authorization '{authorizationId}': {reason}.{debug}");
            }
            throw TranslatePayments(ex.Error, "reauthorize", ex);
        }
        catch (JsonException ex) { throw Unreadable("reauthorize", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable("reauthorize", ex); }

        return new PayPalAuthorization
        {
            PayPalOrderId = reauth.SupplementaryData?.RelatedIds?.OrderId ?? string.Empty,
            AuthorizationId = reauth.Id ?? authorizationId,
            Status = reauth.Status?.Value ?? "UNKNOWN",
            Amount = ParseAmount(reauth.Amount?.Value) ?? amount,
            Currency = reauth.Amount?.CurrencyCode ?? effectiveCurrency,
            ExpiresAt = ParseDate(reauth.ExpirationTime)
        };
    }

    // ---------------------------------------------------------------------------------------------
    // 7. Void a hold.
    // ---------------------------------------------------------------------------------------------
    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey, // 4th positional param on this operation — passed by name
                prefer: "return=minimal",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw TranslatePayments(ex.Error, "void", ex);
        }
        catch (JsonException ex) when (IsEmptyBodyJsonException(ex))
        {
            // A successful void returns HTTP 204 No Content (return=minimal). The SDK still tries to
            // deserialize the empty body into PaymentAuthorization, which throws JsonException even
            // though the void SUCCEEDED — so an empty body here means success, not failure. A real
            // failure is an SdkException<VoidPaymentError> (handled above) or a transport error; a
            // malformed NON-empty 2xx body produces a different JsonException message and still fails.
            _logger.LogInformation("{PayPalVoid}",
                $"PayPal void succeeded (204 No Content) for authorization {authorizationId}.");
        }
        catch (JsonException ex) { throw Unreadable("void", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable("void", ex); }
    }

    // ---------------------------------------------------------------------------------------------
    // 8. Refund a capture, full or partial.
    // ---------------------------------------------------------------------------------------------
    public async Task<PayPalRefund> RefundAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        var effectiveCurrency = string.IsNullOrWhiteSpace(currency) ? _defaultCurrency : currency;

        RefundRequest? body = amount is null
            ? null // null body = full refund
            : new RefundRequest
            {
                Amount = new Money { CurrencyCode = effectiveCurrency, Value = FormatAmount(amount.Value) }
            };

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw TranslatePayments(ex.Error, "refund", ex);
        }
        catch (JsonException ex) { throw Unreadable("refund", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable("refund", ex); }

        return new PayPalRefund
        {
            RefundId = refund.Id ?? string.Empty,
            Status = refund.Status?.Value ?? "UNKNOWN",
            Amount = ParseAmount(refund.Amount?.Value) ?? amount ?? 0m,
            Currency = refund.Amount?.CurrencyCode ?? effectiveCurrency
        };
    }

    // ---------------------------------------------------------------------------------------------
    // 9. Read a hold.
    // ---------------------------------------------------------------------------------------------
    public async Task<PayPalAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        PaymentAuthorization auth;
        try
        {
            auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw TranslatePayments(ex.Error, "get authorization", ex);
        }
        catch (JsonException ex) { throw Unreadable("get authorization", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable("get authorization", ex); }

        return new PayPalAuthorization
        {
            PayPalOrderId = auth.SupplementaryData?.RelatedIds?.OrderId ?? string.Empty,
            AuthorizationId = auth.Id ?? authorizationId,
            Status = auth.Status?.Value ?? "UNKNOWN",
            Amount = ParseAmount(auth.Amount?.Value) ?? 0m,
            Currency = auth.Amount?.CurrencyCode ?? _defaultCurrency,
            ExpiresAt = ParseDate(auth.ExpirationTime)
        };
    }

    // ---------------------------------------------------------------------------------------------
    // 4. Vault a card / delete a vaulted card.
    // ---------------------------------------------------------------------------------------------
    public async Task<PayPalVaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        PaymentTokenResponse response;
        try
        {
            response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: new PaymentTokenRequest
                {
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Name = card.CardholderName,
                            Number = card.Number,
                            Expiry = card.ToPayPalExpiry(),
                            SecurityCode = card.SecurityCode,
                            BillingAddress = BuildBillingAddress(card)
                        }
                    }
                },
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateVault(ex.Error, "vault card", ex);
        }
        catch (JsonException ex) { throw Unreadable("vault card", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable("vault card", ex); }

        var vaultedCard = response.PaymentSource?.Card;
        return new PayPalVaultedCard
        {
            VaultId = response.Id ?? string.Empty,
            Brand = vaultedCard?.Brand?.Value ?? "UNKNOWN",
            Last4 = vaultedCard?.LastDigits ?? string.Empty,
            Expiry = vaultedCard?.Expiry
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateVault(ex.Error, "delete vaulted card", ex);
        }
        catch (JsonException ex) { throw Unreadable("delete vaulted card", ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable("delete vaulted card", ex); }
    }

    // ---------------------------------------------------------------------------------------------
    // 10. Transaction search, paged to exhaustion.
    // ---------------------------------------------------------------------------------------------
    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var startDate = Iso8601(from);
        var endDate = Iso8601(to);
        const int pageSize = 100;

        var results = new List<PayPalTransaction>();
        var page = 1;

        while (true)
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
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
                    page: page,
                    ct: ct);
            }
            catch (SdkException<RawError> ex) // Case B — no typed accessors
            {
                throw Fail("transaction search", $"HTTP {(int)ex.Error.StatusCode}", null, ex);
            }
            catch (JsonException ex) { throw Unreadable("transaction search", ex); }
            catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable("transaction search", ex); }

            foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info?.TransactionId is null)
                {
                    continue;
                }

                results.Add(new PayPalTransaction
                {
                    TransactionId = info.TransactionId,
                    Status = info.TransactionStatus,
                    Amount = ParseAmount(info.TransactionAmount?.Value) ?? 0m,
                    Currency = info.TransactionAmount?.CurrencyCode,
                    Fee = ParseAmount(info.FeeAmount?.Value),
                    InitiationDate = ParseDate(info.TransactionInitiationDate),
                    EventCode = info.TransactionEventCode
                });
            }

            var totalPages = response.TotalPages ?? page;
            if (page >= totalPages)
            {
                break;
            }
            page++;
        }

        return results;
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------
    private static Address? BuildBillingAddress(CardDetails card)
    {
        // Address.CountryCode is required by the SDK, so only build a billing address when we have it.
        if (string.IsNullOrWhiteSpace(card.CountryCode))
        {
            return null;
        }

        return new Address
        {
            CountryCode = card.CountryCode,
            AddressLine1 = card.AddressLine1,
            AddressLine2 = card.AddressLine2,
            AdminArea1 = card.AdminArea1,
            AdminArea2 = card.AdminArea2,
            PostalCode = card.PostalCode
        };
    }

    // Reads the first placed authorization from a purchase-units collection. Both the CreateOrder
    // response (inline card flow) and the AuthorizeOrder response expose the same
    // IReadOnlyList<PurchaseUnit> shape (purchase_units[].payments.authorizations[]), so both flows
    // share this helper.
    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault(a => !string.IsNullOrEmpty(a.Id));

    private static bool RequiresChallenge(Order order)
    {
        if (order.Status is not null && order.Status == OrderStatus.PayerActionRequired)
        {
            return true;
        }

        if (order.Links is not null)
        {
            foreach (var link in order.Links)
            {
                var rel = link.Rel;
                if (rel is not null &&
                    (rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase) ||
                     rel.Contains("3ds", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string Iso8601(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException ||
        (ex is TaskCanceledException && !ct.IsCancellationRequested); // caller cancellation propagates unwrapped

    // True only for the "empty input" JsonException System.Text.Json throws when the response body is
    // empty/whitespace (an HTTP 204 No Content). A malformed but NON-empty body produces a different
    // message ("is an invalid start of a value" / "Expected ...") and is therefore NOT treated as empty.
    private static bool IsEmptyBodyJsonException(JsonException ex) =>
        ex.Message.Contains("does not contain any JSON tokens", StringComparison.OrdinalIgnoreCase);

    // Orders operations expose TryGetError(out Error) then TryGetRawError.
    private PayPalGatewayException TranslateOrders<TError>(SdkException<TError> ex, string op)
        where TError : ApiError
    {
        // The typed accessor lives on the concrete {Operation}Error; read it here where TError is known.
        if (ex.Error is CreateOrderError co && co.TryGetError(out var e1)) return FromError(op, e1, ex);
        if (ex.Error is AuthorizeOrderError ao && ao.TryGetError(out var e2)) return FromError(op, e2, ex);
        if (ex.Error.TryGetRawError(out var raw)) return Fail(op, $"HTTP {(int)raw.StatusCode}", null, ex);
        return Fail(op, "PayPal rejected the request.", null, ex);
    }

    // Payments operations expose TryGetError(out Error), TryGetNoContent(out RawError), TryGetRawError.
    private PayPalGatewayException TranslatePayments(object typedError, string op, Exception inner)
    {
        switch (typedError)
        {
            case CaptureAuthorizedPaymentError e:
                if (e.TryGetError(out var c1)) return FromError(op, c1, inner);
                if (e.TryGetNoContent(out var cn)) return Fail(op, $"HTTP {(int)cn.StatusCode}", null, inner);
                if (e.TryGetRawError(out var cr)) return Fail(op, $"HTTP {(int)cr.StatusCode}", null, inner);
                break;
            case ReauthorizePaymentError e:
                if (e.TryGetError(out var r1)) return FromError(op, r1, inner);
                if (e.TryGetNoContent(out var rn)) return Fail(op, $"HTTP {(int)rn.StatusCode}", null, inner);
                if (e.TryGetRawError(out var rr)) return Fail(op, $"HTTP {(int)rr.StatusCode}", null, inner);
                break;
            case VoidPaymentError e:
                if (e.TryGetError(out var v1)) return FromError(op, v1, inner);
                if (e.TryGetNoContent(out var vn)) return Fail(op, $"HTTP {(int)vn.StatusCode}", null, inner);
                if (e.TryGetRawError(out var vr)) return Fail(op, $"HTTP {(int)vr.StatusCode}", null, inner);
                break;
            case RefundCapturedPaymentError e:
                if (e.TryGetError(out var f1)) return FromError(op, f1, inner);
                if (e.TryGetNoContent(out var fn)) return Fail(op, $"HTTP {(int)fn.StatusCode}", null, inner);
                if (e.TryGetRawError(out var fr)) return Fail(op, $"HTTP {(int)fr.StatusCode}", null, inner);
                break;
            case GetAuthorizedPaymentError e:
                if (e.TryGetError(out var g1)) return FromError(op, g1, inner);
                if (e.TryGetNoContent(out var gn)) return Fail(op, $"HTTP {(int)gn.StatusCode}", null, inner);
                if (e.TryGetRawError(out var gr)) return Fail(op, $"HTTP {(int)gr.StatusCode}", null, inner);
                break;
        }
        return Fail(op, "PayPal rejected the request.", null, inner);
    }

    // Vault operations expose TryGetError1(out Error1) then TryGetRawError.
    private PayPalGatewayException TranslateVault(object typedError, string op, Exception inner)
    {
        switch (typedError)
        {
            case CreatePaymentTokenError e:
                if (e.TryGetError1(out var c1)) return FromError1(op, c1, inner);
                if (e.TryGetRawError(out var cr)) return Fail(op, $"HTTP {(int)cr.StatusCode}", null, inner);
                break;
            case DeletePaymentTokenError e:
                if (e.TryGetError1(out var d1)) return FromError1(op, d1, inner);
                if (e.TryGetRawError(out var dr)) return Fail(op, $"HTTP {(int)dr.StatusCode}", null, inner);
                break;
        }
        return Fail(op, "PayPal rejected the request.", null, inner);
    }

    private PayPalGatewayException FromError(string op, Error e, Exception inner) =>
        Fail(op, AppendIssues(e.Message, e.Details?.Select(d => (d.Issue, d.Description))), e.DebugId, inner);

    private PayPalGatewayException FromError1(string op, Error1 e, Exception inner) =>
        Fail(op, AppendIssues(e.Message, e.Details?.Select(d => (d.Issue, d.Description))), e.DebugId, inner);

    // Appends PayPal's per-issue validation detail (issue code + description) to the safe message so a
    // 422 is diagnosable for operators. PayPal issue codes/descriptions never contain card data.
    private static string AppendIssues(string message, IEnumerable<(string Issue, string? Description)>? details)
    {
        if (details is null)
        {
            return message;
        }

        var joined = string.Join("; ", details.Select(d =>
            string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"));

        return string.IsNullOrEmpty(joined) ? message : $"{message} [{joined}]";
    }

    private PayPalGatewayException Unreachable(string op, Exception inner) =>
        Fail(op, "PayPal is currently unreachable.", null, inner);

    private PayPalGatewayException Unreadable(string op, JsonException inner) =>
        // A JsonException here is either a drifted 2xx body or a non-2xx body that did not match the
        // generated error shape (in which case the HTTP status is lost). Either way we surface a safe,
        // non-specific message rather than leaking System.Text.Json detail.
        Fail(op, "PayPal returned a response that could not be processed.", null, inner);

    private PayPalGatewayException Fail(string op, string safeMessage, string? debugId, Exception? inner)
    {
        var diagnostic = debugId is null
            ? $"PayPal {op} failed: {safeMessage}"
            : $"PayPal {op} failed: {safeMessage} (PayPal debug_id: {debugId})";
        _logger.LogWarning("{PayPalFailure}", diagnostic); // structured arg — never a raw template
        return new PayPalGatewayException(diagnostic, inner);
    }
}
