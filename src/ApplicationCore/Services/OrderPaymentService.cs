using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // All amounts in this integration are settled in US dollars, per the task's fixed currency.
    public const string Currency = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<PaymentMethod> paymentMethodRepository,
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

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IEnumerable<(int CatalogItemId, int Quantity)> items,
        Address shipToAddress,
        CancellationToken cancellationToken = default)
    {
        var lines = items?.ToList() ?? new List<(int, int)>();
        if (lines.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentValidationException("Every item quantity must be greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var itemsById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (!itemsById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new PaymentValidationException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri)) pictureUri = "eCatalog-item-default.png";

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation($"Placed order {order.Id} for buyer {buyerId} awaiting payment; total {order.Total():0.00} {Currency}.");
        return order;
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdAndBuyerSpecification(orderId, buyerId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        // Idempotency: an already-paid order is never charged twice.
        if (order.PaymentStatus == PaymentStatus.Paid)
        {
            _logger.LogInformation($"Order {orderId} is already paid; returning existing payment (no re-charge).");
            return order;
        }
        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} has been refunded and cannot be paid again.");
        }

        // Resolve the payment instrument: exactly one of a one-off card or a saved card.
        if (card is not null && savedPaymentMethodId.HasValue)
        {
            throw new PaymentValidationException("Provide either card details or a saved card id, not both.");
        }

        string? vaultTokenId = null;
        if (savedPaymentMethodId.HasValue)
        {
            var paymentMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdAndBuyerSpecification(savedPaymentMethodId.Value, buyerId), cancellationToken);
            if (paymentMethod is null)
            {
                // Scoped to the buyer, so a missing/foreign saved card is an invalid request for this payer.
                throw new PaymentValidationException($"Saved card {savedPaymentMethodId.Value} was not found.");
            }
            vaultTokenId = paymentMethod.VaultTokenId;
        }
        else if (card is null)
        {
            throw new PaymentValidationException("Provide card details or a saved card id to pay for the order.");
        }

        var amount = order.Total();
        if (amount <= 0m)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} has a non-positive total and cannot be charged.");
        }

        // Deterministic idempotency key = order + instrument fingerprint. A double-click (same order, same
        // instrument) reuses the key so PayPal never double-charges; a genuine retry with a corrected card
        // yields a new key and is allowed to proceed.
        var idempotencyKey = $"eshop-pay-{order.Id}-{InstrumentFingerprint(vaultTokenId, card)}";
        var chargeRequest = new ChargeCardRequest(amount, Currency, idempotencyKey, card, vaultTokenId);

        PaymentResult result;
        try
        {
            result = await _paymentGateway.ChargeAsync(chargeRequest, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning($"Payment for order {orderId} was rejected by PayPal: {ex.ErrorName} (debug_id: {ex.DebugId}).");
            order.MarkPaymentFailed(null);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw;
        }

        if (!result.IsCompleted)
        {
            _logger.LogWarning($"Payment for order {orderId} did not complete; capture status {result.CaptureStatus}.");
            order.MarkPaymentFailed(result.PayPalOrderId);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw new PaymentGatewayException(
                $"PayPal did not complete the capture for order {orderId} (status: {result.CaptureStatus}).",
                errorName: result.CaptureStatus);
        }

        order.MarkAsPaid(result.PayPalOrderId, result.CaptureId);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} paid; PayPal order {result.PayPalOrderId}, capture {result.CaptureId}.");
        return order;
    }

    public async Task<Order> RefundAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdAndBuyerSpecification(orderId, buyerId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        // Idempotency: an already-refunded order is never refunded twice.
        if (order.PaymentStatus == PaymentStatus.Refunded)
        {
            _logger.LogInformation($"Order {orderId} is already refunded; returning existing refund (no re-refund).");
            return order;
        }
        if (order.PaymentStatus != PaymentStatus.Paid || string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            throw new InvalidPaymentOperationException($"Order {orderId} is not paid and cannot be refunded.");
        }

        var idempotencyKey = $"eshop-refund-{order.Id}";
        var refund = await _paymentGateway.RefundAsync(order.PayPalCaptureId!, idempotencyKey, cancellationToken);

        if (!refund.IsSuccessful)
        {
            throw new PaymentGatewayException(
                $"PayPal did not complete the refund for order {orderId} (status: {refund.Status}).",
                errorName: refund.Status);
        }

        order.MarkAsRefunded(refund.RefundId);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} refunded; PayPal refund {refund.RefundId}.");
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    /// <summary>
    /// A short, non-reversible fingerprint of the payment instrument, used only to build the PayPal
    /// idempotency key. Derived from the saved-card token or a one-off PAN; the PAN is hashed and
    /// immediately discarded — it is never stored or logged.
    /// </summary>
    private static string InstrumentFingerprint(string? vaultTokenId, CardDetails? card)
    {
        var material = vaultTokenId ?? card?.Number ?? string.Empty;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
    }
}
