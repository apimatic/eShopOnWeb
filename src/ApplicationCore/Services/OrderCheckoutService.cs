using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderCheckoutService : IOrderCheckoutService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PaymentSettings _paymentSettings;
    private readonly IUriComposer _uriComposer;

    public OrderCheckoutService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IPaymentGateway paymentGateway,
        PaymentSettings paymentSettings,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _paymentGateway = paymentGateway;
        _paymentSettings = paymentSettings;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, Address shipToAddress, IReadOnlyList<OrderItemRequest> items,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items.Count == 0) throw new EmptyOrderException();

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), ct);

        var orderItems = items.Select(requestedItem =>
        {
            Guard.Against.NegativeOrZero(requestedItem.Quantity, nameof(requestedItem.Quantity));
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == requestedItem.CatalogItemId)
                ?? throw new CatalogItemNotFoundException(requestedItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, requestedItem.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);
        return order;
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId,
        CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException(orderId);

        if (order.BuyerId != buyerId)
            throw new ForbiddenAccessException($"Order {orderId} does not belong to the current user.");

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            if (order.Status == OrderStatus.Cancelled)
                throw new InvalidOrderStateException(orderId, order.Status, "pay for");

            // Already authorized (or further along) - a double-click or retry replays the existing state.
            return order;
        }

        string? vaultId = null;
        int? paymentMethodEntityId = null;

        if (paymentMethodId.HasValue)
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct)
                ?? throw new PaymentMethodNotFoundException(paymentMethodId.Value);
            var paymentMethod = buyer.GetPaymentMethod(paymentMethodId.Value);
            vaultId = paymentMethod.VaultId;
            paymentMethodEntityId = paymentMethod.Id;
        }
        else
        {
            Guard.Against.Null(card, nameof(card));
        }

        var amount = order.Total();
        var idempotencyKey = $"authorize-order-{order.PaymentIdempotencySeed:N}";
        var authorization = await _paymentGateway.AuthorizeAsync(amount, _paymentSettings.Currency, card, vaultId, idempotencyKey, ct);

        var payment = new OrderPayment(orderId, _paymentSettings.Currency, amount, paymentMethodEntityId,
            authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status,
            authorization.CreatedAt, authorization.ExpiresAt);

        order.AttachPayment(payment);
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException(orderId);

        if (order.BuyerId != buyerId)
            throw new ForbiddenAccessException($"Order {orderId} does not belong to the current user.");

        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), ct);
        return orders;
    }
}
