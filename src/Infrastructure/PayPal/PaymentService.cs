using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Orchestrates the order-payment flow over the domain entities and <see cref="IPayPalGateway"/>.
/// State transitions are guarded so a repeat request never authorizes, captures, or refunds twice; the
/// local record is always written before the provider call, and the caller-supplied / stable idempotency
/// keys make every money POST safe to replay.
/// </summary>
public sealed class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orders;
    private readonly IReadRepository<CatalogItem> _catalog;
    private readonly IRepository<OrderPayment> _payments;
    private readonly IRepository<SavedCard> _savedCards;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orders,
        IReadRepository<CatalogItem> catalog,
        IRepository<OrderPayment> payments,
        IRepository<SavedCard> savedCards,
        IPayPalGateway gateway,
        IUriComposer uriComposer,
        IOptions<PayPalSettings> settings,
        ILogger<PaymentService> logger)
    {
        _orders = orders;
        _catalog = catalog;
        _payments = payments;
        _savedCards = savedCards;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _settings = settings.Value;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    // ── Place order ────────────────────────────────────────────────────────────
    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, AddressInput? shipTo,
        CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new PaymentValidationException($"Quantity for catalog item {line.CatalogItemId} must be positive.");

            var catalogItem = await _catalog.GetByIdAsync(line.CatalogItemId, ct);
            if (catalogItem is null)
                throw new PaymentValidationException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, items);
        order = await _orders.AddAsync(order, ct);

        // The payment record exists (AwaitingPayment) before any PayPal call, carrying the invoice id and
        // the stable idempotency keys the later authorize/capture use.
        var payment = new OrderPayment(order.Id, buyerId, Currency, order.Total());
        await _payments.AddAsync(payment, ct);

        _logger.LogInformation("Placed order {OrderId} for buyer, total {Amount} {Currency}.",
            order.Id, payment.Amount, Currency);
        return order.Id;
    }

    // ── Authorize (hold) ─────────────────────────────────────────────────────────
    public async Task<PaymentView> AuthorizeAsync(string buyerId, int orderId, PayInput pay, CancellationToken ct)
    {
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

        if (payment.Status == PaymentStatus.Authorized)
            return View(payment);                             // idempotent: hold already placed
        if (payment.Status != PaymentStatus.AwaitingPayment)
            throw new PaymentConflictException($"Order {orderId} is {payment.Status} and cannot be authorized.");

        var (card, vaultId) = await ResolveFundingAsync(buyerId, pay, ct);

        var request = new AuthorizeGatewayRequest(
            Amount: payment.Amount,
            Currency: payment.Currency,
            OrderReference: orderId.ToString(),
            InvoiceId: payment.InvoiceId,
            RequestId: payment.AuthorizeRequestId,           // stable → replay-safe at PayPal
            Card: card,
            VaultId: vaultId);

        // Double-click safety is the state guard above (already-Authorized returns without a second call).
        // On a transport failure the payment stays AwaitingPayment and reconciliation surfaces any orphan hold.
        var result = await _gateway.AuthorizeAsync(request, ct);

        switch (result.Outcome)
        {
            case AuthorizationOutcome.ChallengeRequired:
                throw new PaymentChallengeRequiredException(
                    "This card requires shopper approval in a browser (3-D Secure). This integration does not " +
                    "perform an approval round-trip; use a card that authorizes without a challenge.");
            case AuthorizationOutcome.Failed:
                throw new PaymentValidationException($"The card payment was not authorized ({result.FailureReason}).");
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId!, result.AuthorizationStatus,
            result.ExpiresAt, result.CreatedAt);
        return await SaveTransitionAsync(payment, orderId, ct);
    }

    // ── Fulfil (capture) ──────────────────────────────────────────────────────────
    public async Task<PaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            return View(payment);                             // idempotent: already fulfilled
        if (payment.Status != PaymentStatus.Authorized)
            throw new PaymentConflictException($"Order {orderId} is {payment.Status} and cannot be fulfilled.");

        var authorizationId = payment.AuthorizationId
            ?? throw new PaymentConflictException($"Order {orderId} has no authorization to capture.");

        // Renew a stale hold proactively rather than failing the fulfilment.
        if (payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow.AddMinutes(1))
            authorizationId = await RenewAuthorizationAsync(payment, authorizationId, ct);

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, payment.CaptureRequestId, ct);
        }
        catch (PaymentValidationException)
        {
            // The capture was rejected — most often a hold that expired since we checked. Renew once and retry.
            authorizationId = await RenewAuthorizationAsync(payment, authorizationId, ct);
            capture = await _gateway.CaptureAsync(authorizationId, payment.CaptureRequestId, ct);
        }

        if (capture.Outcome == CaptureOutcome.Failed)
            throw new PaymentValidationException($"Capture failed (status {capture.RawStatus ?? "unknown"}).");

        payment.MarkCaptured(capture.CaptureId, capture.RawStatus, capture.Gross, capture.Fee, capture.Net,
            capture.CreatedAt);
        _logger.LogInformation("Fulfilled order {OrderId}: captured {Gross}, fee {Fee}, net {Net} {Currency}.",
            orderId, capture.Gross, capture.Fee, capture.Net, capture.Currency);
        return await SaveTransitionAsync(payment, orderId, ct);
    }

    // ── Cancel (void) ───────────────────────────────────────────────────────────────
    public async Task<PaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
            return View(payment);                             // idempotent
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            throw new PaymentConflictException($"Order {orderId} is already fulfilled; use a refund, not a cancel.");

        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is { } authId)
            await _gateway.VoidAsync(authId, ct);             // release the held funds

        payment.MarkCancelled();
        _logger.LogInformation("Cancelled order {OrderId}; any held funds released.", orderId);
        return await SaveTransitionAsync(payment, orderId, ct);
    }

    // ── Refund ────────────────────────────────────────────────────────────────────────
    public async Task<RefundView> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentValidationException("A refund idempotency key is required.");

        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            throw new PaymentConflictException($"Order {orderId} is {payment.Status}; only a captured payment can be refunded.");

        // Same key already used → return its recorded outcome (idempotent, never refunds twice).
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return ToRefundView(existing);

        var refundAmount = amount ?? payment.RefundableRemaining;
        if (refundAmount <= 0)
            throw new PaymentValidationException("There is nothing left to refund on this order.");
        if (refundAmount > payment.RefundableRemaining)
            throw new PaymentValidationException(
                $"Refund of {refundAmount} exceeds the refundable remaining {payment.RefundableRemaining} {payment.Currency}.");

        var captureId = payment.CaptureId
            ?? throw new PaymentConflictException($"Order {orderId} has no capture to refund.");

        // Claim first: insert the refund row (unique on OrderPaymentId+IdempotencyKey) BEFORE the PayPal call,
        // carrying the caller's key.
        var refund = payment.StartRefund(idempotencyKey, refundAmount);
        try
        {
            await _payments.UpdateAsync(payment, ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent request won the unique claim; reload and return its result.
            var reloaded = await LoadOwnedPaymentAsync(orderId, buyerId, ct);
            var won = reloaded.FindRefundByKey(idempotencyKey);
            if (won is not null) return ToRefundView(won);
            throw;
        }

        // PayPal-Request-Id namespaced by the capture so a simple caller key cannot collide with the same
        // literal used on a different capture/run; the local unique claim above is what enforces "no double refund".
        var refundRequestId = BuildRefundRequestId(captureId, idempotencyKey);

        RefundResult result;
        try
        {
            // Full refund → omit amount (provider default); partial → pass the amount.
            result = await _gateway.RefundAsync(captureId, amount, payment.Currency, refundRequestId, ct);
        }
        catch (PaymentValidationException)
        {
            // Definite provider rejection (e.g. declined): record the claim as failed so a same-key retry
            // returns the same failure, then surface it. (A PaymentGatewayException — transport/unknown — is
            // left unrecorded on purpose so GET /api/reconciliation re-reads PayPal to settle it.)
            refund.RecordResult(string.Empty, "FAILED", isEffective: false);
            await _payments.UpdateAsync(payment, ct);
            throw;
        }

        var isEffective = result.Outcome != RefundOutcome.Failed;
        refund.RecordResult(result.RefundId, result.RawStatus ?? result.Outcome.ToString(), isEffective);
        if (isEffective)
            payment.ApplyEffectiveRefund(refundAmount);       // counts pending + completed → prevents over-refund

        await _payments.UpdateAsync(payment, ct);
        _logger.LogInformation("Refunded {Amount} {Currency} on order {OrderId} (status {Status}).",
            refundAmount, payment.Currency, orderId, result.RawStatus);
        return new RefundView(result.RefundId, result.RawStatus ?? result.Outcome.ToString(), refundAmount, payment.Currency);
    }

    // ── My orders ─────────────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<PaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var payments = await _payments.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), ct);
        return payments.Select(View).ToList();
    }

    // ── Reconciliation ──────────────────────────────────────────────────────────────────
    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var fetch = await _gateway.SearchTransactionsAsync(from, to, ct);
        // Filter the eShop side on the SAME clock as PayPal — the provider event time, not row-creation time.
        var localPayments = await _payments.ListAsync(new OrderPaymentsByPayPalTimeSpec(from, to), ct);

        // PayPal's transaction reporting lower-cases invoice_id, so line the two sides up case-insensitively.
        var payPalByInvoice = fetch.Transactions
            .Where(t => !string.IsNullOrEmpty(t.InvoiceId))
            .GroupBy(t => t.InvoiceId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var localInvoices = localPayments.Select(p => p.InvoiceId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciledLine>();
        var onlyInEShop = new List<EShopOnlyLine>();
        foreach (var p in localPayments)
        {
            if (payPalByInvoice.TryGetValue(p.InvoiceId, out var tx))
                matched.Add(new ReconciledLine(p.OrderId, p.InvoiceId, p.Status.ToString(), p.Amount,
                    tx.TransactionId, tx.Amount, tx.Status));
            else
                onlyInEShop.Add(new EShopOnlyLine(p.OrderId, p.InvoiceId, p.Status.ToString(), p.Amount));
        }

        var onlyInPayPal = fetch.Transactions
            .Where(t => string.IsNullOrEmpty(t.InvoiceId) || !localInvoices.Contains(t.InvoiceId!))
            .Select(t => new PayPalOnlyLine(t.TransactionId, t.InvoiceId, t.Amount, t.Currency, t.Status))
            .ToList();

        return new ReconciliationReport(from, to, matched, onlyInPayPal, onlyInEShop,
            fetch.PagesFetched, fetch.TotalPages, fetch.Truncated);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────
    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct);
        return payment ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");
    }

    private async Task<OrderPayment> LoadOwnedPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentNotFoundException($"Order {orderId} was not found."); // do not reveal another shopper's order
        return payment;
    }

    private async Task<(CardDetails? card, string? vaultId)> ResolveFundingAsync(string buyerId, PayInput pay,
        CancellationToken ct)
    {
        if (pay.Card is not null && pay.SavedPaymentMethodId is not null)
            throw new PaymentValidationException("Provide either card details or a saved payment method, not both.");

        if (pay.Card is not null)
            return (pay.Card, null);

        if (pay.SavedPaymentMethodId is int id)
        {
            var saved = await _savedCards.FirstOrDefaultAsync(new SavedCardByIdForBuyerSpec(id, buyerId), ct);
            if (saved is null)
                throw new PaymentNotFoundException($"Saved card {id} was not found.");
            return (null, saved.PayPalVaultTokenId);
        }

        throw new PaymentValidationException("Provide card details or a saved payment method to pay with.");
    }

    private async Task<string> RenewAuthorizationAsync(OrderPayment payment, string authorizationId, CancellationToken ct)
    {
        try
        {
            var info = await _gateway.ReauthorizeAsync(authorizationId, payment.CaptureRequestId + "-ra", ct);
            payment.UpdateAuthorization(info.Id, info.Status, info.ExpiresAt);
            await _payments.UpdateAsync(payment, ct);
            _logger.LogInformation("Renewed authorization on order {OrderId}.", payment.OrderId);
            return info.Id;
        }
        catch (PaymentException ex)
        {
            throw new AuthorizationExpiredException(
                "The payment hold has expired and could not be renewed. Ask the shopper to pay for this order " +
                "again before fulfilling it.", ex);
        }
    }

    /// <summary>Persists a state transition; if a concurrent transition won, reload and return the settled state.</summary>
    private async Task<PaymentView> SaveTransitionAsync(OrderPayment payment, int orderId, CancellationToken ct)
    {
        try
        {
            await _payments.UpdateAsync(payment, ct);
            return View(payment);
        }
        catch (DbUpdateConcurrencyException)
        {
            var reloaded = await LoadPaymentAsync(orderId, ct);
            return View(reloaded);
        }
    }

    private static RefundView ToRefundView(PaymentRefund r) =>
        new(r.PayPalRefundId ?? "", r.Status ?? "PENDING", r.Amount, r.Currency);

    /// <summary>
    /// A stable, collision-proof PayPal-Request-Id for a refund: the same (capture, caller-key) always maps
    /// to the same value, but a caller-key reused on a different capture maps to a different value.
    /// </summary>
    private static string BuildRefundRequestId(string captureId, string key)
    {
        var raw = $"{captureId}:{key}";
        if (raw.Length <= 100)
            return raw;
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return "rf-" + System.Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static PaymentView View(OrderPayment p) => new(
        p.OrderId,
        p.Status.ToString(),
        p.Currency,
        p.Amount,
        p.InvoiceId,
        p.PayPalOrderId,
        p.AuthorizationId,
        p.AuthorizationStatus,
        p.AuthorizationExpiresAt,
        p.CaptureId,
        p.CaptureStatus,
        p.CapturedGross,
        p.PaypalFee,
        p.NetAmount,
        p.RefundedAmount,
        p.Refunds.Select(r => new RefundSummary(r.PayPalRefundId, r.Status, r.Amount)).ToList());
}
