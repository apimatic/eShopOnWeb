using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the order payment lifecycle: authorize at checkout, capture at
/// fulfilment, void on cancel, refund on return. All provider state lives on the
/// Payment aggregate so any later request can act on it.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
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
        IOptions<PayPalSettings> payPalSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _payPalSettings = payPalSettings.Value;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new PaymentConflictException("An order requires at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentConflictException("Item quantities must be positive.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray()), ct);

        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
            if (catalogItem is null)
            {
                throw new PaymentConflictException($"Catalog item {item.CatalogItemId} does not exist.");
            }
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);
        return order;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, GatewayCardDetails? card, int? savedCardId, CancellationToken ct = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, ct);

        // Idempotent in effect: a repeated pay call on an authorized order is a no-op.
        if (order.Status == OrderStatus.Authorized && order.Payment?.AuthorizationId is not null)
        {
            return order.Payment;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentConflictException($"Order {orderId} is {order.Status} and cannot be paid.");
        }
        if (card is null && savedCardId is null)
        {
            throw new PaymentConflictException("Provide either card details or a saved card id.");
        }

        string? paymentTokenId = null;
        if (savedCardId is not null)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId.Value, ct);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new EntityNotFoundException($"Saved card {savedCardId} was not found.");
            }
            paymentTokenId = savedCard.PaymentTokenId;
        }

        var total = order.Total();
        var currency = _payPalSettings.Currency;
        // Unique per payment attempt: PayPal refuses duplicate invoice ids.
        var invoiceId = $"order-{order.Id}-{Guid.NewGuid():N}";
        var request = new GatewayAuthorizeRequest(
            total,
            currency,
            invoiceId,
            IdempotencyKey: $"authorize-order-{order.Id}",
            card,
            paymentTokenId);

        var payment = order.Payment ?? new Payment(order.Id, buyerId, currency);
        try
        {
            var authorization = await _paymentGateway.AuthorizeAsync(request, ct);
            payment.MarkAuthorized(
                invoiceId,
                authorization.PayPalOrderId,
                authorization.AuthorizationId,
                authorization.Status,
                authorization.Amount,
                authorization.ExpiresAt,
                savedCardId);
        }
        catch (PaymentDeclinedException ex)
        {
            payment.MarkDeclined(null);
            await SavePaymentAsync(payment, ct);
            throw new PaymentDeclinedException($"Payment for order {orderId} was declined: {ex.Message}");
        }

        await SavePaymentAsync(payment, ct);
        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, ct);
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var order = await GetOrderAsync(orderId, ct);
        var payment = order.Payment;

        // Idempotent: fulfilling an already-captured order returns the recorded capture.
        if (payment?.Status == PaymentStatus.Captured || payment?.Status == PaymentStatus.PartiallyRefunded)
        {
            return payment;
        }
        if (order.Status != OrderStatus.Authorized || payment?.AuthorizationId is null)
        {
            throw new PaymentConflictException($"Order {orderId} is {order.Status} and cannot be fulfilled.");
        }

        var authorizationId = payment.AuthorizationId;
        var state = await _paymentGateway.GetAuthorizationAsync(authorizationId, ct);
        if (IsExpired(state.Status))
        {
            // The hold went stale before fulfilment: renew it rather than failing outright.
            state = await RenewAuthorizationAsync(order, payment, state, ct);
            authorizationId = state.AuthorizationId;
        }

        GatewayCapture capture;
        try
        {
            capture = await _paymentGateway.CaptureAsync(authorizationId, $"capture-order-{order.Id}", ct);
        }
        catch (PaymentGatewayException ex) when (ex.IsProviderRejection)
        {
            // One retry path: the hold may have gone stale between the status check and the capture.
            var refreshed = await _paymentGateway.GetAuthorizationAsync(authorizationId, ct);
            if (!IsExpired(refreshed.Status))
            {
                throw;
            }
            var renewed = await RenewAuthorizationAsync(order, payment, refreshed, ct);
            capture = await _paymentGateway.CaptureAsync(renewed.AuthorizationId, $"capture-order-{order.Id}-renewed", ct);
        }

        if (string.Equals(capture.Status, "DECLINED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentDeclinedException($"Capture for order {orderId} was declined by PayPal.");
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PaypalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);
        return payment;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await GetOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentConflictException($"Order {orderId} is already fulfilled; issue a refund instead of cancelling.");
        }

        var payment = order.Payment;
        if (payment?.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            var voided = await _paymentGateway.VoidAsync(payment.AuthorizationId, $"void-order-{order.Id}", ct);
            payment.MarkVoided(voided.Status);
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await GetOwnedOrderAsync(buyerId, orderId, ct);
        var payment = order.Payment;

        if (payment?.CaptureId is null || payment.CapturedAmount is null)
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
        }

        // Caller-supplied idempotency: a repeated key returns the original refund.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            if (existing.Status == RefundStatuses.Failed)
            {
                // The provider never created this refund; the key may be retried.
                payment.RemoveRefund(existing);
                await _paymentRepository.UpdateAsync(payment, ct);
            }
            else if (existing.PayPalRefundId is null)
            {
                throw new PaymentConflictException(
                    $"A refund with idempotency key '{idempotencyKey}' was already submitted for order {orderId}, " +
                    "but its outcome at PayPal could not be confirmed. It was NOT retried, so no double refund can occur. " +
                    "Check the payment state (e.g. via reconciliation) before issuing a new key.");
            }
            else
            {
                return existing;
            }
        }

        var refundable = payment.RefundableAmount();
        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0m || refundAmount > refundable)
        {
            throw new PaymentConflictException(
                $"Refund amount {refundAmount:F2} exceeds the refundable balance {refundable:F2} on order {orderId}.");
        }

        // Write-ahead: record the refund before calling the provider, so a lost
        // provider response can never lead to a second refund under the same key.
        var refund = payment.BeginRefund(refundAmount, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, ct);

        // A full refund of the whole capture goes out with an empty amount (provider convention).
        var isFullRefund = refundAmount == payment.CapturedAmount.Value && payment.TotalRefunded() == refundAmount;
        try
        {
            var result = await _paymentGateway.RefundAsync(
                payment.CaptureId,
                isFullRefund ? null : refundAmount,
                payment.Currency,
                idempotencyKey,
                ct);
            payment.CompleteRefund(refund, result.RefundId, result.Status);
        }
        catch (PaymentGatewayException ex) when (ex.ProviderErrorName == "DUPLICATE_REQUEST_ID")
        {
            // PayPal already saw this key: the refund was submitted but its response was lost.
            payment.FailRefund(refund, RefundStatuses.SubmittedUnknown);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentConflictException(
                $"A refund with idempotency key '{idempotencyKey}' was already submitted to PayPal for order {orderId}. " +
                "It was not submitted again. Check the payment state before issuing a new key.");
        }
        catch
        {
            payment.FailRefund(refund, RefundStatuses.Failed);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }

        await _paymentRepository.UpdateAsync(payment, ct);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentsSpecification(buyerId), ct);
    }

    private async Task<GatewayAuthorizationState> RenewAuthorizationAsync(Order order, Payment payment, GatewayAuthorizationState state, CancellationToken ct)
    {
        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(
                state.AuthorizationId,
                payment.AuthorizedAmount,
                payment.Currency,
                $"reauthorize-order-{order.Id}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                ct);
            payment.MarkAuthorizationRenewed(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            return renewed;
        }
        catch (PaymentGatewayException)
        {
            payment.MarkAuthorizationStatus(state.Status);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentConflictException(
                $"The PayPal authorization for order {order.Id} expired and can no longer be renewed " +
                "(authorizations can only be renewed between day 4 and day 29 after approval). " +
                "Cancel this order and ask the shopper to place and pay for a new one.");
        }
    }

    private static bool IsExpired(string? status) =>
        string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase);

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
        if (order is null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);
        if (order.BuyerId != buyerId)
        {
            // Do not leak the existence of another shopper's order.
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task SavePaymentAsync(Payment payment, CancellationToken ct)
    {
        if (payment.Id == 0)
        {
            await _paymentRepository.AddAsync(payment, ct);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment, ct);
        }
    }
}
