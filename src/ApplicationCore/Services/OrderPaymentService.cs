using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IIdempotencyService _idempotency;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway gateway,
        IIdempotencyService idempotency,
        IUriComposer uriComposer,
        PaymentSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _idempotency = idempotency;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.CurrencyCode;

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentOperationException("An order must contain at least one item.", PaymentErrorKind.Validation);
        }
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentOperationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.", PaymentErrorKind.Validation);
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentOperationException($"Catalog item {line.CatalogItemId} was not found.", PaymentErrorKind.Validation);

            var picUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, string.IsNullOrEmpty(picUri) ? "eCatalog-item-default.png" : picUri);
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation($"Order {order.Id} placed by {buyerId} for {order.Total().ToString("0.00", CultureInfo.InvariantCulture)} {Currency}.");
        return new PlaceOrderResult(order.Id, order.Total(), Currency);
    }

    public async Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardInput? card, string? savedPaymentMethodId, CancellationToken ct = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, ct);

        if (order.PaymentStatus == OrderPaymentStatus.Authorized)
        {
            return ToView(order); // already authorized — idempotent
        }
        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be paid because it is {order.PaymentStatus}.", PaymentErrorKind.Conflict);
        }

        var hasCard = card is not null;
        var hasSaved = !string.IsNullOrWhiteSpace(savedPaymentMethodId);
        if (hasCard == hasSaved)
        {
            throw new PaymentOperationException("Provide exactly one of a card or a saved payment method to pay with.", PaymentErrorKind.Validation);
        }

        string? vaultId = null;
        if (hasSaved)
        {
            var saved = (await _savedCardRepository.ListAsync(new SavedPaymentMethodByPublicIdSpec(buyerId, savedPaymentMethodId!), ct)).FirstOrDefault()
                ?? throw new PaymentOperationException("Saved payment method not found.", PaymentErrorKind.NotFound);
            vaultId = saved.PayPalVaultId;
        }

        var amount = Math.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        // A globally-unique reference (invoice_id/custom_id): the merchant account requires invoice_id
        // to be unique per transaction, and the in-memory store reuses order ids across process restarts.
        // Stored on the payment and used to line the order up against PayPal during reconciliation.
        var reference = order.Payment?.ReferenceId ?? $"ESHOP-{orderId}-{Guid.NewGuid():N}";
        var claimKey = $"pay-{orderId}";

        if (!await _idempotency.TryClaimAsync(claimKey, ct))
        {
            var reloaded = await LoadOwnedOrderAsync(buyerId, orderId, ct);
            if (reloaded.PaymentStatus == OrderPaymentStatus.Authorized) return ToView(reloaded);
            throw new PaymentOperationException($"A payment for order {orderId} is already in progress.", PaymentErrorKind.Conflict);
        }

        try
        {
            // Persist the payment (with its unique reference) BEFORE calling PayPal, so that if the
            // authorize connection fails after PayPal may have acted, a retry reloads the same reference
            // and reuses the same PayPal-Request-Id — PayPal then deduplicates the hold instead of
            // creating a second one. Any residual drift is surfaced by the reconciliation report.
            order.BeginPayment(Currency, reference, amount);
            await _orderRepository.UpdateAsync(order, ct);

            // The PayPal-Request-Id base is the globally-unique reference (not the per-process claim key),
            // so it never collides with a prior run's cached PayPal response after an in-memory restart.
            var authorization = await _gateway.AuthorizeAsync(
                new AuthorizeCommand(amount, Currency, reference, $"eShopOnWeb order {orderId}", reference, card, vaultId), ct);

            order.RecordAuthorization(authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation($"Order {orderId} authorized. PayPal order {authorization.PayPalOrderId}, authorization {authorization.AuthorizationId} ({authorization.Status}).");
            return ToView(order);
        }
        catch
        {
            await _idempotency.ReleaseAsync(claimKey, ct); // let a later attempt (idempotent at PayPal) proceed
            throw;
        }
    }

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);

        if (order.PaymentStatus == OrderPaymentStatus.Fulfilled) return ToView(order); // idempotent
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.Payment?.AuthorizationId is null)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be fulfilled because it is {order.PaymentStatus}.", PaymentErrorKind.Conflict);
        }

        var claimKey = $"capture-{orderId}";
        if (!await _idempotency.TryClaimAsync(claimKey, ct))
        {
            var reloaded = await LoadOrderAsync(orderId, ct);
            if (reloaded.PaymentStatus == OrderPaymentStatus.Fulfilled) return ToView(reloaded);
            throw new PaymentOperationException($"Fulfilment for order {orderId} is already in progress.", PaymentErrorKind.Conflict);
        }

        try
        {
            var authorizationId = order.Payment!.AuthorizationId!;
            var reference = order.Payment!.ReferenceId;

            // Renew a hold that has already gone stale before attempting capture.
            if (order.Payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            {
                _logger.LogWarning($"Authorization {authorizationId} for order {orderId} expired at {expiry:o}; reauthorizing before capture.");
                authorizationId = await RenewHoldAsync(order, authorizationId, ct);
            }

            GatewayCapture capture;
            try
            {
                capture = await _gateway.CaptureAsync(authorizationId, reference + "-cap", ct);
            }
            catch (PaymentGatewayException ex) when (ex is not AuthorizationNotRenewableException && ex.StatusCode is 422 or 404)
            {
                // The hold may have gone stale between the freshness check and the capture — renew once and retry.
                _logger.LogWarning($"Capture of authorization {authorizationId} for order {orderId} failed ({ex.StatusCode}); attempting to renew the hold and recapture.");
                authorizationId = await RenewHoldAsync(order, authorizationId, ct);
                capture = await _gateway.CaptureAsync(authorizationId, reference + "-cap-r", ct);
            }

            order.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation($"Order {orderId} fulfilled. Capture {capture.CaptureId} ({capture.Status}) amount={capture.CapturedAmount} fee={capture.PayPalFee} net={capture.NetAmount} {Currency}.");
            return ToView(order);
        }
        catch
        {
            await _idempotency.ReleaseAsync(claimKey, ct);
            throw;
        }
    }

    private async Task<string> RenewHoldAsync(Order order, string authorizationId, CancellationToken ct)
    {
        var amount = order.Payment!.AuthorizedAmount;
        var renewed = await _gateway.ReauthorizeAsync(authorizationId, amount, Currency, order.Payment!.ReferenceId + "-reauth", ct);
        order.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        await _orderRepository.UpdateAsync(order, ct);
        return renewed.AuthorizationId;
    }

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled) return ToView(order); // idempotent
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.Payment?.AuthorizationId is null)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be cancelled because it is {order.PaymentStatus}.", PaymentErrorKind.Conflict);
        }

        var claimKey = $"void-{orderId}";
        if (!await _idempotency.TryClaimAsync(claimKey, ct))
        {
            var reloaded = await LoadOrderAsync(orderId, ct);
            if (reloaded.PaymentStatus == OrderPaymentStatus.Cancelled) return ToView(reloaded);
            throw new PaymentOperationException($"Cancellation for order {orderId} is already in progress.", PaymentErrorKind.Conflict);
        }

        try
        {
            await _gateway.VoidAsync(order.Payment!.AuthorizationId!, order.Payment!.ReferenceId + "-void", ct);
            order.RecordCancellation();
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation($"Order {orderId} cancelled; hold {order.Payment!.AuthorizationId} released.");
            return ToView(order);
        }
        catch
        {
            await _idempotency.ReleaseAsync(claimKey, ct);
            throw;
        }
    }

    public async Task<(OrderPaymentView Payment, RefundView Refund)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? note, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrderAsync(buyerId, orderId, ct);

        if (order.Payment?.CaptureId is null ||
            (order.PaymentStatus != OrderPaymentStatus.Fulfilled && order.PaymentStatus != OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentOperationException($"Order {orderId} cannot be refunded because it is {order.PaymentStatus}.", PaymentErrorKind.Conflict);
        }

        // Idempotent replay: the same key already produced a refund on this order.
        var existing = order.Payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            return (ToView(order), ToRefundView(existing));
        }

        var remaining = order.Payment.RefundableRemaining;
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new PaymentOperationException("Refund amount must be greater than zero.", PaymentErrorKind.Validation);
        }
        refundAmount = Math.Round(refundAmount, 2, MidpointRounding.AwayFromZero);
        if (refundAmount > remaining)
        {
            throw new PaymentOperationException(
                $"Refund of {refundAmount.ToString("0.00", CultureInfo.InvariantCulture)} exceeds the {remaining.ToString("0.00", CultureInfo.InvariantCulture)} still refundable against the capture.",
                PaymentErrorKind.Validation);
        }

        var claimKey = $"refund-{orderId}-{idempotencyKey}";
        if (!await _idempotency.TryClaimAsync(claimKey, ct))
        {
            var reloaded = await LoadOwnedOrderAsync(buyerId, orderId, ct);
            var found = reloaded.Payment?.FindRefundByKey(idempotencyKey);
            if (found is not null) return (ToView(reloaded), ToRefundView(found));
            throw new PaymentOperationException($"A refund for order {orderId} under this key is already in progress.", PaymentErrorKind.Conflict);
        }

        try
        {
            // PayPal-Request-Id is the reference + caller key: unique per (order, caller key) and stable
            // for a legitimate replay of the same key, without colliding with a prior run's cache.
            var refund = await _gateway.RefundAsync(order.Payment!.CaptureId!, refundAmount, Currency,
                order.Payment!.ReferenceId + "-refund-" + idempotencyKey, note, ct);
            order.RecordRefund(idempotencyKey, refund.RefundId, refundAmount, refund.Status ?? "PENDING");
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation($"Order {orderId} refunded {refundAmount.ToString("0.00", CultureInfo.InvariantCulture)} {Currency}. Refund {refund.RefundId} ({refund.Status}).");
            var recorded = order.Payment!.FindRefundByKey(idempotencyKey)!;
            return (ToView(order), ToRefundView(recorded));
        }
        catch
        {
            await _idempotency.ReleaseAsync(claimKey, ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new OrdersByBuyerWithPaymentSpec(buyerId), ct);
        return orders.Select(ToView).ToList();
    }

    public async Task<OrderPaymentView?> GetOrderAsync(string buyerId, int orderId, CancellationToken ct = default)
    {
        var order = (await _orderRepository.ListAsync(new OrderWithPaymentByIdSpec(orderId), ct)).FirstOrDefault();
        if (order is null || order.BuyerId != buyerId) return null;
        return ToView(order);
    }

    // --- helpers ---

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        return (await _orderRepository.ListAsync(new OrderWithPaymentByIdSpec(orderId), ct)).FirstOrDefault()
            ?? throw new PaymentOperationException($"Order {orderId} was not found.", PaymentErrorKind.NotFound);
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.BuyerId != buyerId)
        {
            // Do not reveal another shopper's order — treat as not found.
            throw new PaymentOperationException($"Order {orderId} was not found.", PaymentErrorKind.NotFound);
        }
        return order;
    }

    private OrderPaymentView ToView(Order order)
    {
        var p = order.Payment;
        var refunds = p?.Refunds.Select(ToRefundView).ToList() ?? new List<RefundView>();
        return new OrderPaymentView(
            OrderId: order.Id,
            PaymentStatus: order.PaymentStatus.ToString(),
            OrderTotal: order.Total(),
            CurrencyCode: p?.CurrencyCode,
            ReferenceId: p?.ReferenceId,
            AuthorizedAmount: p?.AuthorizedAmount,
            PayPalOrderId: p?.PayPalOrderId,
            AuthorizationId: p?.AuthorizationId,
            AuthorizationStatus: p?.AuthorizationStatus,
            AuthorizationExpiresAt: p?.AuthorizationExpiresAt,
            CaptureId: p?.CaptureId,
            CaptureStatus: p?.CaptureStatus,
            CapturedAmount: p?.CapturedAmount,
            PayPalFee: p?.PayPalFee,
            NetAmount: p?.NetAmount,
            TotalRefunded: p?.TotalRefunded ?? 0m,
            Refunds: refunds);
    }

    private RefundView ToRefundView(PaymentRefund r) =>
        new(r.PayPalRefundId, r.Status, r.Amount, Currency, r.CreatedAt);
}
