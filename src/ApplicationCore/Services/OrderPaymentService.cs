using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates placing, paying for, and refunding orders. Payment amounts always come from catalog
/// prices (USD), never the caller. Payment and refund are idempotent: the order's own state guards
/// against a second charge/refund, and the PayPal-Request-Id passed to the gateway guards against
/// duplicates racing past that check.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private const string CurrencyCode = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<Entities.BuyerAggregate.PaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPalGateway;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<Entities.BuyerAggregate.PaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPalGateway)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPalGateway = payPalGateway;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        if (lines is null || lines.Count == 0)
        {
            throw new PaymentInputException("An order must contain at least one item.");
        }

        // Collapse duplicate catalog item ids into a single line with summed quantity.
        var requestedQuantities = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentInputException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
            requestedQuantities[line.CatalogItemId] = requestedQuantities.GetValueOrDefault(line.CatalogItemId) + line.Quantity;
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(requestedQuantities.Keys.ToArray()), cancellationToken);

        var missing = requestedQuantities.Keys.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Any())
        {
            throw new PaymentInputException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = requestedQuantities.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(command, nameof(command));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderForBuyerWithItemsSpec(buyerId, orderId), cancellationToken)
                    ?? throw new OrderNotFoundException(orderId);

        // Idempotency guard: an already-paid order is returned unchanged so a double-click never
        // triggers a second charge.
        if (order.PaymentStatus == OrderPaymentStatus.Paid)
        {
            return order;
        }
        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} has already been refunded and cannot be paid again.");
        }

        var source = await ResolvePaymentSourceAsync(buyerId, command, cancellationToken);

        var captured = await _payPalGateway.CaptureCardPaymentAsync(
            order.Total(),
            CurrencyCode,
            source,
            idempotencyKey: PaymentIdempotencyKey(order, "pay"),
            orderReference: orderId.ToString(),
            cancellationToken);

        if (!captured.IsCompleted)
        {
            throw new PayPalApiException($"PayPal did not complete the payment for order {orderId} (status: {captured.Status}).");
        }

        order.MarkAsPaid(captured.PayPalOrderId, captured.CaptureId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> RefundOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderForBuyerWithItemsSpec(buyerId, orderId), cancellationToken)
                    ?? throw new OrderNotFoundException(orderId);

        // Idempotency guard: an already-refunded order is returned unchanged.
        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            return order;
        }
        if (order.PaymentStatus != OrderPaymentStatus.Paid || string.IsNullOrEmpty(order.PaymentCaptureId))
        {
            throw new InvalidPaymentOperationException($"Order {orderId} cannot be refunded because it has not been paid.");
        }

        var outcome = await _payPalGateway.RefundCaptureAsync(
            order.PaymentCaptureId,
            idempotencyKey: PaymentIdempotencyKey(order, "refund"),
            cancellationToken);

        if (!outcome.IsCompleted)
        {
            throw new PayPalApiException($"PayPal did not complete the refund for order {orderId} (status: {outcome.Status}).");
        }

        order.MarkAsRefunded(outcome.RefundId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders.OrderByDescending(o => o.OrderDate).ToList();
    }

    // Idempotency key that is stable for a given order within a run but differs across runs, so a
    // double-click is deduplicated by PayPal while a reused order id from a fresh in-memory database
    // (which restarts ids at 1) never collides with a prior run's cached request.
    private static string PaymentIdempotencyKey(Order order, string operation)
        => $"order-{order.Id}-{order.OrderDate.UtcTicks}-{operation}";

    private async Task<CardPaymentSource> ResolvePaymentSourceAsync(string buyerId, PayOrderCommand command, CancellationToken cancellationToken)
    {
        var hasCard = command.Card is not null;
        var hasSaved = command.SavedPaymentMethodId is not null;

        if (hasCard == hasSaved)
        {
            throw new PaymentInputException("Provide exactly one of card details or a saved payment method id to pay.");
        }

        if (hasSaved)
        {
            var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodForBuyerSpecification(buyerId, command.SavedPaymentMethodId!.Value), cancellationToken)
                ?? throw new PaymentMethodNotFoundException(command.SavedPaymentMethodId.Value);

            return new VaultedCardSource(paymentMethod.VaultId);
        }

        CardValidation.Validate(command.Card!);
        return new RawCardSource(command.Card!);
    }
}
