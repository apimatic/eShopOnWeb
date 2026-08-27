using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // PayPal authorizations are honored for a short window; renew when this close to expiry.
    private static readonly TimeSpan AuthorizationExpirySafetyMargin = TimeSpan.FromMinutes(5);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PayPalSettings _payPalSettings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedCard> savedCardRepository,
        IPaymentGateway paymentGateway,
        PayPalSettings payPalSettings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _payPalSettings = payPalSettings;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemLine> items, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Item quantities must be positive.", nameof(items));
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray()), cancellationToken);

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.", nameof(items));
            }
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? "eCatalog-item-default.png" : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, GatewayCard? card, int? savedCardId, CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent replay: the order is already authorized — return the current state.
        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment?.AuthorizationId is not null)
        {
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new OrderPaymentStateException($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        GatewayPaymentSource source;
        string? vaultTokenId = null;
        if (savedCardId.HasValue)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpec(savedCardId.Value), cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new Ardalis.GuardClauses.NotFoundException(savedCardId.Value.ToString(), nameof(SavedCard));
            }
            vaultTokenId = savedCard.VaultTokenId;
            source = GatewayPaymentSource.FromVaultToken(vaultTokenId);
        }
        else
        {
            if (card is null)
            {
                throw new ArgumentException("Either card details or a saved paymentMethodId must be supplied.");
            }
            source = GatewayPaymentSource.FromCard(card);
        }

        var amount = order.Total();
        var currency = _payPalSettings.Currency;

        var payment = order.Payment;
        if (payment is null)
        {
            // The invoice id must be unique per payment (PayPal rejects duplicates), so it
            // carries a random suffix; it is stored on the payment for reconciliation.
            var invoiceId = $"eshop-order-{order.Id}-{Guid.NewGuid():N}";
            var attemptId = Guid.NewGuid().ToString("N");

            var gatewayOrder = await _paymentGateway.CreateOrderAsync(
                amount, currency, $"eshop-order-{order.Id}", invoiceId, $"{attemptId}-create", cancellationToken);

            payment = new OrderPayment(order.Id, gatewayOrder.Id, invoiceId, attemptId, amount, currency);
            order.SetPayment(payment);
            // Persist the provider order id before authorizing, so a retry can resume.
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        var authorization = await _paymentGateway.AuthorizeOrderAsync(
            payment.PayPalOrderId, source, $"{payment.AttemptId}-authorize", cancellationToken);

        if (authorization.Status != "CREATED" && authorization.Status != "PENDING")
        {
            payment.MarkAuthorizationVoided(authorization.Status);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw new OrderPaymentStateException($"Payment for order {orderId} was not authorized (status {authorization.Status}).");
        }

        payment.MarkAuthorized(
            authorization.AuthorizationId,
            authorization.Status,
            authorization.Amount,
            authorization.ExpiryTime,
            cardBrand: authorization.CardBrand,
            cardLastDigits: authorization.CardLastDigits,
            vaultTokenId: vaultTokenId);
        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id} authorized: {authorization.Amount} {currency} held (authorization {authorization.AuthorizationId}).");
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpec(orderId), cancellationToken);
        Guard.Against.Null(order, nameof(order));

        // Idempotent replay.
        if (order.Status == OrderStatus.Fulfilled && order.Payment?.CaptureId is not null)
        {
            return order;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment?.AuthorizationId is null)
        {
            throw new OrderPaymentStateException($"Order {orderId} is {order.Status} and cannot be fulfilled.");
        }

        var payment = order.Payment;
        var authorization = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);

        if (authorization.Status != "CREATED" && authorization.Status != "PENDING")
        {
            payment.MarkAuthorizationVoided(authorization.Status);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw new OrderPaymentStateException(
                $"The authorization for order {orderId} is {authorization.Status} and cannot be captured. Cancel the order and ask the shopper to pay again.");
        }

        if (IsStale(authorization))
        {
            authorization = await RenewAuthorizationAsync(order, payment, authorization, cancellationToken);
        }

        var capture = await _paymentGateway.CaptureAuthorizationAsync(
            authorization.AuthorizationId, payment.AuthorizedAmount, payment.CurrencyCode,
            $"{payment.AttemptId}-capture", cancellationToken);

        if (capture.Status != "COMPLETED" && capture.Status != "PENDING")
        {
            throw new PaymentGatewayException(502, null,
                $"PayPal did not complete the capture for order {orderId} (status {capture.Status}). Retry the fulfilment.");
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.Amount, capture.Fee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id} fulfilled: captured {capture.Amount} {payment.CurrencyCode} (capture {capture.CaptureId}).");
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpec(orderId), cancellationToken);
        Guard.Against.Null(order, nameof(order));

        // Idempotent replay.
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment && order.Status != OrderStatus.PaymentAuthorized)
        {
            throw new OrderPaymentStateException(
                $"Order {orderId} is {order.Status} and can no longer be cancelled; issue a refund instead.");
        }

        if (order.Payment?.AuthorizationId is not null)
        {
            await _paymentGateway.VoidAuthorizationAsync(
                order.Payment.AuthorizationId, $"{order.Payment.AttemptId}-void", cancellationToken);
            order.Payment.MarkAuthorizationVoided("VOIDED");
            _logger.LogInformation($"Order {order.Id} cancelled: authorization {order.Payment.AuthorizationId} voided, held funds released.");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, PaymentRefund Refund)> RefundOrderAsync(string buyerId, bool isAdmin, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpec(orderId), cancellationToken);
        if (order is null || (order.BuyerId != buyerId && !isAdmin))
        {
            throw new Ardalis.GuardClauses.NotFoundException(orderId.ToString(), nameof(Order));
        }

        var payment = order.Payment;
        if (payment?.CaptureId is null ||
            (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded && order.Status != OrderStatus.Refunded))
        {
            throw new OrderPaymentStateException($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotent replay under the caller-supplied key.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return (order, existing);
        }

        var refundable = payment.RefundableAmount;
        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0)
        {
            throw new OrderPaymentStateException($"Order {orderId} is already fully refunded.");
        }
        if (refundAmount > refundable)
        {
            throw new OrderPaymentStateException(
                $"Refund of {refundAmount:0.00} exceeds the refundable remainder of {refundable:0.00} {payment.CurrencyCode} on order {orderId}.");
        }

        // Anchor the provider idempotency key to this payment so caller keys stay
        // unique per capture even on a shared provider account. PayPal allows 108 chars.
        var gatewayIdempotencyKey = $"{payment.AttemptId}-refund-{idempotencyKey}";
        if (gatewayIdempotencyKey.Length > 108)
        {
            gatewayIdempotencyKey = gatewayIdempotencyKey[..108];
        }

        var refund = await _paymentGateway.RefundCaptureAsync(
            payment.CaptureId, refundAmount, payment.CurrencyCode, gatewayIdempotencyKey, cancellationToken);

        var record = payment.AddRefund(refund.RefundId, idempotencyKey, refund.Amount, refund.Status);
        order.UpdateRefundState();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id} refunded {refund.Amount} {payment.CurrencyCode} (refund {refund.RefundId}).");
        return (order, record);
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            // Don't leak the existence of another shopper's order.
            throw new Ardalis.GuardClauses.NotFoundException(orderId.ToString(), nameof(Order));
        }
        return order;
    }

    private bool IsStale(GatewayAuthorization authorization)
        => authorization.ExpiryTime.HasValue
           && authorization.ExpiryTime.Value <= DateTimeOffset.UtcNow + AuthorizationExpirySafetyMargin;

    private async Task<GatewayAuthorization> RenewAuthorizationAsync(Order order, OrderPayment payment, GatewayAuthorization authorization, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(
                authorization.AuthorizationId, payment.AuthorizedAmount, payment.CurrencyCode,
                $"{payment.AttemptId}-reauthorize-{payment.ReauthorizationCount + 1}", cancellationToken);

            payment.MarkReauthorized(renewed.AuthorizationId, renewed.Status, renewed.Amount, renewed.ExpiryTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Order {order.Id}: stale authorization renewed as {renewed.AuthorizationId}.");
            return renewed;
        }
        catch (PaymentGatewayException ex) when (ex.IsClientError)
        {
            _logger.LogWarning($"Order {order.Id}: authorization {authorization.AuthorizationId} could not be renewed ({ex.ErrorName}: {ex.Message}).");
            throw new OrderPaymentStateException(
                $"The PayPal authorization for order {order.Id} expired and can no longer be renewed ({ex.ErrorName ?? "rejected"}). " +
                "Cancel this order and ask the shopper to place and pay for a new one.");
        }
    }
}
