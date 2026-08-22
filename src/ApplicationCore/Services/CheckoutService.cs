using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutService : ICheckoutService
{
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan AuthorizationWindow = TimeSpan.FromDays(29);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _payPalSettings;

    public CheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<Buyer> buyerRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        PayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _payPalSettings = payPalSettings;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderCatalogItem> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new CheckoutException(400, "An order must contain at least one catalog item.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new CheckoutException(400, "Item quantity must be greater than zero.");
            }

            if (!catalogById.TryGetValue(item.CatalogItemId, out var catalogItem))
            {
                throw new CheckoutException(400, $"Catalog item {item.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var address = shipToAddress ?? new Address("123 Main St.", "Anytown", "CA", "US", "12345");
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPayment? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.Status is OrderStatus.PaymentAuthorized or OrderStatus.Fulfilled
            or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new CheckoutException(409, "A cancelled order cannot be paid.");
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new CheckoutException(409, $"Order {orderId} cannot be paid in status {order.Status}.");
        }

        var hasCard = card is not null && !string.IsNullOrWhiteSpace(card.Number);
        if (hasCard == paymentMethodId.HasValue)
        {
            throw new CheckoutException(400, "Provide either card details or a saved payment method, not both.");
        }

        string? vaultId = null;
        if (paymentMethodId.HasValue)
        {
            vaultId = await GetVaultIdAsync(buyerId, paymentMethodId.Value, cancellationToken);
        }
        else
        {
            ValidateCard(card!);
        }

        var currency = RequireCurrency();
        var amount = order.Total();
        order.EnsurePaymentRequestIds();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        if (string.IsNullOrEmpty(order.PayPalOrderId))
        {
            var created = await _payPal.CreateAuthorizeOrderAsync(
                amount,
                currency,
                customId: order.Id.ToString(),
                invoiceId: InvoiceId(order, "AUTH"),
                requestId: order.PayPalCreateRequestId!,
                cancellationToken);
            order.RecordPayPalOrder(created.Id);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        var authorized = await _payPal.AuthorizeOrderAsync(
            order.PayPalOrderId!,
            card,
            vaultId,
            order.PayPalAuthorizeRequestId!,
            cancellationToken);

        var authorization = authorized.Authorization
            ?? throw new CheckoutException(502, "PayPal authorized the order but did not return an authorization id.");

        if (authorization.Amount.HasValue && authorization.Amount.Value != amount)
        {
            throw new CheckoutException(502,
                $"PayPal held {authorization.Amount.Value} {authorization.Currency} but the order total is {amount} {currency}.");
        }

        order.RecordAuthorization(
            authorization.Id,
            authorization.Status,
            authorization.ExpirationTime,
            authorization.CreateTime,
            currency);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            if (order.PayPalCaptureId is not null && (order.PayPalFee is null || order.NetProceeds is null))
            {
                var existing = await _payPal.GetCaptureAsync(order.PayPalCaptureId, cancellationToken);
                order.RecordCapture(
                    existing.Id,
                    existing.Status,
                    existing.Amount,
                    existing.PayPalFee,
                    existing.NetAmount,
                    existing.Currency);
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }

            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new CheckoutException(409, "A cancelled order cannot be fulfilled.");
        }

        if (order.Status != OrderStatus.PaymentAuthorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new CheckoutException(409, "An order must be paid (authorized) before it can be fulfilled.");
        }

        var currency = order.PaymentCurrency ?? RequireCurrency();
        var amount = order.Total();
        var authorizationId = await EnsureFreshAuthorizationAsync(order, amount, currency, cancellationToken);

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                currency,
                InvoiceId(order, "CAP"),
                order.PayPalCaptureRequestId!,
                cancellationToken);
        }
        catch (CheckoutException ex) when (IsStaleAuthorization(ex))
        {
            authorizationId = await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                currency,
                InvoiceId(order, "CAP"),
                order.PayPalCaptureRequestId!,
                cancellationToken);
        }

        order.RecordCapture(
            capture.Id,
            capture.Status,
            capture.Amount,
            capture.PayPalFee,
            capture.NetAmount,
            capture.Currency);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new CheckoutException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (!string.IsNullOrEmpty(order.PayPalOriginalAuthorizationId) || !string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            var voidId = order.PayPalOriginalAuthorizationId ?? order.PayPalAuthorizationId!;
            await _payPal.VoidAuthorizationAsync(voidId, $"{order.Id}-void", cancellationToken);
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CheckoutException(400, "A refund idempotency key is required.");
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw new CheckoutException(409, "Only a fulfilled order can be refunded.");
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId) || order.CapturedAmount is null)
        {
            throw new CheckoutException(409, "This order has no captured PayPal payment to refund.");
        }

        var remaining = order.RemainingRefundable();
        if (remaining <= 0 || order.Status == OrderStatus.Refunded)
        {
            throw new CheckoutException(409, "This order has already been refunded in full.");
        }

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new CheckoutException(400, "Refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw new CheckoutException(400,
                $"Refund amount {refundAmount} exceeds the remaining refundable amount {remaining}.");
        }

        var currency = order.PaymentCurrency ?? RequireCurrency();
        var isFullRemaining = refundAmount == remaining && remaining == order.CapturedAmount;
        var paypalRequestId = $"{order.PayPalCaptureId}:{idempotencyKey}";
        if (paypalRequestId.Length > 108)
        {
            paypalRequestId = paypalRequestId[..108];
        }

        var result = await _payPal.RefundCaptureAsync(
            order.PayPalCaptureId,
            isFullRemaining ? null : refundAmount,
            currency,
            paypalRequestId,
            cancellationToken);

        var refund = order.AddRefund(result.Id, result.Status, result.Amount == 0 ? refundAmount : result.Amount, result.Currency, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new OrdersWithPaymentByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken);
        if (order is null || !order.BelongsTo(buyerId))
        {
            return null;
        }

        return order;
    }

    private async Task<string> EnsureFreshAuthorizationAsync(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        order.EnsurePaymentRequestIds();
        var authorization = await _payPal.GetAuthorizationAsync(order.PayPalAuthorizationId!, cancellationToken);

        if (string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(409,
                "The PayPal authorization was voided and cannot be captured. Ask the shopper to pay the order again.");
        }

        if (string.Equals(authorization.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            return authorization.Id;
        }

        if (NeedsRenewal(order, authorization))
        {
            return await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
        }

        return authorization.Id;
    }

    private async Task<string> RenewAuthorizationAsync(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        if (CannotRenew(order))
        {
            throw CannotRenewException();
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                order.PayPalAuthorizationId!,
                amount,
                currency,
                $"{order.PayPalAuthorizeRequestId}-reauth",
                cancellationToken);

            order.RecordReauthorization(renewed.Id, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.Id;
        }
        catch (CheckoutException ex)
        {
            throw new CheckoutException(409,
                "The PayPal authorization can no longer be renewed. Ask the shopper to pay the order again. "
                + ex.Message);
        }
    }

    private static bool NeedsRenewal(Order order, PayPalAuthorizationResult authorization)
    {
        var now = DateTimeOffset.UtcNow;
        if (string.Equals(authorization.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (authorization.ExpirationTime.HasValue && authorization.ExpirationTime.Value <= now)
        {
            return true;
        }

        var authorizedAt = order.OriginalAuthorizedAt ?? authorization.CreateTime;
        return authorizedAt.HasValue && now >= authorizedAt.Value + HonorPeriod;
    }

    private static bool CannotRenew(Order order)
    {
        var origin = order.OriginalAuthorizedAt;
        return origin.HasValue && DateTimeOffset.UtcNow >= origin.Value + AuthorizationWindow;
    }

    private static CheckoutException CannotRenewException() =>
        new(409,
            "The PayPal authorization can no longer be renewed. The 29-day authorization window has ended. Ask the shopper to pay the order again.");

    private static bool IsStaleAuthorization(CheckoutException ex) =>
        ex.Message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
        || (ex.Message.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
            && ex.Message.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase));

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!order.BelongsTo(buyerId))
        {
            throw new CheckoutException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken);
        return order ?? throw new CheckoutException(404, $"Order {orderId} was not found.");
    }

    private async Task<string> GetVaultIdAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        var method = buyer?.GetPaymentMethod(paymentMethodId);
        if (method?.CardId is null)
        {
            throw new CheckoutException(404, $"Payment method {paymentMethodId} was not found.");
        }

        return method.CardId;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_payPalSettings.Currency))
        {
            throw new CheckoutException(500, "PayPal:Currency is not configured.");
        }

        return _payPalSettings.Currency;
    }

    private static void ValidateCard(CardPayment card)
    {
        if (string.IsNullOrWhiteSpace(card.Number)
            || string.IsNullOrWhiteSpace(card.Expiry)
            || string.IsNullOrWhiteSpace(card.SecurityCode)
            || string.IsNullOrWhiteSpace(card.Name))
        {
            throw new CheckoutException(400, "Card number, expiry, security code, and name are required.");
        }
    }

    private static string InvoiceId(Order order, string purpose) =>
        $"ESHOP-{order.Id}-{purpose}-{order.PayPalCreateRequestId}";
}
