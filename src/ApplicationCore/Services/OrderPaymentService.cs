using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IUriComposer _uriComposer;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalClient payPalClient,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPalClient = payPalClient;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLine> lines,
        Address shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (lines == null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one catalog item.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Each order line must have a quantity greater than zero.");
        }

        var catalogIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            throw new PaymentException("One or more catalog items were not found.", HttpStatusCode.NotFound);
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Authorized
            || order.PaymentStatus == OrderPaymentStatus.Fulfilled
            || order.PaymentStatus == OrderPaymentStatus.Refunded
            || order.PaymentStatus == OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.");
        }

        if ((card == null && paymentMethodId == null) || (card != null && paymentMethodId != null))
        {
            throw new PaymentException("Pay with either card details or a saved payment method, not both.");
        }

        string? vaultId = null;
        if (paymentMethodId.HasValue)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpec(paymentMethodId.Value), cancellationToken);
            if (saved == null || saved.IsDeleted || saved.BuyerId != buyerId)
            {
                throw new PaymentException("Saved payment method was not found.", HttpStatusCode.NotFound);
            }

            vaultId = saved.PayPalVaultId;
        }
        else
        {
            ValidateCard(card!);
        }

        var amount = order.Total();
        order.EnsurePayPalInvoiceId();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var invoiceId = order.PayPalInvoiceId!;
        var customId = invoiceId;
        var createRequestId = invoiceId;

        PayPalOrderResult paypalOrder;
        if (!string.IsNullOrWhiteSpace(order.PayPalOrderId))
        {
            paypalOrder = await _payPalClient.GetOrderAsync(order.PayPalOrderId, cancellationToken);
        }
        else
        {
            paypalOrder = await _payPalClient.CreateOrderAsync(
                amount, customId, invoiceId, card, vaultId, createRequestId, cancellationToken);
            order.AttachPayPalOrder(paypalOrder.Id, paypalOrder.Status, _payPalClient.Currency);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(paypalOrder.AuthorizationId))
        {
            paypalOrder = await _payPalClient.AuthorizeOrderAsync(
                paypalOrder.Id, $"order-authorize-{order.Id}", cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(paypalOrder.AuthorizationId))
        {
            throw new PaymentException("PayPal authorized the order but did not return an authorization id.");
        }

        var authorization = await _payPalClient.GetAuthorizationAsync(paypalOrder.AuthorizationId, cancellationToken);
        if (authorization.Amount.HasValue && authorization.Amount.Value != amount)
        {
            throw new PaymentException(
                $"PayPal held {authorization.Amount.Value} but the order total is {amount}. The hold must match the order to the cent.");
        }

        order.MarkAuthorized(
            paypalOrder.Id,
            paypalOrder.Status,
            paypalOrder.AuthorizationId,
            paypalOrder.AuthorizationStatus ?? "CREATED",
            paypalOrder.AuthorizationExpiration,
            _payPalClient.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized || string.IsNullOrWhiteSpace(order.PayPalAuthorizationId))
        {
            throw new PaymentException($"Order {order.Id} cannot be fulfilled until a PayPal authorization is in place.");
        }

        var authorizationId = await EnsureFreshAuthorization(order, cancellationToken);
        var captureInvoiceId = string.IsNullOrWhiteSpace(order.PayPalInvoiceId)
            ? $"ew{order.Id}-c-{Guid.NewGuid():N}"
            : order.PayPalInvoiceId + "-c";
        var capture = await _payPalClient.CaptureAuthorizationAsync(
            authorizationId,
            order.Total(),
            captureInvoiceId,
            $"order-fulfil-{order.Id}",
            cancellationToken);

        order.MarkFulfilled(
            capture.Id,
            capture.Status,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetAmount,
            "CAPTURED");

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return order;
        }

        if (!string.IsNullOrWhiteSpace(order.PayPalAuthorizationId)
            && order.PaymentStatus == OrderPaymentStatus.Authorized)
        {
            try
            {
                await _payPalClient.VoidAuthorizationAsync(
                    order.PayPalAuthorizationId,
                    $"order-cancel-{order.Id}",
                    cancellationToken);
            }
            catch (PaymentException ex) when (ex.Message.Contains("AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase)
                                              || ex.Message.Contains("already voided", StringComparison.OrdinalIgnoreCase))
            {
                // Idempotent: the hold is already released.
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund requires an idempotencyKey so a retry cannot refund twice.");
        }

        var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(order.PayPalCaptureId)
            || order.PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException("Only a fulfilled order can be refunded.");
        }

        var remaining = order.RemainingRefundable();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentException("There is no remaining captured amount to refund.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException($"Refund of {refundAmount} exceeds the remaining refundable amount of {remaining}.");
        }

        var paypalRefund = await _payPalClient.RefundCaptureAsync(
            order.PayPalCaptureId,
            amount.HasValue ? refundAmount : null,
            idempotencyKey,
            cancellationToken);

        var refund = order.AddRefund(
            paypalRefund.Id,
            paypalRefund.Status,
            idempotencyKey,
            paypalRefund.Amount > 0 ? paypalRefund.Amount : refundAmount);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    private async Task<string> EnsureFreshAuthorization(Order order, CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        PayPalAuthorizationResult current;
        try
        {
            current = await _payPalClient.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                $"The PayPal hold on order {order.Id} could not be loaded ({ex.Message}). Fulfilment cannot continue until the shopper authorizes payment again.",
                HttpStatusCode.Conflict);
        }

        order.UpdateAuthorization(current.Id, current.Status, current.ExpirationTime);

        if (string.Equals(current.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(current.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"The PayPal hold on order {order.Id} is {current.Status} and cannot be captured. Ask the shopper to authorize payment again.",
                HttpStatusCode.Conflict);
        }

        if (string.Equals(current.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(current.Status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            return current.Id;
        }

        var stale = current.ExpirationTime.HasValue && current.ExpirationTime.Value <= DateTimeOffset.UtcNow.AddMinutes(5);
        if (!stale)
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return current.Id;
        }

        try
        {
            var renewed = await _payPalClient.ReauthorizeAsync(
                current.Id,
                order.Total(),
                $"order-reauth-{order.Id}",
                cancellationToken);

            order.UpdateAuthorization(renewed.Id, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.Id;
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                $"The PayPal hold on order {order.Id} has expired and could not be renewed ({ex.Message}). Ask the shopper to authorize payment again before fulfilment.",
                HttpStatusCode.Conflict);
        }
    }

    private async Task<Order> GetOwnedOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException("Order was not found.", HttpStatusCode.NotFound);
        }

        return order;
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new PaymentException("Order was not found.", HttpStatusCode.NotFound);
        }

        return order;
    }

    private static void ValidateCard(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentException("Card number, expiry (YYYY-MM) and security code are required.");
        }
    }
}
