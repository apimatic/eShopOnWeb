using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Orchestrates the pay-for-an-order flow across the repositories and the PayPal gateway. Owns the
/// idempotency, the duplicate-claim handling, the local-record-before-provider ordering, and the
/// status-driven state transitions.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private const int MaxReconciliationWindows = 60;   // ~5 years of 31-day windows — a safety backstop
    private const int MaxPagesPerWindow = 500;
    private const int SearchPageSize = 500;
    private static readonly TimeSpan WindowLength = TimeSpan.FromDays(31);
    private static readonly TimeSpan AuthorizationRenewMargin = TimeSpan.FromMinutes(5);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderPayment> _payments;
    private readonly IRepository<SavedCard> _savedCards;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderPayment> payments,
        IRepository<SavedCard> savedCards,
        IPayPalGateway gateway,
        IUriComposer uriComposer,
        IOptions<PayPalSettings> settings,
        ILogger<OrderPaymentService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _payments = payments;
        _savedCards = savedCards;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        Address? shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentValidationException("Every order line must have a positive quantity.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentValidationException($"Catalog item {line.CatalogItemId} does not exist.");
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, address, items);
        await _orders.AddAsync(order, ct);

        _logger.LogInformation("Order {OrderId} placed for {BuyerId}, total {Total}",
            order.Id, buyerId, order.Total());
        return order;
    }

    public async Task<OrderPayment> PayAsync(string buyerId, int orderId, PayCommand command, CancellationToken ct)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, ct);

        var existing = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (existing is not null)
        {
            switch (existing.Status)
            {
                case OrderPaymentStatus.Authorized:
                case OrderPaymentStatus.Fulfilled:
                case OrderPaymentStatus.PartiallyRefunded:
                case OrderPaymentStatus.Refunded:
                    return existing;   // already paid — idempotent, no PayPal call
                case OrderPaymentStatus.Cancelled:
                    throw new PaymentValidationException("This order was cancelled and cannot be paid.");
            }
        }

        var source = await ResolvePaymentSourceAsync(buyerId, command, ct);

        var amount = order.Total();
        if (amount <= 0m)
            throw new PaymentValidationException("The order total must be greater than zero to be paid.");
        var currency = _settings.Currency;

        // WRITE ORDER: the local claim exists before the PayPal call.
        var payment = existing;
        if (payment is null)
        {
            // Unique per order (a fresh guid) so PayPal's per-merchant invoice-id uniqueness holds even
            // across in-memory-reset runs where order ids repeat. Well under the 127-char limit.
            var invoiceId = $"ESHOP-{orderId}-{Guid.NewGuid():N}";
            payment = new OrderPayment(orderId, buyerId, amount, currency, invoiceId);
            try
            {
                await _payments.AddAsync(payment, ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent "pay" won the unique OrderId claim — treat this as the idempotent duplicate.
                var winner = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
                if (winner is not null) return winner;
                throw;
            }
        }

        var createRequestId = $"create-{payment.InvoiceId}";
        var authorizeRequestId = $"authorize-{payment.InvoiceId}";

        try
        {
            var createRequest = new CreatePayPalOrderRequest(amount, currency, payment.InvoiceId,
                orderId.ToString(CultureInfo.InvariantCulture), $"eShopOnWeb order {orderId}", source);
            var created = await _gateway.CreateOrderAsync(createRequest, createRequestId, ct);

            if (created.RequiresBuyerAction)
                throw new PaymentChallengeRequiredException(
                    "PayPal requires the shopper to approve this card payment in a browser. " +
                    "This integration does not support a browser approval round-trip.");

            payment.RecordPayPalOrder(created.Id);

            string authorizationId;
            string authorizationStatus;
            DateTimeOffset? expiresAt = null;
            DateTimeOffset? processedAt = null;

            if (!string.IsNullOrEmpty(created.AuthorizationId))
            {
                // Some direct-card flows create the authorization at order-creation time.
                authorizationId = created.AuthorizationId!;
                authorizationStatus = created.AuthorizationStatus ?? "CREATED";
            }
            else
            {
                var auth = await _gateway.AuthorizeOrderAsync(created.Id, authorizeRequestId, ct);
                authorizationId = auth.Id;
                authorizationStatus = auth.Status ?? "CREATED";
                expiresAt = auth.ExpiresAt;
                processedAt = auth.CreatedAt;
            }

            if (IsFailedAuthorization(authorizationStatus))
            {
                payment.MarkFailed($"Authorization {authorizationStatus}");
                await _payments.UpdateAsync(payment, ct);
                throw new PayPalGatewayException(
                    $"The card payment was not authorized (status {authorizationStatus}).");
            }

            payment.RecordAuthorization(created.Id, authorizationId, authorizationStatus,
                expiresAt, processedAt ?? DateTimeOffset.UtcNow);
            await _payments.UpdateAsync(payment, ct);

            _logger.LogInformation("Order {OrderId} authorized: paypalOrder={PayPalOrderId} auth={AuthorizationId} status={Status}",
                orderId, created.Id, authorizationId, authorizationStatus);
            return payment;
        }
        catch (PaymentChallengeRequiredException)
        {
            payment.MarkFailed("Browser approval challenge required");
            await SafeUpdateAsync(payment, ct);
            throw;
        }
        catch (PayPalGatewayException ex) when (ex.OutcomeUnknown)
        {
            // The send failed after the request may have landed. Re-read rather than assume failure.
            if (!string.IsNullOrEmpty(payment.PayPalOrderId))
            {
                var recovered = await TryRecoverAuthorizationAsync(payment, ct);
                if (recovered is not null) return recovered;
            }
            _logger.LogWarning(ex, "Authorization outcome unknown for order {OrderId}; left as Authorizing for safe retry", orderId);
            throw;
        }
    }

    private async Task<OrderPayment?> TryRecoverAuthorizationAsync(OrderPayment payment, CancellationToken ct)
    {
        try
        {
            var reread = await _gateway.GetOrderAsync(payment.PayPalOrderId!, ct);
            if (!string.IsNullOrEmpty(reread.AuthorizationId) && !IsFailedAuthorization(reread.AuthorizationStatus))
            {
                payment.RecordAuthorization(payment.PayPalOrderId!, reread.AuthorizationId!,
                    reread.AuthorizationStatus ?? "CREATED", null, DateTimeOffset.UtcNow);
                await _payments.UpdateAsync(payment, ct);
                _logger.LogInformation("Recovered authorization for order {OrderId} by re-reading the PayPal order", payment.OrderId);
                return payment;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not re-read PayPal order {PayPalOrderId} to recover authorization", payment.PayPalOrderId);
        }
        return null;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentForOperatorAsync(orderId, ct);

        if (payment.Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
            return payment;   // already captured — idempotent, no capture

        if (payment.Status != OrderPaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentValidationException(
                $"The order cannot be fulfilled from its current state ({payment.Status}). It must be authorized first.");

        var authorizationId = payment.AuthorizationId;

        // Renew a hold that has already gone stale before attempting the capture.
        if (payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow + AuthorizationRenewMargin)
        {
            authorizationId = await RenewAuthorizationAsync(payment, ct);
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAuthorizationAsync(authorizationId, $"capture-{payment.InvoiceId}", ct);
        }
        catch (PayPalGatewayException ex) when (IsExpiredAuthorization(ex))
        {
            // The hold went stale between the check and the capture — renew and capture once more.
            _logger.LogInformation("Authorization {AuthorizationId} was stale; re-authorizing before capture", authorizationId);
            authorizationId = await RenewAuthorizationAsync(payment, ct);
            capture = await _gateway.CaptureAuthorizationAsync(authorizationId, $"capture-{payment.InvoiceId}-r", ct);
        }
        catch (PayPalGatewayException ex) when (ex.OutcomeUnknown)
        {
            var recovered = await TryRecoverCaptureAsync(payment, ct);
            if (recovered is not null) return recovered;
            _logger.LogWarning(ex, "Capture outcome unknown for order {OrderId}; retry fulfil to reconcile", orderId);
            throw;
        }

        if (IsFailedCapture(capture.Status))
        {
            payment.MarkFailed($"Capture {capture.Status}");
            await _payments.UpdateAsync(payment, ct);
            throw new PayPalGatewayException($"The payment capture failed (status {capture.Status}).");
        }

        payment.RecordCapture(capture.Id, capture.Status ?? "COMPLETED", capture.GrossAmount ?? payment.Amount,
            capture.PaypalFee, capture.NetAmount, capture.CreatedAt ?? DateTimeOffset.UtcNow);
        await _payments.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {OrderId} fulfilled: capture={CaptureId} gross={Gross} fee={Fee} net={Net}",
            orderId, capture.Id, capture.GrossAmount, capture.PaypalFee, capture.NetAmount);
        return payment;
    }

    private async Task<string> RenewAuthorizationAsync(OrderPayment payment, CancellationToken ct)
    {
        try
        {
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, $"reauth-{payment.InvoiceId}", ct);
            payment.RecordReauthorization(reauth.Id, reauth.Status ?? "CREATED", reauth.ExpiresAt);
            await _payments.UpdateAsync(payment, ct);
            return reauth.Id;
        }
        catch (PayPalGatewayException ex)
        {
            throw new PaymentValidationException(
                "This authorization has expired and can no longer be renewed, so the order cannot be fulfilled. " +
                "Ask the shopper to pay again to place a fresh hold. " +
                (ex.ProviderIssue is not null ? $"(PayPal: {ex.ProviderIssue})" : string.Empty));
        }
    }

    private async Task<OrderPayment?> TryRecoverCaptureAsync(OrderPayment payment, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(payment.PayPalOrderId)) return null;
        try
        {
            var reread = await _gateway.GetOrderAsync(payment.PayPalOrderId!, ct);
            if (!string.IsNullOrEmpty(reread.CaptureId) && !IsFailedCapture(reread.CaptureStatus))
            {
                payment.RecordCapture(reread.CaptureId!, reread.CaptureStatus ?? "COMPLETED", payment.Amount,
                    null, null, DateTimeOffset.UtcNow);
                await _payments.UpdateAsync(payment, ct);
                _logger.LogInformation("Recovered capture for order {OrderId} by re-reading the PayPal order", payment.OrderId);
                return payment;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not re-read PayPal order {PayPalOrderId} to recover capture", payment.PayPalOrderId);
        }
        return null;
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentForOperatorAsync(orderId, ct);

        if (payment.Status == OrderPaymentStatus.Cancelled)
            return payment;   // idempotent

        if (payment.Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
            throw new PaymentValidationException(
                "The order has already been fulfilled and cannot be cancelled; refund it instead.");

        if (payment.Status != OrderPaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentValidationException(
                $"The order cannot be cancelled from its current state ({payment.Status}).");

        await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, ct);
        payment.RecordCancellation();
        await _payments.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {OrderId} cancelled; authorization {AuthorizationId} voided",
            orderId, payment.AuthorizationId);
        return payment;
    }

    public async Task<OrderRefund> RefundAsync(string buyerId, int orderId, string idempotencyKey,
        decimal? amount, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        await LoadOwnedOrderAsync(buyerId, orderId, ct);

        var payment = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct)
            ?? throw new PaymentValidationException("This order has no captured payment to refund.");

        var already = payment.FindRefund(idempotencyKey);
        if (already is not null)
            return already;   // repeated request under the same key — idempotent, no refund

        // AddRefund enforces the fulfilled state and the "never beyond captured" cap.
        var refund = payment.AddRefund(idempotencyKey, amount);

        // WRITE ORDER: the pending refund row exists before the PayPal call.
        try
        {
            await _payments.UpdateAsync(payment, ct);
        }
        catch (DbUpdateException)
        {
            var reloaded = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
            var winner = reloaded?.FindRefund(idempotencyKey);
            if (winner is not null) return winner;
            throw;
        }

        try
        {
            var result = await _gateway.RefundCaptureAsync(payment.CaptureId!, amount, payment.Currency,
                idempotencyKey, payment.InvoiceId, ct);

            refund.RecordResult(result.Id, result.Status ?? "PENDING");
            payment.RefreshRefundState();
            await _payments.UpdateAsync(payment, ct);

            if (IsFailedRefund(result.Status))
                throw new PayPalGatewayException($"The refund did not complete (status {result.Status}).");

            _logger.LogInformation("Order {OrderId} refunded {Amount} {Currency}: refund={RefundId} status={Status}",
                orderId, refund.Amount, payment.Currency, result.Id, result.Status);
            return refund;
        }
        catch (PayPalGatewayException ex) when (ex.OutcomeUnknown)
        {
            _logger.LogWarning(ex, "Refund outcome unknown for order {OrderId}; left pending for safe retry under the same key", orderId);
            throw;
        }
        catch (PayPalGatewayException)
        {
            refund.RecordResult(refund.PayPalRefundId ?? "n/a", "FAILED");
            payment.RefreshRefundState();
            await SafeUpdateAsync(payment, ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _payments.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), ct);
        var byOrder = payments.ToDictionary(p => p.OrderId);
        return orders
            .Select(o => new OrderWithPayment(o, byOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    public async Task<OrderWithPayment?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId) return null;
        var payment = await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        return new OrderWithPayment(order, payment);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            throw new PaymentValidationException("The 'to' date must not be earlier than the 'from' date.");

        var payPalTransactions = new List<PayPalTransaction>();
        var truncated = false;
        var windows = 0;

        var windowStart = from;
        while (windowStart < to)
        {
            if (++windows > MaxReconciliationWindows) { truncated = true; break; }

            var windowEnd = windowStart + WindowLength;
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                var result = await _gateway.SearchTransactionsAsync(
                    FormatRfc3339(windowStart), FormatRfc3339(windowEnd), page, SearchPageSize, ct);
                payPalTransactions.AddRange(result.Transactions);
                totalPages = Math.Max(result.TotalPages, 1);

                if (page >= MaxPagesPerWindow && page < totalPages) { truncated = true; break; }
                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        var eShopPayments = await _payments.ListAsync(new OrderPaymentsByProcessedRangeSpecification(from, to), ct);
        return BuildReport(from, to, payPalTransactions, eShopPayments, truncated);
    }

    private static ReconciliationReport BuildReport(DateTimeOffset from, DateTimeOffset to,
        IReadOnlyList<PayPalTransaction> payPalTransactions, IReadOnlyList<OrderPayment> eShopPayments, bool truncated)
    {
        var entries = new List<ReconciliationEntry>();

        // eShop payments keyed by invoice id (the reconciliation match key).
        var eShopByInvoice = eShopPayments
            .Where(p => !string.IsNullOrEmpty(p.InvoiceId))
            .GroupBy(p => p.InvoiceId!)
            .ToDictionary(g => g.Key, g => g.First());

        // PayPal transactions keyed by invoice id.
        var payPalByInvoice = payPalTransactions
            .Where(t => !string.IsNullOrEmpty(t.InvoiceId))
            .GroupBy(t => t.InvoiceId!)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matched = 0;
        var onlyInPayPal = 0;
        var onlyInEShop = 0;

        foreach (var tx in payPalTransactions)
        {
            var invoice = tx.InvoiceId;
            if (!string.IsNullOrEmpty(invoice) && eShopByInvoice.TryGetValue(invoice!, out var payment))
            {
                matched++;
                entries.Add(new ReconciliationEntry(invoice, "matched", payment.OrderId,
                    payment.CapturedAmount ?? payment.Amount, payment.Status.ToString(),
                    tx.TransactionId, tx.Amount, tx.Status));
            }
            else
            {
                onlyInPayPal++;
                entries.Add(new ReconciliationEntry(invoice, "only-in-paypal", null, null, null,
                    tx.TransactionId, tx.Amount, tx.Status));
            }
        }

        foreach (var payment in eShopPayments)
        {
            if (string.IsNullOrEmpty(payment.InvoiceId) || !payPalByInvoice.ContainsKey(payment.InvoiceId!))
            {
                onlyInEShop++;
                entries.Add(new ReconciliationEntry(payment.InvoiceId, "only-in-eshop", payment.OrderId,
                    payment.CapturedAmount ?? payment.Amount, payment.Status.ToString(), null, null, null));
            }
        }

        return new ReconciliationReport(from, to, entries, matched, onlyInPayPal, onlyInEShop,
            payPalTransactions.Count, truncated);
    }

    // ----- helpers -----

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
            throw new OrderNotFoundException(orderId);
        return order;
    }

    private async Task<OrderPayment> LoadPaymentForOperatorAsync(int orderId, CancellationToken ct)
    {
        var order = await _orders.GetByIdAsync(orderId, ct)
            ?? throw new OrderNotFoundException(orderId);
        return await _payments.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct)
            ?? throw new PaymentValidationException(
                $"Order {order.Id} has not been paid, so there is no payment to act on.");
    }

    private async Task<PaymentSourceInput> ResolvePaymentSourceAsync(string buyerId, PayCommand command,
        CancellationToken ct)
    {
        if (command.SavedCardId is { } savedCardId)
        {
            var card = await _savedCards.GetByIdAsync(savedCardId, ct);
            if (card is null || card.BuyerId != buyerId)
                throw new PaymentValidationException("The specified saved card was not found for the current user.");
            return new PaymentSourceInput(null, card.PayPalVaultId);
        }

        if (command.Card is not null)
            return new PaymentSourceInput(command.Card, null);

        throw new PaymentValidationException("Provide either card details or the id of a saved card to pay with.");
    }

    private async Task SafeUpdateAsync(OrderPayment payment, CancellationToken ct)
    {
        try { await _payments.UpdateAsync(payment, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not persist payment state for order {OrderId}", payment.OrderId); }
    }

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static bool IsFailedAuthorization(string? status) =>
        status is not null && (status.Equals("DENIED", StringComparison.OrdinalIgnoreCase)
            || status.Equals("VOIDED", StringComparison.OrdinalIgnoreCase));

    private static bool IsFailedCapture(string? status) =>
        status is not null && (status.Equals("DECLINED", StringComparison.OrdinalIgnoreCase)
            || status.Equals("FAILED", StringComparison.OrdinalIgnoreCase));

    private static bool IsFailedRefund(string? status) =>
        status is not null && (status.Equals("FAILED", StringComparison.OrdinalIgnoreCase)
            || status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase));

    private static bool IsExpiredAuthorization(PayPalGatewayException ex) =>
        (ex.ProviderIssue is not null && ex.ProviderIssue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase))
        || (ex.ProviderIssue is not null && ex.ProviderIssue.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase)
            && ex.ProviderIssue.Contains("VOID", StringComparison.OrdinalIgnoreCase));
}
