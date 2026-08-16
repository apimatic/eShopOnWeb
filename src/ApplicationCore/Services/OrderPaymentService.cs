using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IReadRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    // Fallbacks used only when a catalog item carries no picture; the order-item
    // snapshot requires a non-empty value.
    private const string DefaultShipStreet = "123 Main St.";
    private const string DefaultShipCity = "Kent";
    private const string DefaultShipState = "OH";
    private const string DefaultShipCountry = "United States";
    private const string DefaultShipZip = "44240";

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IReadRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalPaymentGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput? shippingAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.");

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri ?? string.Empty);
            if (string.IsNullOrEmpty(pictureUri))
            {
                pictureUri = "no-image";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            // Amounts come from catalog prices.
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shippingAddress is null
            ? new Address(DefaultShipStreet, DefaultShipCity, DefaultShipState, DefaultShipCountry, DefaultShipZip)
            : new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State,
                shippingAddress.Country, shippingAddress.ZipCode);

        var order = new Order(buyerId, address, items);
        await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation($"Placed order {order.Id} for buyer {buyerId} awaiting payment, total {order.Total()}.");
        return order;
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, CardPaymentDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if ((card is null) == (savedPaymentMethodId is null))
        {
            throw new ArgumentException("Provide either card details or a saved payment method id — exactly one.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdForBuyerSpecification(orderId, buyerId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        // Idempotent in effect: a double-click never authorizes twice.
        if (order.PaymentStatus == OrderPaymentStatus.Authorized)
        {
            return order;
        }
        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new OrderPaymentException(
                $"Order {orderId} is {order.PaymentStatus} and can no longer be authorized.");
        }

        var amount = order.Total();
        var idempotencyKey = $"{order.PaymentReference}-authorize";

        AuthorizationResult result;
        string methodDescription;

        if (savedPaymentMethodId is not null)
        {
            var savedCard = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdForBuyerSpecification(savedPaymentMethodId.Value, buyerId), cancellationToken)
                ?? throw new PaymentMethodNotFoundException(savedPaymentMethodId.Value);

            result = await _gateway.AuthorizeWithVaultedCardAsync(amount, savedCard.PayPalVaultId, idempotencyKey, cancellationToken);
            methodDescription = savedCard.Describe();
        }
        else
        {
            result = await _gateway.AuthorizeWithCardAsync(amount, card!, idempotencyKey, cancellationToken);
            methodDescription = $"{result.CardBrand} ****{result.CardLast4}";
        }

        order.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status,
            result.ExpiresAt, result.Currency, methodDescription);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Authorized order {orderId}: PayPal order {result.PayPalOrderId}, authorization {result.AuthorizationId}.");
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdWithItemsSpecification(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        // Idempotent in effect: fulfilling twice never captures twice.
        if (order.PaymentStatus == OrderPaymentStatus.Captured)
        {
            return order;
        }
        if (order.PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new OrderPaymentException(
                $"Order {orderId} is {order.PaymentStatus} and cannot be fulfilled.");
        }

        var amount = order.Total();
        CaptureResult capture;

        try
        {
            capture = await _gateway.CaptureAuthorizationAsync(order.AuthorizationId!, amount,
                $"{order.PaymentReference}-capture", cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.IsAuthorizationExpired)
        {
            // A hold that has gone stale before fulfilment is renewed rather than failing outright.
            _logger.LogWarning($"Authorization {order.AuthorizationId} for order {orderId} is stale; reauthorizing.");

            ReauthorizationResult reauth;
            try
            {
                reauth = await _gateway.ReauthorizeAsync(order.AuthorizationId!, amount,
                    $"{order.PaymentReference}-reauthorize", cancellationToken);
            }
            catch (PaymentGatewayException reauthEx)
            {
                var reason = reauthEx.Issues.Count > 0 ? string.Join(", ", reauthEx.Issues) : reauthEx.Message;
                throw new OrderPaymentException(
                    $"Order {orderId} cannot be fulfilled: the payment hold has expired and can no longer be renewed ({reason}). " +
                    "Collect a new payment from the shopper before fulfilling.", reauthEx);
            }

            order.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            capture = await _gateway.CaptureAuthorizationAsync(reauth.AuthorizationId, amount,
                $"{order.PaymentReference}-capture-reauth", cancellationToken);
        }

        order.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            $"Fulfilled order {orderId}: captured {capture.GrossAmount} {capture.Currency}, fee {capture.PayPalFee}, net {capture.NetAmount}.");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdWithItemsSpecification(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        // Idempotent in effect.
        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return order;
        }
        if (order.PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new OrderPaymentException(
                $"Order {orderId} is {order.PaymentStatus} and cannot be cancelled. Use a refund after fulfilment.");
        }

        await _gateway.VoidAuthorizationAsync(order.AuthorizationId!, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Cancelled order {orderId}: authorization {order.AuthorizationId} voided, funds released.");
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdForBuyerSpecification(orderId, buyerId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        // Idempotent in effect: repeating under the same key never refunds twice.
        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return (order, existing);
        }

        if (order.PaymentStatus != OrderPaymentStatus.Captured &&
            order.PaymentStatus != OrderPaymentStatus.PartiallyRefunded)
        {
            throw new OrderPaymentException(
                $"Order {orderId} is {order.PaymentStatus} and cannot be refunded.");
        }

        var refundAmount = amount ?? order.RefundableAmount;
        if (refundAmount <= 0m)
        {
            throw new OrderPaymentException($"Refund amount must be greater than zero for order {orderId}.");
        }
        if (refundAmount > order.RefundableAmount)
        {
            throw new OrderPaymentException(
                $"Refund of {refundAmount} exceeds the refundable balance of {order.RefundableAmount} for order {orderId}.");
        }

        // Namespace the PayPal-Request-Id with the order reference so a caller key can't
        // collide with an unrelated order's cached request; local dedupe still uses the raw key.
        var result = await _gateway.RefundCaptureAsync(order.CaptureId!, refundAmount,
            $"{order.PaymentReference}-refund-{idempotencyKey}", cancellationToken);

        var refund = new OrderRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        order.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Refunded {result.Amount} on order {orderId}: refund {result.RefundId}.");
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }
}
