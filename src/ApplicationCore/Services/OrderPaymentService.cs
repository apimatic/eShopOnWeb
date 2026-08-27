using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan AuthorizationExpirySafetyMargin = TimeSpan.FromMinutes(5);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _payPalSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        PayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _payPalSettings = payPalSettings;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address? shippingAddress)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(items, nameof(items));

        foreach (var item in items)
        {
            Guard.Against.NegativeOrZero(item.Quantity, nameof(item.Quantity));
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray()));

        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId)
                ?? throw new NotFoundException(item.CatalogItemId.ToString(), nameof(CatalogItem));
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var order = new Order(buyerId, shippingAddress ?? DefaultAddress.Placeholder, orderItems);
        await _orderRepository.AddAsync(order);
        return order;
    }

    public async Task<Payment> PayOrderAsync(string buyerId, int orderId, GatewayCardDetails? card, int? savedCardId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (string.IsNullOrWhiteSpace(_payPalSettings.Currency))
        {
            throw new InvalidOperationException("PayPal:Currency must be configured (from the PAYPAL_CURRENCY environment variable).");
        }
        if (card is null && savedCardId is null)
        {
            throw new PaymentStateException("Either card details or a saved card id must be supplied to pay an order.");
        }
        if (card is not null && savedCardId is not null)
        {
            throw new PaymentStateException("Supply either card details or a saved card id, not both.");
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId);

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId));
        if (payment is not null && payment.Status != PaymentStatus.PendingAuthorization)
        {
            // Idempotent replay: the hold (or a later state) already exists — never authorize twice.
            return payment;
        }
        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            return payment!;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new OrderStateException($"Order {orderId} cannot be paid while in state {order.Status}.");
        }

        string? vaultTokenId = null;
        if (savedCardId is not null)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpec(savedCardId.Value));
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new NotFoundException(savedCardId.Value.ToString(), nameof(SavedCard));
            }
            vaultTokenId = savedCard.VaultTokenId;
        }

        payment ??= new Payment(orderId, buyerId, order.Total(), _payPalSettings.Currency);
        if (payment.Id == 0)
        {
            await _paymentRepository.AddAsync(payment);
        }

        if (payment.PayPalOrderId is null)
        {
            var gatewayOrder = await _paymentGateway.CreateOrderAsync(
                referenceId: orderId.ToString(),
                amount: payment.Amount,
                currency: payment.Currency,
                idempotencyKey: $"eshop-paypal-order-{payment.PaymentKey}");
            payment.SetPayPalOrderId(gatewayOrder.Id);
            await _paymentRepository.UpdateAsync(payment);
        }

        var authorization = vaultTokenId is not null
            ? await _paymentGateway.AuthorizeOrderWithVaultedCardAsync(payment.PayPalOrderId!, vaultTokenId, $"eshop-authorize-{payment.PaymentKey}")
            : await _paymentGateway.AuthorizeOrderWithCardAsync(payment.PayPalOrderId!, card!, $"eshop-authorize-{payment.PaymentKey}");

        if (authorization.Status != "CREATED" && authorization.Status != "PENDING")
        {
            throw new PaymentGatewayException(
                $"PayPal did not authorize the payment for order {orderId}; authorization status is {authorization.Status}.",
                gatewayErrorName: authorization.Status);
        }
        if (authorization.Amount != payment.Amount ||
            !string.Equals(authorization.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException(
                $"PayPal authorized {authorization.Amount} {authorization.Currency} for order {orderId}, which does not match the order total {payment.Amount} {payment.Currency}.");
        }

        payment.RecordAuthorization(authorization.Id, authorization.Status, authorization.ExpirationTime);
        order.MarkPaymentAuthorized();

        await _paymentRepository.UpdateAsync(payment);
        await _orderRepository.UpdateAsync(order);
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId)
    {
        var order = await GetOrderAsync(orderId);
        var payment = await GetPaymentAsync(orderId);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            // Idempotent replay: money was already taken.
            return payment;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || payment.Status != PaymentStatus.Authorized)
        {
            throw new OrderStateException($"Order {orderId} cannot be fulfilled while in state {order.Status} (payment: {payment.Status}).");
        }

        var authorization = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId!);

        if (authorization.Status == "VOIDED" || authorization.Status == "DENIED")
        {
            throw new PaymentStateException(
                $"PayPal authorization {authorization.Id} for order {orderId} is {authorization.Status} and cannot be captured. " +
                "Cancel this order and ask the shopper to place and pay a new one.");
        }

        var stale = authorization.ExpirationTime.HasValue &&
                    authorization.ExpirationTime.Value <= DateTimeOffset.UtcNow.Add(AuthorizationExpirySafetyMargin);
        if (stale)
        {
            GatewayAuthorization renewed;
            try
            {
                renewed = await _paymentGateway.ReauthorizeAsync(
                    authorization.Id, payment.Amount, payment.Currency, $"eshop-reauthorize-{payment.PaymentKey}");
            }
            catch (PaymentGatewayException ex)
            {
                throw new PaymentStateException(
                    $"PayPal authorization {authorization.Id} for order {orderId} has expired and could not be renewed " +
                    $"({ex.GatewayErrorName ?? ex.Message}). Cancel this order and ask the shopper to place and pay a new one.");
            }

            if (renewed.Status != "CREATED" && renewed.Status != "PENDING")
            {
                throw new PaymentStateException(
                    $"Renewed PayPal authorization {renewed.Id} for order {orderId} is {renewed.Status} and cannot be captured. " +
                    "Cancel this order and ask the shopper to place and pay a new one.");
            }

            payment.RecordAuthorization(renewed.Id, renewed.Status, renewed.ExpirationTime);
        }

        var capture = await _paymentGateway.CaptureAuthorizationAsync(
            payment.AuthorizationId!,
            payment.Amount,
            payment.Currency,
            invoiceId: $"eshop-order-{orderId}-{payment.PaymentKey:N}",
            idempotencyKey: $"eshop-capture-{payment.PaymentKey}");

        if (capture.Status != "COMPLETED" && capture.Status != "PENDING")
        {
            throw new PaymentGatewayException(
                $"PayPal capture for order {orderId} did not complete; capture status is {capture.Status}.",
                gatewayErrorName: capture.Status);
        }
        if (capture.Amount != payment.Amount ||
            !string.Equals(capture.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException(
                $"PayPal captured {capture.Amount} {capture.Currency} for order {orderId}, which does not match the order total {payment.Amount} {payment.Currency}.");
        }

        payment.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);

        var postCapture = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId!);
        payment.UpdateAuthorizationStatus(postCapture.Status, postCapture.ExpirationTime);

        order.MarkFulfilled();

        await _paymentRepository.UpdateAsync(payment);
        await _orderRepository.UpdateAsync(order);
        return payment;
    }

    public async Task<Payment?> CancelOrderAsync(int orderId)
    {
        var order = await GetOrderAsync(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId));

        if (order.Status == OrderStatus.Cancelled)
        {
            return payment;
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new OrderStateException($"Order {orderId} is already fulfilled; issue a refund instead of cancelling.");
        }

        if (payment?.AuthorizationId is not null && payment.Status == PaymentStatus.Authorized)
        {
            try
            {
                var voided = await _paymentGateway.VoidAuthorizationAsync(payment.AuthorizationId, $"eshop-void-{payment.PaymentKey}");
                payment.RecordVoid(voided.Status);
            }
            catch (PaymentGatewayException)
            {
                // The hold may already be gone at PayPal (expired/voided). Verify before releasing locally.
                var current = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId);
                if (current.Status != "VOIDED")
                {
                    throw;
                }
                payment.RecordVoid(current.Status);
            }
            await _paymentRepository.UpdateAsync(payment);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);
        return payment;
    }

    public async Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        if (idempotencyKey.Length > 100)
        {
            throw new ArgumentException("Idempotency key must be at most 100 characters.", nameof(idempotencyKey));
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId);
        var payment = await GetPaymentAsync(orderId);

        var existingHolder = await _paymentRepository.FirstOrDefaultAsync(new PaymentByRefundIdempotencyKeySpec(idempotencyKey));
        var existing = existingHolder?.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            if (existingHolder!.Id != payment.Id)
            {
                throw new DuplicateException($"Idempotency key '{idempotencyKey}' was already used for a refund on another payment.");
            }
            return existing;
        }

        if (order.Status != OrderStatus.Fulfilled ||
            payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentStateException($"Order {orderId} has no captured payment to refund (order: {order.Status}, payment: {payment.Status}).");
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0m || refundAmount > payment.RefundableAmount)
        {
            throw new PaymentStateException(
                $"Refund amount {refundAmount} {payment.Currency} is not refundable; refundable remainder is {payment.RefundableAmount} {payment.Currency}.");
        }

        // Scope the PayPal request id to this payment so the same caller key on a
        // different capture (or a reused key from an earlier environment/DB lifetime)
        // can never collide at PayPal, while repeats for this payment still replay.
        var refund = await _paymentGateway.RefundCaptureAsync(
            payment.CaptureId!, refundAmount, payment.Currency, $"eshop-refund-{payment.PaymentKey}-{idempotencyKey}", noteToPayer);

        if (refund.Amount > 0 && refund.Amount != refundAmount)
        {
            throw new PaymentGatewayException(
                $"PayPal refunded {refund.Amount} {refund.Currency} for order {orderId}, which does not match the requested {refundAmount} {payment.Currency}.");
        }

        var entity = payment.AddRefund(refund.Id, idempotencyKey, refund.Amount > 0 ? refund.Amount : refundAmount, refund.Status);
        await _paymentRepository.UpdateAsync(payment);
        return entity;
    }

    public async Task<IReadOnlyList<Order>> GetBuyerOrdersAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
    }

    public async Task<IReadOnlyList<Payment>> GetPaymentsForOrdersAsync(IEnumerable<int> orderIds)
    {
        return await _paymentRepository.ListAsync(new PaymentsByOrderIdsSpec(orderIds));
    }

    public async Task<Payment?> GetPaymentForOrderAsync(int orderId)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId));
    }

    private async Task<Order> GetOrderAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order is null)
        {
            throw new NotFoundException(orderId.ToString(), nameof(Order));
        }
        return order;
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId)
    {
        var order = await GetOrderAsync(orderId);
        if (order.BuyerId != buyerId)
        {
            // Do not leak the existence of another shopper's order.
            throw new NotFoundException(orderId.ToString(), nameof(Order));
        }
        return order;
    }

    private async Task<Payment> GetPaymentAsync(int orderId)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId));
        if (payment is null)
        {
            throw new NotFoundException(orderId.ToString(), nameof(Payment));
        }
        return payment;
    }
}
