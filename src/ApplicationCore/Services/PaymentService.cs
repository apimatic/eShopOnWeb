using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>
/// Orchestrates the order payment flow against PayPal while keeping the eShop order the source of truth
/// for what has happened. Every money-moving action is idempotent in effect: the order's own state is
/// checked before calling PayPal, and PayPal calls carry a deterministic request id so a retry never
/// authorizes or captures twice.
/// </summary>
public class PaymentService : IPaymentService
{
    // Stable for the lifetime of the process. Combined with the (per-run) order id it yields a
    // reference that is deterministic within a run — so retries reuse the same PayPal-Request-Id —
    // yet unique across runs, so PayPal's invoice-id uniqueness is never violated after a restart.
    private static readonly string RunToken = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        IPaymentSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    public async Task<Order> PlaceOrderAsync(
        string buyerId, IEnumerable<OrderLineItem> lines, Address shipToAddress, CancellationToken ct = default)
    {
        var requested = lines?.Where(l => l.Quantity > 0).ToList() ?? new List<OrderLineItem>();
        if (requested.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item with a positive quantity.");

        var catalogItemIds = requested.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), ct);

        var items = new List<OrderItem>();
        foreach (var line in requested)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentValidationException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            // Price comes from the catalog, never the caller.
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        order = await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation($"Placed order {order.Id} for {buyerId} awaiting payment, total {order.Total()} {Currency}.");
        return order;
    }

    public async Task<Order> AuthorizeOrderAsync(
        int orderId, string buyerId, PaymentInstrument instrument, CancellationToken ct = default)
    {
        var order = await GetOwnedOrderWithItemsAsync(orderId, buyerId, ct);

        // Idempotent in effect: a double-click never authorizes twice.
        if (order.Status == OrderStatus.Authorized && order.Payment is not null)
            return order;
        if (order.Status == OrderStatus.Paid)
            throw new PaymentConflictException($"Order {orderId} has already been paid and cannot be authorized again.");
        if (order.Status == OrderStatus.Cancelled)
            throw new PaymentConflictException($"Order {orderId} has been cancelled and cannot be paid.");

        var (gatewayInstruction, savedPaymentMethodId) = await ResolveInstrumentAsync(buyerId, instrument, ct);

        var reference = BuildReference(orderId);
        var amount = order.Total();
        if (amount <= 0m)
            throw new PaymentConflictException($"Order {orderId} has a non-positive total and cannot be paid.");

        var authorization = await _payPal.AuthorizeAsync(
            amount, Currency, invoiceId: reference, customId: reference,
            gatewayInstruction, requestId: $"auth-{reference}", ct);

        var payment = new PaymentRecord(
            reference, Currency, authorization.PayPalOrderId, authorization.AuthorizationId,
            authorization.Status, amount, authorization.ExpiresAt,
            authorization.CardBrand, authorization.CardLast4, savedPaymentMethodId);

        order.AttachAuthorization(payment);
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation($"Authorized order {orderId}: hold {authorization.AuthorizationId} for {amount} {Currency}.");
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await GetOrderWithItemsAsync(orderId, ct);

        if (order.Status == OrderStatus.Paid)
            return order; // already fulfilled and captured
        if (order.Status != OrderStatus.Authorized || order.Payment is null)
            throw new PaymentConflictException(
                $"Order {orderId} cannot be fulfilled from status {order.Status}; it must be authorized first.");

        var payment = order.Payment;
        var amount = payment.AuthorizedAmount;

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(
                payment.AuthorizationId, amount, payment.Currency, payment.ReferenceId,
                finalCapture: true, requestId: $"cap-{payment.ReferenceId}", ct);
        }
        catch (PayPalException ex) when (ex.AuthorizationNeedsRenewal)
        {
            // The hold went stale before fulfilment: renew it rather than failing outright.
            _logger.LogWarning($"Authorization {payment.AuthorizationId} for order {orderId} is stale; attempting to renew. (debug_id: {ex.DebugId})");
            PayPalAuthorizationResult renewed;
            try
            {
                renewed = await _payPal.ReauthorizeAsync(
                    payment.AuthorizationId, amount, payment.Currency, requestId: $"reauth-{payment.ReferenceId}", ct);
            }
            catch (PayPalException renewEx)
            {
                throw new PaymentConflictException(
                    $"The authorization for order {orderId} has expired and can no longer be renewed " +
                    $"(PayPal: {renewEx.IssueName ?? renewEx.Message}). Collect a new payment for this order before fulfilling.");
            }

            payment.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            capture = await _payPal.CaptureAsync(
                renewed.AuthorizationId, amount, payment.Currency, payment.ReferenceId,
                finalCapture: true, requestId: $"cap-{payment.ReferenceId}-renewed", ct);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation(
            $"Fulfilled order {orderId}: captured {capture.GrossAmount} {capture.Currency} " +
            $"(fee {capture.PayPalFee}, net {capture.NetAmount}) via {capture.CaptureId}.");
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await GetOrderWithItemsAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled)
            return order; // idempotent
        if (order.Status == OrderStatus.Paid)
            throw new PaymentConflictException(
                $"Order {orderId} has been fulfilled and cannot be cancelled; issue a refund instead.");

        // Release the hold if one exists (an order still awaiting payment has none).
        if (order.Status == OrderStatus.Authorized && order.Payment is not null)
        {
            await _payPal.VoidAsync(order.Payment.AuthorizationId, requestId: $"void-{order.Payment.ReferenceId}", ct);
            order.Payment.SetAuthorizationStatus("VOIDED");
            _logger.LogInformation($"Released hold {order.Payment.AuthorizationId} for cancelled order {orderId}.");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<(Order Order, PaymentRefund Refund)> RefundOrderAsync(
        int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentValidationException("A refund idempotency key is required.");

        var order = await GetOwnedOrderWithItemsAsync(orderId, buyerId, ct);

        if (order.Status != OrderStatus.Paid || order.Payment is null || !order.Payment.IsCaptured)
            throw new PaymentConflictException($"Order {orderId} has not been fulfilled, so there is nothing to refund.");

        var payment = order.Payment;

        // Idempotent: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return (order, existing);

        if (payment.RefundableRemaining <= 0m)
            throw new PaymentConflictException($"Order {orderId} has been fully refunded.");

        var effectiveAmount = amount ?? payment.RefundableRemaining;
        if (effectiveAmount <= 0m)
            throw new PaymentValidationException("Refund amount must be positive.");
        if (effectiveAmount > payment.RefundableRemaining)
            throw new PaymentConflictException(
                $"Refund of {effectiveAmount} exceeds the {payment.RefundableRemaining} still refundable on order {orderId}.");

        // The caller's key is also the PayPal request id, so PayPal dedups a repeated refund too.
        var result = await _payPal.RefundAsync(
            payment.CaptureId!, effectiveAmount, payment.Currency, payment.ReferenceId, requestId: idempotencyKey, ct);

        var refund = new PaymentRefund(result.RefundId, result.Status, result.Amount, idempotencyKey);
        payment.AddRefund(refund); // guards that total refunded never exceeds captured
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation($"Refunded {result.Amount} {payment.Currency} on order {orderId} via {result.RefundId}.");
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        return orders;
    }

    public async Task<Order?> GetMyOrderAsync(int orderId, string buyerId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        return order is not null && order.BuyerId == buyerId ? order : null;
    }

    private async Task<Order> GetOwnedOrderWithItemsAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId)
            throw new OrderNotFoundException(orderId); // owner-scoped: do not leak another shopper's order
        return order;
    }

    private async Task<Order> GetOrderWithItemsAsync(int orderId, CancellationToken ct)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<(AuthorizeInstruction Instruction, int? SavedPaymentMethodId)> ResolveInstrumentAsync(
        string buyerId, PaymentInstrument instrument, CancellationToken ct)
    {
        var hasCard = instrument.Card is not null;
        if (hasCard == instrument.UsesSavedCard)
            throw new PaymentValidationException(
                "Provide exactly one of card details or a saved paymentMethodId to pay with.");

        if (instrument.UsesSavedCard)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(instrument.SavedPaymentMethodId!.Value, buyerId), ct)
                ?? throw new PaymentMethodNotFoundException(instrument.SavedPaymentMethodId!.Value);
            return (new AuthorizeInstruction(null, saved.VaultId), saved.Id);
        }

        return (new AuthorizeInstruction(instrument.Card, null), null);
    }

    private static string BuildReference(int orderId) => $"ESHOP-{RunToken}-{orderId}";
}
