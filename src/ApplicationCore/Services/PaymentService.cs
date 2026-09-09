using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the additive payment capability over the existing order model and the
/// <see cref="IPaymentGateway"/> abstraction. Holds no SDK types. Enforces ownership, the
/// authorize/capture/void/refund state machine, effect-idempotency, and the refund invariants.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IUriComposer uriComposer,
        IPaymentGateway gateway,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _logger = logger;
    }

    private string Currency => _gateway.CurrencyCode;

    // ---------------------------------------------------------------- place

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, ShippingAddressInput? shipTo, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new PaymentConflictException("An order must contain at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentConflictException("Every item quantity must be greater than zero.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentResourceNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation("Order {0} placed by buyer, total {1} {2}, awaiting payment.", order.Id, order.Total(), Currency);
        return order.Id;
    }

    // ---------------------------------------------------------------- pay (authorize)

    public async Task<OrderPaymentView> PayAsync(string buyerId, int orderId, PayCommand command, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);
        var total = order.Total();
        if (total <= 0m)
        {
            throw new PaymentConflictException("The order total must be greater than zero to take a payment.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);

        // Effect-idempotency: an order already authorized (or beyond) is not authorized again.
        if (payment is not null && payment.Status != PaymentStatus.PendingPayment)
        {
            _logger.LogInformation("Pay called on order {0} already in state {1}; returning existing payment.", orderId, payment.Status);
            return ToView(order, payment);
        }

        if (payment is null)
        {
            var invoiceId = $"ESHOP-{orderId}-{Guid.NewGuid():N}";
            payment = new OrderPayment(orderId, buyerId, total, Currency, invoiceId);
            payment = await _paymentRepository.AddAsync(payment, ct);
        }

        var (card, vaultId) = await ResolveFundingAsync(buyerId, command, ct);

        var request = new GatewayAuthorizeRequest(
            Amount: total,
            CurrencyCode: Currency,
            InvoiceId: payment.InvoiceId,
            CustomId: orderId.ToString(),
            Description: $"eShopOnWeb order {orderId}",
            IdempotencyKey: payment.AuthorizeIdempotencyKey,
            Card: card,
            VaultId: vaultId);

        try
        {
            var auth = await _gateway.AuthorizeAsync(request, ct);
            payment.MarkAuthorized(auth.PayPalOrderId, auth.AuthorizationId, auth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Order {0} authorized: paypalOrder {1}, authorization {2}, expires {3}.",
                orderId, auth.PayPalOrderId, auth.AuthorizationId, auth.ExpiresAt);
            return ToView(order, payment);
        }
        catch (Exception)
        {
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }
    }

    private async Task<(GatewayCard? card, string? vaultId)> ResolveFundingAsync(string buyerId, PayCommand command, CancellationToken ct)
    {
        var hasCard = command.Card is not null;
        var hasSaved = command.SavedPaymentMethodId is not null;

        if (hasCard == hasSaved)
        {
            throw new PaymentConflictException("Provide exactly one funding source: either card details or a saved card id.");
        }

        if (hasCard)
        {
            return (command.Card, null);
        }

        var saved = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(command.SavedPaymentMethodId!.Value, buyerId), ct);
        if (saved is null)
        {
            throw new PaymentResourceNotFoundException($"Saved card {command.SavedPaymentMethodId} was not found for this shopper.");
        }
        return (null, saved.PayPalVaultId);
    }

    // ---------------------------------------------------------------- fulfil (capture)

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct)
            ?? throw new PaymentResourceNotFoundException($"No payment found for order {orderId}.");

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            _logger.LogInformation("Fulfil called on order {0} already captured; returning existing payment.", orderId);
            return await ToViewAsync(payment, ct);
        }

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentConflictException(
                $"Order {orderId} is in state {payment.Status} and cannot be fulfilled. It must be authorized first.");
        }

        await EnsureFreshAuthorizationAsync(payment, ct);

        GatewayCapture capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, payment.CaptureIdempotencyKey, ct);
        }
        catch (PaymentGatewayException ex)
        {
            // The hold may have gone stale between our check and the capture. Try one renewal + retry.
            _logger.LogWarning("Capture of order {0} failed ({1}); attempting re-authorization then one retry.", orderId, ex.Message);
            await RenewAuthorizationOrFailAsync(payment, ct);
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, payment.CaptureIdempotencyKey, ct);
        }

        payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.Fee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Order {0} fulfilled: capture {1}, gross {2}, fee {3}, net {4}.",
            orderId, capture.CaptureId, capture.GrossAmount, capture.Fee, capture.NetAmount);
        return await ToViewAsync(payment, ct);
    }

    /// <summary>Proactively renew a hold that is past its expiry before attempting capture.</summary>
    private async Task EnsureFreshAuthorizationAsync(OrderPayment payment, CancellationToken ct)
    {
        if (!payment.IsAuthorizationStale(DateTimeOffset.UtcNow))
        {
            return;
        }
        _logger.LogWarning("Authorization {0} for order {1} is stale (expired {2}); renewing before capture.",
            payment.AuthorizationId!, payment.OrderId, payment.AuthorizationExpiresAt);
        await RenewAuthorizationOrFailAsync(payment, ct);
    }

    private async Task RenewAuthorizationOrFailAsync(OrderPayment payment, CancellationToken ct)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(
                payment.AuthorizationId!, payment.Amount, payment.CurrencyCode,
                $"reauth-{payment.InvoiceId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}", ct);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Order {0} authorization renewed: {1}, expires {2}.",
                payment.OrderId, renewed.AuthorizationId, renewed.ExpiresAt);
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentConflictException(
                $"The authorization for order {payment.OrderId} has expired and can no longer be renewed " +
                $"({ex.Message}). Ask the shopper to pay for the order again.");
        }
    }

    // ---------------------------------------------------------------- cancel (void)

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct)
            ?? throw new PaymentResourceNotFoundException($"No payment found for order {orderId}.");

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return await ToViewAsync(payment, ct);
        }

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentConflictException(
                $"Order {orderId} is in state {payment.Status} and cannot be cancelled. " +
                "Only an authorized (not yet captured) order can be cancelled; a captured order must be refunded.");
        }

        await _gateway.VoidAsync(payment.AuthorizationId!, payment.VoidIdempotencyKey, ct);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Order {0} cancelled; authorization {1} voided, funds released.", orderId, payment.AuthorizationId);
        return await ToViewAsync(payment, ct);
    }

    // ---------------------------------------------------------------- refund

    public async Task<RefundResult> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct)
            ?? throw new PaymentResourceNotFoundException($"No payment found for order {orderId}.");
        if (payment.BuyerId != buyerId)
        {
            throw new PaymentResourceNotFoundException($"No payment found for order {orderId}.");
        }

        // Repeat under the same idempotency key → return the same refund, never a second one.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation("Refund idempotency key already seen for order {0}; returning existing refund {1}.", orderId, existing.PayPalRefundId);
            return new RefundResult(existing.PayPalRefundId, existing.Status, existing.Amount, ToView(payment));
        }

        if (payment.CaptureId is null || payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment available to refund.");
        }

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
        {
            throw new PaymentConflictException($"Order {orderId} has already been fully refunded.");
        }

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentConflictException("The refund amount must be greater than zero.");
        }
        if (refundAmount > remaining)
        {
            throw new PaymentConflictException(
                $"The refund amount {refundAmount} exceeds the remaining refundable amount {remaining} for order {orderId}.");
        }

        // The caller's key only needs to be unique per capture (we dedup on it above and never call
        // PayPal twice for the same key). Namespace it with the capture id so the PayPal-Request-Id is
        // globally unique — two captures may legitimately reuse the same caller key.
        var payPalRequestId = $"rf-{payment.CaptureId}-{idempotencyKey}";
        var refund = await _gateway.RefundAsync(payment.CaptureId!, refundAmount, payment.CurrencyCode, payPalRequestId, ct);
        var stored = payment.AddRefund(idempotencyKey, refundAmount, refund.RefundId, refund.Status);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Order {0} refunded {1} {2}; refund {3}, status {4}.",
            orderId, refundAmount, payment.CurrencyCode, refund.RefundId, refund.Status);
        return new RefundResult(stored.PayPalRefundId, stored.Status, stored.Amount, ToView(payment));
    }

    // ---------------------------------------------------------------- my orders

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), ct);
        var paymentByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => paymentByOrder.TryGetValue(o.Id, out var p) ? ToView(o, p) : ToPendingView(o, Currency))
            .ToList();
    }

    // ---------------------------------------------------------------- reconciliation

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
        {
            throw new PaymentConflictException("The reconciliation 'to' date must not be earlier than 'from'.");
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);

        // Index every eShop payment we have by its external invoice id.
        var allPayments = await _paymentRepository.ListAsync(ct);
        var byInvoice = allPayments
            .Where(p => !string.IsNullOrEmpty(p.InvoiceId))
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationEntry>();
        var payPalOnly = new List<ReconciliationEntry>();
        var seenInvoices = new HashSet<string>(StringComparer.Ordinal);

        foreach (var t in transactions)
        {
            int? matchedOrderId = null;
            if (!string.IsNullOrEmpty(t.InvoiceId) && byInvoice.TryGetValue(t.InvoiceId!, out var p))
            {
                matchedOrderId = p.OrderId;
                seenInvoices.Add(t.InvoiceId!);
            }

            var entry = new ReconciliationEntry(
                t.TransactionId, t.InvoiceId, t.Status, t.Amount, t.CurrencyCode, t.Fee, t.InitiationDate, matchedOrderId);

            if (matchedOrderId is not null)
            {
                matched.Add(entry);
            }
            else
            {
                payPalOnly.Add(entry);
            }
        }

        // eShop captures in the window that PayPal's report did not show.
        var capturedInRange = await _paymentRepository.ListAsync(new CapturedOrderPaymentsInRangeSpecification(from, to), ct);
        var eShopOnly = capturedInRange
            .Where(p => p.InvoiceId is null || !seenInvoices.Contains(p.InvoiceId))
            .Select(p => new UnmatchedOrderPayment(
                p.OrderId, p.CaptureId, p.InvoiceId, p.CapturedAmount, p.CurrencyCode, p.Status.ToString()))
            .ToList();

        _logger.LogInformation("Reconciliation {0}..{1}: {2} PayPal txns, {3} matched, {4} paypal-only, {5} eshop-only.",
            from, to, transactions.Count, matched.Count, payPalOnly.Count, eShopOnly.Count);

        return new ReconciliationReport(from, to, transactions.Count, matched, payPalOnly, eShopOnly);
    }

    // ---------------------------------------------------------------- saved cards

    public async Task<SavedCardView> SaveCardAsync(string buyerId, SaveCardCommand command, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(command?.Card, nameof(command.Card));

        // Reuse the shopper's existing PayPal customer id so their cards stay under one customer.
        var existing = await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        var customerId = existing.Select(m => m.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var vaulted = await _gateway.VaultCardAsync(
            new GatewayVaultCardRequest(command!.Card, customerId, SanitizeMerchantCustomerId(buyerId)), ct);

        var saved = new SavedPaymentMethod(
            buyerId, vaulted.VaultId, vaulted.CustomerId,
            vaulted.Brand, vaulted.Last4, vaulted.Expiry, vaulted.CardholderName, command.Alias);
        saved = await _savedCardRepository.AddAsync(saved, ct);

        _logger.LogInformation("Buyer saved card {0} ({1} ****{2}).", saved.Id, saved.Brand, saved.Last4);
        return ToView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        return cards.OrderByDescending(c => c.CreatedAt).Select(ToView).ToList();
    }

    public async Task RemoveSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var saved = await _savedCardRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), ct)
            ?? throw new PaymentResourceNotFoundException($"Saved card {paymentMethodId} was not found for this shopper.");

        await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultId, ct);
        await _savedCardRepository.DeleteAsync(saved, ct);
        _logger.LogInformation("Buyer removed saved card {0}.", paymentMethodId);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found for this shopper.");
        }
        return order;
    }

    private static string SanitizeMerchantCustomerId(string buyerId)
    {
        // merchant_customer_id allows [0-9a-zA-Z-_.^*$@#], max 64.
        var cleaned = new string(buyerId.Where(c =>
            char.IsLetterOrDigit(c) || "-_.^*$@#".IndexOf(c) >= 0).ToArray());
        if (cleaned.Length == 0)
        {
            cleaned = "eshop-customer";
        }
        return cleaned.Length > 64 ? cleaned.Substring(0, 64) : cleaned;
    }

    private async Task<OrderPaymentView> ToViewAsync(OrderPayment payment, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(payment.OrderId), ct);
        return ToView(order, payment);
    }

    private static OrderPaymentView ToView(OrderPayment payment) => ToView(null, payment);

    private static OrderPaymentView ToView(Order? order, OrderPayment payment)
    {
        var refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundView(r.PayPalRefundId, r.Status, r.Amount, r.CreatedAt))
            .ToList();

        return new OrderPaymentView(
            OrderId: payment.OrderId,
            Status: payment.Status.ToString(),
            Amount: payment.Amount,
            CurrencyCode: payment.CurrencyCode,
            OrderDate: order?.OrderDate ?? payment.CreatedAt,
            PayPalOrderId: payment.PayPalOrderId,
            AuthorizationId: payment.AuthorizationId,
            AuthorizationExpiresAt: payment.AuthorizationExpiresAt,
            CaptureId: payment.CaptureId,
            CapturedAmount: payment.CapturedAmount,
            PayPalFee: payment.PayPalFee,
            NetAmount: payment.NetAmount,
            TotalRefunded: payment.TotalRefunded(),
            Refunds: refunds);
    }

    private static OrderPaymentView ToPendingView(Order order, string currencyCode) => new(
        OrderId: order.Id,
        Status: PaymentStatus.PendingPayment.ToString(),
        Amount: order.Total(),
        CurrencyCode: currencyCode,
        OrderDate: order.OrderDate,
        PayPalOrderId: null,
        AuthorizationId: null,
        AuthorizationExpiresAt: null,
        CaptureId: null,
        CapturedAmount: null,
        PayPalFee: null,
        NetAmount: null,
        TotalRefunded: 0m,
        Refunds: Array.Empty<RefundView>());

    private static SavedCardView ToView(SavedPaymentMethod m) => new(
        PaymentMethodId: m.Id,
        Brand: m.Brand,
        Last4: m.Last4,
        Expiry: m.Expiry,
        CardholderName: m.CardholderName,
        Alias: m.Alias,
        CreatedAt: m.CreatedAt);
}
