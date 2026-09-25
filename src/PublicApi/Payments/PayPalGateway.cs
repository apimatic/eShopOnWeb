using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Requests.Orders;
using PayPalServerSdk.Requests.Payments;
using PayPalServerSdk.Requests.TransactionSearch;
using PayPalServerSdk.Requests.Vault;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The single place the PayPal Server SDK is called. Translates every SDK failure to
/// <see cref="PayPalIntegrationException"/>, sends deterministic <c>PayPal-Request-Id</c> keys so a
/// repeat is safe, bounds each whole operation with a cancellation deadline, and settles unknown
/// outcomes by re-reading provider state. Card details flow straight into SDK models and are never
/// persisted or logged (request-body logging is off on the client).
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);
    private const int PageSizeMax = 500;
    private const int MaxReconciliationPages = 1000;

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options,
        ILogger<PayPalGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Authorize (place a hold)

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeInstruction instruction, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        var ct = cts.Token;

        var card = !string.IsNullOrEmpty(instruction.VaultId)
            ? new CardRequest { VaultId = instruction.VaultId }
            : BuildCard(instruction.OneOffCard!);

        var orderRequest = new CreateOrderRequest
        {
            // Keyed by the globally-unique invoice id (not the resettable eShop order id) so the key
            // never collides with a prior run and PayPal never replays a stale order.
            PayPalRequestId = $"paypal-order-{instruction.InvoiceId}",
            Prefer = "return=representation",
            Body = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new[]
                {
                    new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = _options.Currency,
                            Value = FormatAmount(instruction.Amount),
                        },
                        InvoiceId = instruction.InvoiceId,
                        CustomId = instruction.OrderId.ToString(CultureInfo.InvariantCulture),
                    },
                },
                PaymentSource = new PaymentSource { Card = card },
            },
        };

        Order order;
        try
        {
            order = await CallA<Order, CreateOrderError>("CreateOrder",
                t => _client.Orders.CreateOrder(orderRequest, cancellationToken: t),
                e => e.TryGetError(out var err) ? err : null, ct);
        }
        catch (SdkException ex) when (IsTransport(ex))
        {
            // Unknown outcome: retry once with the same request id (idempotent replay).
            _logger.LogWarning(ex, "CreateOrder outcome unknown for order {OrderId}; retrying with the same request id.", instruction.OrderId);
            try
            {
                order = await CallA<Order, CreateOrderError>("CreateOrder(settle)",
                    t => _client.Orders.CreateOrder(orderRequest, cancellationToken: t),
                    e => e.TryGetError(out var err) ? err : null, ct);
            }
            catch (SdkException ex2) when (IsTransport(ex2))
            {
                throw new PayPalIntegrationException(
                    "The authorization could not be confirmed (provider unreachable). Retry with the same request id to settle.",
                    null, null, ex2, outcomeUnknown: true);
            }
        }

        var payPalOrderId = order.Id
            ?? throw new PayPalIntegrationException("PayPal did not return an order id.");
        EnsureNoChallenge("CreateOrder", order.Status, payPalOrderId);

        var authorization = FindAuthorization(order.PurchaseUnits);
        if (authorization is null)
        {
            // Two-step: the order is approved but not yet authorized — authorize it now.
            authorization = await AuthorizeExistingOrder(instruction.InvoiceId, payPalOrderId, ct);
        }

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PayPalIntegrationException("PayPal did not create an authorization for the order.");
        }

        if (authorization.Status is { } status && status == AuthorizationStatus.Denied)
        {
            throw new PayPalIntegrationException("The card authorization was denied by PayPal.");
        }

        var descriptor = instruction.InstrumentDescriptor ?? DescriptorFromCard(instruction.OneOffCard);
        var expiresAt = ParseTime(authorization.ExpirationTime);

        _logger.LogInformation(
            "Authorized order {OrderId}: paypalOrder={PayPalOrderId} authorization={AuthorizationId} status={Status}",
            instruction.OrderId, payPalOrderId, authorization.Id, authorization.Status?.Value);

        return new AuthorizationResult(payPalOrderId, authorization.Id!,
            authorization.Status?.Value ?? "CREATED", expiresAt, descriptor);
    }

    private async Task<AuthorizationWithAdditionalData?> AuthorizeExistingOrder(string invoiceId, string payPalOrderId, CancellationToken ct)
    {
        var request = new AuthorizeOrderRequest
        {
            Id = payPalOrderId,
            PayPalRequestId = $"authorize-{invoiceId}",
            Prefer = "return=representation",
        };

        OrderStatus? status;
        IReadOnlyList<PurchaseUnit>? purchaseUnits;
        try
        {
            var response = await CallA<OrderAuthorizeResponse, AuthorizeOrderError>("AuthorizeOrder",
                t => _client.Orders.AuthorizeOrder(request, cancellationToken: t),
                e => e.TryGetError(out var err) ? err : null, ct);
            status = response.Status;
            purchaseUnits = response.PurchaseUnits;
        }
        catch (SdkException ex) when (IsTransport(ex))
        {
            // Unknown outcome: re-read the order to see whether the authorization landed.
            _logger.LogWarning(ex, "AuthorizeOrder outcome unknown for invoice {InvoiceId}; re-reading order {PayPalOrderId}.", invoiceId, payPalOrderId);
            Order reread;
            try
            {
                reread = await ReadOrder(payPalOrderId, ct);
            }
            catch (SdkException ex2) when (IsTransport(ex2))
            {
                throw new PayPalIntegrationException(
                    "The authorization could not be confirmed (provider unreachable).",
                    null, null, ex2, outcomeUnknown: true);
            }
            status = reread.Status;
            purchaseUnits = reread.PurchaseUnits;
        }

        EnsureNoChallenge("AuthorizeOrder", status, payPalOrderId);
        return FindAuthorization(purchaseUnits);
    }

    // ---------------------------------------------------------------- Capture (take at fulfilment)

    public async Task<CaptureResult> CaptureAsync(CaptureInstruction instruction, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        var ct = cts.Token;

        var authorizationId = instruction.AuthorizationId;
        string? renewedId = null;
        DateTimeOffset? renewedExpiresAt = null;

        // Proactive: if the hold is already stale, renew it before trying to capture.
        var stale = instruction.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow;
        if (stale)
        {
            (authorizationId, renewedExpiresAt) = await Reauthorize(instruction, authorizationId, ct);
            renewedId = authorizationId;
        }

        try
        {
            return await DoCapture(instruction, authorizationId, renewedId, renewedExpiresAt, ct);
        }
        catch (PayPalIntegrationException ex) when (renewedId is null && !ex.OutcomeUnknown && IsRenewable(ex))
        {
            // Reactive: the hold was stale though we did not detect it up front — renew and re-capture.
            _logger.LogWarning(ex, "Capture rejected for order {OrderId}; attempting to renew the authorization.", instruction.OrderId);
            var (newId, newExpiresAt) = await Reauthorize(instruction, authorizationId, ct);
            return await DoCapture(instruction, newId, newId, newExpiresAt, ct);
        }
    }

    private async Task<(string authorizationId, DateTimeOffset? expiresAt)> Reauthorize(
        CaptureInstruction instruction, string authorizationId, CancellationToken ct)
    {
        var request = new ReauthorizePaymentRequest
        {
            AuthorizationId = authorizationId,
            PayPalRequestId = $"reauth-{instruction.InvoiceId}-{authorizationId}",
            Prefer = "return=representation",
            Body = new ReauthorizeRequest { Amount = Money(instruction.Amount) },
        };

        PaymentAuthorization result;
        try
        {
            result = await CallA<PaymentAuthorization, ReauthorizePaymentError>("ReauthorizePayment",
                t => _client.Payments.ReauthorizePayment(request, cancellationToken: t),
                e => e.TryGetError(out var err) ? err : null, ct);
        }
        catch (SdkException ex) when (IsTransport(ex))
        {
            throw new PayPalIntegrationException(
                "The authorization renewal could not be confirmed (provider unreachable).",
                null, null, ex, outcomeUnknown: true);
        }
        catch (PayPalIntegrationException ex)
        {
            // The hold can no longer be renewed (e.g. beyond the 29-day window) — operator-actionable.
            throw new PayPalIntegrationException(
                $"The authorization for order {instruction.OrderId} has expired and can no longer be renewed ({ex.Message}). Ask the shopper to pay for the order again.",
                ex.StatusCode, ex.DebugId, ex);
        }

        var newId = result.Id
            ?? throw new PayPalIntegrationException("PayPal did not return a renewed authorization id.");
        _logger.LogWarning("Renewed authorization for order {OrderId}: {OldAuthorization} -> {NewAuthorization}",
            instruction.OrderId, authorizationId, newId);
        return (newId, ParseTime(result.ExpirationTime));
    }

    private async Task<CaptureResult> DoCapture(CaptureInstruction instruction, string authorizationId,
        string? renewedId, DateTimeOffset? renewedExpiresAt, CancellationToken ct)
    {
        var request = new CaptureAuthorizedPaymentRequest
        {
            AuthorizationId = authorizationId,
            PayPalRequestId = renewedId is null
                ? $"capture-{instruction.InvoiceId}"
                : $"capture-{instruction.InvoiceId}-{authorizationId}",
            Prefer = "return=representation",
            Body = new CaptureRequest { FinalCapture = true },
        };

        try
        {
            var captured = await CallA<CapturedPayment, CaptureAuthorizedPaymentError>("CaptureAuthorizedPayment",
                t => _client.Payments.CaptureAuthorizedPayment(request, cancellationToken: t),
                e => e.TryGetError(out var err) ? err : null, ct);

            var captureId = captured.Id
                ?? throw new PayPalIntegrationException("PayPal did not return a capture id.");
            var breakdown = captured.SellerReceivableBreakdown;
            var gross = ParseAmount(breakdown?.GrossAmount) ?? ParseAmount(captured.Amount) ?? instruction.Amount;

            _logger.LogInformation(
                "Captured order {OrderId}: capture={CaptureId} status={Status} gross={Gross} fee={Fee} net={Net}",
                instruction.OrderId, captureId, captured.Status?.Value, gross,
                ParseAmount(breakdown?.PaypalFee), ParseAmount(breakdown?.NetAmount));

            return new CaptureResult(captureId, captured.Status?.Value ?? "COMPLETED", gross,
                ParseAmount(breakdown?.PaypalFee), ParseAmount(breakdown?.NetAmount), renewedId, renewedExpiresAt);
        }
        catch (SdkException ex) when (IsTransport(ex))
        {
            // Unknown outcome: re-read the order and look for a capture that already landed.
            _logger.LogWarning(ex, "Capture outcome unknown for order {OrderId}; re-reading order {PayPalOrderId}.", instruction.OrderId, instruction.PayPalOrderId);
            Order order;
            try
            {
                order = await ReadOrder(instruction.PayPalOrderId, ct);
            }
            catch (SdkException ex2) when (IsTransport(ex2))
            {
                throw new PayPalIntegrationException(
                    "The capture could not be confirmed (provider unreachable).",
                    null, null, ex2, outcomeUnknown: true);
            }

            var capture = FindCapture(order.PurchaseUnits);
            if (capture?.Id is { } id)
            {
                var breakdown = capture.SellerReceivableBreakdown;
                var gross = ParseAmount(breakdown?.GrossAmount) ?? ParseAmount(capture.Amount) ?? instruction.Amount;
                return new CaptureResult(id, capture.Status?.Value ?? "COMPLETED", gross,
                    ParseAmount(breakdown?.PaypalFee), ParseAmount(breakdown?.NetAmount), renewedId, renewedExpiresAt);
            }

            throw new PayPalIntegrationException(
                "The capture could not be confirmed; PayPal has no capture recorded for this order yet.",
                null, null, ex, outcomeUnknown: true);
        }
    }

    // ---------------------------------------------------------------- Void (cancel a hold)

    public async Task<string> VoidAsync(int orderId, string invoiceId, string authorizationId, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        var ct = cts.Token;

        var request = new VoidPaymentRequest
        {
            AuthorizationId = authorizationId,
            PayPalRequestId = $"void-{invoiceId}",
            Prefer = "return=representation",
        };

        try
        {
            var result = await _client.Payments.VoidPayment(request, cancellationToken: ct);
            var status = result.Status?.Value ?? "VOIDED";
            _logger.LogInformation("Voided authorization for order {OrderId}: authorization={AuthorizationId} status={Status}",
                orderId, authorizationId, status);
            return status;
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300)
        {
            // A successful void returns 204 No Content (no body to deserialize) — confirm via a read.
            _logger.LogInformation("Void of authorization {AuthorizationId} returned {Status} with no body; confirming via read.",
                authorizationId, (int)ex.StatusCode);
            return await ConfirmVoided(orderId, authorizationId, ex, ct);
        }
        catch (ApiException<VoidPaymentError> ex)
        {
            throw TranslateApiError("VoidPayment", ex.StatusCode, ex.Error.TryGetError(out var err) ? err : null, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw TranslateDeserialization("VoidPayment", ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "PayPal VoidPayment: credentials could not be applied.");
            throw new PayPalIntegrationException("PayPal VoidPayment: credentials could not be applied.", null, null, ex);
        }
        catch (SdkException ex) when (IsTransport(ex))
        {
            // Unknown outcome: re-read the authorization to see whether the void landed.
            _logger.LogWarning(ex, "Void outcome unknown for order {OrderId}; re-reading authorization {AuthorizationId}.", orderId, authorizationId);
            return await ConfirmVoided(orderId, authorizationId, ex, ct);
        }
    }

    private async Task<string> ConfirmVoided(int orderId, string authorizationId, Exception cause, CancellationToken ct)
    {
        PaymentAuthorization reread;
        try
        {
            reread = await CallA<PaymentAuthorization, GetAuthorizedPaymentError>("GetAuthorizedPayment",
                t => _client.Payments.GetAuthorizedPayment(new GetAuthorizedPaymentRequest { AuthorizationId = authorizationId }, cancellationToken: t),
                e => e.TryGetError(out var err) ? err : null, ct);
        }
        catch (SdkException ex) when (IsTransport(ex))
        {
            throw new PayPalIntegrationException(
                "The cancellation could not be confirmed (provider unreachable).",
                null, null, ex, outcomeUnknown: true);
        }

        if (reread.Status is { } status && status == AuthorizationStatus.Voided)
        {
            _logger.LogInformation("Confirmed authorization {AuthorizationId} for order {OrderId} is voided.", authorizationId, orderId);
            return reread.Status.Value;
        }

        throw new PayPalIntegrationException(
            "The cancellation could not be confirmed; the authorization is not voided at PayPal.",
            null, null, cause, outcomeUnknown: true);
    }

    // ---------------------------------------------------------------- Refund a captured payment

    public async Task<RefundResult> RefundAsync(RefundInstruction instruction, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        var ct = cts.Token;

        // Empty body = full refund; an amount = partial refund.
        var body = instruction.Amount is { } amount
            ? new RefundRequest
            {
                Amount = Money(amount),
                InvoiceId = instruction.InvoiceId,
                CustomId = instruction.OrderId.ToString(CultureInfo.InvariantCulture),
            }
            : null;

        var request = new RefundCapturedPaymentRequest
        {
            CaptureId = instruction.CaptureId,
            // Caller-supplied idempotency key, namespaced by the unique invoice id so the same key can
            // never collide across orders or runs; repeating it replays the same refund.
            PayPalRequestId = $"refund-{instruction.InvoiceId}-{instruction.IdempotencyKey}",
            Prefer = "return=representation",
            Body = body,
        };

        Refund refund;
        try
        {
            refund = await CallA<Refund, RefundCapturedPaymentError>("RefundCapturedPayment",
                t => _client.Payments.RefundCapturedPayment(request, cancellationToken: t),
                e => e.TryGetError(out var err) ? err : null, ct);
        }
        catch (SdkException ex) when (IsTransport(ex))
        {
            throw new PayPalIntegrationException(
                "The refund could not be confirmed (provider unreachable). Repeat with the same idempotency key to settle.",
                null, null, ex, outcomeUnknown: true);
        }

        var refundId = refund.Id
            ?? throw new PayPalIntegrationException("PayPal did not return a refund id.");
        var amountRefunded = ParseAmount(refund.Amount) ?? instruction.Amount ?? 0m;

        _logger.LogInformation("Refunded order {OrderId}: refund={RefundId} status={Status} amount={Amount}",
            instruction.OrderId, refundId, refund.Status?.Value, amountRefunded);

        return new RefundResult(refundId, refund.Status?.Value ?? "COMPLETED", amountRefunded);
    }

    // ---------------------------------------------------------------- Vault (save) a card

    public async Task<VaultResult> VaultCardAsync(VaultCardInstruction instruction, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        var ct = cts.Token;

        var request = new CreatePaymentTokenRequest
        {
            PayPalRequestId = $"vault-{instruction.CustomerId}-{CardFingerprint(instruction.Card)}",
            Body = new PaymentTokenRequest
            {
                Customer = new Customer { Id = instruction.CustomerId },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Number = instruction.Card.Number,
                        Expiry = instruction.Card.Expiry,
                        SecurityCode = instruction.Card.SecurityCode,
                        Name = instruction.Card.CardholderName,
                        BillingAddress = BuildAddress(instruction.Card.BillingAddress),
                    },
                },
            },
        };

        PaymentTokenResponse response;
        try
        {
            response = await CallA<PaymentTokenResponse, CreatePaymentTokenError>("CreatePaymentToken",
                t => _client.Vault.CreatePaymentToken(request, cancellationToken: t),
                e => e.TryGetError(out var err) ? err : null, ct);
        }
        catch (SdkException ex) when (IsTransport(ex))
        {
            throw new PayPalIntegrationException(
                "The card could not be saved (provider unreachable).",
                null, null, ex, outcomeUnknown: true);
        }

        var vaultId = response.Id
            ?? throw new PayPalIntegrationException("PayPal did not return a vault id for the saved card.");
        var card = response.PaymentSource?.Card;
        var brand = card?.Brand is { } b ? b.Value : null;

        _logger.LogInformation("Vaulted a card for customer {CustomerId}: vaultId={VaultId} brand={Brand} last4={Last4}",
            instruction.CustomerId, vaultId, brand, card?.LastDigits);

        return new VaultResult(vaultId, brand, card?.LastDigits, card?.Expiry, card?.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        var ct = cts.Token;

        var request = new DeletePaymentTokenRequest { Id = vaultId };
        try
        {
            await CallA<bool, DeletePaymentTokenError>("DeletePaymentToken",
                async t => { await _client.Vault.DeletePaymentToken(request, cancellationToken: t); return true; },
                e => e.TryGetError(out var err) ? err : null, ct);
        }
        catch (SdkException ex) when (IsTransport(ex))
        {
            throw new PayPalIntegrationException(
                "The saved card could not be deleted (provider unreachable).",
                null, null, ex, outcomeUnknown: true);
        }

        _logger.LogInformation("Deleted vaulted card {VaultId}.", vaultId);
    }

    // ---------------------------------------------------------------- Reconciliation

    public async Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        using var cts = Budget(cancellationToken);
        var ct = cts.Token;

        var transactions = new List<PayPalTransaction>();
        var pagesRead = 0;
        var totalPages = 0;
        var coveredAllPages = true;

        // PayPal's transaction search supports at most a 31-day range per call — chunk a wider range.
        var windowStart = from;
        while (windowStart < to && coveredAllPages)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            var windowTotalPages = 1;
            do
            {
                var request = new SearchTransactionsRequest
                {
                    StartDate = Rfc3339(windowStart),
                    EndDate = Rfc3339(windowEnd),
                    Fields = "transaction_info",
                    BalanceAffectingRecordsOnly = "N",
                    PageSize = PageSizeMax,
                    Page = page,
                };

                var response = await CallB<SearchResponse>("SearchTransactions",
                    t => _client.TransactionSearch.SearchTransactions(request, cancellationToken: t), ct);

                windowTotalPages = response.TotalPages ?? 1;
                if (page == 1)
                {
                    totalPages += windowTotalPages;
                }

                if (response.TransactionDetails is { } details)
                {
                    foreach (var detail in details)
                    {
                        transactions.Add(MapTransaction(detail));
                    }
                }

                pagesRead++;
                if (pagesRead >= MaxReconciliationPages)
                {
                    coveredAllPages = false;
                    _logger.LogWarning("Reconciliation hit the {Cap}-page safety cap; the report is partial.", MaxReconciliationPages);
                    break;
                }

                page++;
            }
            while (page <= windowTotalPages);

            windowStart = windowEnd;
        }

        _logger.LogInformation("Reconciliation read {PagesRead} page(s) across {TotalPages} total; {Count} transaction(s).",
            pagesRead, totalPages, transactions.Count);

        return new TransactionSearchResult(transactions, pagesRead, totalPages, coveredAllPages);
    }

    // ---------------------------------------------------------------- Error boundary

    /// <summary>Run a Case-A (typed error) call, translating every SDK failure except transport ones.</summary>
    private async Task<T> CallA<T, TError>(string operation, Func<CancellationToken, Task<T>> call,
        Func<TError, Error?> extractError, CancellationToken ct) where TError : ApiError
    {
        try
        {
            return await call(ct);
        }
        catch (ApiException<TError> ex)
        {
            throw TranslateApiError(operation, ex.StatusCode, extractError(ex.Error), ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw TranslateDeserialization(operation, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "PayPal {Operation}: credentials could not be applied.", operation);
            throw new PayPalIntegrationException($"PayPal {operation}: credentials could not be applied.", null, null, ex);
        }
        // SdkConnectionException / SdkTimeoutException are intentionally NOT caught here — the write
        // methods catch them to settle the outcome by re-reading provider state.
    }

    /// <summary>Run a Case-B (raw error) call, wrapping transport failures too (used by reads).</summary>
    private async Task<T> CallB<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        try
        {
            return await call(ct);
        }
        catch (ApiException<RawError> ex)
        {
            _logger.LogError(ex, "PayPal {Operation} failed: {Status}", operation, (int)ex.StatusCode);
            throw new PayPalIntegrationException($"PayPal {operation} failed with status {(int)ex.StatusCode}.", ex.StatusCode, null, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw TranslateDeserialization(operation, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "PayPal {Operation}: credentials could not be applied.", operation);
            throw new PayPalIntegrationException($"PayPal {operation}: credentials could not be applied.", null, null, ex);
        }
        catch (SdkException ex) when (IsTransport(ex))
        {
            _logger.LogError(ex, "PayPal {Operation}: provider unreachable.", operation);
            throw new PayPalIntegrationException($"PayPal {operation}: provider unreachable.", null, null, ex);
        }
    }

    private PayPalIntegrationException TranslateApiError(string operation, HttpStatusCode status, Error? error, Exception inner)
    {
        if (error is not null)
        {
            _logger.LogError(inner, "PayPal {Operation} failed: {Status} {Name} - {Message} (debug_id={DebugId})",
                operation, (int)status, error.Name, error.Message, error.DebugId);
            return new PayPalIntegrationException($"PayPal {operation} failed: {error.Name} - {error.Message}", status, error.DebugId, inner);
        }

        _logger.LogError(inner, "PayPal {Operation} failed: {Status}", operation, (int)status);
        return new PayPalIntegrationException($"PayPal {operation} failed with status {(int)status}.", status, null, inner);
    }

    private PayPalIntegrationException TranslateDeserialization(string operation, ResponseDeserializationException ex)
    {
        _logger.LogError(ex, "PayPal {Operation}: response could not be processed ({Status}, target {Target}).",
            operation, (int)ex.StatusCode, ex.TargetType);
        return new PayPalIntegrationException($"PayPal {operation} returned a response that could not be processed.", ex.StatusCode, null, ex);
    }

    private Task<Order> ReadOrder(string payPalOrderId, CancellationToken ct) =>
        CallA<Order, GetOrderError>("GetOrder",
            t => _client.Orders.GetOrder(new GetOrderRequest { Id = payPalOrderId }, cancellationToken: t),
            e => e.TryGetError(out var err) ? err : null, ct);

    // ---------------------------------------------------------------- Helpers

    private static CancellationTokenSource Budget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    private static bool IsTransport(SdkException ex) => ex is SdkConnectionException or SdkTimeoutException;

    private static bool IsRenewable(PayPalIntegrationException ex) =>
        ex.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.NotFound;

    private void EnsureNoChallenge(string operation, OrderStatus? status, string payPalOrderId)
    {
        if (status is { } s && s == OrderStatus.PayerActionRequired)
        {
            _logger.LogWarning("PayPal {Operation} for PayPal order {PayPalOrderId} requires payer action (browser challenge).",
                operation, payPalOrderId);
            throw new PayerActionRequiredException(
                "This card requires the shopper to approve the payment in a browser (e.g. 3-D Secure). " +
                "This integration does not support a browser approval step; use a card that authorizes without a challenge.");
        }
    }

    private CardRequest BuildCard(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = BuildAddress(card.BillingAddress),
    };

    private static Address? BuildAddress(BillingAddress? address) => address is null ? null : new Address
    {
        CountryCode = address.CountryCode,
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea1 = address.AdminArea1,
        AdminArea2 = address.AdminArea2,
        PostalCode = address.PostalCode,
    };

    private Money Money(decimal amount) => new()
    {
        CurrencyCode = _options.Currency,
        Value = FormatAmount(amount),
    };

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(Money? money) =>
        money is not null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateTimeOffset? ParseTime(string? value) =>
        !string.IsNullOrEmpty(value) &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string Rfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string? DescriptorFromCard(CardDetails? card)
    {
        if (card is null || card.Number.Length < 4)
        {
            return null;
        }
        return $"Card ****{card.Number[^4..]}";
    }

    /// <summary>A non-sensitive fingerprint (last 4 + expiry) so re-saving the same card is idempotent.</summary>
    private static string CardFingerprint(CardDetails card)
    {
        var last4 = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
        var material = $"{last4}|{card.Expiry}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private static AuthorizationWithAdditionalData? FindAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null)
        {
            return null;
        }
        foreach (var purchaseUnit in purchaseUnits)
        {
            var authorizations = purchaseUnit.Payments?.Authorizations;
            if (authorizations is null)
            {
                continue;
            }
            foreach (var authorization in authorizations)
            {
                if (!string.IsNullOrEmpty(authorization.Id))
                {
                    return authorization;
                }
            }
        }
        return null;
    }

    private static OrdersCapture? FindCapture(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null)
        {
            return null;
        }
        foreach (var purchaseUnit in purchaseUnits)
        {
            var captures = purchaseUnit.Payments?.Captures;
            if (captures is null)
            {
                continue;
            }
            foreach (var capture in captures)
            {
                if (!string.IsNullOrEmpty(capture.Id))
                {
                    return capture;
                }
            }
        }
        return null;
    }

    private static PayPalTransaction MapTransaction(TransactionDetails detail)
    {
        var info = detail.TransactionInfo;
        return new PayPalTransaction(
            info?.TransactionId,
            info?.InvoiceId,
            ParseAmount(info?.TransactionAmount),
            info?.TransactionAmount?.CurrencyCode,
            ParseAmount(info?.FeeAmount),
            info?.TransactionStatus,
            info?.TransactionInitiationDate,
            info?.TransactionEventCode);
    }
}
