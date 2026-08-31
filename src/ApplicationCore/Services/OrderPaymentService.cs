using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // PayPal-Request-Ids are deterministic per order within a process run; the run
    // prefix keeps them unique across in-memory database restarts (order ids reset).
    private static readonly string RunPrefix = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly PayPalSettings _payPalSettings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IRepository<CatalogItem> itemRepository,
        IPayPalClient payPalClient,
        PayPalSettings payPalSettings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _itemRepository = itemRepository;
        _payPalClient = payPalClient;
        _payPalSettings = payPalSettings;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }
        foreach (var item in items)
        {
            Guard.Against.NegativeOrZero(item.Quantity, nameof(item.Quantity));
        }

        var catalogItemsSpec = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpec, cancellationToken);

        var missing = items.Select(i => i.CatalogItemId).Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Payment> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card == null && savedPaymentMethodId == null)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId.");
        }
        if (card != null && savedPaymentMethodId != null)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.");
        }

        var order = await GetOwnOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotency: a paid-for order returns its existing authorization.
        var existingPayment = await GetPaymentForOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.PaymentAuthorized && existingPayment != null)
        {
            return existingPayment;
        }
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new PaymentException($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        var total = order.Total();
        var currency = _payPalSettings.Currency;

        // PayPal business accounts reject duplicate invoice ids; ours are unique per payment attempt.
        var invoiceId = $"eshop-{RunPrefix}-order-{orderId}-{Guid.NewGuid():N}";

        PayPalOrderResult payPalOrder;
        if (savedPaymentMethodId != null)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedPaymentMethodId.Value, cancellationToken);
            if (savedCard == null || savedCard.BuyerId != buyerId)
            {
                throw new PaymentException($"Saved payment method {savedPaymentMethodId} was not found.");
            }
            payPalOrder = await _payPalClient.CreateOrderWithVaultedCardAsync(
                total, currency, savedCard.VaultTokenId, invoiceId, $"eshop-{RunPrefix}-order-{orderId}-create", cancellationToken);
        }
        else
        {
            payPalOrder = await _payPalClient.CreateOrderAsync(
                total, currency, card!, invoiceId, $"eshop-{RunPrefix}-order-{orderId}-create", cancellationToken);
        }

        if (string.Equals(payPalOrder.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                "PayPal requires the shopper to approve this card payment in a browser (3D Secure challenge); " +
                "this integration does not support approval round-trips.");
        }

        // For direct card payments PayPal authorizes inline during order creation;
        // otherwise the order is approved and a separate authorize call is required.
        var authorization = payPalOrder.Authorization
            ?? await _payPalClient.AuthorizeOrderAsync(
                payPalOrder.OrderId, $"eshop-{RunPrefix}-order-{orderId}-authorize", cancellationToken);

        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(authorization.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"PayPal did not authorize the payment (status {authorization.Status}). The card may have been declined; no money was held.");
        }

        if (authorization.Amount != total)
        {
            _logger.LogWarning($"Order {orderId}: PayPal authorized {authorization.Amount} {authorization.Currency}, expected {total} {currency}.");
        }

        var payment = new Payment(orderId, buyerId, payPalOrder.OrderId, authorization.Amount, authorization.Currency);
        payment.MarkAuthorized(authorization.AuthorizationId, authorization.Status, authorization.ExpirationTime);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);

        // Idempotency: fulfilling an already-fulfilled order returns the existing capture.
        if (order.Status == OrderStatus.Fulfilled && payment != null)
        {
            return payment;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || payment == null || payment.AuthorizationId == null)
        {
            throw new PaymentException($"Order {orderId} is {order.Status} and cannot be fulfilled.");
        }

        var authorization = await _payPalClient.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);

        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            // The hold has gone stale (expired/denied) before fulfilment: renew it.
            _logger.LogInformation($"Order {orderId}: authorization {payment.AuthorizationId} is {authorization.Status}; attempting reauthorization.");
            authorization = await RenewAuthorizationAsync(payment, authorization, cancellationToken);
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPalClient.CaptureAuthorizationAsync(
                authorization.AuthorizationId, $"eshop-{RunPrefix}-order-{orderId}-capture", cancellationToken);
        }
        catch (Exception captureError)
        {
            // A hold that went stale between the status check and the capture: renew once, then retry.
            _logger.LogWarning($"Order {orderId}: capture failed ({captureError.Message}); attempting reauthorization.");
            authorization = await RenewAuthorizationAsync(payment, authorization, cancellationToken);
            capture = await _payPalClient.CaptureAuthorizationAsync(
                authorization.AuthorizationId, $"eshop-{RunPrefix}-order-{orderId}-capture-retry", cancellationToken);
        }

        payment.UpdateAuthorizationState(authorization.AuthorizationId, "CAPTURED", authorization.ExpirationTime);
        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    private async Task<PayPalAuthorizationResult> RenewAuthorizationAsync(Payment payment, PayPalAuthorizationResult stale, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPalClient.ReauthorizeAuthorizationAsync(
                stale.AuthorizationId, payment.AuthorizedAmount, payment.Currency,
                $"eshop-{RunPrefix}-order-{payment.OrderId}-reauthorize", cancellationToken);
            payment.UpdateAuthorizationState(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return renewed;
        }
        catch (Exception ex)
        {
            throw new PaymentException(
                $"The PayPal authorization for order {payment.OrderId} has gone stale and could not be renewed " +
                $"({ex.Message}). Ask the shopper to pay again, or cancel the order.", ex);
        }
    }

    public async Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);

        // Idempotency: cancelling an already-cancelled order is a no-op.
        if (order.Status == OrderStatus.Cancelled)
        {
            return payment;
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentException($"Order {orderId} is fulfilled and cannot be cancelled; refund it instead.");
        }

        if (payment != null && payment.AuthorizationId != null && !payment.IsVoided && !payment.IsCaptured)
        {
            await _payPalClient.VoidAuthorizationAsync(
                payment.AuthorizationId, $"eshop-{RunPrefix}-order-{orderId}-void", cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<PaymentRefund> RefundOrderAsync(string buyerId, bool isAdmin, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null || (!isAdmin && order.BuyerId != buyerId))
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);
        if (payment == null || !payment.IsCaptured)
        {
            throw new PaymentException($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotency: a repeated request under the same key returns the original refund.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0 || refundAmount > payment.RefundableAmount)
        {
            throw new PaymentException(
                $"Refund of {refundAmount:0.00} {payment.Currency} exceeds the refundable balance of {payment.RefundableAmount:0.00} {payment.Currency}.");
        }

        var result = await _payPalClient.RefundCaptureAsync(
            payment.CaptureId!, refundAmount, payment.Currency,
            $"eshop-{RunPrefix}-refund-{orderId}-{idempotencyKey}", cancellationToken);

        var refund = payment.AddRefund(result.RefundId, result.Status, refundAmount, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var spec = new CustomerOrdersWithItemsSpecification(buyerId);
        return await _orderRepository.ListAsync(spec, cancellationToken);
    }

    public async Task<Payment?> GetPaymentForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var spec = new PaymentByOrderIdSpec(orderId);
        return await _paymentRepository.FirstOrDefaultAsync(spec, cancellationToken);
    }

    private async Task<Order> GetOwnOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var spec = new OrderWithItemsByIdSpec(orderId);
        var order = await _orderRepository.FirstOrDefaultAsync(spec, cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }
}
