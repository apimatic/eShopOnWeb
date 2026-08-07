using System;
using System.Collections.Concurrent;
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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Coordinates the domain (<see cref="Order"/>), the PayPal gateway and persistence to place, pay and
/// refund orders. Idempotency is enforced on two levels: the order's <see cref="OrderPaymentStatus"/>
/// short-circuits a repeat pay/refund, and a stable per-order PayPal idempotency key makes the provider
/// itself dedupe any request that still slips through (e.g. a retry after a lost response). A per-order
/// in-process lock serialises concurrent attempts on the same order (this app runs single-host).
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private const string DefaultShipToStreet = "123 Main St.";
    private const string DefaultShipToCity = "Kent";
    private const string DefaultShipToState = "OH";
    private const string DefaultShipToCountry = "United States";
    private const string DefaultShipToZip = "44240";

    // Serialises payment operations per order id so a double-click cannot start two charges/refunds at once.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<PaymentMethod> paymentMethodRepository,
        IPayPalPaymentGateway paymentGateway,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        // Consolidate duplicate lines for the same catalog item into a single order item.
        var quantities = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);

        var missing = quantities.Keys.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = quantities.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            // Price comes from the catalog (USD), per requirements.
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var shipToAddress = new Address(DefaultShipToStreet, DefaultShipToCity, DefaultShipToState,
            DefaultShipToCountry, DefaultShipToZip);

        var order = new Order(buyerId, shipToAddress, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, PaymentInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (!instruction.IsValid)
        {
            throw new PaymentException("Provide either card details or a saved card id to pay — exactly one is required.");
        }

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(
                new OrderWithItemsByIdAndBuyerSpec(orderId, buyerId), cancellationToken);
            if (order is null)
            {
                throw new OrderNotFoundException(orderId);
            }

            // Idempotency: never charge an order twice. A repeat pay of an already-paid order returns
            // the existing payment; refunded orders cannot be paid again.
            if (order.PaymentStatus == OrderPaymentStatus.Paid)
            {
                return order;
            }
            if (order.PaymentStatus == OrderPaymentStatus.Refunded)
            {
                throw new PaymentException($"Order {orderId} has been refunded and cannot be paid again.");
            }

            var amount = Money.Usd(order.Total());
            // Stable per-order key: any retry of THIS order's payment maps to the same PayPal request, so
            // the provider dedupes it and no double charge is possible. The order's immutable creation
            // instant is included so the key is globally unique to this order even when order ids are reused
            // (as they are with the in-memory provider, which resets ids on restart) — without it, a fresh
            // order that happens to reuse an id could collide with a prior order's cached PayPal result.
            var idempotencyKey = PaymentIdempotencyKey("pay", order);

            PaymentAuthorization authorization;
            string cardDescription;

            if (instruction.UsesSavedCard)
            {
                var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
                    new PaymentMethodByIdAndBuyerSpecification(instruction.SavedPaymentMethodId!.Value, buyerId),
                    cancellationToken);
                if (paymentMethod is null)
                {
                    throw new PaymentException($"Saved card {instruction.SavedPaymentMethodId} was not found.");
                }

                authorization = await _paymentGateway.ChargeVaultedCardAsync(
                    amount, paymentMethod.CardId, idempotencyKey, cancellationToken);
                cardDescription = paymentMethod.Description;
            }
            else
            {
                authorization = await _paymentGateway.ChargeCardAsync(
                    amount, instruction.Card!, idempotencyKey, cancellationToken);
                cardDescription = DescribeCard(authorization.Card);
            }

            order.MarkPaid(authorization.PayPalOrderId, authorization.CaptureId, cardDescription);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> RefundOrderAsync(string buyerId, int orderId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(
                new OrderWithItemsByIdAndBuyerSpec(orderId, buyerId), cancellationToken);
            if (order is null)
            {
                throw new OrderNotFoundException(orderId);
            }

            // Idempotency: a repeat refund of an already-refunded order returns the existing refund.
            if (order.PaymentStatus == OrderPaymentStatus.Refunded)
            {
                return order;
            }
            if (order.PaymentStatus != OrderPaymentStatus.Paid || string.IsNullOrEmpty(order.PayPalCaptureId))
            {
                throw new PaymentException($"Order {orderId} has not been paid and cannot be refunded.");
            }

            var idempotencyKey = PaymentIdempotencyKey("refund", order);
            var receipt = await _paymentGateway.RefundAsync(order.PayPalCaptureId!, idempotencyKey, cancellationToken);

            order.MarkRefunded(receipt.RefundId);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    // A PayPal-Request-Id stable for a given order (so retries dedupe) yet unique across distinct orders
    // even when order ids are reused (the order's creation instant disambiguates them).
    private static string PaymentIdempotencyKey(string operation, Order order) =>
        $"{operation}-order-{order.Id}-{order.OrderDate.UtcTicks}";

    private static string DescribeCard(CardDisplay card)
    {
        var brand = string.IsNullOrWhiteSpace(card.Brand) ? "Card" : card.Brand;
        var expiry = card.ExpiryMonth is > 0 && card.ExpiryYear is > 0
            ? $" (exp {card.ExpiryMonth:00}/{card.ExpiryYear})"
            : string.Empty;
        return $"{brand} ending in {card.Last4}{expiry}";
    }
}
