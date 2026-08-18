using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalPaymentService _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentConfiguration _configuration;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalPaymentService payPal,
        IUriComposer uriComposer,
        IPaymentConfiguration configuration)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _configuration = configuration;
    }

    private string Currency => _configuration.CurrencyCode;

    public async Task<int> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineInput> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        var lineList = lines?.ToList() ?? new List<OrderLineInput>();
        if (lineList.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");
        if (lineList.Any(l => l.Quantity <= 0))
            throw new PaymentValidationException("Every order line must have a quantity greater than zero.");

        var ids = lineList.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lineList)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentValidationException($"Catalog item {line.CatalogItemId} was not found.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            // Amount comes from the catalog price, never the caller.
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, Currency, order.Total());
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return order.Id;
    }

    public async Task AuthorizeAsync(string buyerId, int orderId, PayOrderInput input, CancellationToken cancellationToken = default)
    {
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a repeat once already authorized is a no-op, never a second hold.
        if (payment.IsAuthorized)
            return;

        if (payment.Status != PaymentStatus.AwaitingPayment)
            throw new PaymentConflictException($"Order {orderId} cannot be paid in its current state ({payment.Status}).");

        var source = await ResolvePaymentSourceAsync(buyerId, input, cancellationToken);

        // Stable key derived from the order's unique reference — a double-click reuses it, so PayPal
        // returns the same hold rather than creating a second one.
        var idempotencyKey = $"authorize-{payment.Reference}";
        var result = await _payPal.AuthorizeAsync(payment.Amount, payment.CurrencyCode, source, idempotencyKey, cancellationToken);

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
    }

    public async Task FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // Idempotent: fulfilling an already-captured order does not capture again.
        if (payment.IsCaptured)
            return;

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentConflictException($"Order {orderId} is not awaiting fulfilment (state {payment.Status}); it must be authorized first.");

        bool renewed = false;

        // Proactively renew a hold that has already gone stale rather than letting the capture fail.
        if (payment.AuthorizationExpiresAt is DateTimeOffset expiry && expiry <= DateTimeOffset.UtcNow)
        {
            await RenewAuthorizationAsync(payment, orderId, cancellationToken);
            renewed = true;
        }

        var captureKey = $"capture-{payment.Reference}";
        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(payment.AuthorizationId!, captureKey, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (!renewed && ex.GetType() == typeof(PaymentGatewayException))
        {
            // The hold may have gone stale between authorize and fulfil — renew once and retry the capture.
            // If it can no longer be renewed, RenewAuthorizationAsync throws ReauthorizationExpiredException,
            // which surfaces to the operator rather than failing silently.
            await RenewAuthorizationAsync(payment, orderId, cancellationToken);
            capture = await _payPal.CaptureAsync(payment.AuthorizationId!, captureKey, cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.Fee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
    }

    private async Task RenewAuthorizationAsync(OrderPayment payment, int orderId, CancellationToken cancellationToken)
    {
        var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, $"reauth-{payment.Reference}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}", cancellationToken);
        payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
            return; // idempotent

        if (payment.IsCaptured)
            throw new PaymentConflictException($"Order {orderId} has already been fulfilled; use a refund to return money.");

        // Release the hold at PayPal if one exists; if the order was never authorized there is nothing to void.
        if (payment.IsAuthorized && payment.AuthorizationId is not null)
        {
            await _payPal.VoidAsync(payment.AuthorizationId, $"void-{payment.Reference}", cancellationToken);
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
    }

    public async Task<string> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        if (!payment.IsCaptured || payment.CaptureId is null)
            throw new PaymentConflictException($"Order {orderId} has not been fulfilled; there is nothing to refund.");

        // Idempotent: repeating a refund under the same key returns the original refund, never a second one.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return existing.PayPalRefundId;

        var remaining = payment.RemainingRefundable();
        if (remaining <= 0m)
            throw new PaymentConflictException($"Order {orderId} has already been fully refunded.");

        if (amount is decimal requested)
        {
            if (requested <= 0m)
                throw new PaymentValidationException("Refund amount must be greater than zero.");
            if (requested > remaining)
                throw new PaymentConflictException($"Refund amount {requested} exceeds the remaining refundable balance {remaining}.");
        }

        // amount == null → full refund of the remaining balance.
        var refundAmount = amount ?? remaining;
        // Local dedup above already guarantees one refund per (order, caller key). The id handed to PayPal
        // is namespaced with the order's unique reference so a caller key reused across different orders is
        // globally unique at PayPal, while a genuine repeat is short-circuited before we ever call.
        var payPalRequestId = $"refund-{payment.Reference}-{idempotencyKey}";
        var result = await _payPal.RefundAsync(payment.CaptureId, amount, payment.CurrencyCode, payPalRequestId, cancellationToken);

        var recordedAmount = result.Amount > 0m ? result.Amount : refundAmount;
        payment.AddRefund(new PaymentRefund(result.RefundId, idempotencyKey, recordedAmount, result.Status ?? "UNKNOWN"));
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return result.RefundId;
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), cancellationToken);
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id);

        var views = new List<OrderPaymentView>();
        foreach (var payment in payments.OrderByDescending(p => p.OrderId))
        {
            ordersById.TryGetValue(payment.OrderId, out var order);

            var items = order?.OrderItems
                .Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
                .ToList() ?? new List<OrderLineView>();

            var refunds = payment.Refunds
                .OrderBy(r => r.CreatedAt)
                .Select(r => new OrderPaymentRefundView(r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt))
                .ToList();

            views.Add(new OrderPaymentView(
                payment.OrderId,
                order?.OrderDate ?? default,
                payment.Amount,
                payment.CurrencyCode,
                payment.Status.ToString(),
                payment.PayPalOrderId,
                payment.AuthorizationId,
                payment.AuthorizationStatus,
                payment.AuthorizationExpiresAt,
                payment.CaptureId,
                payment.CaptureStatus,
                payment.CapturedAmount,
                payment.PayPalFee,
                payment.NetAmount,
                payment.RefundedAmount(),
                refunds,
                items));
        }

        return views;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var payPalTransactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsWithRefundsSpecification(), cancellationToken);

        // eShop's own record of settlements in the range: each capture and each refund is one event,
        // keyed by the PayPal transaction id we stored for it.
        var eShopEvents = new Dictionary<string, (int OrderId, string Status)>();
        foreach (var payment in payments)
        {
            if (payment.CaptureId is not null && InRange(payment.CapturedAt, from, to))
                eShopEvents[payment.CaptureId] = (payment.OrderId, payment.CaptureStatus ?? payment.Status.ToString());

            foreach (var refund in payment.Refunds)
            {
                if (InRange(refund.CreatedAt, from, to))
                    eShopEvents[refund.PayPalRefundId] = (payment.OrderId, $"REFUND:{refund.Status}");
            }
        }

        var payPalIds = new HashSet<string>();
        var lines = new List<ReconciliationLine>();

        foreach (var tx in payPalTransactions)
        {
            payPalIds.Add(tx.TransactionId);
            var matched = eShopEvents.TryGetValue(tx.TransactionId, out var eShop);
            lines.Add(new ReconciliationLine(
                matched ? ReconciliationMatch.Matched : ReconciliationMatch.InPayPalOnly,
                tx.TransactionId,
                tx.Amount,
                tx.CurrencyCode,
                tx.Status,
                tx.Date,
                matched ? eShop.OrderId : null,
                matched ? eShop.Status : null));
        }

        // Settlements eShop recorded that PayPal's report for this range does not (yet) show.
        foreach (var kvp in eShopEvents)
        {
            if (payPalIds.Contains(kvp.Key))
                continue;

            lines.Add(new ReconciliationLine(
                ReconciliationMatch.InEShopOnly,
                kvp.Key,
                null,
                _configuration.CurrencyCode,
                null,
                null,
                kvp.Value.OrderId,
                kvp.Value.Status));
        }

        return new ReconciliationReport(
            from,
            to,
            payPalTransactions.Count,
            lines.Count(l => l.Match == ReconciliationMatch.Matched),
            lines.Count(l => l.Match == ReconciliationMatch.InPayPalOnly),
            lines.Count(l => l.Match == ReconciliationMatch.InEShopOnly),
            lines);
    }

    private static bool InRange(DateTimeOffset? when, DateTimeOffset from, DateTimeOffset to) =>
        when.HasValue && when.Value >= from && when.Value <= to;

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);
        return payment ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");
    }

    private async Task<OrderPayment> LoadOwnedPaymentAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);
        // Not-owned is reported as not-found so one shopper can never learn of another's order.
        if (payment is null || payment.BuyerId != buyerId)
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        return payment;
    }

    private async Task<PayPalPaymentSource> ResolvePaymentSourceAsync(string buyerId, PayOrderInput input, CancellationToken cancellationToken)
    {
        Guard.Against.Null(input, nameof(input));

        var hasCard = input.Card is not null;
        var hasSaved = input.SavedPaymentMethodId is not null;

        if (hasCard == hasSaved)
            throw new PaymentValidationException("Provide either card details or a saved card id, but not both.");

        if (hasCard)
            return PayPalPaymentSource.FromCard(input.Card!);

        var saved = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(input.SavedPaymentMethodId!.Value, buyerId), cancellationToken);
        if (saved is null)
            throw new PaymentValidationException($"Saved card {input.SavedPaymentMethodId} was not found.");

        return PayPalPaymentSource.FromVault(saved.VaultId);
    }
}
