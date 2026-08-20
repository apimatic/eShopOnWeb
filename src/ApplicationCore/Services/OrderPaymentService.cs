using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShipToAddress =
        new("123 Main Street", "Seattle", "WA", "US", "98101");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentSettings _paymentSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        IPaymentSettings paymentSettings)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _paymentSettings = paymentSettings;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required to place an order.", 401);
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.");
        }

        var grouped = request.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (grouped.Any(i => i.Quantity <= 0))
        {
            throw new PaymentException("Each item quantity must be greater than zero.");
        }

        var ids = grouped.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !catalogById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentException($"Catalog item(s) not found: {string.Join(", ", missing)}.", 404);
        }

        var orderItems = grouped.Select(item =>
        {
            var catalogItem = catalogById[item.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var address = request.ShipToAddress ?? DefaultShipToAddress;
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(PayOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(request.OrderId, cancellationToken);
        order.EnsureOwnedBy(request.BuyerId);

        if (order.IsAlreadyAuthorized())
        {
            return order;
        }

        order.EnsurePayable();

        if (order.Total() <= 0)
        {
            throw new PaymentException("The order total must be greater than zero.");
        }

        if (request.Card == null && request.PaymentMethodId == null)
        {
            throw new PaymentException("Provide card details or a saved paymentMethodId.");
        }

        if (request.Card != null && request.PaymentMethodId != null)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.");
        }

        var payment = order.EnsurePayment(_paymentSettings.Currency);
        var invoiceId = payment.EnsureInvoiceId();
        var customId = invoiceId;
        var idempotencyKey = $"pay-{order.Id}-{payment.EnsureGatewayRequestId()}";
        await _orderRepository.UpdateAsync(order, cancellationToken);

        AuthorizationResult authorization;
        if (request.PaymentMethodId != null)
        {
            var saved = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpecification(request.PaymentMethodId.Value, request.BuyerId),
                cancellationToken);
            if (saved == null)
            {
                throw new PaymentException("The saved card was not found or is not available to this shopper.", 404);
            }

            authorization = await _paymentGateway.AuthorizeVaultedCardAsync(
                invoiceId,
                customId,
                order.Total(),
                _paymentSettings.Currency,
                saved.PayPalPaymentTokenId,
                idempotencyKey,
                cancellationToken);
        }
        else
        {
            authorization = await _paymentGateway.AuthorizeCardAsync(
                invoiceId,
                customId,
                order.Total(),
                _paymentSettings.Currency,
                request.Card!,
                idempotencyKey,
                cancellationToken);
        }

        if (!string.Equals(authorization.Currency, _paymentSettings.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException("PayPal authorized the hold in a different currency than configured.");
        }

        if (authorization.AuthorizedAmount != decimal.Round(order.Total(), 2))
        {
            throw new PaymentException(
                $"PayPal held {authorization.AuthorizedAmount} {_paymentSettings.Currency} but the order total is {decimal.Round(order.Total(), 2)} {_paymentSettings.Currency}.");
        }

        payment.RecordPayPalOrder(authorization.PayPalOrderId, authorization.PayPalOrderStatus, invoiceId);
        payment.RecordAuthorization(
            authorization.AuthorizationId,
            authorization.AuthorizationStatus,
            authorization.AuthorizedAmount,
            authorization.ExpirationTime,
            DateTimeOffset.UtcNow);
        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);

        if (order.IsAlreadyFulfilled())
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId == null)
        {
            throw new PaymentException($"Order {orderId} cannot be fulfilled until a payment hold is in place.", 409);
        }

        var payment = order.Payment;
        var authorizationId = payment.AuthorizationId!;
        var amount = decimal.Round(order.Total(), 2);
        var invoiceId = payment.InvoiceId ?? payment.EnsureInvoiceId();

        authorizationId = await EnsureFreshAuthorizationAsync(payment, authorizationId, amount, cancellationToken);

        CaptureResult capture;
        try
        {
            capture = await _paymentGateway.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                _paymentSettings.Currency,
                invoiceId,
                $"fulfil-{order.Id}-{payment.EnsureGatewayRequestId()}",
                cancellationToken);
        }
        catch (PaymentException ex) when (IsExpiredAuthorization(ex))
        {
            authorizationId = await RenewAuthorizationOrThrowAsync(payment, authorizationId, amount, cancellationToken);
            capture = await _paymentGateway.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                _paymentSettings.Currency,
                invoiceId,
                $"fulfil-{order.Id}-{payment.EnsureGatewayRequestId()}-retry",
                cancellationToken);
        }

        payment.RecordCapture(
            capture.CaptureId,
            capture.CaptureStatus,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetProceeds,
            DateTimeOffset.UtcNow);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);

        if (order.IsAlreadyCancelled())
        {
            return order;
        }

        order.EnsureCancellable();

        if (order.Payment?.AuthorizationId != null)
        {
            await _paymentGateway.VoidAuthorizationAsync(
                order.Payment.AuthorizationId,
                $"cancel-{order.Id}-{order.Payment.GatewayRequestId ?? order.Id.ToString()}",
                cancellationToken);
            order.Payment.RecordVoid("VOIDED");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(RefundOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException("An idempotencyKey is required for refunds.");
        }

        var order = await GetRequiredOrderAsync(request.OrderId, cancellationToken);
        order.EnsureOwnedBy(request.BuyerId);

        if (order.Payment?.CaptureId == null ||
            order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw new PaymentException($"Order {request.OrderId} has no captured payment to refund.", 409);
        }

        var existing = order.Payment.FindRefundByIdempotencyKey(request.IdempotencyKey);
        if (existing != null)
        {
            return (order, existing);
        }

        if (order.Status == OrderStatus.Refunded || order.Payment.RefundableRemaining <= 0.001m)
        {
            throw new PaymentException("This order has already been refunded in full.", 409);
        }

        var remaining = order.Payment.RefundableRemaining;
        var amount = request.Amount.HasValue ? decimal.Round(request.Amount.Value, 2) : remaining;
        if (amount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }

        if (amount > remaining)
        {
            throw new PaymentException(
                $"Refund of {amount} exceeds the remaining captured amount of {remaining}.");
        }

        var result = await _paymentGateway.RefundCaptureAsync(
            order.Payment.CaptureId,
            request.Amount.HasValue ? amount : null,
            _paymentSettings.Currency,
            request.IdempotencyKey,
            cancellationToken);

        var refund = order.Payment.AddRefund(result.PayPalRefundId, result.Amount, result.Status, request.IdempotencyKey);
        order.MarkRefunded();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            return null;
        }

        order.EnsureOwnedBy(buyerId);
        return order;
    }

    private async Task<Order> GetRequiredOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    private async Task<string> EnsureFreshAuthorizationAsync(
        OrderPayment payment,
        string authorizationId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        try
        {
            var (status, expiration) = await _paymentGateway.GetAuthorizationAsync(authorizationId, cancellationToken);
            payment.RecordAuthorization(
                authorizationId,
                status,
                payment.AuthorizedAmount ?? amount,
                expiration ?? payment.AuthorizationExpiration,
                payment.AuthorizedAt ?? DateTimeOffset.UtcNow);

            if (string.Equals(status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "DENIED", StringComparison.OrdinalIgnoreCase))
            {
                throw new AuthorizationUnrenewableException(
                    $"The payment hold is {status} and cannot be captured. Ask the shopper to pay again.");
            }

            if (IsAuthorizationStale(payment, expiration))
            {
                return await RenewAuthorizationOrThrowAsync(payment, authorizationId, amount, cancellationToken);
            }

            return authorizationId;
        }
        catch (AuthorizationUnrenewableException)
        {
            throw;
        }
        catch (PaymentException ex) when (IsExpiredAuthorization(ex))
        {
            return await RenewAuthorizationOrThrowAsync(payment, authorizationId, amount, cancellationToken);
        }
    }

    private async Task<string> RenewAuthorizationOrThrowAsync(
        OrderPayment payment,
        string authorizationId,
        decimal amount,
        CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(
                authorizationId,
                amount,
                _paymentSettings.Currency,
                $"reauth-{payment.OrderId}-{payment.EnsureGatewayRequestId()}",
                cancellationToken);

            payment.RecordReauthorization(
                renewed.AuthorizationId,
                renewed.AuthorizationStatus,
                renewed.AuthorizedAmount,
                renewed.ExpirationTime,
                DateTimeOffset.UtcNow);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new AuthorizationUnrenewableException(
                "The payment hold has expired and PayPal can no longer renew it. " +
                "Ask the shopper to pay again so a new hold can be placed. " +
                $"PayPal said: {ex.Message}");
        }
    }

    private static bool IsAuthorizationStale(OrderPayment payment, DateTimeOffset? expiration)
    {
        var now = DateTimeOffset.UtcNow;
        if (expiration.HasValue && expiration.Value <= now)
        {
            return true;
        }

        if (payment.AuthorizedAt.HasValue && now - payment.AuthorizedAt.Value > TimeSpan.FromDays(3))
        {
            return true;
        }

        return false;
    }

    private static bool IsExpiredAuthorization(PaymentException exception)
    {
        var code = exception.ErrorCode ?? string.Empty;
        var message = exception.Message ?? string.Empty;
        return code.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
            || code.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("authorization has expired", StringComparison.OrdinalIgnoreCase)
            || message.Contains("AUTH_EXPIRED", StringComparison.OrdinalIgnoreCase);
    }
}
