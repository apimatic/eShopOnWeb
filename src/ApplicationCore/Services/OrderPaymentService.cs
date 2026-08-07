using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // PayPal REST APIs settle in USD for this integration; amounts always come from catalog prices.
    private const string CurrencyCode = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
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

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));

        // Collapse duplicate item ids and reject non-positive quantities.
        var requested = lines
            .Where(l => l.Quantity > 0)
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        if (requested.Count == 0)
        {
            return new PlaceOrderResult(PlaceOrderOutcome.EmptyOrder, Error: "An order must contain at least one item with a positive quantity.");
        }

        var catalogSpec = new CatalogItemsSpecification(requested.Keys.ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogSpec, cancellationToken);

        var missing = requested.Keys.Where(id => catalogItems.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
        {
            return new PlaceOrderResult(PlaceOrderOutcome.CatalogItemNotFound, Error: $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = requested.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        // Shipping is out of scope for this payment-focused surface; use a placeholder address so the
        // existing Order model's required ShipToAddress is satisfied.
        var shipToAddress = new Address("123 Main St.", "Kents Hill", "KY", "USA", "12345");

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        return new PlaceOrderResult(PlaceOrderOutcome.Placed, order);
    }

    public async Task<PayOrderResult> PayOrderAsync(string buyerId, int orderId, OrderPaymentInput input, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(input, nameof(input));

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (order is null)
        {
            return new PayOrderResult(PayOrderOutcome.OrderNotFound);
        }

        // Idempotency by state: a repeated pay for an order already settled returns the existing
        // outcome without contacting PayPal again, so a double-click never double-charges.
        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            return new PayOrderResult(PayOrderOutcome.AlreadyPaid, order);
        }
        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            return new PayOrderResult(PayOrderOutcome.AlreadyRefunded, order);
        }

        var hasCard = input.Card is not null;
        var hasSaved = input.SavedPaymentMethodId is not null;
        if (hasCard == hasSaved)
        {
            return new PayOrderResult(PayOrderOutcome.InvalidRequest,
                Error: "Provide exactly one of card details or a saved payment method id.");
        }

        var amount = order.Total();
        // A per-order idempotency key, generated once and persisted, so retries of the same order's
        // payment reuse it and PayPal de-duplicates the create-and-capture at the source.
        var idempotencyKey = order.EnsurePaymentIdempotencyKey();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        GatewayPaymentResult result;
        if (hasCard)
        {
            result = await _paymentGateway.ChargeCardAsync(new CardChargeRequest
            {
                Amount = amount,
                CurrencyCode = CurrencyCode,
                Card = input.Card!,
                IdempotencyKey = idempotencyKey,
                CustomId = order.Id.ToString(),
                Description = $"eShopOnWeb order {order.Id}"
            }, cancellationToken);
        }
        else
        {
            var spec = new PaymentMethodByIdForOwnerSpecification(buyerId, input.SavedPaymentMethodId!.Value);
            var savedCard = await _paymentMethodRepository.FirstOrDefaultAsync(spec, cancellationToken);
            if (savedCard is null)
            {
                return new PayOrderResult(PayOrderOutcome.SavedCardNotFound,
                    Error: "The specified saved card was not found for this shopper.");
            }

            result = await _paymentGateway.ChargeVaultedCardAsync(new VaultedCardChargeRequest
            {
                Amount = amount,
                CurrencyCode = CurrencyCode,
                VaultToken = savedCard.VaultToken,
                IdempotencyKey = idempotencyKey,
                CustomId = order.Id.ToString(),
                Description = $"eShopOnWeb order {order.Id}"
            }, cancellationToken);
        }

        if (!result.Success || string.IsNullOrEmpty(result.CaptureId) || string.IsNullOrEmpty(result.PayPalOrderId))
        {
            _logger.LogWarning($"Payment failed for order {order.Id}. Status: {result.Status ?? "n/a"}, DebugId: {result.DebugId ?? "n/a"}");
            return new PayOrderResult(PayOrderOutcome.PaymentFailed, order,
                Error: result.ErrorMessage ?? "The payment could not be completed.");
        }

        order.MarkAsPaid(result.PayPalOrderId!, result.CaptureId!);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return new PayOrderResult(PayOrderOutcome.Paid, order);
    }

    public async Task<RefundOrderResult> RefundOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (order is null)
        {
            return new RefundOrderResult(RefundOrderOutcome.OrderNotFound);
        }

        // Idempotent: an already-refunded order returns the existing refund without issuing another.
        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            return new RefundOrderResult(RefundOrderOutcome.AlreadyRefunded, order);
        }
        if (order.PaymentStatus != PaymentStatus.Paid || string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            return new RefundOrderResult(RefundOrderOutcome.NotPaid, order,
                Error: "Only a paid order can be refunded.");
        }

        var idempotencyKey = order.EnsureRefundIdempotencyKey();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        var result = await _paymentGateway.RefundAsync(order.PayPalCaptureId!, idempotencyKey, cancellationToken);

        if (!result.Success || string.IsNullOrEmpty(result.RefundId))
        {
            _logger.LogWarning($"Refund failed for order {order.Id}. Status: {result.Status ?? "n/a"}, DebugId: {result.DebugId ?? "n/a"}");
            return new RefundOrderResult(RefundOrderOutcome.RefundFailed, order,
                Error: result.ErrorMessage ?? "The refund could not be completed.");
        }

        order.MarkAsRefunded(result.RefundId!);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return new RefundOrderResult(RefundOrderOutcome.Refunded, order);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var spec = new CustomerOrdersWithItemsSpecification(buyerId);
        return await _orderRepository.ListAsync(spec, cancellationToken);
    }

    private async Task<Order?> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        // Treat another shopper's order as not found, so ownership is never leaked.
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }
        return order;
    }
}
