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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IUriComposer _uriComposer;

    // PayPal retains idempotency keys for days, and the in-memory store restarts
    // order ids from 1 every run — so keys are namespaced per app run to stay
    // unique across runs while remaining stable for retries within a run.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IPayPalClient payPalClient,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _payPalClient = payPalClient;
        _uriComposer = uriComposer;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }

        foreach (var item in items)
        {
            Guard.Against.NegativeOrZero(item.Quantity, nameof(item.Quantity));
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).Distinct().ToArray()), cancellationToken);

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {item.CatalogItemId} does not exist.", nameof(items));
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Payment> PayOrderAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId, string currency, CancellationToken cancellationToken = default)
    {
        if (card is null == savedPaymentMethodId is null)
        {
            throw new ArgumentException("Provide either one-off card details or a saved payment method id, not both.");
        }

        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureOrderOwnership(order, buyerId);

        var existingPayment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (existingPayment is not null && existingPayment.IsAuthorized)
        {
            // Idempotent retry: the order is already paid (authorized).
            return existingPayment;
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new PaymentProcessingException($"Order {orderId} is not awaiting payment (status: {order.Status}).");
        }

        string? vaultTokenId = null;
        if (savedPaymentMethodId is not null)
        {
            var savedMethod = await _savedPaymentMethodRepository.GetByIdAsync(savedPaymentMethodId.Value, cancellationToken);
            if (savedMethod is null || savedMethod.BuyerId != buyerId)
            {
                throw new ArgumentException($"Saved payment method {savedPaymentMethodId} does not exist.", nameof(savedPaymentMethodId));
            }

            vaultTokenId = savedMethod.VaultTokenId;
        }

        var total = order.Total();
        // The invoice id must be unique per merchant account per transaction, so it
        // carries a per-attempt suffix; duplicate protection comes from the
        // idempotency keys, not from the invoice id.
        var payAttemptId = Guid.NewGuid().ToString("N")[..8];
        var payPalOrderId = await _payPalClient.CreateOrderAsync(
            total, currency,
            customId: order.Id.ToString(),
            invoiceId: $"ESHOP-{order.Id}-{payAttemptId}",
            idempotencyKey: $"eshop-{RunId}-paypal-order-{order.Id}-{payAttemptId}",
            cancellationToken);

        var authorization = vaultTokenId is not null
            ? await _payPalClient.AuthorizeOrderWithVaultedCardAsync(payPalOrderId, vaultTokenId, $"eshop-{RunId}-authorize-order-{order.Id}-{payAttemptId}", cancellationToken)
            : await _payPalClient.AuthorizeOrderWithCardAsync(payPalOrderId, card!, $"eshop-{RunId}-authorize-order-{order.Id}-{payAttemptId}", cancellationToken);

        if (authorization.Amount != total)
        {
            throw new PaymentProcessingException(
                $"PayPal authorized {authorization.Amount} but the order total is {total}; refusing to proceed with a mismatched hold.");
        }

        var payment = existingPayment ?? new Payment(order.Id, buyerId, currency, total);
        payment.SetAuthorization(authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);

        if (existingPayment is null)
        {
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkPaid();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, string currency, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        if (order.Status == OrderStatus.Fulfilled && payment is not null && payment.IsCaptured)
        {
            // Idempotent retry: already fulfilled and captured.
            return payment;
        }

        if (payment is null || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new PaymentProcessingException($"Order {orderId} has no payment to capture; the shopper must pay first.");
        }

        if (order.Status != OrderStatus.AwaitingFulfilment)
        {
            throw new PaymentProcessingException($"Order {orderId} cannot be fulfilled from status {order.Status}.");
        }

        var authorization = await _payPalClient.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        payment.SetAuthorizationStatus(authorization.Status, authorization.ExpiresAt);

        PayPalCapture capture;
        if (authorization.Status is "CREATED" or "PENDING")
        {
            if (authorization.ExpiresAt is not null && authorization.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                await RenewAuthorizationAsync(orderId, payment, currency, cancellationToken);
            }

            capture = await CaptureAsync(orderId, payment, currency, cancellationToken);
        }
        else if (authorization.Status is "CAPTURED" or "PARTIALLY_CAPTURED")
        {
            // PayPal already captured (e.g. we crashed after capturing but before saving).
            // Re-issuing the capture with the same idempotency key returns the original capture.
            capture = await _payPalClient.CaptureAuthorizationAsync(
                payment.AuthorizationId, payment.AuthorizedAmount, currency, $"eshop-{RunId}-capture-order-{orderId}", cancellationToken);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentProcessingException(
                $"The authorization for order {orderId} is {authorization.Status} at PayPal and cannot be captured. " +
                "Ask the shopper to pay for the order again before fulfilling it.");
        }

        if (capture.Status is "DECLINED" or "FAILED")
        {
            throw new PaymentProcessingException(
                $"PayPal declined the capture for order {orderId} (status: {capture.Status}). " +
                "Do not ship; ask the shopper to provide another payment method.");
        }

        if (capture.Amount != payment.AuthorizedAmount)
        {
            throw new PaymentProcessingException(
                $"PayPal captured {capture.Amount} for order {orderId} but the authorized amount is {payment.AuthorizedAmount}; " +
                "refusing to fulfil against a mismatched capture.");
        }

        payment.SetCapture(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            // Idempotent retry.
            return order;
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        if (payment is not null && payment.IsAuthorized)
        {
            await _payPalClient.VoidAuthorizationAsync(payment.AuthorizationId!, $"eshop-{RunId}-void-order-{orderId}", cancellationToken);
            payment.SetAuthorizationStatus("VOIDED", payment.AuthorizationExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<PaymentRefund> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, string? noteToPayer, string currency, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureOrderOwnership(order, buyerId);

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null || !payment.IsCaptured)
        {
            throw new PaymentProcessingException($"Order {orderId} has no captured payment to refund.");
        }

        var existingRefund = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existingRefund is not null)
        {
            // Idempotent retry under the same key: never refund twice.
            return existingRefund;
        }

        var remaining = payment.RemainingRefundable;
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new ArgumentException("The refund amount must be positive.", nameof(amount));
        }

        if (refundAmount > remaining)
        {
            throw new PaymentProcessingException(
                $"Cannot refund {refundAmount:0.00} {payment.Currency}: only {remaining:0.00} {payment.Currency} of the captured amount remains refundable.");
        }

        var refund = await _payPalClient.RefundCaptureAsync(
            payment.CaptureId!, refundAmount, currency, $"eshop-{RunId}-refund-{orderId}-{idempotencyKey}", noteToPayer, cancellationToken);

        var paymentRefund = payment.AddRefund(refund.RefundId, idempotencyKey, refund.Amount, refund.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkRefunded(payment.RemainingRefundable == 0m);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return paymentRefund;
    }

    private async Task<PayPalCapture> CaptureAsync(int orderId, Payment payment, string currency, CancellationToken cancellationToken)
    {
        try
        {
            return await _payPalClient.CaptureAuthorizationAsync(
                payment.AuthorizationId!, payment.AuthorizedAmount, currency, $"eshop-{RunId}-capture-order-{orderId}", cancellationToken);
        }
        catch (PayPalApiException)
        {
            // The hold may have gone stale between our status check and the capture;
            // renew it once, then retry the capture with the same idempotency key.
            await RenewAuthorizationAsync(orderId, payment, currency, cancellationToken);
            return await _payPalClient.CaptureAuthorizationAsync(
                payment.AuthorizationId!, payment.AuthorizedAmount, currency, $"eshop-{RunId}-capture-order-{orderId}", cancellationToken);
        }
    }

    private async Task RenewAuthorizationAsync(int orderId, Payment payment, string currency, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPalClient.ReauthorizeAsync(
                payment.AuthorizationId!, payment.AuthorizedAmount, currency, $"eshop-{RunId}-reauthorize-order-{orderId}", cancellationToken);
            payment.SetAuthorizationStatus(renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationNotRenewableException(
                $"The authorization for order {orderId} has expired and PayPal could not renew it " +
                "(authorizations can only be renewed within 29 days). " +
                "Ask the shopper to pay for the order again, then fulfil it.",
                ex.DebugId);
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        Guard.Against.Null(order, nameof(order), $"Order {orderId} does not exist.");
        return order;
    }

    private static void EnsureOrderOwnership(Order order, string buyerId)
    {
        if (order.BuyerId != buyerId)
        {
            // Deliberately indistinguishable from "does not exist" so one shopper
            // cannot probe another shopper's orders.
            throw new ArgumentException($"Order {order.Id} does not exist.");
        }
    }
}
