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
/// Orchestrates the money movement for orders: authorize at checkout, capture at
/// fulfilment, void on cancel, refund on return. All provider state that later
/// requests need (ids and statuses) is persisted on <see cref="OrderPayment"/>.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<Entities.BuyerAggregate.SavedCard> _savedCardRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPalGateway;
    private readonly PayPalSettings _payPalSettings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<Entities.BuyerAggregate.SavedCard> savedCardRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPalGateway,
        PayPalSettings payPalSettings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _payPalGateway = payPalGateway;
        _payPalSettings = payPalSettings;
        _logger = logger;
    }

    private string Currency => string.IsNullOrWhiteSpace(_payPalSettings.Currency)
        ? throw new InvalidOperationException("PayPal:Currency is not configured.")
        : _payPalSettings.Currency!;

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items,
        Address? shippingAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items == null || items.Count == 0)
        {
            throw new PaymentConflictException("An order must contain at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentConflictException("Item quantities must be positive.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).Distinct().ToArray()), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
            if (catalogItem == null)
            {
                throw new PaymentConflictException($"Catalog item {item.CatalogItemId} does not exist.");
            }
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var order = new Order(buyerId,
            shippingAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "N/A"),
            orderItems);

        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} placed by {buyerId}, total {order.Total()} {Currency}, awaiting payment.");
        return order;
    }

    public async Task<OrderPaymentState?> PayOrderAsync(string buyerId, int orderId, CardDetails? card,
        int? savedCardId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null || order.BuyerId != buyerId) return null;

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {orderId} is cancelled and cannot be paid.");
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentConflictException($"Order {orderId} is already fulfilled and paid.");
        }

        // Idempotency: a double-click returns the existing authorization instead of holding funds twice.
        var existingPayment = await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);
        if (existingPayment != null)
        {
            return new OrderPaymentState(order, existingPayment);
        }

        string? vaultTokenId = null;
        if (savedCardId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId.Value, cancellationToken);
            if (savedCard == null || savedCard.BuyerId != buyerId)
            {
                throw new PaymentConflictException($"Saved card {savedCardId} does not exist.");
            }
            vaultTokenId = savedCard.VaultTokenId;
        }
        if (card == null && vaultTokenId == null)
        {
            throw new PaymentConflictException("Provide either card details or a savedCardId to pay with.");
        }

        var total = order.Total();
        // OrderDate.Ticks keeps the key unique per order even if order ids are recycled
        // (e.g. in-memory database reset), while staying stable across retries.
        var idempotencyKey = $"eshop-order-{order.Id}-{order.OrderDate.Ticks}-pay";
        PayPalAuthorizationInfo authorization;
        try
        {
            authorization = await _payPalGateway.AuthorizePaymentAsync(
                order.Id.ToString(), total, Currency, card, vaultTokenId,
                idempotencyKey, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            var issues = ex.Issues.Count > 0 ? $" ({string.Join("; ", ex.Issues)})" : string.Empty;
            throw new PaymentDeclinedException($"PayPal could not authorize the payment: {ex.Message}{issues}");
        }

        if (string.Equals(authorization.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This integration does not support approval round-trips.");
        }
        if (string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentDeclinedException("PayPal denied the card authorization.");
        }

        var payment = new OrderPayment(order.Id, buyerId, authorization.PayPalOrderId,
            authorization.AuthorizationId, authorization.Status, authorization.Amount,
            authorization.Currency, authorization.ExpirationTime);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id}: authorized {authorization.Amount} {authorization.Currency} " +
            $"(authorization {authorization.AuthorizationId}, status {authorization.Status}).");
        return new OrderPaymentState(order, payment);
    }

    public async Task<OrderPaymentState?> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null) return null;

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotency: fulfilling an already-fulfilled order returns the recorded capture.
        if (order.Status == OrderStatus.Fulfilled && payment?.IsCaptured == true)
        {
            return new OrderPaymentState(order, payment);
        }
        if (order.Status != OrderStatus.PaymentAuthorized || payment == null)
        {
            throw new PaymentConflictException(
                $"Order {orderId} is not awaiting fulfilment (status: {order.Status}).");
        }

        var amount = order.Total();
        var capture = await CaptureWithRenewalAsync(order, payment, amount, cancellationToken);

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id}: captured {capture.Amount} {capture.Currency} " +
            $"(capture {capture.CaptureId}, fee {capture.PayPalFee}, net {capture.NetAmount}).");
        return new OrderPaymentState(order, payment);
    }

    private async Task<PayPalCaptureInfo> CaptureWithRenewalAsync(Order order, OrderPayment payment,
        decimal amount, CancellationToken cancellationToken)
    {
        // Keyed on the PayPal authorization id: globally unique and stable across retries.
        var captureKey = $"eshop-capture-{payment.AuthorizationId}";

        // A stale authorization must be renewed rather than failing the fulfilment outright.
        var authorization = await _payPalGateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        var stale = string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase);

        if (!stale)
        {
            try
            {
                return await _payPalGateway.CaptureAuthorizationAsync(
                    payment.AuthorizationId, amount, Currency, captureKey, cancellationToken);
            }
            catch (PaymentGatewayException ex) when (ex.IsUnprocessable)
            {
                _logger.LogWarning($"Order {order.Id}: capture failed ({ex.ErrorName}); attempting to renew the authorization.");
            }
        }

        PayPalAuthorizationInfo renewed;
        try
        {
            renewed = await _payPalGateway.ReauthorizeAsync(
                payment.AuthorizationId, amount, Currency, $"eshop-reauthorize-{payment.AuthorizationId}", cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw new AuthorizationNotRenewableException(
                $"The authorization for order {order.Id} has expired and PayPal could not renew it " +
                $"({ex.ErrorName}: {ex.Message}). Do not fulfil this order against the old hold; " +
                "ask the shopper to pay again so a fresh authorization can be captured.");
        }

        payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return await _payPalGateway.CaptureAuthorizationAsync(
            payment.AuthorizationId, amount, Currency, captureKey, cancellationToken);
    }

    public async Task<OrderPaymentState?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null) return null;

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return new OrderPaymentState(order, payment);
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentConflictException(
                $"Order {orderId} is already fulfilled; issue a refund instead of cancelling.");
        }

        if (payment != null && !payment.IsCaptured
            && !string.Equals(payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            // Release the shopper's held funds; no money ever moves.
            var current = await _payPalGateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            if (!string.Equals(current.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
            {
                await _payPalGateway.VoidAuthorizationAsync(
                    payment.AuthorizationId, $"eshop-void-{payment.AuthorizationId}", cancellationToken);
            }
            payment.MarkAuthorizationVoided("VOIDED");
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id}: cancelled; any held funds released.");
        return new OrderPaymentState(order, payment);
    }

    public async Task<PaymentRefund?> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null || order.BuyerId != buyerId) return null;

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment?.IsCaptured != true)
        {
            throw new PaymentConflictException(
                $"Order {orderId} has no captured payment to refund.");
        }

        // Idempotency: a repeated request under the same key returns the original refund.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var refundable = payment.RefundableAmount;
        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0)
        {
            throw new PaymentConflictException($"Order {orderId} is already fully refunded.");
        }
        if (refundAmount > refundable)
        {
            throw new PaymentConflictException(
                $"Refund of {refundAmount} exceeds the refundable balance of {refundable} " +
                $"(captured {payment.CapturedAmount}, already refunded {payment.RefundedAmount}).");
        }

        var info = await _payPalGateway.RefundCaptureAsync(
            payment.CaptureId!, amount.HasValue ? refundAmount : (decimal?)null, Currency,
            idempotencyKey, noteToPayer, cancellationToken);

        var refund = new PaymentRefund(info.RefundId, idempotencyKey, info.Amount, info.Status, noteToPayer);
        payment.AddRefund(refund);
        payment.SetCaptureStatus(payment.RefundableAmount <= 0 ? "REFUNDED" : "PARTIALLY_REFUNDED");
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {order.Id}: refunded {info.Amount} {info.Currency} (refund {info.RefundId}).");
        return refund;
    }

    public async Task<IReadOnlyList<OrderPaymentState>> ListMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var result = new List<OrderPaymentState>();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var payment = await _paymentRepository.FirstOrDefaultAsync(
                new OrderPaymentByOrderIdSpecification(order.Id), cancellationToken);
            result.Add(new OrderPaymentState(order, payment));
        }
        return result;
    }
}
