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
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShipTo = new("123 Main St", "San Jose", "CA", "US", "95131");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPalGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPalGateway = payPalGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<CreatePaidOrderItem> items,
        ShipToAddressDto? shipTo,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new PaymentException("An order must contain at least one catalog item.", 400);
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        if (catalogItems.Count != catalogIds.Length)
        {
            throw new PaymentException("One or more catalog items were not found.", 400);
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException("Quantity must be greater than zero.", 400);
            }

            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "placeholder"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri),
                catalogItem.Price,
                line.Quantity));
        }

        var address = shipTo is null
            ? DefaultShipTo
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> AuthorizeAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new OrderPaymentStateException($"Order {order.Id} cannot be paid from status {order.Status}.");
        }

        if ((card is null) == (paymentMethodId is null))
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.", 400);
        }

        var amount = new MoneyAmount(decimal.Round(order.Total(), 2), _payPalGateway.Currency);
        var purchaseItems = order.OrderItems.Select(i => new PurchaseItem(
            i.ItemOrdered.ProductName,
            i.Units.ToString(),
            new MoneyAmount(decimal.Round(i.UnitPrice, 2), amount.Currency))).ToList();
        var requestId = $"eshop-pay-{OrderKey(order)}";

        AuthorizationResult authorization;
        if (paymentMethodId is int methodId)
        {
            var method = await _paymentMethodRepository.GetByIdAsync(methodId, cancellationToken)
                ?? throw new PaymentException("Saved payment method was not found.", 404);
            if (!method.BelongsTo(buyerId))
            {
                throw new PaymentException("Saved payment method was not found.", 404);
            }

            authorization = await _payPalGateway.AuthorizeVaultedCardAsync(
                InvoiceId(order), CustomId(order), amount, purchaseItems, method.PayPalPaymentTokenId, requestId, cancellationToken);
        }
        else
        {
            authorization = await _payPalGateway.AuthorizeCardAsync(
                InvoiceId(order), CustomId(order), amount, purchaseItems, card!, requestId, cancellationToken);
        }

        order.MarkAuthorized(
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.Expiration,
            authorization.Amount.Value,
            authorization.Amount.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Authorized order {OrderId} with PayPal authorization {AuthorizationId}", order.Id, authorization.AuthorizationId);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.Payment.AuthorizationId))
        {
            throw new OrderPaymentStateException($"Order {order.Id} cannot be fulfilled from status {order.Status}.");
        }

        var authorizationId = await EnsureFreshAuthorizationAsync(order, cancellationToken);
        var amount = new MoneyAmount(decimal.Round(order.Total(), 2), order.Payment.Currency);
        var capture = await _payPalGateway.CaptureAsync(authorizationId, amount, $"eshop-fulfil-{OrderKey(order)}", cancellationToken);

        order.MarkFulfilled(
            capture.CaptureId,
            capture.Status,
            capture.Gross.Value,
            capture.Fee?.Value,
            capture.Net?.Value);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Fulfilled order {OrderId} with PayPal capture {CaptureId}", order.Id, capture.CaptureId);
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
            await _payPalGateway.VoidAsync(order.Payment.AuthorizationId, $"eshop-void-{OrderKey(order)}", cancellationToken);
            order.MarkCancelled("VOIDED");
        }
        else
        {
            order.MarkCancelled(null);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}", order.Id);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (string.IsNullOrEmpty(order.Payment.CaptureId))
        {
            throw new OrderPaymentStateException($"Order {order.Id} has no captured payment to refund.");
        }

        var refundAmount = amount ?? order.RefundableRemaining();
        refundAmount = decimal.Round(refundAmount, 2);
        if (refundAmount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.", 400);
        }

        if (refundAmount > order.RefundableRemaining())
        {
            throw new PaymentException(
                $"Refund of {refundAmount} exceeds remaining captured funds {order.RefundableRemaining()}.", 400);
        }

        var result = await _payPalGateway.RefundAsync(
            order.Payment.CaptureId,
            new MoneyAmount(refundAmount, order.Payment.Currency),
            idempotencyKey,
            cancellationToken);

        var refund = order.AddRefund(result.RefundId, result.Status, result.Amount.Value, result.Amount.Currency, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Refunded {Amount} on order {OrderId} as PayPal refund {RefundId}", refundAmount, order.Id, result.RefundId);
        return refund;
    }

    public Task<Order> GetShopperOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default) =>
        GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListShopperOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var authorizationId = order.Payment.AuthorizationId
            ?? throw new OrderPaymentStateException($"Order {order.Id} has no PayPal authorization.");

        AuthorizationDetails details;
        try
        {
            details = await _payPalGateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            throw new AuthorizationUnrenewableException(
                $"PayPal authorization {authorizationId} is no longer available. Ask the shopper to pay the order again.");
        }

        var stale = details.Expiration is DateTimeOffset expiration && expiration <= DateTimeOffset.UtcNow.AddMinutes(5);
        var notCapturable = !string.Equals(details.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(details.Status, "PENDING", StringComparison.OrdinalIgnoreCase);

        if (!stale && !notCapturable)
        {
            return authorizationId;
        }

        try
        {
            var renewed = await _payPalGateway.ReauthorizeAsync(
                authorizationId,
                new MoneyAmount(decimal.Round(order.Total(), 2), order.Payment.Currency),
                $"eshop-reauth-{OrderKey(order)}",
                cancellationToken);

            order.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Expiration);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Renewed PayPal authorization for order {OrderId} to {AuthorizationId}", order.Id, renewed.AuthorizationId);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new AuthorizationUnrenewableException(
                $"PayPal authorization {authorizationId} is stale and cannot be renewed ({ex.Message}). Ask the shopper to pay the order again.");
        }
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!order.BelongsTo(buyerId))
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        return order ?? throw new PaymentException($"Order {orderId} was not found.", 404);
    }

    private static string OrderKey(Order order) => $"{order.Id}-{order.OrderDate.UtcTicks}";
    private static string InvoiceId(Order order) => $"ORDER-{OrderKey(order)}";
    private static string CustomId(Order order) => order.Id.ToString();
}
