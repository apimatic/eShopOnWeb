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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    public const string DefaultStreet = "2211 N First Street";
    public const string DefaultCity = "San Jose";
    public const string DefaultState = "CA";
    public const string DefaultCountry = "US";
    public const string DefaultZipCode = "95131";

    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromDays(29);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<OrderPaymentService> _logger;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        IAppLogger<OrderPaymentService> logger,
        IPayPalSettings paypalSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _logger = logger;
        _currency = paypalSettings.Currency;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, Address? shipTo, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required.", HttpStatusCode.Unauthorized);
        }

        if (items is null || items.Count == 0)
        {
            throw new PaymentException("An order must contain at least one catalog item.");
        }

        if (items.Any(line => line.Quantity <= 0))
        {
            throw new PaymentException("Item quantity must be greater than zero.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new PaymentException("One or more catalog items were not found.", HttpStatusCode.NotFound);
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo ?? new Address(DefaultStreet, DefaultCity, DefaultState, DefaultCountry, DefaultZipCode);
        var order = new Order(buyerId, address, orderItems);
        order.Payment.SetCurrency(_currency);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, CardPaymentDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.EnsureOwnedBy(buyerId);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            _logger.LogInformation("Pay skipped for order {0}; already in status {1}.", order.Id, order.Status);
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", HttpStatusCode.Conflict);
        }

        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
        {
            throw new PaymentException("The order total must be greater than zero.");
        }

        var invoiceId = $"ESHOP-{order.Id}";
        var requestId = $"eshop-pay-{order.Id}";

        PayPalAuthorizationResult auth;
        if (paymentMethodId.HasValue)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByBuyerAndIdSpecification(buyerId, paymentMethodId.Value), cancellationToken);
            if (method is null)
            {
                throw new PaymentException("Saved payment method was not found.", HttpStatusCode.NotFound);
            }

            auth = await _payPal.AuthorizeVaultedCardPaymentAsync(amount, _currency, invoiceId, method.PayPalPaymentTokenId, requestId, cancellationToken);
        }
        else if (card is not null)
        {
            ValidateCard(card);
            auth = await _payPal.AuthorizeCardPaymentAsync(amount, _currency, invoiceId, card, requestId, cancellationToken);
        }
        else
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId.");
        }

        var authorized = PayPalMoneyFormat.Parse(auth.Amount.Value);
        if (authorized != amount)
        {
            throw new PaymentException($"PayPal held {auth.Amount.Value} but the order total is {PayPalMoneyFormat.ToValue(amount)}.");
        }

        order.MarkAuthorized(
            auth.PayPalOrderId,
            auth.AuthorizationId,
            auth.Status,
            auth.CreatedAt,
            auth.ExpiresAt,
            _currency,
            auth.CardBrand,
            auth.CardLast4);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            _logger.LogInformation("Fulfil skipped for order {0}; already captured.", order.Id);
            return order;
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.Payment.AuthorizationId))
        {
            throw new PaymentException("The order has no payment hold to capture.", HttpStatusCode.Conflict);
        }

        var authorizationId = await EnsureFreshAuthorizationAsync(order, cancellationToken);
        var capture = await _payPal.CaptureAuthorizationAsync(authorizationId, $"eshop-capture-{order.Id}", cancellationToken);

        order.MarkFulfilled(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PaypalFee, capture.NetAmount);
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

        if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(order.Payment.AuthorizationId))
        {
            await _payPal.VoidAuthorizationAsync(order.Payment.AuthorizationId, $"eshop-void-{order.Id}", cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund idempotency key is required.");
        }

        var order = await GetOrderAsync(orderId, cancellationToken);
        order.EnsureOwnedBy(buyerId);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (string.IsNullOrEmpty(order.Payment.CaptureId) || order.Payment.CapturedAmount is null)
        {
            throw new PaymentException("There is no captured payment to refund.", HttpStatusCode.Conflict);
        }

        var remaining = order.RemainingRefundable();
        var refundAmount = decimal.Round(amount ?? remaining, 2, MidpointRounding.AwayFromZero);
        if (refundAmount <= 0)
        {
            throw new PaymentException("Nothing remains to refund.", HttpStatusCode.Conflict);
        }

        var paypalRefund = await _payPal.RefundCaptureAsync(
            order.Payment.CaptureId,
            refundAmount,
            order.Payment.Currency,
            idempotencyKey,
            cancellationToken);

        var refund = order.AddRefund(paypalRefund.RefundId, paypalRefund.Status, paypalRefund.Amount, paypalRefund.Currency, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException($"Order {orderId} was not found.", HttpStatusCode.NotFound);
        }

        return order;
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var authorizationId = order.Payment.AuthorizationId!;
        var originalCreated = order.Payment.OriginalAuthorizationCreatedAt ?? order.Payment.AuthorizationCreatedAt ?? DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;

        if (now - originalCreated > AuthorizationLifetime)
        {
            throw new PaymentException(
                "The payment hold can no longer be renewed (PayPal authorizations last 29 days). Ask the shopper to pay again, then fulfil the new hold.",
                HttpStatusCode.Conflict);
        }

        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException)
        {
            details = new PayPalAuthorizationDetails
            {
                AuthorizationId = authorizationId,
                Status = order.Payment.AuthorizationStatus ?? "UNKNOWN",
                CreatedAt = originalCreated,
                ExpiresAt = order.Payment.AuthorizationExpiresAt,
                Amount = new PayPalMoney { CurrencyCode = order.Payment.Currency, Value = PayPalMoneyFormat.ToValue(order.Total()) }
            };
        }

        var honorElapsed = now - originalCreated >= HonorPeriod;
        var expired = string.Equals(details.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase)
                      || (details.ExpiresAt.HasValue && details.ExpiresAt.Value <= now);

        if (!honorElapsed && !expired)
        {
            return authorizationId;
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                order.Total(),
                order.Payment.Currency,
                $"eshop-reauth-{order.Id}",
                cancellationToken);

            order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.CreatedAt, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Renewed PayPal authorization for order {0} to {1}.", order.Id, renewed.AuthorizationId);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                "The payment hold is stale and PayPal could not renew it. Ask the shopper to pay again before fulfilling. " + ex.Message,
                HttpStatusCode.Conflict);
        }
    }

    internal static void ValidateCard(CardPaymentDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException("Card number and expiry are required.");
        }
    }
}
