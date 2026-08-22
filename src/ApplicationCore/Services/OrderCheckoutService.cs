using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderCheckoutService : IOrderCheckoutService
{
    public static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);

    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly OrderOperationGate _gate;

    public OrderCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        OrderOperationGate gate)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _gate = gate;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items, Address? shipToAddress)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new CheckoutException(401, "A signed-in shopper is required to place an order.");
        }

        if (items == null || items.Count == 0)
        {
            throw new CheckoutException(400, "An order requires at least one catalog item.");
        }

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new CheckoutException(400, "Each order item quantity must be greater than zero.");
            }
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));
        var byId = catalogItems.ToDictionary(c => c.Id);

        foreach (var catalogItemId in catalogItemIds)
        {
            if (!byId.ContainsKey(catalogItemId))
            {
                throw new CheckoutException(400, $"Catalog item {catalogItemId} was not found.");
            }
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = byId[item.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        return await _orderRepository.AddAsync(order);
    }

    public async Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId)
    {
        var order = await LoadOrder(orderId);
        EnsureBuyerOwns(order, buyerId);
        return order;
    }

    public Task<Order> GetOrderForOperatorAsync(int orderId) => LoadOrder(orderId);

    public async Task<IReadOnlyList<Order>> ListOrdersForBuyerAsync(string buyerId)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId));
    }

    public Task<Order> PayWithCardAsync(int orderId, string buyerId, CardPaymentSource card) =>
        _gate.RunAsync(orderId, () => PayAsync(orderId, buyerId, card, vaultId: null));

    public Task<Order> PayWithSavedCardAsync(int orderId, string buyerId, int paymentMethodId) =>
        _gate.RunAsync(orderId, async () =>
        {
            var method = await _paymentMethodRepository.GetByIdAsync(paymentMethodId);
            if (method == null || !string.Equals(method.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
            {
                throw new CheckoutException(404, "Saved payment method was not found.");
            }

            return await PayAsync(orderId, buyerId, card: null, vaultId: method.PayPalPaymentTokenId);
        });

    public Task<Order> FulfilAsync(int orderId) =>
        _gate.RunAsync(orderId, async () =>
        {
            var order = await LoadOrder(orderId);

            if (order.PaymentStatus is OrderPaymentStatus.Fulfilled
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                return order;
            }

            if (order.PaymentStatus != OrderPaymentStatus.Authorized)
            {
                throw new CheckoutException(409,
                    $"Order {order.Id} cannot be fulfilled while payment status is {order.PaymentStatus}. An authorization must be in place first.");
            }

            var authorizationId = order.Payment.AuthorizationId
                ?? throw new CheckoutException(409, $"Order {order.Id} is missing a PayPal authorization id.");
            var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
            var currency = order.Payment.Currency
                ?? throw new CheckoutException(409, $"Order {order.Id} is missing a payment currency.");

            authorizationId = await EnsureFreshAuthorizationAsync(order, authorizationId, amount, currency);

            PayPalCaptureResult capture;
            try
            {
                capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId,
                    amount,
                    currency,
                    invoiceId: $"eshop-{order.PaymentIdempotencyKey}",
                    idempotencyKey: $"eshop-capture-{order.PaymentIdempotencyKey}");
            }
            catch (CheckoutException ex) when (IsStaleAuthorization(ex))
            {
                authorizationId = await RenewAuthorizationOrThrow(order, authorizationId, amount, currency);
                capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId,
                    amount,
                    currency,
                    invoiceId: $"eshop-{order.PaymentIdempotencyKey}",
                    idempotencyKey: $"eshop-capture-{order.PaymentIdempotencyKey}-retry");
            }

            order.MarkFulfilled(
                capture.CaptureId,
                capture.Status,
                capture.CapturedAmount,
                capture.PaypalFee,
                capture.NetAmount,
                DateTimeOffset.UtcNow);
            await _orderRepository.UpdateAsync(order);
            return order;
        });

    public Task<Order> CancelAsync(int orderId) =>
        _gate.RunAsync(orderId, async () =>
        {
            var order = await LoadOrder(orderId);

            if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            {
                return order;
            }

            if (order.PaymentStatus == OrderPaymentStatus.AwaitingPayment)
            {
                order.MarkCancelled(DateTimeOffset.UtcNow);
                await _orderRepository.UpdateAsync(order);
                return order;
            }

            if (order.PaymentStatus != OrderPaymentStatus.Authorized)
            {
                throw new CheckoutException(409,
                    $"Order {order.Id} cannot be cancelled after fulfilment. Use a refund to return captured funds.");
            }

            var voidId = order.Payment.OriginalAuthorizationId ?? order.Payment.AuthorizationId;
            if (!string.IsNullOrEmpty(voidId))
            {
                await _payPal.VoidAuthorizationAsync(voidId, $"eshop-cancel-{order.PaymentIdempotencyKey}");
            }

            order.MarkCancelled(DateTimeOffset.UtcNow);
            await _orderRepository.UpdateAsync(order);
            return order;
        });

    public Task<OrderRefund> RefundAsync(int orderId, string actorBuyerId, bool actorIsAdmin, decimal? amount, string idempotencyKey) =>
        _gate.RunAsync(orderId, async () =>
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new CheckoutException(400, "Refunds require an idempotency key.");
            }

            var order = await LoadOrder(orderId);
            if (!actorIsAdmin)
            {
                EnsureBuyerOwns(order, actorBuyerId);
            }

            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing != null)
            {
                return existing;
            }

            if (order.PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
            {
                throw new CheckoutException(409,
                    $"Order {order.Id} cannot be refunded while payment status is {order.PaymentStatus}.");
            }

            var captureId = order.Payment.CaptureId
                ?? throw new CheckoutException(409, $"Order {order.Id} is missing a PayPal capture id.");
            var currency = order.Payment.Currency
                ?? throw new CheckoutException(409, $"Order {order.Id} is missing a payment currency.");

            var refundAmount = amount ?? order.RemainingRefundable();
            if (refundAmount <= 0m)
            {
                throw new CheckoutException(409, $"Order {order.Id} has no remaining refundable amount.");
            }

            if (refundAmount > order.RemainingRefundable())
            {
                throw new CheckoutException(409,
                    $"Refund of {refundAmount.ToString("0.00", CultureInfo.InvariantCulture)} exceeds remaining refundable amount {order.RemainingRefundable().ToString("0.00", CultureInfo.InvariantCulture)}.");
            }

            var paypalRefund = await _payPal.RefundCaptureAsync(
                captureId,
                refundAmount,
                currency,
                $"eshop-refund-{order.PaymentIdempotencyKey}-{idempotencyKey}");
            var refund = order.RecordRefund(
                idempotencyKey,
                paypalRefund.RefundId,
                paypalRefund.Status,
                paypalRefund.Amount,
                paypalRefund.Currency);
            await _orderRepository.UpdateAsync(order);
            return refund;
        });

    private async Task<Order> PayAsync(int orderId, string buyerId, CardPaymentSource? card, string? vaultId)
    {
        var order = await LoadOrder(orderId);
        EnsureBuyerOwns(order, buyerId);

        if (order.PaymentStatus is OrderPaymentStatus.Authorized
            or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return order;
        }

        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new CheckoutException(409, $"Order {order.Id} cannot be paid while payment status is {order.PaymentStatus}.");
        }

        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        if (amount <= 0m)
        {
            throw new CheckoutException(400, "An order total of zero cannot be authorized.");
        }

        var currency = _payPal.Currency;
        var lines = order.OrderItems.Select(i => new PayPalPurchaseLine
        {
            Name = Truncate(i.ItemOrdered.ProductName, 127),
            Sku = i.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture),
            Description = Truncate(i.ItemOrdered.ProductName, 127),
            UnitAmount = i.UnitPrice,
            Quantity = i.Units
        }).ToList();

        var invoiceId = $"eshop-{order.PaymentIdempotencyKey}";
        var idempotencyKey = $"eshop-authorize-{order.PaymentIdempotencyKey}";

        PayPalAuthorizationResult authorization;
        if (!string.IsNullOrEmpty(vaultId))
        {
            authorization = await _payPal.AuthorizeVaultedCardAsync(amount, currency, invoiceId, lines, vaultId, idempotencyKey);
        }
        else
        {
        if (card == null)
            {
                throw new CheckoutException(400, "Pay with either card details or a saved paymentMethodId.");
            }

            authorization = await _payPal.AuthorizeCardAsync(amount, currency, invoiceId, lines, CardInputNormalizer.Normalize(card), idempotencyKey);
        }

        if (authorization.Amount != amount)
        {
            throw new CheckoutException(502,
                $"PayPal authorized {authorization.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {authorization.Currency} but the order total is {amount.ToString("0.00", CultureInfo.InvariantCulture)} {currency}.");
        }

        order.MarkAuthorized(
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.Amount,
            authorization.Currency,
            authorization.CreateTime,
            authorization.ExpirationTime,
            authorization.CardLastDigits,
            authorization.CardBrand);
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, string authorizationId, decimal amount, string currency)
    {
        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId);
        }
        catch (CheckoutException)
        {
            details = new PayPalAuthorizationDetails
            {
                AuthorizationId = authorizationId,
                Status = order.Payment.AuthorizationStatus ?? "CREATED",
                CreateTime = order.Payment.AuthorizationCreatedAt,
                ExpirationTime = order.Payment.AuthorizationExpiresAt
            };
        }

        if (string.Equals(details.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase) ||
            IsHonorPeriodElapsed(details.CreateTime ?? order.Payment.AuthorizationCreatedAt))
        {
            return await RenewAuthorizationOrThrow(order, authorizationId, amount, currency);
        }

        return authorizationId;
    }

    private async Task<string> RenewAuthorizationOrThrow(Order order, string authorizationId, decimal amount, string currency)
    {
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                amount,
                currency,
                $"eshop-reauth-{order.PaymentIdempotencyKey}");

            order.MarkReauthorized(renewed.AuthorizationId, renewed.Status, renewed.CreateTime, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order);
            return renewed.AuthorizationId;
        }
        catch (CheckoutException ex)
        {
            throw new CheckoutException(409,
                "The PayPal authorization for this order has expired and cannot be renewed. " +
                "Capture is no longer possible against this hold. Ask the shopper to authorize payment again before fulfilment. " +
                ex.Message);
        }
    }

    private static bool IsHonorPeriodElapsed(DateTimeOffset? createdAt)
    {
        if (createdAt == null)
        {
            return false;
        }

        return DateTimeOffset.UtcNow >= createdAt.Value.ToUniversalTime() + AuthorizationHonorPeriod;
    }

    private static bool IsStaleAuthorization(CheckoutException ex)
    {
        var text = ex.Message ?? string.Empty;
        return text.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("AUTHORIZATION_DENIED", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Order> LoadOrder(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentDetailsSpec(orderId));
        if (order == null)
        {
            throw new CheckoutException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private static void EnsureBuyerOwns(Order order, string buyerId)
    {
        if (!order.BelongsTo(buyerId))
        {
            throw new CheckoutException(404, $"Order {order.Id} was not found.");
        }
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value.Substring(0, max);
    }
}
