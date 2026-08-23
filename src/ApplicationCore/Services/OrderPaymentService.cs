using System;
using System.Collections.Generic;
using System.Linq;
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
    private static readonly Address DefaultShipTo = new("123 Test Street", "Seattle", "WA", "US", "98101");

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogItemRepository;
    private readonly IReadRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogItemRepository,
        IReadRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        ShippingAddressRequest? shipTo)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required.", 401);
        }

        if (items is null || items.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.");
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new OrderLineRequest(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (grouped.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new PaymentException("Catalog item id and quantity must be greater than zero.");
        }

        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(grouped.Select(i => i.CatalogItemId).ToArray()));

        var orderItems = new List<OrderItem>();
        foreach (var line in grouped)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.", 404);

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = ToAddress(shipTo) ?? DefaultShipTo;
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order);
        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId}", order.Id, buyerId);
        return order;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, CardPaymentRequest? card, int? paymentMethodId)
    {
        var order = await GetRequiredOrderAsync(orderId);
        EnsureBuyer(order, buyerId);

        if (order.Status is OrderStatus.PaymentAuthorized or OrderStatus.Fulfilled
            or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", 409);
        }

        var amount = MoneyFormatter.Round(order.Total(), _payPal.Currency);
        if (amount <= 0)
        {
            throw new PaymentException("Order total must be greater than zero.");
        }

        var requestId = $"eshop-pay-{order.Id}-{order.OrderDate.UtcTicks}";
        var invoiceId = $"eShop-{order.Id}-{order.OrderDate.UtcTicks}";
        var customId = order.Id.ToString();

        PayPalAuthorizationResult authorization;
        if (paymentMethodId.HasValue)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId.Value, buyerId))
                ?? throw new PaymentMethodNotFoundException(paymentMethodId.Value);

            authorization = await _payPal.AuthorizeVaultedCardAsync(
                invoiceId, customId, amount, method.PaypalPaymentTokenId, requestId);
        }
        else if (card is not null)
        {
            authorization = await _payPal.AuthorizeCardAsync(
                invoiceId, customId, amount, ToPayPalCard(card), requestId);
        }
        else
        {
            throw new PaymentException("Provide card details or a saved paymentMethodId.");
        }

        if (order.Payment is null)
        {
            order.AttachPayment(new OrderPayment(order.Id, authorization.Currency, authorization.AuthorizedAmount));
        }

        order.Payment!.RecordAuthorization(
            authorization.PaypalOrderId,
            authorization.PaypalOrderStatus,
            authorization.AuthorizationId,
            authorization.AuthorizationStatus,
            authorization.AuthorizedAmount,
            authorization.Currency,
            authorization.ExpirationTime,
            authorization.CreateTime);
        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Authorized order {OrderId} authorization {AuthorizationId}", order.Id, authorization.AuthorizationId);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId)
    {
        var order = await GetRequiredOrderAsync(orderId);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be fulfilled.", 409);
        }

        if (order.Status != OrderStatus.PaymentAuthorized || string.IsNullOrEmpty(order.Payment?.PaypalAuthorizationId))
        {
            throw new PaymentException("The order has no PayPal authorization to capture.", 409);
        }

        var payment = order.Payment;
        var authorizationId = payment.PaypalAuthorizationId!;
        var current = await _payPal.GetAuthorizationAsync(authorizationId);

        if (string.Equals(current.AuthorizationStatus, "CAPTURED", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(payment.PaypalCaptureId))
        {
            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order);
            return order;
        }

        if (NeedsReauthorization(current, payment))
        {
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(
                    authorizationId, payment.AuthorizedAmount, $"eshop-reauth-{order.Id}-{DateTimeOffset.UtcNow.UtcTicks}");
                payment.RecordReauthorization(
                    renewed.AuthorizationId,
                    renewed.AuthorizationStatus,
                    renewed.ExpirationTime,
                    renewed.CreateTime);
                authorizationId = renewed.AuthorizationId;
                _logger.LogInformation("Reauthorized order {OrderId} as {AuthorizationId}", order.Id, authorizationId);
            }
            catch (PaymentException ex)
            {
                throw new PaymentException(
                    "PayPal can no longer renew this authorization. Ask the shopper to pay again, then fulfil the new authorization. "
                    + ex.Message,
                    ex.StatusCode == 0 ? 409 : ex.StatusCode);
            }
        }

        var capture = await _payPal.CaptureAuthorizationAsync(
            authorizationId,
            payment.AuthorizedAmount,
            $"eShop-{order.Id}-c-{order.OrderDate.UtcTicks}",
            $"eshop-fulfil-{order.Id}-{order.OrderDate.UtcTicks}");

        payment.RecordCapture(
            capture.CaptureId,
            capture.CaptureStatus,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetAmount,
            capture.Currency);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Captured order {OrderId} as {CaptureId}", order.Id, capture.CaptureId);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId)
    {
        var order = await GetRequiredOrderAsync(orderId);
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
        }

        if (!string.IsNullOrEmpty(order.Payment?.PaypalAuthorizationId)
            && !string.Equals(order.Payment.PaypalAuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(order.Payment.PaypalAuthorizationStatus, "CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            await _payPal.VoidAuthorizationAsync(order.Payment.PaypalAuthorizationId, $"eshop-cancel-{order.Id}-{order.OrderDate.UtcTicks}");
            order.Payment.RecordVoid("VOIDED");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Cancelled order {OrderId}", order.Id);
        return order;
    }

    public async Task<OrderRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("idempotencyKey is required for refunds.");
        }

        var order = await GetRequiredOrderAsync(orderId);
        EnsureBuyer(order, buyerId);

        if (order.Payment is null || string.IsNullOrEmpty(order.Payment.PaypalCaptureId)
            || order.Status is OrderStatus.PendingPayment or OrderStatus.PaymentAuthorized or OrderStatus.Cancelled)
        {
            throw new PaymentException("The order has no captured payment to refund.", 409);
        }

        var existing = order.Payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var remaining = order.Payment.RemainingRefundable;
        var refundAmount = amount.HasValue
            ? MoneyFormatter.Round(amount.Value, order.Payment.Currency)
            : remaining;

        if (refundAmount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund of {refundAmount} exceeds the remaining captured amount of {remaining} {order.Payment.Currency}.");
        }

        var result = await _payPal.RefundCaptureAsync(order.Payment.PaypalCaptureId, refundAmount, idempotencyKey);
        var refund = order.Payment.AddRefund(result.RefundId, result.RefundStatus, result.Amount, result.Currency, idempotencyKey);
        order.MarkRefunded(order.Payment.RemainingRefundable <= 0);
        await _orderRepository.UpdateAsync(order);
        _logger.LogInformation("Refunded {Amount} on order {OrderId}", result.Amount, order.Id);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId) =>
        await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));

    public async Task<Order> GetBuyerOrderAsync(string buyerId, int orderId)
    {
        var order = await GetRequiredOrderAsync(orderId);
        EnsureBuyer(order, buyerId);
        return order;
    }

    private async Task<Order> GetRequiredOrderAsync(int orderId) =>
        await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId))
        ?? throw new OrderNotFoundException(orderId);

    private static void EnsureBuyer(Order order, string buyerId)
    {
        if (!order.BelongsTo(buyerId))
        {
            throw new PaymentException("This order does not belong to the signed-in shopper.", 403);
        }
    }

    private static bool NeedsReauthorization(PayPalAuthorizationResult current, OrderPayment payment)
    {
        if (string.Equals(current.AuthorizationStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(current.AuthorizationStatus, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var created = current.CreateTime ?? payment.AuthorizationCreated;
        if (created.HasValue && created.Value <= DateTimeOffset.UtcNow.AddDays(-3))
        {
            return true;
        }

        var expires = current.ExpirationTime ?? payment.AuthorizationExpiration;
        return expires.HasValue && expires.Value <= DateTimeOffset.UtcNow.AddHours(1);
    }

    private static Address? ToAddress(ShippingAddressRequest? shipTo)
    {
        if (shipTo is null || string.IsNullOrWhiteSpace(shipTo.Street))
        {
            return null;
        }

        return new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);
    }

    private static PayPalCardDetails ToPayPalCard(CardPaymentRequest card)
    {
        PayPalBillingAddress? billing = null;
        if (card.BillingAddress is not null)
        {
            billing = new PayPalBillingAddress(
                card.BillingAddress.Street,
                null,
                card.BillingAddress.City,
                card.BillingAddress.State,
                card.BillingAddress.ZipCode,
                NormalizeCountry(card.BillingAddress.Country));
        }

        return new PayPalCardDetails(
            CardInput.NormalizeNumber(card.Number),
            CardInput.NormalizeExpiry(card.Expiry),
            card.SecurityCode.Trim(),
            string.IsNullOrWhiteSpace(card.Name) ? "Shopper" : card.Name.Trim(),
            billing);
    }

    private static string NormalizeCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return "US";
        }

        return country.Trim().ToUpperInvariant() switch
        {
            "UNITED STATES" => "US",
            "USA" => "US",
            var code when code.Length == 2 => code,
            var other => other.Length >= 2 ? other[..2] : "US"
        };
    }
}

internal static class CardInput
{
    public static string NormalizeNumber(string? number) =>
        new string((number ?? string.Empty).Where(char.IsDigit).ToArray());

    public static string NormalizeExpiry(string? expiry)
    {
        var raw = (expiry ?? string.Empty).Trim();
        if (raw.Length == 7 && raw[4] == '-')
        {
            return raw;
        }

        if (raw.Length is 5 or 7 && raw[2] == '/')
        {
            var month = raw[..2];
            var year = raw[3..];
            if (year.Length == 2)
            {
                year = "20" + year;
            }

            return $"{year}-{month}";
        }

        throw new PaymentException("Card expiry must be YYYY-MM.");
    }
}
