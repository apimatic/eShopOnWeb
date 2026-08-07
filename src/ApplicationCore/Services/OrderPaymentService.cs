using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places orders and drives their PayPal payment lifecycle. All PayPal interaction is delegated
/// to <see cref="IPayPalGateway"/>; this service owns ownership checks, idempotency and the
/// order aggregate's state transitions.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    /// <summary>Currency for all amounts, per the task (catalog prices are in USD).</summary>
    private const string CurrencyCode = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalGateway payPalGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _payPalGateway = payPalGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentProcessingException("An order must contain at least one item.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentProcessingException(
                    $"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentProcessingException($"Catalog item {line.CatalogItemId} was not found.");

            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri)
                ? "eCatalog-item-default.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation("Created order {0} for buyer with {1} line(s), total {2} {3}.",
            order.Id, orderItems.Count, order.Total(), CurrencyCode);

        return order;
    }

    public async Task<Order> PayOrderAsync(
        string buyerId,
        int orderId,
        CardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent: an already-paid order returns its existing result without charging again.
        if (order.PaymentStatus == OrderPaymentStatus.Paid)
        {
            _logger.LogInformation("Order {0} is already paid; returning existing payment.", orderId);
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new PaymentProcessingException($"Order {orderId} has been refunded and cannot be paid.");
        }

        // Resolve the payment source: a saved card (by our id) or one-off card details.
        CardPaymentSource source;
        string sourceDiscriminator;
        if (savedPaymentMethodId is int savedId)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(buyerId, savedId), cancellationToken)
                ?? throw new PaymentMethodNotFoundException(savedId);

            source = CardPaymentSource.FromVault(saved.PayPalVaultId);
            sourceDiscriminator = saved.PayPalVaultId;
        }
        else if (card is not null)
        {
            source = CardPaymentSource.FromCard(card);
            sourceDiscriminator = card.Number;
        }
        else
        {
            throw new PaymentProcessingException(
                "A payment requires either card details or a saved payment method id.");
        }

        // Deterministic key: identical retries dedupe at PayPal; a different card yields a new key.
        var idempotencyKey = IdempotencyKey.Derive("pay", orderId.ToString(), sourceDiscriminator);

        var result = await _payPalGateway.CreateAndCaptureOrderAsync(
            order.Total(), CurrencyCode, source, idempotencyKey, cancellationToken);

        if (!result.IsCompleted || result.CaptureId is null)
        {
            _logger.LogWarning(
                "Payment for order {0} did not complete (order status {1}, capture status {2}).",
                orderId, result.OrderStatus, result.CaptureStatus ?? "none");
            throw new PaymentProcessingException(
                $"Payment for order {orderId} was not completed (status: {result.CaptureStatus ?? result.OrderStatus}).");
        }

        order.MarkPaid(result.PayPalOrderId, result.CaptureId);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {0} paid; PayPal order {1}, capture {2}.",
            orderId, result.PayPalOrderId, result.CaptureId);

        return order;
    }

    public async Task<Order> RefundOrderAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent: an already-refunded order returns its existing result without refunding again.
        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            _logger.LogInformation("Order {0} is already refunded; returning existing refund.", orderId);
            return order;
        }

        if (order.PaymentStatus != OrderPaymentStatus.Paid || order.PayPalCaptureId is null)
        {
            throw new PaymentProcessingException($"Order {orderId} has not been paid and cannot be refunded.");
        }

        var idempotencyKey = IdempotencyKey.Derive("refund", orderId.ToString(), order.PayPalCaptureId);

        var result = await _payPalGateway.RefundCaptureAsync(
            order.PayPalCaptureId, idempotencyKey, cancellationToken);

        if (!result.IsCompleted)
        {
            _logger.LogWarning("Refund for order {0} did not complete (status {1}).", orderId, result.Status);
            throw new PaymentProcessingException(
                $"Refund for order {orderId} was not completed (status: {result.Status}).");
        }

        order.MarkRefunded(result.RefundId);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {0} refunded; PayPal refund {1}.", orderId, result.RefundId);

        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders.OrderByDescending(o => o.OrderDate).ToList();
    }

    public async Task<Order?> GetOrderForBuyerAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithItemsByIdSpec(orderId), cancellationToken);
        return order is not null && order.BuyerId == buyerId ? order : null;
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await GetOrderForBuyerAsync(buyerId, orderId, ct);
        return order ?? throw new OrderNotFoundException(orderId);
    }
}
