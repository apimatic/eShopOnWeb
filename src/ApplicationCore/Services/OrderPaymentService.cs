using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentGateway _gateway;
    private readonly PaymentGatewayOptions _options;
    private readonly IAppLogger<OrderPaymentService> _logger;
    private readonly OrderOperationLock _operationLock;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IUriComposer uriComposer,
        IPaymentGateway gateway,
        PaymentGatewayOptions options,
        IAppLogger<OrderPaymentService> logger,
        OrderOperationLock operationLock)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _options = options;
        _logger = logger;
        _operationLock = operationLock;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        if (items == null || items.Count == 0)
        {
            throw new PaymentStateException("An order must contain at least one item.");
        }
        if (items.Any(i => i.Units <= 0))
        {
            throw new PaymentStateException("Every order item must have a quantity of at least one.");
        }

        var catalogItemsSpecification = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification, cancellationToken);

        var missingIds = items.Select(i => i.CatalogItemId).Distinct().Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new PaymentStateException($"Unknown catalog item id(s): {string.Join(", ", missingIds)}.");
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Units);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Payment> PayOrderAsync(string buyerId, int orderId, GatewayCardDetails? card, int? savedCardId, CancellationToken cancellationToken = default)
    {
        using var _ = await _operationLock.AcquireAsync(orderId, cancellationToken);
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentWithRefundsByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent replay: the hold is already in place, just report it again.
        if (order.Status == OrderStatus.AwaitingFulfilment && payment?.Status == PaymentStatus.Authorized)
        {
            return payment;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentStateException($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        string? vaultTokenId = null;
        if (savedCardId.HasValue)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpecification(savedCardId.Value), cancellationToken);
            if (savedCard == null || savedCard.BuyerId != buyerId)
            {
                throw new PaymentStateException($"Saved card {savedCardId.Value} was not found for this shopper.");
            }
            vaultTokenId = savedCard.VaultTokenId;
        }
        else if (card == null)
        {
            throw new PaymentStateException("Provide either card details or a savedCardId to pay the order.");
        }

        if (payment == null)
        {
            payment = new Payment(orderId, buyerId, order.Total(), _options.Currency);
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }

        try
        {
            // A fresh PayPal-Request-Id per attempt: PayPal replays the stored result of a
            // reused key (including failures), while idempotency-in-effect is guaranteed by
            // the order/payment state machine above and the per-order lock.
            var authorization = await _gateway.AuthorizeAsync(
                payment.Amount,
                payment.Currency,
                referenceId: $"eshop-order-{orderId}",
                card,
                vaultTokenId,
                idempotencyKey: $"eshop-order-{orderId}-authorize-{Guid.NewGuid():N}",
                cancellationToken);

            payment.MarkAuthorized(
                authorization.ProcessorOrderId,
                authorization.AuthorizationId,
                authorization.Status,
                authorization.ExpiresAt);
            order.MarkPaymentAuthorized();
        }
        catch
        {
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw;
        }

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {orderId} authorized: payment {payment.Id}, authorization {payment.AuthorizationId}.");
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var _ = await _operationLock.AcquireAsync(orderId, cancellationToken);
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentWithRefundsByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent replay: money was already taken.
        if (order.Status == OrderStatus.Fulfilled && payment?.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }
        if (order.Status != OrderStatus.AwaitingFulfilment || payment == null)
        {
            throw new PaymentStateException($"Order {orderId} is {order.Status} and cannot be fulfilled; it must be paid first.");
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId == null)
        {
            throw new PaymentStateException($"Order {orderId} has no successful authorization to capture (payment state: {payment.Status}).");
        }

        var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        var renewable = authorization.Status == "CREATED" || authorization.Status == "PENDING";
        var stale = authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow;

        if (!renewable || stale)
        {
            try
            {
                var renewed = await _gateway.ReauthorizeAuthorizationAsync(
                    payment.AuthorizationId,
                    payment.Amount,
                    payment.Currency,
                    idempotencyKey: $"eshop-order-{orderId}-reauthorize-{Guid.NewGuid():N}",
                    cancellationToken);
                payment.MarkAuthorizationRenewed(renewed.Status, renewed.ExpiresAt);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                _logger.LogInformation($"Authorization {payment.AuthorizationId} for order {orderId} was stale and has been renewed.");
            }
            catch (PaymentGatewayException ex)
            {
                throw new AuthorizationRenewalException(
                    $"The PayPal authorization {payment.AuthorizationId} for order {orderId} is no longer valid " +
                    $"(status: {authorization.Status}, expired: {authorization.ExpiresAt}) and could not be renewed: {ex.Message} " +
                    $"Cancel the order, or ask the shopper to pay again so a fresh hold can be captured.");
            }
        }

        var capture = await _gateway.CaptureAuthorizationAsync(
            payment.AuthorizationId,
            payment.Amount,
            payment.Currency,
            idempotencyKey: $"eshop-order-{orderId}-capture-{Guid.NewGuid():N}",
            cancellationToken);

        if (capture.Status != "COMPLETED")
        {
            throw new PaymentGatewayException($"PayPal capture {capture.CaptureId} for order {orderId} returned status {capture.Status}; the order was not fulfilled.");
        }

        payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.Fee, capture.NetAmount);
        order.MarkFulfilled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {orderId} fulfilled: capture {payment.CaptureId}, gross {capture.GrossAmount} {capture.Currency}, fee {capture.Fee}, net {capture.NetAmount}.");
        return payment;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var _ = await _operationLock.AcquireAsync(orderId, cancellationToken);
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException(orderId);
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment?.Status == PaymentStatus.Authorized && payment.AuthorizationId != null)
        {
            await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, $"eshop-order-{orderId}-void-{Guid.NewGuid():N}", cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Authorization {payment.AuthorizationId} for cancelled order {orderId} was voided; no money moved.");
        }
        else if (payment?.Status == PaymentStatus.Pending)
        {
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<PaymentRefund> RefundOrderAsync(int orderId, string idempotencyKey, decimal? amount, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentStateException("An idempotencyKey is required to issue a refund.");
        }

        using var _ = await _operationLock.AcquireAsync(orderId, cancellationToken);
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentWithRefundsByOrderIdSpecification(orderId), cancellationToken);
        if (payment == null || payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentStateException($"Order {orderId} has no captured payment to refund (payment state: {payment?.Status.ToString() ?? "none"}).");
        }

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0 || refundAmount > payment.RefundableAmount)
        {
            throw new PaymentStateException(
                $"Refund of {refundAmount} {payment.Currency} is not possible; the refundable remainder on order {orderId} is {payment.RefundableAmount} {payment.Currency}.");
        }

        // The caller's key is the idempotency guarantee (enforced above via the stored
        // refunds); PayPal-Request-Id gets a per-attempt suffix because PayPal replays
        // the stored result of a reused key, including failures. The caller's key is
        // still sent to PayPal as custom_id for reconciliation.
        var result = await _gateway.RefundCaptureAsync(
            payment.CaptureId!,
            refundAmount,
            payment.Currency,
            $"{idempotencyKey}-{Guid.NewGuid():N}",
            customId: idempotencyKey,
            noteToPayer,
            cancellationToken);

        var refund = payment.AddRefund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation($"Order {orderId} refunded: refund {result.RefundId}, amount {result.Amount} {result.Currency}.");
        return refund;
    }

    public async Task<IReadOnlyList<OrderPaymentSummary>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerIdSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .Select(o => new OrderPaymentSummary(o, paymentsByOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    public async Task<(Order Order, Payment? Payment)?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            return null;
        }
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentWithRefundsByOrderIdSpecification(orderId), cancellationToken);
        return (order, payment);
    }
}
