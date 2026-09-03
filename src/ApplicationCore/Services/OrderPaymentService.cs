using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private const string DefaultPicture = "eCatalog-item-default.png";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentGateway _gateway;
    private readonly PayPalSettings _settings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPaymentGateway gateway,
        PayPalSettings settings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _settings = settings;
    }

    public async Task<int> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
            throw new PaymentOperationException(PaymentOperationError.Validation, "An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentOperationException(PaymentOperationError.Validation, "Every item quantity must be greater than zero.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
                throw new PaymentOperationException(PaymentOperationError.Validation, $"Catalog item {line.CatalogItemId} does not exist.");

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri))
                pictureUri = DefaultPicture;

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order.Id;
    }

    public async Task<AuthorizationOutcome> PayAsync(string buyerId, int orderId, CardDetails? card,
        int? paymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent in effect: if the order is already authorized, don't authorize again.
        if (order.PaymentStatus == PaymentStatus.Authorized && order.Payment is not null)
            return BuildAuthorizationOutcome(order);

        if (!order.IsAwaitingPayment)
            throw new PaymentOperationException(PaymentOperationError.Conflict,
                $"Order {orderId} is not awaiting payment (current status: {order.PaymentStatus}).");

        string? vaultId = null;
        if (paymentMethodId is not null)
        {
            if (card is not null)
                throw new PaymentOperationException(PaymentOperationError.Validation,
                    "Provide either card details or a saved paymentMethodId, not both.");

            var pm = await _paymentMethodRepository.GetByIdAsync(paymentMethodId.Value, cancellationToken);
            if (pm is null || pm.BuyerId != buyerId)
                throw new PaymentOperationException(PaymentOperationError.NotFound, "Saved card not found.");
            vaultId = pm.PayPalVaultId;
        }
        else if (card is null)
        {
            throw new PaymentOperationException(PaymentOperationError.Validation,
                "A card or a saved paymentMethodId is required to pay.");
        }

        var amount = order.Total();
        if (amount <= 0)
            throw new PaymentOperationException(PaymentOperationError.Conflict, "Order total must be greater than zero.");

        var currency = _settings.CurrencyCode;
        // Deterministic idempotency key per (order, payment source): a double-click reuses the same
        // key so PayPal deduplicates; a genuine retry with a different card gets a distinct key. The
        // order's globally-unique seed keeps the key unique per order (not tied to a reusable id).
        var idempotencyKey = $"auth-{order.IdempotencySeed:N}-{ShortHash(vaultId ?? card!.Number)}";

        var auth = await _gateway.AuthorizeAsync(amount, currency, card, vaultId, idempotencyKey, cancellationToken);

        order.AuthorizePayment(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt, currency, paymentMethodId);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return BuildAuthorizationOutcome(order);
    }

    public async Task<CaptureOutcome> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        // Idempotent: already fulfilled.
        if (order.PaymentStatus == PaymentStatus.Captured && order.Payment?.CaptureId is not null)
            return BuildCaptureOutcome(order);

        if (order.PaymentStatus != PaymentStatus.Authorized || order.Payment?.AuthorizationId is null)
            throw new PaymentOperationException(PaymentOperationError.Conflict,
                $"Order {orderId} is not awaiting fulfilment (current status: {order.PaymentStatus}).");

        var authorizationId = order.Payment.AuthorizationId!;
        var renewed = false;

        // Renew a stale hold rather than failing the fulfilment outright.
        if (IsAuthorizationStale(order.Payment))
        {
            authorizationId = await RenewAuthorizationOrThrowAsync(order, authorizationId, cancellationToken);
            renewed = true;
        }

        var captureKey = $"capture-{order.IdempotencySeed:N}";
        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, captureKey, cancellationToken);
        }
        catch (PaymentGatewayException) when (!renewed)
        {
            // The hold may have gone stale without us knowing its expiry; try one renewal + recapture.
            authorizationId = await RenewAuthorizationOrThrowAsync(order, authorizationId, cancellationToken);
            capture = await _gateway.CaptureAsync(authorizationId, captureKey, cancellationToken);
        }

        order.CapturePayment(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return BuildCaptureOutcome(order);
    }

    public async Task CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        switch (order.PaymentStatus)
        {
            case PaymentStatus.Cancelled:
                return; // idempotent

            case PaymentStatus.AwaitingPayment:
                // No money ever moved — nothing to release at PayPal.
                order.CancelPayment();
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return;

            case PaymentStatus.Authorized when order.Payment?.AuthorizationId is not null:
                await _gateway.VoidAuthorizationAsync(order.Payment.AuthorizationId!, cancellationToken);
                order.CancelPayment();
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return;

            default:
                throw new PaymentOperationException(PaymentOperationError.Conflict,
                    $"Order {orderId} cannot be cancelled after fulfilment (current status: {order.PaymentStatus}); issue a refund instead.");
        }
    }

    public async Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentOperationException(PaymentOperationError.Validation, "An idempotency key is required for a refund.");

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.Payment?.CaptureId is null ||
            (order.PaymentStatus != PaymentStatus.Captured && order.PaymentStatus != PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentOperationException(PaymentOperationError.Conflict,
                $"Order {orderId} has no captured payment to refund (current status: {order.PaymentStatus}).");
        }

        // Idempotent: the same key must not refund twice.
        var existing = order.Payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
            return BuildRefundOutcome(order, existing);

        var remaining = order.Payment.RefundableRemaining();
        if (remaining <= 0)
            throw new PaymentOperationException(PaymentOperationError.Conflict, $"Order {orderId} is already fully refunded.");

        decimal refundAmount;
        if (amount is null)
        {
            refundAmount = remaining;
        }
        else
        {
            if (amount.Value <= 0)
                throw new PaymentOperationException(PaymentOperationError.Validation, "Refund amount must be greater than zero.");
            if (amount.Value > remaining)
                throw new PaymentOperationException(PaymentOperationError.Validation,
                    $"Refund amount {amount.Value} exceeds the refundable remaining {remaining}.");
            refundAmount = amount.Value;
        }

        var currency = order.Payment.CurrencyCode;
        var refund = await _gateway.RefundAsync(order.Payment.CaptureId!, refundAmount, currency, idempotencyKey, cancellationToken);

        var record = new OrderRefund(refund.RefundId, refundAmount, refund.Status, idempotencyKey);
        order.RecordRefund(record);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return BuildRefundOutcome(order, record);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpec(buyerId), cancellationToken);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null)
            throw new PaymentOperationException(PaymentOperationError.NotFound, $"Order {orderId} not found.");
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        // Do not reveal another shopper's order — treat as not found.
        if (order is null || order.BuyerId != buyerId)
            throw new PaymentOperationException(PaymentOperationError.NotFound, $"Order {orderId} not found.");
        return order;
    }

    private async Task<string> RenewAuthorizationOrThrowAsync(Order order, string authorizationId, CancellationToken ct)
    {
        try
        {
            var reauth = await _gateway.ReauthorizeAsync(authorizationId, $"reauth-{order.Id}-{authorizationId}", ct);
            order.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _orderRepository.UpdateAsync(order, ct);
            return reauth.AuthorizationId;
        }
        catch (PaymentGatewayException ex)
        {
            var debug = ex.DebugId is not null ? $" (PayPal debug id: {ex.DebugId})" : string.Empty;
            throw new PaymentOperationException(PaymentOperationError.Conflict,
                $"The payment hold for order {order.Id} has expired and can no longer be renewed: {ex.Message}{debug}. Ask the shopper to pay again.");
        }
    }

    private static bool IsAuthorizationStale(OrderPayment payment) =>
        payment.AuthorizationExpiresAt is DateTimeOffset expiresAt &&
        expiresAt <= DateTimeOffset.UtcNow.AddMinutes(1);

    private static AuthorizationOutcome BuildAuthorizationOutcome(Order order)
    {
        var p = order.Payment!;
        return new AuthorizationOutcome(order.PaymentStatus, p.PayPalOrderId, p.AuthorizationId!,
            p.AuthorizationStatus!, p.Amount, p.CurrencyCode);
    }

    private static CaptureOutcome BuildCaptureOutcome(Order order)
    {
        var p = order.Payment!;
        return new CaptureOutcome(order.PaymentStatus, p.CaptureId!, p.CaptureStatus!,
            p.CapturedGross ?? 0m, p.PayPalFee, p.NetAmount, p.CurrencyCode);
    }

    private static RefundOutcome BuildRefundOutcome(Order order, OrderRefund refund)
    {
        var p = order.Payment!;
        return new RefundOutcome(refund.PayPalRefundId, refund.Status, refund.Amount,
            p.TotalRefunded(), order.PaymentStatus, p.CurrencyCode);
    }

    /// <summary>
    /// A short, stable, non-reversible token used only to make an idempotency key vary by payment
    /// source. The input (a vault id or a card number) is never stored or logged.
    /// </summary>
    private static string ShortHash(string source)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        var sb = new StringBuilder(12);
        for (var i = 0; i < 6; i++)
            sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }
}
