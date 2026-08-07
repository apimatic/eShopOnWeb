using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private const string Currency = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPalGateway;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPalGateway)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPalGateway = payPalGateway;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines, Address? shipToAddress = null, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));

        // Collapse duplicate item ids into a single line so quantities add up correctly.
        var requestedQuantities = lines
            .Where(l => l.Quantity > 0 && l.CatalogItemId > 0)
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        if (requestedQuantities.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one catalog item with a positive quantity.", nameof(lines));
        }

        var catalogItemsSpec = new CatalogItemsSpecification(requestedQuantities.Keys.ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpec, cancellationToken);

        var missing = requestedQuantities.Keys.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Any())
        {
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", nameof(lines));
        }

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            // Price comes from the catalog, never from the caller.
            return new OrderItem(itemOrdered, catalogItem.Price, requestedQuantities[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultAddress(), orderItems);

        await _orderRepository.AddAsync(order, cancellationToken);

        return order;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(instruction, nameof(instruction));

        if (!instruction.IsValid)
        {
            throw new ArgumentException("Provide exactly one payment source: either card details or a saved payment method id.", nameof(instruction));
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotency in effect: an already-paid order is returned unchanged, so a double-click cannot
        // produce a second charge.
        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            return order;
        }

        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            throw new PaymentFailedException($"Order {orderId} has been refunded and can no longer be paid.");
        }

        var amount = order.Total();
        if (amount <= 0m)
        {
            throw new PaymentFailedException($"Order {orderId} has no payable amount.");
        }

        // Stable, globally-unique idempotency key for this order's payment, so PayPal de-duplicates a
        // double-click while never colliding with another order or a previous run.
        var idempotencyKey = $"pay-{order.PaymentIdempotencyToken:N}";

        PayPalCaptureResult capture;
        if (instruction.HasSavedCard)
        {
            var savedCard = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(instruction.SavedPaymentMethodId!.Value, buyerId), cancellationToken);

            if (savedCard is null)
            {
                throw new PaymentMethodNotFoundException($"Saved payment method {instruction.SavedPaymentMethodId} was not found.");
            }

            capture = await _payPalGateway.CaptureWithVaultedCardAsync(amount, Currency, savedCard.PayPalVaultId, idempotencyKey, cancellationToken);
        }
        else
        {
            capture = await _payPalGateway.CaptureWithCardAsync(amount, Currency, instruction.Card!, idempotencyKey, cancellationToken);
        }

        order.MarkPaid(capture.PayPalOrderId, capture.CaptureId);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return order;
    }

    public async Task<Order> RefundOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotency in effect: an already-refunded order is returned unchanged.
        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            return order;
        }

        if (order.PaymentStatus != PaymentStatus.Paid || string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            throw new PaymentFailedException($"Order {orderId} cannot be refunded because it has not been paid.");
        }

        // Key the refund on the globally-unique capture id so a double refund de-duplicates at PayPal and
        // never collides with another order or a previous run.
        var idempotencyKey = $"refund-{order.PayPalCaptureId}";
        var refund = await _payPalGateway.RefundCaptureAsync(order.PayPalCaptureId!, idempotencyKey, cancellationToken);

        order.MarkRefunded(refund.RefundId);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new CustomerOrderByIdSpecification(orderId, buyerId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private static Address DefaultAddress() =>
        new Address("N/A", "N/A", "N/A", "US", "00000");
}
