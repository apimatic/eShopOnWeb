using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPal;
using PayPal.Core;
using PayPal.Core.ErrorResponse;
using PayPal.Core.Exceptions;
using PayPal.Core.Hooks;
using PayPal.Errors;
using PayPalModels = PayPal.Models;
using PayPalEnums = PayPal.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The single place the PayPal SDK is used. Every method translates SDK failures into
/// <see cref="PayPalGatewayException"/> (and its subtypes), and bounds each call with a total deadline.
/// </summary>
public sealed class PayPalPaymentGateway : IPayPalPaymentGateway
{
    // A stable per-process suffix so PayPal invoice_ids stay unique across in-memory restarts
    // (eShop order ids restart at 1 each run). The join back to an order uses custom_id, not invoice_id.
    private static readonly string InstanceTag = Guid.NewGuid().ToString("N").Substring(0, 8);

    private const int MaxReconciliationPages = 100;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);

    private readonly PayPalClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken ct = default)
    {
        using var scope = Bounded(ct, out var budget);

        var customId = request.OrderId.ToString(CultureInfo.InvariantCulture);
        var invoiceId = $"ESHOP-{request.OrderId}-{InstanceTag}";

        var orderRequest = new PayPalModels.OrderRequest
        {
            Intent = PayPalEnums.CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PayPalModels.PurchaseUnitRequest
                {
                    ReferenceId = customId,
                    CustomId = customId,
                    InvoiceId = invoiceId,
                    Description = $"eShopOnWeb order {request.OrderId}",
                    Amount = new PayPalModels.AmountWithBreakdown
                    {
                        CurrencyCode = request.CurrencyCode,
                        Value = FormatAmount(request.Amount)
                    }
                }
            },
            PaymentSource = BuildPaymentSource(request)
        };

        PayPalModels.Order created;
        try
        {
            created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: Rid($"auth-{request.IdempotencyKey}-create"),
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: budget);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            var err = ex.Error.TryGetError(out var e) ? e : null;
            throw MapError("Create order", err, StatusOf(ex.Error), ex);
        }
        catch (Exception ex) when (IsTransport(ex, budget, ct))
        {
            throw Unreachable("Create order", ex);
        }

        GuardNoChallenge(created.Status, created.Links, "authorize");

        // With an inline card/vault payment source, PayPal authorizes the order at creation time, so the
        // authorization is already on the created order. Only call AuthorizeOrder when it is not yet present
        // (e.g. an order that was merely approved and awaits authorization).
        var authorization = FirstAuthorization(created.PurchaseUnits);
        if (authorization is null)
        {
            PayPalModels.OrderAuthorizeResponse authorized;
            try
            {
                authorized = await _client.Orders.AuthorizeOrder(
                    id: created.Id!,
                    payPalMockResponse: null,
                    payPalRequestId: Rid($"auth-{request.IdempotencyKey}-authorize"),
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: budget);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                var err = ex.Error.TryGetError(out var e) ? e : null;
                throw MapError("Authorize order", err, StatusOf(ex.Error), ex);
            }
            catch (Exception ex) when (IsTransport(ex, budget, ct))
            {
                throw Unreachable("Authorize order", ex);
            }

            GuardNoChallenge(authorized.Status, authorized.Links, "authorize");
            authorization = FirstAuthorization(authorized.PurchaseUnits);
        }

        if (authorization?.Id is not { Length: > 0 } authorizationId)
        {
            throw new PayPalGatewayException(
                "PayPal did not return an authorization for the order (the card may have been declined).",
                statusCode: 402);
        }

        _logger.LogInformation(
            "PayPal authorized order {OrderId}: paypalOrder={PayPalOrderId} authorization={AuthorizationId} status={Status}",
            request.OrderId, created.Id, authorizationId, authorization.Status?.Value);

        return new AuthorizationResult(created.Id!, authorizationId, authorization.Status?.Value);
    }

    public async Task<CaptureResult> CaptureAsync(CaptureCommand command, CancellationToken ct = default)
    {
        using var scope = Bounded(ct, out var budget);

        var authorizationId = command.AuthorizationId;
        string? renewedAuthorizationId = null;

        var captureBody = new PayPalModels.CaptureRequest
        {
            FinalCapture = true,
            Amount = new PayPalModels.Money
            {
                CurrencyCode = command.CurrencyCode,
                Value = FormatAmount(command.Amount)
            }
        };

        PayPalModels.CapturedPayment captured;
        try
        {
            captured = await CaptureOnceAsync(authorizationId, captureBody, command.IdempotencyKey, budget);
        }
        catch (PayPalAuthorizationStaleSignal)
        {
            // The authorization is stale — renew it, then capture the renewed authorization.
            renewedAuthorizationId = await ReauthorizeAsync(authorizationId, command, budget);
            authorizationId = renewedAuthorizationId;
            captured = await CaptureOnceAsync(authorizationId, captureBody, $"{command.IdempotencyKey}-recapture", budget);
        }

        var breakdown = captured.SellerReceivableBreakdown;
        var gross = breakdown is not null ? ParseAmount(breakdown.GrossAmount.Value) : ParseAmount(captured.Amount?.Value);
        var fee = breakdown?.PaypalFee is { } f ? ParseAmount(f.Value) : (decimal?)null;
        var net = breakdown?.NetAmount is { } n ? ParseAmount(n.Value) : (decimal?)null;

        _logger.LogInformation(
            "PayPal captured authorization {AuthorizationId}: capture={CaptureId} gross={Gross} fee={Fee} net={Net}",
            authorizationId, captured.Id, gross, fee, net);

        return new CaptureResult(captured.Id!, captured.Status?.Value, gross, fee, net, renewedAuthorizationId);
    }

    private async Task<PayPalModels.CapturedPayment> CaptureOnceAsync(
        string authorizationId, PayPalModels.CaptureRequest body, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            return await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: Rid($"capture-{idempotencyKey}"),
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            var err = ex.Error.TryGetError(out var e) ? e : null;
            var status = ex.Error.TryGetRawError(out var raw) ? (int)raw.StatusCode
                : (ex.Error.TryGetNoContent(out var nc) ? (int)nc.StatusCode : (int?)null);
            if (IsExpiredAuthorization(err))
            {
                _logger.LogWarning("Authorization {AuthorizationId} is stale; will attempt reauthorization.", authorizationId);
                throw new PayPalAuthorizationStaleSignal();
            }
            throw MapError("Capture", err, status, ex);
        }
        catch (Exception ex) when (IsTransport(ex, ct, ct))
        {
            throw Unreachable("Capture", ex);
        }
    }

    private async Task<string> ReauthorizeAsync(string authorizationId, CaptureCommand command, CancellationToken ct)
    {
        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: Rid($"reauth-{command.IdempotencyKey}"),
                payPalAuthAssertion: null,
                body: new PayPalModels.ReauthorizeRequest
                {
                    Amount = new PayPalModels.Money
                    {
                        CurrencyCode = command.CurrencyCode,
                        Value = FormatAmount(command.Amount)
                    }
                },
                prefer: "return=representation",
                ct: ct);

            var newId = reauth.Id ?? authorizationId;
            _logger.LogInformation("Reauthorized {Old} -> {New} (status {Status}).", authorizationId, newId, reauth.Status?.Value);
            return newId;
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            var err = ex.Error.TryGetError(out var e) ? e : null;
            throw new PayPalAuthorizationExpiredException(
                "The authorization has expired and could not be renewed. Re-collect payment from the shopper before fulfilling this order.",
                issue: err?.Details is { Count: > 0 } d ? d[0].Issue : err?.Name,
                debugId: err?.DebugId,
                inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex, ct, ct))
        {
            throw Unreachable("Reauthorize", ex);
        }
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct = default)
    {
        using var scope = Bounded(ct, out var budget);

        // A successful void returns 204 No Content; the SDK still tries to deserialize the (empty) body into
        // PaymentAuthorization and throws JsonException. Capture the real HTTP status via a response hook so a
        // 2xx-with-empty-body is recognised as success rather than a transport failure.
        int? httpStatus = null;
        var requestOptions = new RequestOptions
        {
            Hooks = new[] { SdkHook.OnResponse((res, _) => httpStatus = (int)res.StatusCode) }
        };

        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: Rid($"void-{authorizationId}"),
                prefer: "return=minimal",
                requestOptions: requestOptions,
                ct: budget);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            var err = ex.Error.TryGetError(out var e) ? e : null;
            var status = ex.Error.TryGetRawError(out var raw) ? (int)raw.StatusCode
                : (ex.Error.TryGetNoContent(out var nc) ? (int)nc.StatusCode : (int?)null);
            throw MapError("Void authorization", err, status, ex);
        }
        catch (JsonException) when (httpStatus is >= 200 and < 300)
        {
            // Void succeeded (2xx) with no body to deserialize — this is the expected success path.
        }
        catch (Exception ex) when (IsTransport(ex, budget, ct))
        {
            throw Unreachable("Void authorization", ex);
        }

        _logger.LogInformation("PayPal voided authorization {AuthorizationId}.", authorizationId);
    }

    public async Task<RefundResult> RefundAsync(RefundCommand command, CancellationToken ct = default)
    {
        using var scope = Bounded(ct, out var budget);

        var body = new PayPalModels.RefundRequest
        {
            Amount = command.Amount is { } amt
                ? new PayPalModels.Money { CurrencyCode = command.CurrencyCode, Value = FormatAmount(amt) }
                : null
        };

        PayPalModels.Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: command.CaptureId,
                payPalMockResponse: null,
                payPalRequestId: Rid($"refund-{command.IdempotencyKey}"),
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: budget);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            var err = ex.Error.TryGetError(out var e) ? e : null;
            var status = ex.Error.TryGetRawError(out var raw) ? (int)raw.StatusCode
                : (ex.Error.TryGetNoContent(out var nc) ? (int)nc.StatusCode : (int?)null);
            throw MapError("Refund", err, status, ex);
        }
        catch (Exception ex) when (IsTransport(ex, budget, ct))
        {
            throw Unreachable("Refund", ex);
        }

        var refundedAmount = refund.Amount is { } m ? ParseAmount(m.Value) : (command.Amount ?? 0m);
        _logger.LogInformation("PayPal refunded capture {CaptureId}: refund={RefundId} amount={Amount} status={Status}",
            command.CaptureId, refund.Id, refundedAmount, refund.Status?.Value);

        return new RefundResult(refund.Id!, refund.Status?.Value, refundedAmount);
    }

    public async Task<SavedCardResult> VaultCardAsync(VaultCardCommand command, CancellationToken ct = default)
    {
        using var scope = Bounded(ct, out var budget);

        var customer = string.IsNullOrEmpty(command.PayPalCustomerId)
            ? null
            : new PayPalModels.Customer { Id = command.PayPalCustomerId };

        var body = new PayPalModels.PaymentTokenRequest
        {
            Customer = customer,
            PaymentSource = new PayPalModels.PaymentTokenRequestPaymentSource
            {
                Card = new PayPalModels.PaymentTokenRequestCard
                {
                    Name = command.Card.CardholderName,
                    Number = command.Card.Number,
                    Expiry = command.Card.Expiry,
                    SecurityCode = command.Card.SecurityCode,
                    BillingAddress = BuildBillingAddress(command.Card)
                }
            }
        };

        PayPalModels.PaymentTokenResponse token;
        try
        {
            token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: body,
                ct: budget);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            var err = ex.Error.TryGetError(out var e) ? e : null;
            throw MapError("Save card", err, StatusOf(ex.Error), ex);
        }
        catch (Exception ex) when (IsTransport(ex, budget, ct))
        {
            throw Unreachable("Save card", ex);
        }

        var card = token.PaymentSource?.Card;
        var customerId = token.Customer?.Id ?? command.PayPalCustomerId ?? string.Empty;

        _logger.LogInformation("PayPal vaulted a card: token={TokenId} customer={CustomerId} brand={Brand} last4={Last4}",
            token.Id, customerId, card?.Brand?.Value, card?.LastDigits);

        return new SavedCardResult(
            token.Id!,
            customerId,
            card?.Brand?.Value,
            card?.LastDigits,
            card?.Expiry,
            card?.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default)
    {
        using var scope = Bounded(ct, out var budget);
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultTokenId, ct: budget);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            var err = ex.Error.TryGetError(out var e) ? e : null;
            throw MapError("Delete card", err, StatusOf(ex.Error), ex);
        }
        catch (Exception ex) when (IsTransport(ex, budget, ct))
        {
            throw Unreachable("Delete card", ex);
        }

        _logger.LogInformation("PayPal deleted vaulted card {TokenId}.", vaultTokenId);
    }

    public async Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        using var scope = Bounded(ct, out var budget);

        var startDate = FormatDate(from);
        var endDate = FormatDate(to);
        var results = new List<ReconciliationTransaction>();

        int page = 1;
        int totalPages = 1;
        do
        {
            PayPalModels.SearchResponse response;
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
                    pageSize: 100,
                    page: page,
                    ct: budget);
            }
            catch (SdkException<RawError> ex)
            {
                // TransactionSearch is Case B — the RawError carries the status directly.
                throw MapRaw("Search transactions", ex.Error, ex);
            }
            catch (Exception ex) when (IsTransport(ex, budget, ct))
            {
                throw Unreachable("Search transactions", ex);
            }

            if (response.TransactionDetails is { } details)
            {
                foreach (var d in details)
                {
                    var info = d.TransactionInfo;
                    if (info is null) continue;
                    results.Add(new ReconciliationTransaction(
                        info.TransactionId,
                        info.TransactionStatus,
                        info.TransactionAmount is { } m ? ParseAmount(m.Value) : (decimal?)null,
                        info.TransactionAmount?.CurrencyCode,
                        info.InvoiceId,
                        info.CustomField,
                        TryParseDate(info.TransactionInitiationDate)));
                }
            }

            totalPages = response.TotalPages ?? 1;
            page++;
        }
        while (page <= totalPages && page <= MaxReconciliationPages);

        return results;
    }

    // --- helpers ---

    private static PayPalModels.AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PayPalModels.PurchaseUnit>? units) =>
        units?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    private PayPalModels.PaymentSource BuildPaymentSource(AuthorizeRequest request)
    {
        if (!string.IsNullOrEmpty(request.VaultTokenId))
        {
            return new PayPalModels.PaymentSource
            {
                Card = new PayPalModels.CardRequest { VaultId = request.VaultTokenId }
            };
        }

        var card = request.Card ?? throw new PayPalGatewayException(
            "No payment instrument supplied: provide card details or a saved card id.", statusCode: 400);

        return new PayPalModels.PaymentSource
        {
            Card = new PayPalModels.CardRequest
            {
                Name = card.CardholderName,
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                BillingAddress = BuildBillingAddress(card)
            }
        };
    }

    private static PayPalModels.Address BuildBillingAddress(CardDetails card) =>
        new()
        {
            CountryCode = string.IsNullOrWhiteSpace(card.BillingCountryCode) ? "US" : card.BillingCountryCode!,
            PostalCode = card.BillingPostalCode
        };

    private static void GuardNoChallenge(PayPalEnums.OrderStatus? status, IReadOnlyList<PayPalModels.LinkDescription>? links, string action)
    {
        var needsApproval = status == PayPalEnums.OrderStatus.PayerActionRequired
            || (links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase)) ?? false);
        if (needsApproval)
        {
            throw new PayPalChallengeRequiredException(
                $"PayPal requires the shopper to approve this payment in a browser (e.g. 3-D Secure), " +
                $"which this integration does not support. Cannot {action} the order.");
        }
    }

    private static bool IsExpiredAuthorization(PayPalModels.Error? error)
    {
        if (error is null) return false;
        if (error.Name?.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) == true) return true;
        return error.Details?.Any(d =>
            d.Issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
            || d.Issue.Contains("REAUTH", StringComparison.OrdinalIgnoreCase)) ?? false;
    }

    private PayPalGatewayException MapError(string op, PayPalModels.Error? error, int? status, Exception inner)
    {
        var issue = error?.Details is { Count: > 0 } d ? d[0].Issue : null;
        _logger.LogWarning(
            "PayPal {Op} failed: status={Status} name={Name} issue={Issue} debugId={DebugId}",
            op, status, error?.Name, issue, error?.DebugId);
        var message = error is not null
            ? $"{op} was rejected by PayPal: {error.Name}{(issue is null ? "" : $" ({issue})")}."
            : $"{op} failed at PayPal.";
        return new PayPalGatewayException(message, status, error?.Name, issue, error?.DebugId, inner);
    }

    private PayPalGatewayException MapRaw(string op, RawError raw, Exception inner)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("PayPal {Op} failed: status={Status} body={Body}", op, status, Safe(raw));
        return new PayPalGatewayException($"{op} failed at PayPal (HTTP {status}).", status, inner: inner);
    }

    private PayPalGatewayException Unreachable(string op, Exception inner)
    {
        _logger.LogWarning(inner, "PayPal {Op} could not reach the provider.", op);
        return new PayPalGatewayException($"{op}: PayPal could not be reached.", statusCode: null, inner: inner);
    }

    private static string Safe(RawError raw)
    {
        try { return raw.ReadAsString(); } catch { return "<unreadable>"; }
    }

    private static int? StatusOf<TError>(TError error) where TError : ApiError =>
        error.TryGetRawError(out var raw) ? (int)raw.StatusCode : (int?)null;

    private static bool IsTransport(Exception ex, CancellationToken budget, CancellationToken caller)
    {
        // A JsonException on a 2xx body, or a genuine transport failure/timeout, all reach here. A cancel
        // requested by the *caller* (request aborted) is not ours to wrap — let it propagate.
        if (ex is OperationCanceledException && caller.IsCancellationRequested) return false;
        return ex is HttpRequestException || ex is OperationCanceledException || ex is JsonException;
    }

    private static IDisposable Bounded(CancellationToken ct, out CancellationToken budget)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        budget = cts.Token;
        return cts;
    }

    // Scope a PayPal-Request-Id to this process so it stays stable within a run (double-click dedup) but does
    // not collide across restarts, where in-memory order ids restart at 1.
    private static string Rid(string suffix) => $"{InstanceTag}-{suffix}";

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    private static string FormatDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static DateTimeOffset? TryParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : (DateTimeOffset?)null;

    /// <summary>Internal signal that a capture failed because its authorization is stale and reauthorizable.</summary>
    private sealed class PayPalAuthorizationStaleSignal : Exception { }
}
