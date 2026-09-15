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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the payment lifecycle of an order (Flow 1). It owns the order state machine and
/// the idempotency guards; the actual money movement is delegated to <see cref="IPaymentGateway"/>.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentValidationException("Every order line must have a quantity of at least one.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentValidationException($"Catalog item {line.CatalogItemId} does not exist.");

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri))
            {
                pictureUri = "eCatalog-item-default.png";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> AuthorizeAsync(
        string buyerId, int orderId, PaymentInstruction instruction, CancellationToken cancellationToken)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotency in effect: a double-click never authorizes twice.
        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
        {
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentValidationException(
                $"Order {orderId} cannot be paid because it is {order.Status}.");
        }

        var amount = order.Total();
        if (amount <= 0m)
        {
            throw new PaymentValidationException($"Order {orderId} has no payable total.");
        }

        // Deterministic keys so a retried call reaches the same PayPal order rather than a new one.
        var idempotencyKey = $"eshop-authorize-order-{orderId}";
        var invoiceId = $"eshop-order-{orderId}";

        int? savedCardId = null;
        PayPalAuthorizationResult result;

        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdForBuyerSpec(buyerId, savedId), cancellationToken)
                ?? throw new PaymentMethodNotFoundException(savedId);

            savedCardId = savedCard.Id;
            result = await _gateway.AuthorizeWithVaultAsync(
                amount, idempotencyKey, savedCard.PayPalVaultId, invoiceId, cancellationToken);
        }
        else if (instruction.Card is not null)
        {
            result = await _gateway.AuthorizeWithCardAsync(
                amount, idempotencyKey, instruction.Card, invoiceId, cancellationToken);
        }
        else
        {
            throw new PaymentValidationException(
                "Provide either card details or the id of a saved payment method to pay with.");
        }

        var payment = new Payment(
            _gateway.Currency,
            amount,
            result.PayPalOrderId,
            result.AuthorizationId,
            result.AuthorizationStatus,
            result.ExpiresAt,
            savedCardId);

        order.AttachPayment(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order; // already fulfilled and captured
        }
        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
        {
            throw new PaymentValidationException(
                $"Order {orderId} cannot be fulfilled because it is {order.Status}.");
        }

        var payment = order.Payment;
        var amount = payment.AuthorizedAmount;

        await RenewAuthorizationIfStaleAsync(orderId, payment, amount, cancellationToken);

        var capture = await _gateway.CaptureAsync(
            payment.AuthorizationId, amount, $"eshop-capture-order-{orderId}", cancellationToken);

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    /// <summary>
    /// If the hold has gone stale before fulfilment, renew it rather than letting the capture fail.
    /// A hold that can no longer be renewed is reported in terms an operator can act on.
    /// </summary>
    private async Task RenewAuthorizationIfStaleAsync(int orderId, Payment payment, decimal amount, CancellationToken cancellationToken)
    {
        PayPalAuthorizationDetails details;
        try
        {
            details = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            // Could not read the hold; fall through and let the capture attempt surface the reason.
            return;
        }

        var status = details.Status?.ToUpperInvariant() ?? string.Empty;
        var expired = status == "EXPIRED"
            || (details.ExpiresAt.HasValue && details.ExpiresAt.Value <= DateTimeOffset.UtcNow);

        if (status is "VOIDED" or "DENIED")
        {
            throw new PaymentValidationException(
                $"Order {orderId} cannot be fulfilled: its authorization is {status} and cannot be captured. " +
                "The buyer must be asked to pay again.");
        }

        if (!expired)
        {
            return;
        }

        try
        {
            var renewed = await _gateway.ReauthorizeAsync(
                payment.AuthorizationId, amount, $"eshop-reauth-order-{orderId}", cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.AuthorizationStatus, renewed.ExpiresAt);
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentValidationException(
                $"Order {orderId} cannot be fulfilled: its authorization has expired and could not be renewed " +
                $"({ex.Message}). The buyer must be asked to pay again.");
        }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // already cancelled and released
        }
        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
        {
            throw new PaymentValidationException(
                $"Order {orderId} cannot be cancelled because it is {order.Status}. " +
                "Only an order awaiting fulfilment can be cancelled; a fulfilled order must be refunded instead.");
        }

        await _gateway.VoidAuthorizationAsync(order.Payment.AuthorizationId, cancellationToken);
        order.Payment.MarkVoided();
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, PaymentRefund Refund)> RefundAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.Status != OrderStatus.Fulfilled || order.Payment?.CaptureId is null)
        {
            throw new PaymentValidationException(
                $"Order {orderId} cannot be refunded because it has not been captured (it is {order.Status}).");
        }

        var payment = order.Payment;

        // Idempotency: a repeat under the same key returns the original refund, never a second one.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            return (order, existing);
        }

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
        {
            throw new PaymentValidationException(
                $"Order {orderId} has already been fully refunded; nothing remains to refund.");
        }

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentValidationException("A refund amount must be greater than zero.");
        }
        if (refundAmount > remaining)
        {
            throw new PaymentValidationException(
                $"Refund of {refundAmount:0.00} {payment.Currency} exceeds the {remaining:0.00} {payment.Currency} " +
                "still available to refund on this order.");
        }

        var result = await _gateway.RefundAsync(
            payment.CaptureId!, refundAmount, $"eshop-refund-order-{orderId}-{idempotencyKey}", cancellationToken);

        var refund = new PaymentRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        payment.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentByBuyerSpec(buyerId), cancellationToken);
        return orders;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal that the order exists but belongs to someone else.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }
}
