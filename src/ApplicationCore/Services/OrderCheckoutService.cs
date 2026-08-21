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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderCheckoutService : IOrderCheckoutService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly ISavedPaymentMethodService _savedPaymentMethodService;
    private readonly IPayPalPaymentsGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalSettings _payPalSettings;

    public OrderCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        ISavedPaymentMethodService savedPaymentMethodService,
        IPayPalPaymentsGateway payPal,
        IUriComposer uriComposer,
        IPayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _savedPaymentMethodService = savedPaymentMethodService;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _payPalSettings = payPalSettings;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new PaymentException("An order must contain at least one catalog item.");
        }

        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException("Each order line must have a quantity greater than zero.");
            }
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new EntityNotFoundException("One or more catalog items were not found.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentRequest? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentConflictException("A cancelled order cannot be paid.");
        }

        if (card is null && savedPaymentMethodId is null)
        {
            throw new PaymentException("Provide card details or a saved paymentMethodId.");
        }

        if (card is not null && savedPaymentMethodId is not null)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.");
        }

        var paymentSource = await BuildPaymentSourceAsync(buyerId, card, savedPaymentMethodId, cancellationToken);
        var currency = RequireCurrency();
        var amount = new PayPalMoney(currency, MoneyFormat.ToPayPalValue(order.Total()));

        if (string.IsNullOrEmpty(order.PayPalOrderId))
        {
            var created = await _payPal.CreateAuthorizeOrderAsync(
                new PayPalCreateOrderRequest(amount, order.PayPalInvoiceId, order.PayPalCustomId, $"eShopOnWeb order {order.Id}"),
                $"eshop-create-{order.Id}-{Guid.NewGuid():N}",
                cancellationToken);

            order.AttachPayPalOrder(created.Id, currency);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        var authorization = await _payPal.AuthorizeOrderAsync(
            order.PayPalOrderId!,
            paymentSource,
            $"eshop-auth-{order.PayPalOrderId}",
            cancellationToken);

        EnsureAmountMatches(authorization.AmountValue, order.Total());

        order.RecordAuthorization(
            authorization.PayPalOrderId.Length > 0 ? authorization.PayPalOrderId : order.PayPalOrderId!,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.CreateTime,
            authorization.ExpirationTime,
            currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new PaymentConflictException("An order can only be fulfilled after payment has been authorized.");
        }

        var currency = order.Currency ?? RequireCurrency();
        var authorizationId = await EnsureFreshAuthorizationAsync(order, currency, cancellationToken);

        var capture = await _payPal.CaptureAuthorizationAsync(
            authorizationId,
            new PayPalCaptureRequest(
                new PayPalMoney(currency, MoneyFormat.ToPayPalValue(order.Total())),
                FinalCapture: true,
                order.PayPalInvoiceId),
            $"eshop-capture-{order.PayPalAuthorizationId}",
            cancellationToken);

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            MoneyFormat.FromPayPalValue(capture.AmountValue),
            MoneyFormat.FromPayPalValue(capture.PayPalFeeValue),
            MoneyFormat.FromPayPalValue(capture.NetAmountValue));

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

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentConflictException("A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId) && order.Status == OrderStatus.Authorized)
        {
            await _payPal.VoidAuthorizationAsync(order.PayPalAuthorizationId, $"eshop-void-{order.PayPalAuthorizationId}", cancellationToken);
            order.RecordCancellation("VOIDED");
        }
        else
        {
            order.RecordCancellation(order.PayPalAuthorizationStatus);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund idempotency key is required.");
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId) ||
            order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new PaymentConflictException("A refund can only be issued after the order has been fulfilled.");
        }

        var refundAmount = amount ?? order.RemainingRefundable();
        refundAmount = decimal.Round(refundAmount, 2, System.MidpointRounding.AwayFromZero);
        if (refundAmount <= 0)
        {
            throw new PaymentConflictException("There is no remaining captured amount to refund.");
        }

        if (refundAmount > order.RemainingRefundable())
        {
            throw new PaymentConflictException(
                $"Refund of {MoneyFormat.ToPayPalValue(refundAmount)} exceeds remaining refundable amount {MoneyFormat.ToPayPalValue(order.RemainingRefundable())}.");
        }

        var currency = order.Currency ?? RequireCurrency();
        var payPalRequestId = BuildRefundRequestId(order.PayPalCaptureId, idempotencyKey);
        PayPalRefund paypalRefund;
        try
        {
            paypalRefund = await _payPal.RefundCaptureAsync(
                order.PayPalCaptureId,
                new PayPalMoney(currency, MoneyFormat.ToPayPalValue(refundAmount)),
                payPalRequestId,
                cancellationToken);
        }
        catch (PaymentGatewayException ex) when (
            string.Equals(ex.PayPalErrorName, "DUPLICATE_REQUEST_ID", StringComparison.OrdinalIgnoreCase))
        {
            var replay = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (replay != null)
            {
                return replay;
            }

            throw new PaymentConflictException(
                "This refund idempotency key was already processed. Check existing refunds on the order rather than retrying with the same key against a new capture.");
        }

        var recorded = order.RecordRefund(
            paypalRefund.RefundId,
            paypalRefund.Status,
            MoneyFormat.FromPayPalValue(paypalRefund.AmountValue) == 0m
                ? refundAmount
                : MoneyFormat.FromPayPalValue(paypalRefund.AmountValue),
            idempotencyKey);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return recorded;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<Order> GetMyOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        return await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, string currency, CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        var now = System.DateTimeOffset.UtcNow;

        PayPalAuthorization? current = null;
        try
        {
            current = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
            if (!string.IsNullOrEmpty(current.Status))
            {
                if (current.Status is "VOIDED" or "DENIED")
                {
                    throw new AuthorizationExpiredException(
                        $"The PayPal authorization is {current.Status} and can no longer be captured. Ask the shopper to pay again.");
                }

                if (current.ExpirationTime is not null)
                {
                    order.RefreshAuthorization(current.AuthorizationId, current.Status, current.CreateTime, current.ExpirationTime);
                }
            }
        }
        catch (PaymentGatewayException) when (order.RequiresAuthorizationRenewal(now))
        {
            // Fall through to renewal below when PayPal no longer has a usable authorization.
        }

        var expired = order.RequiresAuthorizationRenewal(now) ||
                      (current?.ExpirationTime is not null && current.ExpirationTime <= now);

        if (!expired)
        {
            return order.PayPalAuthorizationId!;
        }

        if (!order.CanRenewAuthorization(now))
        {
            throw new AuthorizationExpiredException(
                "The PayPal authorization is outside the 29-day renewal window and cannot be recaptured. Ask the shopper to pay again.");
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                new PayPalMoney(currency, MoneyFormat.ToPayPalValue(order.Total())),
                $"eshop-reauth-{order.PayPalAuthorizationId}",
                cancellationToken);

            order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.CreateTime, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order.PayPalAuthorizationId!;
        }
        catch (PaymentGatewayException ex)
        {
            throw new AuthorizationExpiredException(
                $"The PayPal authorization is stale and could not be renewed. Ask the shopper to pay again. {ex.Message}");
        }
    }

    private async Task<PayPalCardPaymentSource> BuildPaymentSourceAsync(
        string buyerId,
        CardPaymentRequest? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken)
    {
        if (savedPaymentMethodId is not null)
        {
            var saved = await _savedPaymentMethodService.GetOwnedAsync(buyerId, savedPaymentMethodId.Value, cancellationToken);
            return new PayPalCardPaymentSource(
                Number: null,
                Expiry: null,
                SecurityCode: null,
                Name: null,
                VaultId: saved.PayPalPaymentTokenId,
                BillingAddress: null,
                IsStoredCredential: true);
        }

        return ToCardSource(card!);
    }

    internal static PayPalCardPaymentSource ToCardSource(CardPaymentRequest card)
    {
        var number = MoneyFormat.DigitsOnly(card.Number);
        if (number.Length is < 13 or > 19)
        {
            throw new PaymentException("Card number is invalid.");
        }

        var securityCode = MoneyFormat.DigitsOnly(card.SecurityCode);
        if (securityCode.Length is < 3 or > 4)
        {
            throw new PaymentException("Card security code is invalid.");
        }

        PayPalBillingAddress? billing = null;
        if (card.BillingAddress != null)
        {
            if (string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
            {
                throw new PaymentException("Billing address countryCode is required.");
            }

            billing = new PayPalBillingAddress(
                card.BillingAddress.CountryCode,
                card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2,
                card.BillingAddress.AdminArea2,
                card.BillingAddress.AdminArea1,
                card.BillingAddress.PostalCode);
        }

        return new PayPalCardPaymentSource(
            number,
            MoneyFormat.NormalizeExpiry(card.Expiry),
            securityCode,
            card.Name,
            VaultId: null,
            billing,
            IsStoredCredential: false);
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, System.StringComparison.Ordinal))
        {
            throw new ForbiddenOperationException("You cannot act on another shopper's order.");
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_payPalSettings.Currency))
        {
            throw new PaymentException("PayPal:Currency is not configured.", 500);
        }

        return _payPalSettings.Currency.Trim().ToUpperInvariant();
    }

    private static string BuildRefundRequestId(string captureId, string idempotencyKey)
    {
        var sanitized = new string(idempotencyKey.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (string.IsNullOrEmpty(sanitized))
        {
            sanitized = "refund";
        }

        var combined = $"{captureId}-{sanitized}";
        return combined.Length <= 108 ? combined : combined[..108];
    }

    private static void EnsureAmountMatches(string? authorizedValue, decimal orderTotal)
    {
        if (string.IsNullOrEmpty(authorizedValue))
        {
            return;
        }

        var authorized = MoneyFormat.FromPayPalValue(authorizedValue);
        if (authorized != decimal.Round(orderTotal, 2, System.MidpointRounding.AwayFromZero))
        {
            throw new PaymentGatewayException(
                $"PayPal authorized {authorizedValue} which does not match the order total {MoneyFormat.ToPayPalValue(orderTotal)}.",
                502);
        }
    }
}
