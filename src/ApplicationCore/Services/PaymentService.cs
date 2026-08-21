using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly PayPalSettings _settings;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IReadRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IPayPalPaymentGateway gateway,
        PayPalSettings settings)
    {
        _orderRepository = orderRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _settings = settings;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines.Count == 0)
            throw new PaymentStateException("An order must contain at least one item.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new PaymentStateException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new EntityNotFoundException($"Catalog item {line.CatalogItemId} was not found.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, ct);
        return order;
    }

    public async Task<Order> AuthorizeOrderAsync(int orderId, string buyerId, PayPalCardData? card, int? savedPaymentMethodId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdAndBuyerSpec(orderId, buyerId), ct)
            ?? throw new EntityNotFoundException($"Order {orderId} was not found.");

        // Idempotent in effect: a repeated pay on an already-authorized order does not authorize again.
        if (order.Status == OrderStatus.Authorized && order.Payment is not null)
            return order;

        if (order.Status != OrderStatus.AwaitingPayment)
            throw new PaymentStateException($"Order {orderId} cannot be paid because it is {order.Status}.");

        string? vaultId = null;
        if (savedPaymentMethodId is not null)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(savedPaymentMethodId.Value, buyerId), ct)
                ?? throw new EntityNotFoundException($"Saved payment method {savedPaymentMethodId} was not found.");
            vaultId = saved.VaultId;
        }
        else if (card is null)
        {
            throw new PaymentStateException("A card or a saved payment method must be supplied to pay.");
        }

        var currency = _settings.Currency;
        var request = new PayPalAuthorizationRequest(
            Amount: order.Total(),
            Currency: currency,
            OrderReference: order.Id.ToString(),
            Card: vaultId is null ? card : null,
            VaultId: vaultId);

        var result = await _gateway.AuthorizeAsync(request, $"eshop-authorize-{order.PaymentReference}", ct);

        var payment = new Payment(result.PayPalOrderId, result.AuthorizationId, result.Status, order.Total(), currency);
        order.AttachAuthorization(payment);
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithPaymentSpec(orderId), ct)
            ?? throw new EntityNotFoundException($"Order {orderId} was not found.");

        var payment = order.Payment
            ?? throw new PaymentStateException($"Order {orderId} has no authorized payment to capture.");

        // Idempotent in effect: fulfilling an already-fulfilled order does not capture again.
        if (order.Status == OrderStatus.Fulfilled && payment.CaptureId is not null)
            return order;

        if (order.Status != OrderStatus.Authorized)
            throw new PaymentStateException($"Order {orderId} cannot be fulfilled because it is {order.Status}.");

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId, $"eshop-capture-{payment.AuthorizationId}", ct);
        }
        catch (AuthorizationExpiredException)
        {
            // The hold went stale before fulfilment: renew it, then capture the renewed hold.
            // A hold that can no longer be renewed throws AuthorizationNotRenewableException (operator-actionable).
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId, payment.AuthorizedAmount, payment.Currency, ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status);
            await _orderRepository.UpdateAsync(order, ct);

            capture = await _gateway.CaptureAsync(reauth.AuthorizationId, $"eshop-capture-{reauth.AuthorizationId}", ct);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithPaymentSpec(orderId), ct)
            ?? throw new EntityNotFoundException($"Order {orderId} was not found.");

        if (order.Status == OrderStatus.Cancelled)
            return order; // idempotent

        var payment = order.Payment
            ?? throw new PaymentStateException($"Order {orderId} has no authorization to cancel.");

        if (order.Status != OrderStatus.Authorized)
            throw new PaymentStateException($"Order {orderId} cannot be cancelled because it is {order.Status}.");

        await _gateway.VoidAsync(payment.AuthorizationId, ct);
        payment.MarkVoided();
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdAndBuyerSpec(orderId, buyerId), ct)
            ?? throw new EntityNotFoundException($"Order {orderId} was not found.");

        var payment = order.Payment
            ?? throw new PaymentStateException($"Order {orderId} has no captured payment to refund.");

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
            throw new PaymentStateException($"Order {orderId} cannot be refunded because it is {order.Status}.");

        if (payment.CaptureId is null)
            throw new PaymentStateException($"Order {orderId} has no capture to refund.");

        // Idempotency: repeating a request under the same key returns the same refund, never a second one.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return order;

        // A partly-refunded order can never become refundable beyond what was captured.
        var remaining = payment.RemainingCapturedAmount();
        if (remaining <= 0m)
            throw new PaymentStateException($"Order {orderId} has already been fully refunded.");

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
            throw new PaymentStateException("Refund amount must be greater than zero.");
        if (refundAmount > remaining)
            throw new PaymentStateException($"Refund amount {refundAmount:0.00} exceeds the remaining captured amount {remaining:0.00}.");

        var result = await _gateway.RefundAsync(payment.CaptureId, amount, payment.Currency, idempotencyKey, ct);

        var recordedAmount = result.Amount > 0m ? result.Amount : refundAmount;
        payment.AddRefund(new PaymentRefund(result.RefundId, recordedAmount, result.Status, idempotencyKey));
        order.ApplyRefundState();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new OrdersByBuyerSpec(buyerId), ct);
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardData card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(card, ct);
        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.LastDigits,
            vaulted.Expiry, vaulted.CardholderName ?? card.CardholderName);
        await _paymentMethodRepository.AddAsync(saved, ct);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
    }

    public async Task DeleteCardAsync(int paymentMethodId, string buyerId, CancellationToken ct)
    {
        var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), ct)
            ?? throw new EntityNotFoundException($"Saved payment method {paymentMethodId} was not found.");

        await _gateway.DeleteVaultedCardAsync(saved.VaultId, ct);
        await _paymentMethodRepository.DeleteAsync(saved, ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var capturedOrders = await _orderRepository.ListAsync(new CapturedOrdersSpec(), ct);

        var eshopByCapture = capturedOrders
            .Where(o => o.Payment?.CaptureId is not null)
            .GroupBy(o => o.Payment!.CaptureId!)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<ReconciliationMatch>();
        var inPayPalOnly = new List<ReconciliationTransaction>();
        var matchedCaptureIds = new HashSet<string>();

        foreach (var tx in transactions)
        {
            if (tx.TransactionId is not null && eshopByCapture.TryGetValue(tx.TransactionId, out var order))
            {
                matched.Add(new ReconciliationMatch(order.Id, tx.TransactionId, tx.Amount,
                    order.Payment!.CapturedAmount ?? 0m, tx.Status, order.Status.ToString()));
                matchedCaptureIds.Add(tx.TransactionId);
            }
            else
            {
                inPayPalOnly.Add(tx);
            }
        }

        var inEshopOnly = eshopByCapture
            .Where(kv => !matchedCaptureIds.Contains(kv.Key))
            .Select(kv => new ReconciliationEshopEntry(kv.Value.Id, kv.Key,
                kv.Value.Payment!.CapturedAmount ?? 0m, kv.Value.Status.ToString()))
            .ToList();

        return new ReconciliationReport(from, to, transactions.Count, matched, inPayPalOnly, inEshopOnly);
    }
}
