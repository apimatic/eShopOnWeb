using System;
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

public class OrderPaymentService : IOrderPaymentService
{
    private const string CurrencyCode = "USD";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IPayPalPaymentGateway gateway,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        if (lines is null || lines.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one item.");
        }

        // Combine duplicate lines and validate quantities.
        var quantities = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
            quantities[line.CatalogItemId] = quantities.TryGetValue(line.CatalogItemId, out var q) ? q + line.Quantity : line.Quantity;
        }

        var ids = quantities.Keys.ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var (catalogItemId, quantity) in quantities)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == catalogItemId)
                ?? throw new PaymentValidationException($"Catalog item {catalogItemId} does not exist.");

            // Snapshot name/price/picture so the order is unaffected by later catalog changes. Price is authoritative.
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order?> PayOrderAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdAndBuyerSpecification(orderId, buyerId), cancellationToken);
        if (order is null)
        {
            return null;
        }

        // Idempotent in effect: never charge a second time for an already-paid order.
        if (order.PaymentStatus == OrderPaymentStatus.Paid)
        {
            return order;
        }
        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new PaymentValidationException($"Order {orderId} has been refunded and cannot be paid again.");
        }

        ValidateInstruction(instruction);

        var amount = order.Total();
        if (amount <= 0m)
        {
            throw new PaymentValidationException("Order total must be greater than zero to take a payment.");
        }

        var idempotencyKey = order.PaymentIdempotencyToken.ToString("N");
        var invoiceId = $"eshop-order-{order.Id}-{idempotencyKey[..8]}";

        CardChargeResult charge;
        try
        {
            if (instruction.SavedPaymentMethodId is int savedId)
            {
                var vaultId = await ResolveSavedCardVaultIdAsync(buyerId, savedId, cancellationToken);
                charge = await _gateway.ChargeWithVaultedCardAsync(amount, CurrencyCode, vaultId, idempotencyKey, invoiceId, cancellationToken);
            }
            else
            {
                charge = await _gateway.ChargeWithCardAsync(amount, CurrencyCode, instruction.Card!, idempotencyKey, invoiceId, cancellationToken);
            }
        }
        catch (PaymentValidationException)
        {
            throw;
        }
        catch
        {
            order.MarkPaymentFailed();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw;
        }

        order.MarkPaid(charge.PayPalOrderId, charge.CaptureId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order?> RefundOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdAndBuyerSpecification(orderId, buyerId), cancellationToken);
        if (order is null)
        {
            return null;
        }

        // Idempotent in effect: an already-refunded order is returned unchanged (no double refund).
        if (order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            return order;
        }
        if (order.PaymentStatus != OrderPaymentStatus.Paid || string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            throw new PaymentValidationException($"Order {orderId} is not paid and cannot be refunded (status: {order.PaymentStatus}).");
        }

        var idempotencyKey = order.PaymentIdempotencyToken.ToString("N") + "-refund";
        var refund = await _gateway.RefundCaptureAsync(order.PayPalCaptureId, idempotencyKey, cancellationToken);

        order.MarkRefunded(refund.RefundId);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders.OrderByDescending(o => o.OrderDate).ToList();
    }

    private static void ValidateInstruction(PaymentInstruction instruction)
    {
        var hasCard = instruction.Card is not null;
        var hasSaved = instruction.SavedPaymentMethodId is not null;

        if (hasCard == hasSaved)
        {
            throw new PaymentValidationException("Provide exactly one of: card details or a saved card id.");
        }

        if (hasCard)
        {
            var card = instruction.Card!;
            if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.ExpiryMonthYear)
                || string.IsNullOrWhiteSpace(card.SecurityCode) || string.IsNullOrWhiteSpace(card.CardholderName))
            {
                throw new PaymentValidationException("Card number, expiry (YYYY-MM), security code and cardholder name are required.");
            }
        }
    }

    private async Task<string> ResolveSavedCardVaultIdAsync(string buyerId, int savedPaymentMethodId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var paymentMethod = buyer?.FindPaymentMethod(savedPaymentMethodId);
        if (paymentMethod is null || string.IsNullOrEmpty(paymentMethod.CardId))
        {
            // Unknown to this shopper (never existed, deleted, or belongs to another shopper).
            throw new PaymentValidationException($"Saved card {savedPaymentMethodId} was not found for this shopper.");
        }
        return paymentMethod.CardId;
    }
}
