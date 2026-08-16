using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentConfiguration _paymentConfiguration;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalPaymentGateway gateway,
        IUriComposer uriComposer,
        IPaymentConfiguration paymentConfiguration)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _paymentConfiguration = paymentConfiguration;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
            throw new PaymentOperationException("An order must contain at least one line.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentOperationException("Every order line must have a quantity of at least one.");

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentOperationException($"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        order.StartPayment(_paymentConfiguration.Currency);

        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, PayOrderInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);
        var payment = RequirePayment(order);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment.IsAuthorized)
            return order;
        if (!payment.IsAwaitingPayment)
            throw new PaymentOperationException($"Order {orderId} is not awaiting payment (payment is '{payment.Status}').");

        var payPalInstrument = await ResolveInstrumentAsync(instruction, buyerId, cancellationToken);

        var result = await _gateway.AuthorizeAsync(payPalInstrument, payment.Amount,
            IdempotencyKeys.Authorize(payment.IdempotencySeed), cancellationToken);

        payment.MarkAuthorized(result.PayPalOrderId ?? string.Empty, result.AuthorizationId, result.Status, result.ExpiresAt);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = RequirePayment(order);

        // Idempotent in effect: fulfilling an already-fulfilled order never captures twice.
        if (payment.IsCaptured)
            return order;
        if (!payment.IsAuthorized)
            throw new PaymentOperationException($"Order {orderId} cannot be fulfilled (payment is '{payment.Status}').");

        // A hold that has gone stale must be renewed rather than failing the fulfilment.
        var renewed = false;
        if (payment.IsAuthorizationStale(DateTimeOffset.Now))
        {
            await RenewAuthorizationAsync(payment, cancellationToken);
            renewed = true;
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, IdempotencyKeys.Capture(payment.IdempotencySeed), cancellationToken);
        }
        catch (PaymentGatewayException) when (!renewed)
        {
            // The hold may have expired without a recorded expiry — renew once and retry the capture.
            // If it can no longer be renewed, RenewAuthorizationAsync surfaces an operator-actionable message.
            await RenewAuthorizationAsync(payment, cancellationToken);
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, IdempotencyKeys.Capture(payment.IdempotencySeed), cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = RequirePayment(order);

        // Idempotent in effect.
        if (payment.Status == PaymentStatus.Voided)
            return order;
        if (!payment.IsAuthorized)
            throw new PaymentOperationException(
                $"Order {orderId} cannot be cancelled (payment is '{payment.Status}'). " +
                "Only an order holding funds but not yet fulfilled can be cancelled.");

        await _gateway.VoidAsync(payment.AuthorizationId!, IdempotencyKeys.Void(payment.IdempotencySeed), cancellationToken);
        payment.MarkVoided();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Refund> RefundAsync(int orderId, string buyerId, string idempotencyKey, decimal? amount,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);
        var payment = RequirePayment(order);

        // Idempotent on the caller's key: a repeated request under the same key never refunds twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return existing;

        if (!payment.IsCaptured || payment.CaptureId is null)
            throw new PaymentOperationException($"Order {orderId} cannot be refunded (payment is '{payment.Status}').");

        var effectiveAmount = amount ?? payment.RefundableRemaining;
        if (effectiveAmount <= 0m)
            throw new PaymentOperationException("Refund amount must be greater than zero.");
        if (effectiveAmount > payment.RefundableRemaining)
            throw new PaymentOperationException(
                $"Refund of {effectiveAmount:0.00} exceeds the {payment.RefundableRemaining:0.00} still refundable on this capture.");

        // Compose the caller's key with the capture id so it is idempotent per (capture, key) and
        // globally unique — a caller key reused against a different capture never collides at PayPal.
        var result = await _gateway.RefundAsync(payment.CaptureId, effectiveAmount,
            $"{payment.CaptureId}:{idempotencyKey}", cancellationToken);

        var refund = payment.StartRefund(idempotencyKey, effectiveAmount);
        payment.CompleteRefund(refund, result.RefundId, MapRefundStatus(result.Status));
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public async Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default) =>
        await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

    // ---- helpers ----

    private async Task RenewAuthorizationAsync(Payment payment, CancellationToken cancellationToken)
    {
        var renewal = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
            IdempotencyKeys.Reauthorize(payment.IdempotencySeed), cancellationToken);
        payment.RenewAuthorization(renewal.AuthorizationId, renewal.Status, renewal.ExpiresAt);
    }

    private async Task<PayPalPaymentInstrument> ResolveInstrumentAsync(PayOrderInstruction instruction, string buyerId,
        CancellationToken cancellationToken)
    {
        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdForBuyerSpecification(savedId, buyerId), cancellationToken);
            if (saved is null)
                throw new PaymentOperationException($"Saved card {savedId} was not found for this shopper.");
            return PayPalPaymentInstrument.FromVaultToken(saved.PayPalVaultTokenId);
        }

        if (instruction.Card is not null)
            return PayPalPaymentInstrument.FromCard(instruction.Card);

        throw new PaymentOperationException("Payment requires either card details or a saved card id.");
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken);
        if (order is null)
            throw new OrderNotFoundException(orderId);
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new OrderNotFoundException(orderId); // never reveal another shopper's order
        return order;
    }

    private static Payment RequirePayment(Order order) =>
        order.Payment ?? throw new PaymentOperationException($"Order {order.Id} has no payment record.");

    private static RefundStatus MapRefundStatus(string payPalStatus) => payPalStatus?.ToUpperInvariant() switch
    {
        "COMPLETED" => RefundStatus.Completed,
        "PENDING" => RefundStatus.Pending,
        "CANCELLED" => RefundStatus.Cancelled,
        "FAILED" => RefundStatus.Failed,
        _ => RefundStatus.Pending
    };
}

/// <summary>
/// Stable idempotency keys derived from the payment's unique seed, so a retried payment operation
/// reuses the same key at PayPal (PayPal-Request-Id) and never double-charges — while remaining
/// globally unique across payments (even when order ids repeat after an in-memory restart).
/// </summary>
internal static class IdempotencyKeys
{
    public static string Authorize(Guid seed) => $"eshop-{seed}-authorize";
    public static string Capture(Guid seed) => $"eshop-{seed}-capture";
    public static string Reauthorize(Guid seed) => $"eshop-{seed}-reauthorize";
    public static string Void(Guid seed) => $"eshop-{seed}-void";
}
