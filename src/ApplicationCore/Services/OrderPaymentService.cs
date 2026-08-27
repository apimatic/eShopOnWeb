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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan AuthorizationExpirySafetyMargin = TimeSpan.FromMinutes(5);

    // Serializes payment state transitions per order within the process, so a concurrent
    // double-click can never pass the status check twice and authorize/capture twice.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _payPalSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalClient payPalClient,
        IUriComposer uriComposer,
        IOptions<PayPalSettings> payPalSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPalClient = payPalClient;
        _uriComposer = uriComposer;
        _payPalSettings = payPalSettings.Value;
    }

    private string Currency => _payPalSettings.Currency;

    public async Task<Order> CreateOrderAsync(string buyerId, Address shipToAddress, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (items is null || items.Count == 0)
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
        foreach (var line in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new ResourceNotFoundException($"Catalog item {line.CatalogItemId} does not exist.");
            }
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, CardPaymentSource? card, int? paymentMethodId, CancellationToken cancellationToken = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            return await PayCoreAsync(orderId, buyerId, card, paymentMethodId, cancellationToken);
        }
        finally
        {
            orderLock.Release();
        }
    }

    private async Task<Order> PayCoreAsync(int orderId, string buyerId, CardPaymentSource? card, int? paymentMethodId, CancellationToken cancellationToken)
    {
        if ((card is null) == (paymentMethodId is null))
        {
            throw new PaymentConflictException("Supply exactly one payment source: card details for a one-off payment, or a saved paymentMethodId.");
        }

        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        // Idempotency: a double-click on an already-authorized order returns the existing hold.
        if (order.Status == OrderStatus.AwaitingFulfilment && order.Payment is not null)
        {
            return order;
        }
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new PaymentConflictException($"Order {orderId} cannot be paid from status {order.Status}.");
        }

        string? vaultId = null;
        int? usedPaymentMethodId = null;
        string? cardBrand = null;
        string? cardLastDigits = null;

        if (paymentMethodId.HasValue)
        {
            var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdSpecification(paymentMethodId.Value), cancellationToken);
            if (paymentMethod is null || paymentMethod.BuyerId != buyerId)
            {
                throw new ResourceNotFoundException($"Payment method {paymentMethodId.Value} was not found.");
            }
            vaultId = paymentMethod.VaultTokenId;
            usedPaymentMethodId = paymentMethod.Id;
            cardBrand = paymentMethod.Brand;
            cardLastDigits = paymentMethod.LastDigits;
        }
        else if (card is not null)
        {
            cardLastDigits = card.Number.Length >= 4 ? card.Number[^4..] : null;
        }

        var total = order.Total();
        // PayPal requires invoice ids to be unique per merchant account; the in-memory
        // store recycles order ids across runs, so the invoice id carries a unique suffix.
        var invoiceId = $"eshop-order-{order.Id}-{Guid.NewGuid():N}";
        var payPalOrder = await _payPalClient.CreateOrderAsync(
            total, Currency,
            customId: order.Id.ToString(),
            invoiceId: invoiceId,
            requestId: $"eshop-order-{order.Id}-create-{invoiceId[^8..]}",
            cancellationToken);

        var authorization = await _payPalClient.AuthorizeOrderAsync(
            payPalOrder.Id, card, vaultId,
            requestId: $"eshop-order-{order.Id}-authorize-{invoiceId[^8..]}",
            cancellationToken);

        if (authorization.RequiresBuyerAction)
        {
            throw new PaymentRequiresBuyerActionException(
                "PayPal requires the shopper to approve this payment in a browser (e.g. a 3-D Secure challenge). " +
                "This server-to-server integration does not support an approval round-trip.");
        }

        cardBrand ??= authorization.CardBrand;
        cardLastDigits ??= authorization.CardLastDigits;

        if (authorization.AuthorizationId is null)
        {
            throw new PayPalApiException(System.Net.HttpStatusCode.BadGateway, null, null, null,
                $"PayPal did not return an authorization for order {orderId} (order status: {authorization.OrderStatus}).");
        }

        if (!string.Equals(authorization.AuthorizationStatus, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            var failedPayment = new OrderPayment(payPalOrder.Id, invoiceId, authorization.AuthorizationId,
                authorization.AuthorizationStatus ?? "UNKNOWN", total, Currency, null,
                usedPaymentMethodId, cardBrand, cardLastDigits);
            failedPayment.MarkAuthorizationFailed(authorization.AuthorizationStatus ?? "UNKNOWN");
            order.MarkAuthorized(failedPayment); // records the attempt; immediately marked failed below
            order.Cancel();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw new PaymentDeclinedException(
                $"PayPal declined the authorization for order {orderId} (status: {authorization.AuthorizationStatus}).");
        }

        var payment = new OrderPayment(payPalOrder.Id, invoiceId, authorization.AuthorizationId,
            authorization.AuthorizationStatus!, authorization.Amount ?? total, authorization.Currency ?? Currency,
            authorization.ExpirationTime, usedPaymentMethodId, cardBrand, cardLastDigits);
        order.MarkAuthorized(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            return await FulfilCoreAsync(orderId, cancellationToken);
        }
        finally
        {
            orderLock.Release();
        }
    }

    private async Task<Order> FulfilCoreAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order; // idempotent
        }
        if (order.Status != OrderStatus.AwaitingFulfilment || order.Payment?.AuthorizationId is null)
        {
            throw new PaymentConflictException($"Order {orderId} cannot be fulfilled from status {order.Status}.");
        }

        var payment = order.Payment;
        var total = order.Total();

        var authorization = await _payPalClient.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        payment.UpdateAuthorization(authorization.Id, authorization.Status, authorization.ExpirationTime);

        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw new PaymentConflictException(
                $"PayPal reports the authorization for order {orderId} as {authorization.Status}, so it cannot be captured. " +
                "Release the shopper by cancelling, or ask the shopper to pay again.");
        }

        var isStale = authorization.ExpirationTime.HasValue &&
                      authorization.ExpirationTime.Value <= DateTimeOffset.UtcNow.Add(AuthorizationExpirySafetyMargin);
        if (isStale)
        {
            try
            {
                authorization = await _payPalClient.ReauthorizeAsync(
                    authorization.Id, total, Currency,
                    requestId: $"eshop-reauthorize-{payment.InvoiceId}",
                    cancellationToken);
                payment.UpdateAuthorization(authorization.Id, authorization.Status, authorization.ExpirationTime);
            }
            catch (PayPalApiException ex) when (ex.IsClientError)
            {
                await _orderRepository.UpdateAsync(order, cancellationToken);
                throw new PaymentConflictException(
                    $"The authorization for order {orderId} went stale and PayPal could not renew it " +
                    $"({ex.Issue ?? ex.ErrorName ?? ex.Message}). The hold can no longer be renewed; " +
                    "cancel this order and ask the shopper to place and pay for a new one.");
            }
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPalClient.CaptureAuthorizationAsync(
                authorization.Id, total, Currency,
                invoiceId: payment.InvoiceId,
                requestId: $"eshop-capture-{payment.InvoiceId}",
                cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.IsClientError && !isStale)
        {
            // The hold may have gone stale without a reliable expiration hint: renew once and retry.
            try
            {
                var renewed = await _payPalClient.ReauthorizeAsync(
                    authorization.Id, total, Currency,
                    requestId: $"eshop-reauthorize-{payment.InvoiceId}",
                    cancellationToken);
                payment.UpdateAuthorization(renewed.Id, renewed.Status, renewed.ExpirationTime);
                capture = await _payPalClient.CaptureAuthorizationAsync(
                    renewed.Id, total, Currency,
                    invoiceId: payment.InvoiceId,
                    requestId: $"eshop-capture-{payment.InvoiceId}",
                    cancellationToken);
            }
            catch (PayPalApiException renewEx) when (renewEx.IsClientError)
            {
                await _orderRepository.UpdateAsync(order, cancellationToken);
                throw new PaymentConflictException(
                    $"The authorization for order {orderId} could not be captured or renewed " +
                    $"({renewEx.Issue ?? renewEx.ErrorName ?? renewEx.Message}). The hold can no longer be renewed; " +
                    "cancel this order and ask the shopper to place and pay for a new one.");
            }
        }

        if (string.Equals(capture.Status, "DECLINED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(capture.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw new PaymentDeclinedException($"PayPal declined the capture for order {orderId} (status: {capture.Status}).");
        }

        if (capture.Amount != total)
        {
            // The capture PayPal returned does not match the order total; do not trust it.
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw new PayPalApiException(System.Net.HttpStatusCode.BadGateway, null, null, null,
                $"PayPal captured {capture.Amount:0.00} {capture.Currency} for order {orderId}, which does not match the order total {total:0.00} {Currency}. Reconcile manually before retrying fulfilment.");
        }

        payment.MarkCaptured(capture.Id, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            return await CancelCoreAsync(orderId, cancellationToken);
        }
        finally
        {
            orderLock.Release();
        }
    }

    private async Task<Order> CancelCoreAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }

        if (order.Status == OrderStatus.AwaitingFulfilment && order.Payment?.AuthorizationId is not null
            && order.Payment.Status == PaymentStatus.Authorized)
        {
            // Release the shopper's held funds; no money ever moves.
            await _payPalClient.VoidAuthorizationAsync(
                order.Payment.AuthorizationId,
                requestId: $"eshop-void-{order.Payment.InvoiceId}",
                cancellationToken);
            order.Payment.MarkVoided();
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, decimal? amount, string idempotencyKey, string? note, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            return await RefundCoreAsync(orderId, amount, idempotencyKey, note, cancellationToken);
        }
        finally
        {
            orderLock.Release();
        }
    }

    private async Task<PaymentRefund> RefundCoreAsync(int orderId, decimal? amount, string idempotencyKey, string? note, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = order.Payment;

        if (order.Status != OrderStatus.Fulfilled || payment?.CaptureId is null)
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund (status: {order.Status}).");
        }

        // Idempotency: a repeated request under the same key returns the original refund.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refundable = payment.RefundableAmount;
        var refundAmount = Math.Round(amount ?? refundable, 2, MidpointRounding.AwayFromZero);
        if (refundAmount <= 0m)
        {
            throw new PaymentConflictException($"Order {orderId} has already been refunded in full; nothing remains refundable.");
        }
        if (refundAmount > refundable)
        {
            throw new PaymentConflictException(
                $"Cannot refund {refundAmount:0.00} {payment.Currency} against order {orderId}: " +
                $"only {refundable:0.00} {payment.Currency} of the captured {payment.CapturedAmount:0.00} remains refundable.");
        }

        var refund = await _payPalClient.RefundCaptureAsync(
            payment.CaptureId, refundAmount, payment.Currency, note,
            requestId: idempotencyKey,
            cancellationToken);

        var entity = payment.AddRefund(idempotencyKey, refund.Id, refund.Amount, refund.Status, note);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return entity;
    }

    public async Task<IReadOnlyList<Order>> ListOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new OrdersByBuyerSpecification(buyerId), cancellationToken);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }
}
