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
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Default implementation of the order payment lifecycle. Holds the business rules;
/// all processor interaction goes through IPaymentProcessor. Idempotency is enforced
/// in effect: state checks make repeats no-ops, and deterministic idempotency keys are
/// passed to the processor so network retries never double-charge.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShippingAddress = new Address("N/A", "N/A", "N/A", "N/A", "N/A");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentProcessor _paymentProcessor;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IUriComposer uriComposer,
        IPaymentProcessor paymentProcessor,
        IOptions<PaymentOptions> paymentOptions)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _uriComposer = uriComposer;
        _paymentProcessor = paymentProcessor;
        _currency = paymentOptions.Value.Currency;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items,
        Address? shippingAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new PaymentRequestValidationException("An order must contain at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentRequestValidationException("Item quantities must be greater than zero.");
        }

        var catalogItemsSpec = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpec, cancellationToken);

        var missingIds = items.Select(i => i.CatalogItemId).Distinct()
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new PaymentRequestValidationException($"Unknown catalog item id(s): {string.Join(", ", missingIds)}.");
        }

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress ?? DefaultShippingAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderPayment> PayOrderAsync(string buyerId, int orderId, PaymentSourceSelection source,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentStateConflictException($"Order {orderId} is cancelled and cannot be paid.");
        }

        int? savedPaymentMethodId = null;
        PaymentSourceSelection processorSource = source;
        if (source is PaymentSourceSelection.SavedCard savedCard)
        {
            var savedMethod = await _savedPaymentMethodRepository.GetByIdAsync(savedCard.SavedPaymentMethodId, cancellationToken);
            if (savedMethod is null || savedMethod.BuyerId != buyerId)
            {
                throw new NotFoundException($"Payment method {savedCard.SavedPaymentMethodId} was not found.");
            }
            savedPaymentMethodId = savedMethod.Id;
            processorSource = new PaymentSourceSelection.VaultedCardToken(savedMethod.PayPalVaultTokenId);
        }
        else if (source is PaymentSourceSelection.OneOffCard oneOff)
        {
            ValidateCard(oneOff.Card);
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is not null)
        {
            switch (payment.Status)
            {
                case OrderPaymentStatus.Authorized:
                    // Idempotent repeat: the hold already exists, return it unchanged.
                    return payment;
                case OrderPaymentStatus.Captured:
                    throw new PaymentStateConflictException($"Order {orderId} has already been paid and captured.");
                default:
                    // Failed / voided / abandoned attempt: start a fresh authorization cycle.
                    payment.BeginNewAuthorizationAttempt();
                    break;
            }
        }
        else
        {
            payment = OrderPayment.CreatePending(order.Id, buyerId, order.Total(), _currency);
        }

        try
        {
            var authorization = await _paymentProcessor.AuthorizeAsync(payment.Amount, payment.Currency,
                processorSource, $"eshop-order-{order.Id}", AuthorizationInvoiceId(order.Id, payment.PaymentReference),
                payment.PaymentReference, cancellationToken);

            if (authorization.Status is not "CREATED" and not "PENDING")
            {
                payment.MarkAuthorizationFailed($"Authorization status {authorization.Status}");
                await SavePaymentAsync(payment, cancellationToken);
                throw new PaymentDeclinedException(
                    $"The payment was declined (authorization status {authorization.Status}). No money was taken.");
            }

            payment.MarkAuthorized(authorization.ProcessorOrderId, authorization.AuthorizationId, authorization.Status,
                authorization.ExpiresAt, authorization.CardBrand, authorization.CardLastDigits, savedPaymentMethodId);
            order.MarkPaymentAuthorized();
        }
        catch (PaymentProcessorException ex) when (ex.ProcessorStatusCode is >= 400 and < 500)
        {
            payment.MarkAuthorizationFailed(ex.Message);
            await SavePaymentAsync(payment, cancellationToken);
            throw new PaymentDeclinedException($"The payment was declined: {ex.Message}");
        }

        await SavePaymentAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<OrderPayment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentStateConflictException($"Order {orderId} has no payment; it cannot be fulfilled.");
        }
        if (order.Status == OrderStatus.Fulfilled && payment.Status == OrderPaymentStatus.Captured)
        {
            // Idempotent repeat of a completed fulfilment.
            return payment;
        }
        if (payment.Status != OrderPaymentStatus.Authorized || payment.PayPalAuthorizationId is null)
        {
            throw new PaymentStateConflictException(
                $"Order {orderId} cannot be fulfilled because its payment is {payment.Status}; expected an active authorization.");
        }

        var authorization = await _paymentProcessor.GetAuthorizationAsync(payment.PayPalAuthorizationId, cancellationToken);
        var freshness = await EnsureFreshAuthorizationAsync(payment, authorization, cancellationToken);

        var invoiceId = CaptureInvoiceId(orderId, payment.PaymentReference);
        ProcessorCapture capture;
        try
        {
            capture = await _paymentProcessor.CaptureAsync(payment.PayPalAuthorizationId!, payment.Amount,
                payment.Currency, invoiceId, CaptureIdempotencyKey(payment), cancellationToken);
        }
        catch (PaymentProcessorException ex) when (!freshness.renewed && ex.ProcessorStatusCode is >= 400 and < 500)
        {
            // The hold may have gone stale between the check and the capture: renew once, then retry the capture.
            var renewed = await TryRenewAuthorizationAsync(payment, cancellationToken);
            if (!renewed)
            {
                throw NotRenewable(orderId, payment, ex);
            }
            capture = await _paymentProcessor.CaptureAsync(payment.PayPalAuthorizationId!, payment.Amount,
                payment.Currency, invoiceId, CaptureIdempotencyKey(payment), cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.Fee, capture.NetAmount, capture.CapturedAt);
        order.MarkFulfilled();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<OrderPayment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            // Idempotent repeat.
            return payment;
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentStateConflictException(
                $"Order {orderId} has already been fulfilled and can no longer be cancelled; issue a refund instead.");
        }

        if (payment is not null && payment.Status == OrderPaymentStatus.Authorized && payment.PayPalAuthorizationId is not null)
        {
            await _paymentProcessor.VoidAuthorizationAsync(payment.PayPalAuthorizationId,
                $"eshop-voi-{payment.PaymentReference}", cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<RefundResult> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey, string? note,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new NotFoundException($"Order {orderId} has no payment to refund.");
        }

        // Idempotency: a repeat under the same key returns the original refund.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return new RefundResult(existing, payment);
        }

        if (payment.Status != OrderPaymentStatus.Captured || payment.PayPalCaptureId is null)
        {
            throw new PaymentStateConflictException(
                $"Order {orderId} cannot be refunded because its payment is {payment.Status}; only captured payments can be refunded.");
        }

        var remaining = payment.RemainingRefundable();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new PaymentStateConflictException($"Order {orderId} has already been refunded in full.");
        }
        if (refundAmount > remaining)
        {
            throw new PaymentStateConflictException(
                $"Cannot refund {refundAmount:0.00} {payment.Currency}: only {remaining:0.00} {payment.Currency} of the captured amount remains refundable.");
        }

        var refund = await _paymentProcessor.RefundCaptureAsync(payment.PayPalCaptureId, refundAmount, payment.Currency,
            RefundInvoiceId(orderId, idempotencyKey), note, idempotencyKey, cancellationToken);

        var entity = payment.AddRefund(idempotencyKey, refund.RefundId, refund.Status, refund.Amount, note);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return new RefundResult(entity, payment);
    }

    public async Task<IReadOnlyList<OrderWithPayment>> ListOrdersWithPaymentsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = orderIds.Length == 0
            ? new List<OrderPayment>()
            : await _paymentRepository.ListAsync(new OrderPaymentsByOrderIdsSpec(orderIds), cancellationToken);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderWithPayment(o, payments.FirstOrDefault(p => p.OrderId == o.Id)))
            .ToList();
    }

    /// <summary>
    /// Captures against a fresh hold. If the authorization has gone stale, renews it first.
    /// Throws AuthorizationNotRenewableException when the hold can no longer be renewed.
    /// </summary>
    private async Task<(bool renewed, ProcessorAuthorizationState state)> EnsureFreshAuthorizationAsync(
        OrderPayment payment, ProcessorAuthorizationState authorization, CancellationToken cancellationToken)
    {
        var stale = authorization.Status is not "CREATED" and not "PENDING"
                    || (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow);
        if (!stale)
        {
            return (false, authorization);
        }

        if (!await TryRenewAuthorizationAsync(payment, cancellationToken))
        {
            throw NotRenewable(payment.OrderId, payment, null);
        }
        return (true, await _paymentProcessor.GetAuthorizationAsync(payment.PayPalAuthorizationId!, cancellationToken));
    }

    private async Task<bool> TryRenewAuthorizationAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _paymentProcessor.ReauthorizeAsync(payment.PayPalAuthorizationId!, payment.Amount,
                payment.Currency, $"eshop-rea-{payment.PaymentReference}-{payment.RenewalCount + 1}", cancellationToken);
            payment.MarkAuthorizationRenewed(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return true;
        }
        catch (PaymentProcessorException ex) when (ex.ProcessorStatusCode is >= 400 and < 500)
        {
            return false;
        }
    }

    private static AuthorizationNotRenewableException NotRenewable(int orderId, OrderPayment payment, Exception? inner)
    {
        var detail = inner is null ? string.Empty : $" Processor reported: {inner.Message}";
        return new AuthorizationNotRenewableException(
            $"The authorization for order {orderId} has expired and can no longer be renewed, so the order cannot be fulfilled. " +
            $"Void any remaining hold, ask the shopper to pay again (POST /api/orders/{orderId}/pay), then fulfil the order.{detail}");
    }

    private async Task SavePaymentAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        if (payment.Id == 0)
        {
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
    }

    // PayPal enforces unique invoice ids per transaction, so each step gets its own
    // deterministic-but-unique invoice id; the stable custom_id carries the order link.
    private static string AuthorizationInvoiceId(int orderId, string paymentReference)
        => TruncateInvoiceId($"ESHOP-ORDER-{orderId}-AUTH-{paymentReference}");

    private static string CaptureInvoiceId(int orderId, string paymentReference)
        => TruncateInvoiceId($"ESHOP-ORDER-{orderId}-CAP-{paymentReference}");

    private static string RefundInvoiceId(int orderId, string idempotencyKey)
        => TruncateInvoiceId($"ESHOP-ORDER-{orderId}-RFD-{idempotencyKey}");

    private static string TruncateInvoiceId(string invoiceId)
        => invoiceId.Length <= 127 ? invoiceId : invoiceId[..127];

    private static string CaptureIdempotencyKey(OrderPayment payment)
        => $"eshop-cap-{payment.PaymentReference}-{payment.RenewalCount}";

    private static void ValidateCard(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentRequestValidationException("Card number and expiry are required.");
        }
    }
}
