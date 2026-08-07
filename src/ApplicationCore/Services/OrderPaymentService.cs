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
    public const string CurrencyCode = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
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

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new InvalidPaymentRequestException($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation("Placed order {0} for buyer with {1} line(s), total {2} {3}.",
            order.Id, items.Count, order.Total(), CurrencyCode);

        return order;
    }

    public async Task<Order> PayOrderAsync(int orderId, string buyerId, PaymentInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(instruction, nameof(instruction));

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdWithItemsForBuyerSpecification(orderId, buyerId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        // Idempotent in effect: a repeat pay for an already-paid order returns the existing result
        // rather than charging again.
        if (order.PaymentStatus == OrderPaymentStatus.Paid)
        {
            _logger.LogInformation("Order {0} is already paid; returning existing payment.", order.Id);
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new PaymentStateException($"Order {order.Id} has been refunded and cannot be paid.");
        }

        var amount = order.Total();
        if (amount <= 0m)
        {
            throw new PaymentStateException($"Order {order.Id} has a non-positive total and cannot be charged.");
        }

        // Stable per-order idempotency key: PayPal de-dupes a double-clicked pay via PayPal-Request-Id.
        var idempotencyKey = $"pay-{order.PaymentReference}";

        PaymentCaptureResult capture = await ChargeAsync(order, buyerId, instruction, amount, idempotencyKey, cancellationToken);

        if (!capture.IsCompleted)
        {
            throw new PaymentGatewayException(
                $"PayPal did not complete the payment (capture status: {capture.CaptureStatus}).", 402);
        }

        order.MarkPaid(capture.PayPalOrderId, capture.CaptureId, DateTimeOffset.UtcNow);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {0} paid via PayPal capture {1}.", order.Id, capture.CaptureId);
        return order;
    }

    private async Task<PaymentCaptureResult> ChargeAsync(Order order, string buyerId, PaymentInstruction instruction,
        decimal amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        var hasCard = instruction.Card is not null;
        var hasSaved = instruction.SavedPaymentMethodId.HasValue;

        if (hasCard == hasSaved)
        {
            throw new InvalidPaymentRequestException(
                "A payment must specify exactly one of a card or a saved payment method.");
        }

        if (instruction.UsesSavedCard)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdForBuyerSpecification(instruction.SavedPaymentMethodId!.Value, buyerId),
                cancellationToken);
            if (savedCard is null)
            {
                throw new SavedPaymentMethodNotFoundException(instruction.SavedPaymentMethodId!.Value);
            }

            return await _paymentGateway.ChargeVaultedCardAsync(amount, CurrencyCode, savedCard.PayPalVaultId,
                idempotencyKey, cancellationToken);
        }

        return await _paymentGateway.ChargeCardAsync(amount, CurrencyCode, instruction.Card!, idempotencyKey,
            cancellationToken);
    }

    public async Task<Order> RefundOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdWithItemsForBuyerSpecification(orderId, buyerId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        // Idempotent in effect: a repeat refund of an already-refunded order returns the existing result.
        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            _logger.LogInformation("Order {0} is already refunded; returning existing refund.", order.Id);
            return order;
        }

        if (order.PaymentStatus != OrderPaymentStatus.Paid || string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            throw new PaymentStateException($"Order {order.Id} is not paid and cannot be refunded.");
        }

        var idempotencyKey = $"refund-{order.PaymentReference}";
        var refund = await _paymentGateway.RefundCaptureAsync(order.PayPalCaptureId!, idempotencyKey, cancellationToken);

        if (!refund.IsCompleted)
        {
            throw new PaymentGatewayException(
                $"PayPal did not complete the refund (status: {refund.RefundStatus}).", 502);
        }

        order.MarkRefunded(refund.RefundId, DateTimeOffset.UtcNow);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {0} refunded via PayPal refund {1}.", order.Id, refund.RefundId);
        return order;
    }
}
