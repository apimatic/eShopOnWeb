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
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShipTo =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address? shipTo,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.");
        }

        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException("Quantity must be greater than zero.");
            }
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new PaymentException("One or more catalog items were not found.");
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

        var order = new Order(buyerId, shipTo ?? DefaultShipTo, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentInput? card,
        int? paymentMethodId,
        CancellationToken cancellationToken)
    {
        var order = await GetOwnedOrder(buyerId, orderId, cancellationToken);

        if (order.Status is OrderPaymentStatus.Authorized or OrderPaymentStatus.Fulfilled)
        {
            return order;
        }

        if (order.Status != OrderPaymentStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException($"Order {order.Id} cannot be paid from status {order.Status}.");
        }

        if (card is not null && paymentMethodId is not null)
        {
            throw new PaymentException("Provide either card details or a saved payment method, not both.");
        }

        string? vaultId = null;
        if (paymentMethodId is not null)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId.Value, buyerId), cancellationToken);
            if (saved is null)
            {
                throw new PaymentMethodNotFoundException(paymentMethodId.Value);
            }

            vaultId = saved.VaultId;
        }
        else if (card is null)
        {
            throw new PaymentException("Card details or a saved payment method is required.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var auth = await _paymentGateway.AuthorizeAsync(
            order.Id,
            order.Total(),
            card,
            vaultId,
            $"eshop-order-{order.Id}-authorize",
            cts.Token);

        order.RecordAuthorization(
            auth.PaypalOrderId,
            auth.AuthorizationId,
            auth.Status,
            auth.ExpiresAt,
            _paymentGateway.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.Status == OrderPaymentStatus.Fulfilled)
        {
            return order;
        }

        if (order.Status != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(order.AuthorizationId))
        {
            throw new InvalidOrderStateException($"Order {order.Id} cannot be fulfilled from status {order.Status}.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var authorizationId = order.AuthorizationId;
        if (order.IsAuthorizationExpired(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15)))
        {
            authorizationId = await RenewAuthorization(order, cts.Token);
        }

        CaptureResult capture;
        try
        {
            capture = await _paymentGateway.CaptureAsync(
                authorizationId,
                $"eshop-order-{order.Id}-capture",
                cts.Token);
        }
        catch (PaymentException) when (!order.IsAuthorizationExpired(DateTimeOffset.UtcNow, TimeSpan.Zero))
        {
            authorizationId = await RenewAuthorization(order, cts.Token);
            capture = await _paymentGateway.CaptureAsync(
                authorizationId,
                $"eshop-order-{order.Id}-capture",
                cts.Token);
        }

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetAmount);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);
        if (order.Status == OrderPaymentStatus.Cancelled)
        {
            return order;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        if (order.Status == OrderPaymentStatus.Authorized && !string.IsNullOrEmpty(order.AuthorizationId))
        {
            await _paymentGateway.VoidAsync(
                order.AuthorizationId,
                $"eshop-order-{order.Id}-void",
                cts.Token);
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await GetOwnedOrder(buyerId, orderId, cancellationToken);

        if (order.FindRefundByIdempotencyKey(idempotencyKey) is not null)
        {
            return order;
        }

        if (string.IsNullOrEmpty(order.CaptureId))
        {
            throw new InvalidOrderStateException($"Order {order.Id} has no captured payment to refund.");
        }

        var refundAmount = amount ?? order.RemainingRefundable();
        if (refundAmount > order.RemainingRefundable())
        {
            throw new RefundLimitException(
                $"Refund of {refundAmount:0.00} exceeds the remaining captured amount {order.RemainingRefundable():0.00}.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        var result = await _paymentGateway.RefundAsync(
            order.CaptureId,
            amount,
            $"eshop-order-{order.Id}-refund-{idempotencyKey}",
            cts.Token);

        order.RecordRefund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public Task<Order> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken) =>
        GetOwnedOrder(buyerId, orderId, cancellationToken);

    private async Task<string> RenewAuthorization(Order order, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Renewing PayPal authorization for order {0}", order.Id);
        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(
                order.AuthorizationId!,
                order.Total(),
                $"eshop-order-{order.Id}-reauth",
                cancellationToken);

            order.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new StaleAuthorizationException(
                "The payment hold on this order has expired and PayPal could not renew it. " +
                "Ask the shopper to authorize the order again before fulfilment. " +
                ex.Message);
        }
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        return order ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<Order> GetOwnedOrder(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenOrderAccessException();
        }

        return order;
    }
}
