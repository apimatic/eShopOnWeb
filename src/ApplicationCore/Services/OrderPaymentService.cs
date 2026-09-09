using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPal;
    private readonly IPayPalConfiguration _config;
    private readonly IUriComposer _uriComposer;
    private readonly KeyedAsyncLock _locks;
    private readonly PaymentReferenceFactory _references;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalClient payPal,
        IPayPalConfiguration config,
        IUriComposer uriComposer,
        KeyedAsyncLock locks,
        PaymentReferenceFactory references,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _config = config;
        _uriComposer = uriComposer;
        _locks = locks;
        _references = references;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        ShipToAddressInput? shipTo, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentOperationException("An order must contain at least one item.", 400);
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentOperationException("Every order line must have a quantity of at least 1.", 400);

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentOperationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", 400);

        var orderItems = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri ?? string.Empty);
            if (string.IsNullOrEmpty(pictureUri)) pictureUri = "none";
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            // Price is snapshotted from the current catalog price (amounts come from catalog prices).
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems, _config.Currency);
        await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation("Order {OrderId} placed by {BuyerId} awaiting payment, total {Total}.",
            order.Id, buyerId, order.Total());
        return order;
    }

    public async Task<Order> AuthorizeAsync(int orderId, string buyerId, PayPalCard? card, int? paymentMethodId,
        CancellationToken ct = default)
    {
        if (card is null && paymentMethodId is null)
            throw new PaymentOperationException("Provide either card details or a saved paymentMethodId to pay with.", 400);
        if (card is not null && paymentMethodId is not null)
            throw new PaymentOperationException("Provide either card details or a saved paymentMethodId, not both.", 400);

        using var _ = await _locks.LockAsync(OrderLockKey(orderId), ct);

        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);
        var payment = order.Payment;

        // Idempotent in effect: a double-clicked pay returns the existing hold.
        if (payment.Status == PaymentStatus.Authorized)
            return order;
        if (payment.Status != PaymentStatus.AwaitingPayment)
            throw new PaymentOperationException(
                $"Order {orderId} is {payment.Status} and can no longer be authorized.", 409);

        string? vaultId = null;
        if (paymentMethodId is not null)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdForBuyerSpec(paymentMethodId.Value, buyerId), ct);
            if (method is null)
                throw new PaymentOperationException(
                    $"Saved card {paymentMethodId} was not found for this shopper.", 404);
            vaultId = method.PayPalVaultId;
        }

        var amount = RoundToCents(order.Total());
        if (amount <= 0m)
            throw new PaymentOperationException("Order total must be greater than zero to authorize.", 400);

        var authorization = await _payPal.AuthorizeAsync(
            amount, payment.Currency, _references.InvoiceId(orderId), _references.CustomId(orderId),
            card, vaultId, _references.AuthorizeRequestId(), ct);

        payment.RecordAuthorization(authorization.PayPalOrderId, authorization.AuthorizationId,
            authorization.Status, authorization.ExpiresAt, authorization.InstrumentDescription);

        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Order {OrderId} authorized (PayPal auth {AuthId}) for {Amount} {Currency}.",
            orderId, authorization.AuthorizationId, amount, payment.Currency);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        using var _ = await _locks.LockAsync(OrderLockKey(orderId), ct);

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct)
            ?? throw new PaymentOperationException($"Order {orderId} was not found.", 404);
        var payment = order.Payment;

        if (payment.Status == PaymentStatus.Captured
            || payment.Status == PaymentStatus.Refunded
            || payment.Status == PaymentStatus.PartiallyRefunded)
            return order; // already fulfilled (idempotent)
        if (payment.Status != PaymentStatus.Authorized)
            throw new PaymentOperationException(
                $"Order {orderId} is {payment.Status}; only an authorized order can be fulfilled.", 409);

        var amount = RoundToCents(order.Total());
        var authId = payment.AuthorizationId!;

        // Renew proactively if the hold has already expired, then capture.
        if (payment.AuthorizationExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow)
            authId = await RenewAuthorizationAsync(order, amount, ct);

        PayPalCapture capture;
        try
        {
            capture = await _payPal.CaptureAsync(authId, amount, payment.Currency, _references.CaptureRequestId(orderId), ct);
        }
        catch (PayPalApiException ex) when (IsStaleAuthorization(ex))
        {
            // The hold went stale between the expiry check and capture — renew and retry once.
            _logger.LogWarning("Capture of order {OrderId} failed as stale ({Message}); attempting reauthorization.",
                orderId, ex.Message);
            authId = await RenewAuthorizationAsync(order, amount, ct);
            capture = await _payPal.CaptureAsync(authId, amount, payment.Currency, _references.CaptureRequestId(orderId), ct);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Order {OrderId} fulfilled: captured {Gross} {Currency}, fee {Fee}, net {Net}.",
            orderId, capture.GrossAmount, capture.Currency, capture.PayPalFee, capture.NetAmount);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct = default)
    {
        using var _ = await _locks.LockAsync(OrderLockKey(orderId), ct);

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct)
            ?? throw new PaymentOperationException($"Order {orderId} was not found.", 404);
        var payment = order.Payment;

        if (payment.Status == PaymentStatus.Cancelled)
            return order; // idempotent
        if (payment.Status != PaymentStatus.Authorized)
            throw new PaymentOperationException(
                $"Order {orderId} is {payment.Status}; only an authorized (unfulfilled) order can be cancelled.", 409);

        await _payPal.VoidAsync(payment.AuthorizationId!, ct);
        payment.RecordCancellation();
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Order {OrderId} cancelled; authorization {AuthId} voided.",
            orderId, payment.AuthorizationId);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        using var _ = await _locks.LockAsync(OrderLockKey(orderId), ct);

        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);
        var payment = order.Payment;

        // Repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
            return existing;

        if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded)
            throw new PaymentOperationException(
                $"Order {orderId} is {payment.Status}; only a captured order can be refunded.", 409);

        var refundable = payment.RefundableAmount;
        if (refundable <= 0m)
            throw new PaymentOperationException($"Order {orderId} has nothing left to refund.", 409);

        var refundAmount = amount is null ? refundable : RoundToCents(amount.Value);
        if (refundAmount <= 0m)
            throw new PaymentOperationException("Refund amount must be greater than zero.", 400);
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > refundable)
            throw new PaymentOperationException(
                $"Refund of {refundAmount} exceeds the {refundable} still refundable on order {orderId}.", 409);

        var result = await _payPal.RefundAsync(payment.CaptureId!, amount is null ? null : refundAmount,
            payment.Currency, _references.RefundRequestId(idempotencyKey), ct);

        var refund = new PaymentRefund(result.RefundId, refundAmount, result.Status, idempotencyKey);
        payment.RecordRefund(refund);
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Order {OrderId} refunded {Amount} {Currency} (PayPal refund {RefundId}).",
            orderId, refundAmount, payment.Currency, result.RefundId);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpec(buyerId), ct);
        return orders;
    }

    private async Task<string> RenewAuthorizationAsync(Order order, decimal amount, CancellationToken ct)
    {
        var payment = order.Payment;
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, amount, payment.Currency, ct);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation("Order {OrderId} authorization renewed to {AuthId}.", order.Id, renewed.AuthorizationId);
            return renewed.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentOperationException(
                $"The authorization for order {order.Id} has expired and can no longer be renewed " +
                $"({ex.Message}). Ask the shopper to pay for the order again before fulfilling it.", 422);
        }
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId, buyerId), ct);
        if (order is null)
            throw new PaymentOperationException($"Order {orderId} was not found.", 404);
        return order;
    }

    private static bool IsStaleAuthorization(PayPalApiException ex)
    {
        // PayPal signals a hold that must be renewed before it can be captured with these issues.
        var m = ex.Message?.ToUpperInvariant() ?? string.Empty;
        return m.Contains("EXPIRED") || m.Contains("REAUTHORIZATION_REQUIRED");
    }

    private static decimal RoundToCents(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string OrderLockKey(int orderId) => $"order-{orderId}";
}
