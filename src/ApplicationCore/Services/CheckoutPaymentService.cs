using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutPaymentService : ICheckoutPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly IPayPalSettings _payPalSettings;
    private readonly IAppLogger<CheckoutPaymentService> _logger;

    public CheckoutPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        IPayPalSettings payPalSettings,
        IAppLogger<CheckoutPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _payPalSettings = payPalSettings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLine> lines,
        Address shipTo,
        string currency,
        CancellationToken cancellationToken)
    {
        if (lines == null || lines.Count == 0)
            throw new CheckoutException(400, "The order must contain at least one catalog item.");

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new CheckoutException(400, "Each order line must have a quantity greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
            throw new CheckoutException(400, "One or more catalog items were not found.");

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipTo, items);
        order.SetCurrency(currency);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation($"Placed order {order.Id} for {buyerId} totaling {order.Total()} {currency}.");
        return order;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        PayPalCardInput? card,
        string? paymentMethodId,
        CancellationToken cancellationToken)
    {
        var order = await LoadOrder(orderId, cancellationToken);
        EnsureBuyer(order, buyerId);

        if (order.IsCancelled())
            throw new CheckoutException(409, "This order has been cancelled.");
        if (order.IsCaptured())
            throw new CheckoutException(409, "This order has already been captured.");
        if (order.HasActiveAuthorization())
            return order;

        var hasCard = card != null && !string.IsNullOrWhiteSpace(card.Number);
        var hasVault = !string.IsNullOrWhiteSpace(paymentMethodId);
        if (hasCard == hasVault)
            throw new CheckoutException(400, "Provide either card details or a saved paymentMethodId, not both or neither.");

        var currency = RequireCurrency();
        var amountValue = PayPalMoneyFormat.ToApiValue(order.Total(), currency);
        var invoiceId = $"{order.Id}-{Guid.NewGuid():N}";

        PayPalAuthorizationResult result;
        var requestId = $"{order.Id}:pay:{Guid.NewGuid():N}";
        if (hasVault)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByTokenSpec(paymentMethodId!), cancellationToken);
            if (saved == null || !string.Equals(saved.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
                throw new CheckoutException(404, "The saved payment method was not found.");

            result = await _payPal.AuthorizeSavedCardAsync(
                order.Id, invoiceId, amountValue, currency, saved.PaymentTokenId, requestId, cancellationToken);
        }
        else
        {
            result = await _payPal.AuthorizeCardAsync(
                order.Id, invoiceId, amountValue, currency, card!, requestId, cancellationToken);
        }

        var held = PayPalMoneyFormat.Parse(result.Amount.Value);
        if (!PayPalMoneyFormat.AmountsEqual(held, order.Total(), currency))
            throw new CheckoutException(502, "PayPal authorized an amount that does not match the order total.");

        order.RecordAuthorization(
            result.PayPalOrderId,
            result.AuthorizationId,
            result.Status,
            result.ExpirationTime,
            currency);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrder(orderId, cancellationToken);

        if (order.IsCancelled())
            throw new CheckoutException(409, "This order has been cancelled.");
        if (order.PaymentStatus is OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
            return order;

        if (string.IsNullOrEmpty(order.AuthorizationId))
            throw new CheckoutException(409, "This order has no PayPal authorization to capture.");

        var currency = string.IsNullOrEmpty(order.Currency) ? RequireCurrency() : order.Currency;
        var amountValue = PayPalMoneyFormat.ToApiValue(order.Total(), currency);
        var authorizationId = order.AuthorizationId;

        var snapshot = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        if (string.Equals(snapshot.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
            throw new CheckoutException(409, "The authorization has been voided and cannot be captured.");
        if (string.Equals(snapshot.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
            throw new CheckoutException(409, "The authorization was denied and cannot be captured.");

        if (order.AuthorizationLooksExpired(DateTimeOffset.UtcNow)
            || LooksExpired(snapshot.ExpirationTime))
        {
            authorizationId = await RenewAuthorization(order, authorizationId, amountValue, currency, cancellationToken);
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(authorizationId, order.Id, $"{order.Id}:capture:{Guid.NewGuid():N}", cancellationToken);
        }
        catch (CheckoutException ex) when (LooksLikeExpiredAuthorization(ex.Message))
        {
            authorizationId = await RenewAuthorization(order, authorizationId, amountValue, currency, cancellationToken);
            capture = await _payPal.CaptureAsync(authorizationId, order.Id, $"{order.Id}:capture:{Guid.NewGuid():N}", cancellationToken);
        }

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            PayPalMoneyFormat.Parse(capture.Amount.Value),
            capture.PaypalFee == null ? null : PayPalMoneyFormat.Parse(capture.PaypalFee.Value),
            capture.NetAmount == null ? null : PayPalMoneyFormat.Parse(capture.NetAmount.Value));
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrder(orderId, cancellationToken);

        if (order.IsCancelled())
            return order;
        if (order.IsCaptured())
            throw new CheckoutException(409, "A captured order cannot be cancelled; issue a refund instead.");

        if (!string.IsNullOrEmpty(order.AuthorizationId))
        {
            var voided = await _payPal.VoidAsync(order.AuthorizationId, $"{order.Id}:void", cancellationToken);
            order.RecordVoid(voided.Status);
        }
        else
        {
            order.CancelWithoutAuthorization();
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new CheckoutException(400, "A refund idempotency key is required.");

        var order = await LoadOrder(orderId, cancellationToken);
        EnsureBuyer(order, buyerId);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
            return existing;

        if (!order.IsCaptured() || string.IsNullOrEmpty(order.CaptureId))
            throw new CheckoutException(409, "Refunds are only allowed after the order has been fulfilled.");

        var currency = string.IsNullOrEmpty(order.Currency) ? RequireCurrency() : order.Currency;
        var remaining = order.RemainingRefundable();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
            throw new CheckoutException(409, "There is no remaining captured amount to refund.");
        if (refundAmount > remaining)
            throw new CheckoutException(409, $"Refund of {refundAmount} exceeds remaining refundable amount {remaining}.");

        var fullRefund = PayPalMoneyFormat.AmountsEqual(refundAmount, remaining, currency)
            && PayPalMoneyFormat.AmountsEqual(remaining, order.CapturedAmount ?? 0m, currency);

        var result = await _payPal.RefundAsync(
            order.CaptureId,
            PayPalMoneyFormat.ToApiValue(refundAmount, currency),
            currency,
            idempotencyKey,
            fullRefund,
            cancellationToken);

        var recorded = order.RecordRefund(
            result.RefundId,
            result.Status,
            PayPalMoneyFormat.Parse(result.Amount.Value),
            currency,
            idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return recorded;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null || !order.BelongsTo(buyerId))
            return null;
        return order;
    }

    private async Task<string> RenewAuthorization(
        Order order,
        string authorizationId,
        string amountValue,
        string currency,
        CancellationToken cancellationToken)
    {
        PayPalAuthorizationSnapshot renewed;
        try
        {
            renewed = await _payPal.ReauthorizeAsync(
                authorizationId, amountValue, currency, $"{order.Id}:reauth", cancellationToken);
        }
        catch (CheckoutException ex)
        {
            throw new CheckoutException(
                409,
                "The PayPal authorization could not be renewed. Capture is not possible on this hold; the shopper must authorize a new payment. " + ex.Message,
                ex);
        }

        order.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return renewed.AuthorizationId;
    }

    private async Task<Order> LoadOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
            throw new CheckoutException(404, "The order was not found.");
        return order;
    }

    private static void EnsureBuyer(Order order, string buyerId)
    {
        if (!order.BelongsTo(buyerId))
            throw new CheckoutException(404, "The order was not found.");
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_payPalSettings.Currency))
            throw new CheckoutException(500, "PayPal:Currency is not configured.");
        return _payPalSettings.Currency;
    }

    private static bool LooksLikeExpiredAuthorization(string message) =>
        message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
        || message.Contains("EXPIRED_AUTHORIZATION", StringComparison.OrdinalIgnoreCase)
        || message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
        || message.Contains("honor period", StringComparison.OrdinalIgnoreCase);

    private static bool LooksExpired(string? expirationTime)
    {
        if (string.IsNullOrEmpty(expirationTime))
            return false;
        return DateTimeOffset.TryParse(expirationTime, out var expires)
            && expires <= DateTimeOffset.UtcNow;
    }
}
