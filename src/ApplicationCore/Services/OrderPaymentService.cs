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

public class OrderPaymentService : IOrderPaymentService
{
    // All amounts in this integration are in US dollars.
    private const string Currency = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IBuyerService _buyerService;
    private readonly IPaymentGateway _paymentGateway;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IBuyerService buyerService,
        IPaymentGateway paymentGateway)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _buyerService = buyerService;
        _paymentGateway = paymentGateway;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId, Address shipToAddress, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidPaymentRequestException("An order must contain at least one item.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new InvalidPaymentRequestException("Every order line must have a quantity of at least 1.");
        }

        // Price from the catalog — never trust client-supplied prices.
        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidPaymentRequestException(
                $"The following catalog item id(s) do not exist: {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<PayOrderResult> PayOrderAsync(
        string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent in effect: a repeated pay on an already-paid order returns the existing state
        // rather than charging again.
        if (order.PaymentStatus == OrderPaymentStatus.Paid)
        {
            return new PayOrderResult(order, null, null, AlreadyPaid: true);
        }

        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new PaymentConflictException("This order has already been refunded and cannot be paid again.");
        }

        var hasCard = card is not null;
        var hasSaved = savedPaymentMethodId is not null;
        if (hasCard == hasSaved)
        {
            throw new InvalidPaymentRequestException(
                "Provide either card details or a saved paymentMethodId to pay with — exactly one, not both.");
        }

        var amount = order.Total();
        // Deterministic per-order idempotency key (based on the order's globally-unique payment
        // reference, not its sequential id): PayPal will not create a second charge for the same order
        // even if this request is retried or double-clicked, and the key never collides across runs.
        var idempotencyKey = $"pay-{order.PaymentReference}";

        CardChargeResult charge;
        if (hasSaved)
        {
            var buyer = await _buyerService.GetBuyerAsync(buyerId, cancellationToken);
            var paymentMethod = buyer?.GetPaymentMethod(savedPaymentMethodId!.Value)
                ?? throw new PaymentMethodNotFoundException(savedPaymentMethodId!.Value);

            charge = await _paymentGateway.ChargeVaultedCardAsync(amount, Currency, paymentMethod.VaultId, idempotencyKey, cancellationToken);
        }
        else
        {
            charge = await _paymentGateway.ChargeCardAsync(amount, Currency, card!, idempotencyKey, cancellationToken);
        }

        order.MarkPaid(charge.PayPalOrderId, charge.CaptureId);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return new PayOrderResult(order, charge.CardBrand, charge.Last4, AlreadyPaid: false);
    }

    public async Task<RefundOrderResult> RefundOrderAsync(
        string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent in effect: a repeated refund returns the existing state rather than refunding again.
        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            return new RefundOrderResult(order, order.PayPalRefundId, AlreadyRefunded: true);
        }

        if (order.PaymentStatus != OrderPaymentStatus.Paid || order.PayPalCaptureId is null)
        {
            throw new PaymentConflictException("Only a paid order can be refunded.");
        }

        var idempotencyKey = $"refund-{order.PaymentReference}";
        var refund = await _paymentGateway.RefundCaptureAsync(order.PayPalCaptureId, idempotencyKey, cancellationToken);

        order.MarkRefunded(refund.RefundId);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return new RefundOrderResult(order, refund.RefundId, AlreadyRefunded: false);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);

        // A missing order and someone else's order are treated identically so ownership cannot be probed.
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }
}
