using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the eShop order and payment state with the Finix gateway. All
/// money movement is delegated to <see cref="IFinixPaymentGateway"/>; this service
/// owns ownership checks, the payment state machine and idempotency.
/// </summary>
public sealed class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BuyerLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<PaymentRefund> _refundRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IRepository<ShopperFinixProfile> _profileRepository;
    private readonly IFinixPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<PaymentRefund> refundRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IRepository<ShopperFinixProfile> profileRepository,
        IFinixPaymentGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentRepository = paymentRepository;
        _refundRepository = refundRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _profileRepository = profileRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<OrderInfo> CreateOrderAsync(string buyerId, IReadOnlyList<CreateOrderLine> items,
        PaymentAddress? shipToAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation, "A signed-in shopper is required.");
        }

        if (items is null || items.Count == 0)
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                "At least one catalog item is required to place an order.");
        }

        if (items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                "Every order line needs a positive catalog item id and quantity.");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(
            new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        if (catalogItems.Count != catalogItemIds.Length)
        {
            var missing = catalogItemIds.Except(catalogItems.Select(c => c.Id));
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            var catalogItem = catalogItems.Single(c => c.Id == line.CatalogItemId);
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, line.Quantity));
        }

        var address = ToAddress(shipToAddress);
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        var amountInCents = ToCents(order.Total());
        if (amountInCents <= 0)
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                "The order total must be greater than zero.");
        }

        var payment = new OrderPayment(order.Id, buyerId, amountInCents, _gateway.Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Created order {OrderId} for buyer {BuyerId} with total {Amount} {Currency}.",
            order.Id, buyerId, amountInCents, _gateway.Currency);

        return MapOrder(order, payment, Array.Empty<PaymentRefund>());
    }

    public async Task<OrderInfo> PayOrderAsync(string buyerId, int orderId, PaymentCardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken)
    {
        using var orderLock = await AcquireLockAsync(OrderLocks, orderId, cancellationToken);

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(PaymentErrorKind.NotFound, "Order not found.");
        }

        var payment = await GetPaymentAsync(orderId, cancellationToken);

        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
            case PaymentStatus.Fulfilled:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                return await MapOrderAsync(order, payment, cancellationToken);
            case PaymentStatus.Canceled:
                throw new PaymentOperationException(PaymentErrorKind.Conflict,
                    "This order was canceled and can no longer be paid.");
        }

        if (savedPaymentMethodId.HasValue && card is not null)
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                "Send either card details or a saved payment method id, not both.");
        }

        string paymentInstrumentId;
        if (savedPaymentMethodId.HasValue)
        {
            var saved = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(savedPaymentMethodId.Value, buyerId),
                cancellationToken);

            if (saved is null)
            {
                throw new PaymentOperationException(PaymentErrorKind.NotFound,
                    "Saved payment method not found.");
            }

            FinixCardResult finixCard;
            try
            {
                finixCard = await _gateway.GetCardAsync(saved.FinixPaymentInstrumentId, cancellationToken);
            }
            catch (FinixGatewayException ex)
            {
                throw new PaymentOperationException(PaymentErrorKind.Conflict,
                    $"The saved card can no longer be used: {ex.Message}", innerException: ex);
            }

            if (!finixCard.Enabled)
            {
                throw new PaymentOperationException(PaymentErrorKind.Conflict,
                    "The saved card has been disabled at Finix and cannot be used. Ask the shopper to save a new card.");
            }

            paymentInstrumentId = saved.FinixPaymentInstrumentId;
        }
        else if (card is not null)
        {
            ValidateCard(card);
            paymentInstrumentId = await CreateOneOffCardAsync(buyerId, card, cancellationToken);
        }
        else
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                "Provide card details or a saved payment method id.");
        }

        var idempotencyKey = payment.EnsureAuthorizationIdempotencyKey();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        FinixAuthorizationResult authorization;
        try
        {
            authorization = await _gateway.AuthorizeAsync(paymentInstrumentId, payment.AmountInCents,
                payment.Currency, idempotencyKey, orderId, buyerId, cancellationToken);
        }
        catch (FinixGatewayException ex) when (ex.IsDecline)
        {
            payment.MarkAuthorizationFailed(ex.Message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentOperationException(PaymentErrorKind.PaymentDeclined,
                $"Finix declined the card: {ex.Message}", innerException: ex);
        }
        catch (FinixGatewayException ex)
        {
            payment.MarkAuthorizationUnknown(ex.Message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentOperationException(PaymentErrorKind.GatewayUnavailable,
                ex.Message, innerException: ex);
        }

        if (authorization.AmountInCents != payment.AmountInCents)
        {
            var message = $"Finix authorized {authorization.AmountInCents} cents but the order total is " +
                          $"{payment.AmountInCents} cents. The hold does not match the order and was not accepted.";
            payment.MarkAuthorizationFailed(message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentOperationException(PaymentErrorKind.Conflict, message);
        }

        if (!IsAuthorizedState(authorization.State))
        {
            var message = $"Finix did not authorize the payment. State: {authorization.State ?? "UNKNOWN"}. " +
                          $"{authorization.FailureMessage}";
            payment.MarkAuthorizationFailed(message.Trim());
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentOperationException(PaymentErrorKind.PaymentDeclined, message.Trim());
        }

        payment.MarkAuthorized(authorization.AuthorizationId, authorization.State ?? "UNKNOWN",
            authorization.ExpiresAt, paymentInstrumentId);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Authorized order {OrderId}; authorization {AuthorizationId} for {Amount} cents.",
            orderId, authorization.AuthorizationId, payment.AmountInCents);

        return await MapOrderAsync(order, payment, cancellationToken);
    }

    public async Task<OrderInfo> FulfilOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        using var orderLock = await AcquireLockAsync(OrderLocks, orderId, cancellationToken);

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentOperationException(PaymentErrorKind.NotFound, "Order not found.");
        }

        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Fulfilled)
        {
            // A repeat fulfil call is idempotent. If the first call returned
            // before Finix had created the fee records, refresh them now.
            if (payment.FinixTransferId is not null && (payment.FinixFeeInCents ?? 0) == 0)
            {
                try
                {
                    var details = await _gateway.GetCaptureAsync(payment.FinixTransferId, cancellationToken);
                    payment.UpdateCaptureDetails(details.TransferState ?? "UNKNOWN",
                        details.AmountInCents, details.FeeInCents, details.NetAmountInCents);
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                }
                catch (FinixGatewayException)
                {
                    // Keep the recorded capture; a later fulfil call can retry.
                }
            }

            return await MapOrderAsync(order, payment, cancellationToken);
        }

        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentOperationException(PaymentErrorKind.Conflict,
                $"Order {orderId} cannot be fulfilled from payment status {payment.Status}. " +
                "Only an authorized (held) payment can be captured.");
        }

        if (string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            throw new PaymentOperationException(PaymentErrorKind.Conflict,
                $"Order {orderId} has no Finix authorization id on record, so it cannot be captured.");
        }

        FinixAuthorizationResult authorization;
        try
        {
            authorization = await _gateway.GetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
        }
        catch (FinixGatewayException ex)
        {
            payment.MarkCaptureFailed(ex.Message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentOperationException(PaymentErrorKind.GatewayUnavailable, ex.Message,
                innerException: ex);
        }

        if (!IsHoldCapturable(authorization))
        {
            var (message, action) = DescribeUncapturableHold(authorization);
            payment.MarkCaptureFailed(message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentOperationException(PaymentErrorKind.HoldNotCapturable, message, action);
        }

        var idempotencyKey = payment.EnsureCaptureIdempotencyKey();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        FinixCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.FinixAuthorizationId, payment.AmountInCents,
                idempotencyKey, cancellationToken);
        }
        catch (FinixGatewayException ex) when (ex.IsHoldNotCapturable)
        {
            var (message, action) = DescribeUncapturableHold(authorization);
            var combined = $"{message} Finix said: {ex.Message}";
            payment.MarkCaptureFailed(combined);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentOperationException(PaymentErrorKind.HoldNotCapturable, combined, action, ex);
        }
        catch (FinixGatewayException ex)
        {
            payment.MarkCaptureFailed(ex.Message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentOperationException(PaymentErrorKind.GatewayUnavailable, ex.Message,
                innerException: ex);
        }

        payment.MarkCaptured(capture.TransferId, capture.TransferState ?? "UNKNOWN",
            capture.AmountInCents, capture.FeeInCents, capture.NetAmountInCents);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation(
            "Captured order {OrderId}; transfer {TransferId}, amount {Amount} cents, fee {Fee} cents, net {Net} cents.",
            orderId, capture.TransferId, capture.AmountInCents, capture.FeeInCents, capture.NetAmountInCents);

        return await MapOrderAsync(order, payment, cancellationToken);
    }

    public async Task<OrderInfo> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        using var orderLock = await AcquireLockAsync(OrderLocks, orderId, cancellationToken);

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentOperationException(PaymentErrorKind.NotFound, "Order not found.");
        }

        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Canceled)
        {
            return await MapOrderAsync(order, payment, cancellationToken);
        }

        if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentOperationException(PaymentErrorKind.Conflict,
                "This payment has already been captured. Use the refunds endpoint instead of cancel.");
        }

        if (payment.Status == PaymentStatus.Authorized &&
            !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            FinixAuthorizationResult authorization;
            try
            {
                authorization = await _gateway.GetAuthorizationAsync(payment.FinixAuthorizationId,
                    cancellationToken);
            }
            catch (FinixGatewayException ex)
            {
                throw new PaymentOperationException(PaymentErrorKind.GatewayUnavailable, ex.Message,
                    innerException: ex);
            }

            var shouldVoid = !authorization.IsVoid &&
                             !string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase) &&
                             !string.Equals(authorization.State, "FAILED", StringComparison.OrdinalIgnoreCase) &&
                             (authorization.ExpiresAt is null || authorization.ExpiresAt > DateTimeOffset.UtcNow);

            if (shouldVoid)
            {
                try
                {
                    var voided = await _gateway.VoidAsync(payment.FinixAuthorizationId, cancellationToken);
                    payment.MarkCanceled(voided.State ?? "VOIDED");
                }
                catch (FinixGatewayException ex)
                {
                    payment.MarkCaptureFailed(ex.Message);
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    throw new PaymentOperationException(PaymentErrorKind.GatewayUnavailable, ex.Message,
                        innerException: ex);
                }
            }
            else
            {
                // Already voided, expired or otherwise no longer capturable: no money is held.
                payment.MarkCanceled(authorization.IsVoid ? "VOIDED" : authorization.State);
            }
        }
        else
        {
            payment.MarkCanceled(null);
        }

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Canceled order {OrderId}; no funds were captured.", orderId);

        return await MapOrderAsync(order, payment, cancellationToken);
    }

    public async Task<RefundInfo> RefundOrderAsync(int orderId, long? amountInCents,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                "An idempotency key is required for refunds.");
        }

        using var orderLock = await AcquireLockAsync(OrderLocks, orderId, cancellationToken);

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentOperationException(PaymentErrorKind.NotFound, "Order not found.");
        }

        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded))
        {
            throw new PaymentOperationException(PaymentErrorKind.Conflict,
                $"Order {orderId} has not been captured, so there is nothing to refund. " +
                "Cancel an authorized order instead.");
        }

        if (string.IsNullOrWhiteSpace(payment.FinixTransferId))
        {
            throw new PaymentOperationException(PaymentErrorKind.Conflict,
                $"Order {orderId} has no capture transfer id on record, so it cannot be refunded.");
        }

        var refundable = payment.RefundableAmountInCents;
        var refundAmount = amountInCents ?? refundable;

        if (refundAmount <= 0)
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                "The refund amount must be greater than zero.");
        }

        if (refundAmount > refundable)
        {
            throw new PaymentOperationException(PaymentErrorKind.Conflict,
                $"Refund of {refundAmount} cents exceeds the remaining refundable amount of {refundable} cents.");
        }

        var existing = await _refundRepository.FirstOrDefaultAsync(
            new PaymentRefundByKeySpec(payment.Id, idempotencyKey), cancellationToken);

        if (existing is not null)
        {
            if (existing.AmountInCents != refundAmount)
            {
                throw new PaymentOperationException(PaymentErrorKind.Conflict,
                    $"Idempotency key '{idempotencyKey}' was already used for a refund of " +
                    $"{existing.AmountInCents} cents; a different amount is not allowed.");
            }

            if (existing.Status == RefundStatus.Succeeded)
            {
                return MapRefund(existing);
            }

            if (existing.Status == RefundStatus.Failed)
            {
                throw new PaymentOperationException(PaymentErrorKind.Conflict,
                    $"A previous refund under idempotency key '{idempotencyKey}' failed: {existing.LastError}");
            }

            // Pending: retry the same Finix idempotency key below.
        }

        var refund = existing ?? new PaymentRefund(payment.Id, orderId, payment.BuyerId,
            refundAmount, idempotencyKey);

        if (existing is null)
        {
            await _refundRepository.AddAsync(refund, cancellationToken);
        }

        try
        {
            var result = await _gateway.RefundAsync(payment.FinixTransferId, refundAmount,
                idempotencyKey, cancellationToken);

            refund.MarkSucceeded(result.RefundTransferId, result.State);
            await _refundRepository.UpdateAsync(refund, cancellationToken);

            var allRefunds = await _refundRepository.ListAsync(
                new PaymentRefundsByOrderPaymentSpec(payment.Id), cancellationToken);
            var totalRefunded = allRefunds
                .Where(r => r.Status == RefundStatus.Succeeded)
                .Sum(r => r.AmountInCents);

            payment.MarkRefunded(totalRefunded);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            _logger.LogInformation("Refunded {Amount} cents on order {OrderId}; refund {RefundId}.",
                refundAmount, orderId, result.RefundTransferId);

            return MapRefund(refund);
        }
        catch (FinixGatewayException ex)
        {
            refund.MarkFailed(ex.Message);
            await _refundRepository.UpdateAsync(refund, cancellationToken);
            throw new PaymentOperationException(
                ex.IsDecline ? PaymentErrorKind.PaymentDeclined : PaymentErrorKind.GatewayUnavailable,
                ex.Message, innerException: ex);
        }
    }

    public async Task<IReadOnlyList<OrderInfo>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(
            new OrderPaymentsByBuyerSpec(buyerId), cancellationToken);
        var refunds = await _refundRepository.ListAsync(
            new PaymentRefundsByBuyerSpec(buyerId), cancellationToken);

        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);
        var refundsByPayment = refunds.GroupBy(r => r.OrderPaymentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PaymentRefund>)g.ToList());

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order =>
            {
                if (!paymentsByOrder.TryGetValue(order.Id, out var payment))
                {
                    payment = new OrderPayment(order.Id, order.BuyerId, ToCents(order.Total()),
                        _gateway.Currency);
                }

                var paymentRefunds = refundsByPayment.TryGetValue(payment.Id, out var list)
                    ? list
                    : Array.Empty<PaymentRefund>();

                return MapOrder(order, payment, paymentRefunds);
            })
            .ToList();
    }

    public async Task<OrderInfo> GetOrderAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(PaymentErrorKind.NotFound, "Order not found.");
        }

        var payment = await GetPaymentAsync(orderId, cancellationToken);
        return await MapOrderAsync(order, payment, cancellationToken);
    }

    public async Task<SavedCardInfo> SavePaymentMethodAsync(string buyerId, PaymentCardDetails card,
        CancellationToken cancellationToken)
    {
        ValidateCard(card);

        using var buyerLock = await AcquireLockAsync(BuyerLocks, buyerId, cancellationToken);

        var profile = await _profileRepository.FirstOrDefaultAsync(
            new ShopperFinixProfileByBuyerSpec(buyerId), cancellationToken);

        if (profile is null)
        {
            var identity = await _gateway.CreateBuyerIdentityAsync(buyerId, card.Name, card.Address,
                cancellationToken);
            profile = new ShopperFinixProfile(buyerId, identity.IdentityId);
            await _profileRepository.AddAsync(profile, cancellationToken);
        }

        var finixCard = await _gateway.CreateCardAsync(profile.FinixIdentityId, card, cancellationToken);
        var saved = new SavedPaymentMethod(buyerId, finixCard.PaymentInstrumentId, finixCard.Brand,
            finixCard.LastFour, finixCard.ExpirationMonth ?? card.ExpirationMonth,
            finixCard.ExpirationYear ?? card.ExpirationYear, finixCard.CardType,
            finixCard.Name ?? card.Name);

        await _savedPaymentMethodRepository.AddAsync(saved, cancellationToken);

        return MapSavedCard(saved);
    }

    public async Task<IReadOnlyList<SavedCardInfo>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var saved = await _savedPaymentMethodRepository.ListAsync(
            new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);

        return saved.Select(MapSavedCard).ToList();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        using var buyerLock = await AcquireLockAsync(BuyerLocks, buyerId, cancellationToken);

        var saved = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);

        if (saved is null)
        {
            throw new PaymentOperationException(PaymentErrorKind.NotFound,
                "Saved payment method not found.");
        }

        saved.MarkDeleted();
        await _savedPaymentMethodRepository.UpdateAsync(saved, cancellationToken);

        try
        {
            await _gateway.DisableCardAsync(saved.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (FinixGatewayException ex)
        {
            // The local record is already gone, so the card can no longer be used
            // through eShop. Log the Finix-side failure for an operator to follow up.
            _logger.LogWarning("Card {PaymentMethodId} was removed locally but Finix disable failed: {Message}",
                paymentMethodId, ex.Message);
        }
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                "'from' must be earlier than or equal to 'to'.");
        }

        var transactions = await _gateway.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new AllOrderPaymentsSpec(), cancellationToken);
        var refunds = await _refundRepository.ListAsync(new AllPaymentRefundsSpec(), cancellationToken);

        var paymentsInRange = payments
            .Where(p => IsInRange(p, from, to))
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixAuthorizationId) ||
                        !string.IsNullOrWhiteSpace(p.FinixTransferId))
            .ToList();
        var refundsByPayment = refunds.GroupBy(r => r.OrderPaymentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var paymentsByAuthorization = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixAuthorizationId))
            .GroupBy(p => p.FinixAuthorizationId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var paymentsByCapture = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixTransferId))
            .GroupBy(p => p.FinixTransferId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var refundsByTransfer = refunds
            .Where(r => !string.IsNullOrWhiteSpace(r.FinixRefundTransferId))
            .GroupBy(r => r.FinixRefundTransferId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matchedOrderIds = new HashSet<int>();
        var finixTransactions = new List<FinixTransactionInfo>();
        var missingInEshop = new List<FinixTransactionInfo>();

        foreach (var transaction in transactions)
        {
            int? matchedOrderId = null;
            var matchStatus = "MissingInEshop";

            if (transaction.Kind == "AUTHORIZATION" &&
                paymentsByAuthorization.TryGetValue(transaction.Id, out var byAuthorization))
            {
                matchedOrderId = byAuthorization.OrderId;
            }
            else if (transaction.Kind == "TRANSFER" &&
                     paymentsByCapture.TryGetValue(transaction.Id, out var byCapture))
            {
                matchedOrderId = byCapture.OrderId;
            }
            else if (transaction.Kind == "TRANSFER" &&
                     refundsByTransfer.TryGetValue(transaction.Id, out var refund))
            {
                matchedOrderId = refund.OrderId;
            }
            else if (transaction.Kind == "TRANSFER" &&
                     !string.IsNullOrWhiteSpace(transaction.ParentTransferId) &&
                     paymentsByCapture.TryGetValue(transaction.ParentTransferId, out var parent))
            {
                matchedOrderId = parent.OrderId;
            }

            if (matchedOrderId.HasValue)
            {
                matchedOrderIds.Add(matchedOrderId.Value);
                matchStatus = "Matched";
            }

            var info = new FinixTransactionInfo
            {
                Id = transaction.Id,
                Kind = transaction.Kind,
                Type = transaction.Type,
                State = transaction.State,
                AmountInCents = transaction.AmountInCents,
                Currency = transaction.Currency,
                CreatedAt = transaction.CreatedAt,
                ParentTransferId = transaction.ParentTransferId,
                MatchedOrderId = matchedOrderId,
                MatchStatus = matchStatus
            };

            finixTransactions.Add(info);
            if (!matchedOrderId.HasValue)
            {
                missingInEshop.Add(info);
            }
        }

        var eshopPayments = new List<EshopPaymentInfo>();
        var missingInFinix = new List<EshopPaymentInfo>();

        foreach (var payment in paymentsInRange)
        {
            var matched = matchedOrderIds.Contains(payment.OrderId) ||
                          (payment.FinixAuthorizationId is not null &&
                           transactions.Any(t => t.Id == payment.FinixAuthorizationId)) ||
                          (payment.FinixTransferId is not null &&
                           transactions.Any(t => t.Id == payment.FinixTransferId));

            var info = new EshopPaymentInfo
            {
                OrderId = payment.OrderId,
                BuyerId = payment.BuyerId,
                Status = payment.Status.ToString(),
                AuthorizationId = payment.FinixAuthorizationId,
                CaptureTransferId = payment.FinixTransferId,
                CapturedAmountInCents = payment.CapturedAmountInCents,
                RefundedAmountInCents = payment.RefundedAmountInCents,
                CreatedAt = payment.CreatedAt,
                MatchStatus = matched ? "Matched" : "MissingInFinix"
            };

            eshopPayments.Add(info);
            if (!matched)
            {
                missingInFinix.Add(info);
            }
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FinixTransactions = finixTransactions,
            EshopPayments = eshopPayments,
            MissingInEshop = missingInEshop,
            MissingInFinix = missingInFinix
        };
    }

    private async Task<string> CreateOneOffCardAsync(string buyerId, PaymentCardDetails card,
        CancellationToken cancellationToken)
    {
        using var buyerLock = await AcquireLockAsync(BuyerLocks, buyerId, cancellationToken);

        var profile = await _profileRepository.FirstOrDefaultAsync(
            new ShopperFinixProfileByBuyerSpec(buyerId), cancellationToken);

        if (profile is null)
        {
            var identity = await _gateway.CreateBuyerIdentityAsync(buyerId, card.Name, card.Address,
                cancellationToken);
            profile = new ShopperFinixProfile(buyerId, identity.IdentityId);
            await _profileRepository.AddAsync(profile, cancellationToken);
        }

        var finixCard = await _gateway.CreateCardAsync(profile.FinixIdentityId, card, cancellationToken);
        return finixCard.PaymentInstrumentId;
    }

    private async Task<OrderPayment> GetPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpec(orderId), cancellationToken);

        if (payment is null)
        {
            throw new PaymentOperationException(PaymentErrorKind.Conflict,
                $"Order {orderId} has no payment record.");
        }

        return payment;
    }

    private async Task<OrderInfo> MapOrderAsync(Order order, OrderPayment payment,
        CancellationToken cancellationToken)
    {
        var refunds = await _refundRepository.ListAsync(
            new PaymentRefundsByOrderPaymentSpec(payment.Id), cancellationToken);
        return MapOrder(order, payment, refunds);
    }

    private static OrderInfo MapOrder(Order order, OrderPayment payment,
        IReadOnlyList<PaymentRefund> refunds)
    {
        var items = order.OrderItems.Select(item => new OrderItemInfo
        {
            CatalogItemId = item.ItemOrdered.CatalogItemId,
            ProductName = item.ItemOrdered.ProductName,
            UnitPriceInCents = ToCents(item.UnitPrice),
            Quantity = item.Units,
            LineTotalInCents = ToCents(item.UnitPrice * item.Units)
        }).ToList();

        return new OrderInfo
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            TotalInCents = ToCents(order.Total()),
            Currency = payment.Currency,
            ShipToAddress = new PaymentAddress
            {
                Line1 = order.ShipToAddress.Street,
                City = order.ShipToAddress.City,
                Region = order.ShipToAddress.State,
                PostalCode = order.ShipToAddress.ZipCode,
                Country = order.ShipToAddress.Country
            },
            Items = items,
            Payment = MapPayment(payment, refunds)
        };
    }

    private static OrderPaymentInfo MapPayment(OrderPayment payment,
        IReadOnlyList<PaymentRefund> refunds) =>
        new()
        {
            OrderId = payment.OrderId,
            BuyerId = payment.BuyerId,
            Status = payment.Status.ToString(),
            AmountInCents = payment.AmountInCents,
            Currency = payment.Currency,
            PaymentInstrumentId = payment.PaymentInstrumentId,
            AuthorizationId = payment.FinixAuthorizationId,
            AuthorizationState = payment.AuthorizationState,
            AuthorizedAt = payment.AuthorizedAt,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureTransferId = payment.FinixTransferId,
            TransferState = payment.TransferState,
            CapturedAt = payment.CapturedAt,
            CapturedAmountInCents = payment.CapturedAmountInCents,
            FinixFeeInCents = payment.FinixFeeInCents,
            NetAmountInCents = payment.NetAmountInCents,
            RefundedAmountInCents = payment.RefundedAmountInCents,
            RefundableAmountInCents = payment.RefundableAmountInCents,
            CanceledAt = payment.CanceledAt,
            VoidState = payment.VoidState,
            LastError = payment.LastError,
            CreatedAt = payment.CreatedAt,
            UpdatedAt = payment.UpdatedAt,
            Refunds = refunds.Select(MapRefund).ToList()
        };

    private static SavedCardInfo MapSavedCard(SavedPaymentMethod saved) =>
        new()
        {
            PaymentMethodId = saved.Id,
            FinixPaymentInstrumentId = saved.FinixPaymentInstrumentId,
            Brand = saved.Brand,
            LastFour = saved.LastFour,
            ExpirationMonth = saved.ExpirationMonth,
            ExpirationYear = saved.ExpirationYear,
            CardType = saved.CardType,
            Name = saved.Name,
            CreatedAt = saved.CreatedAt
        };

    private static RefundInfo MapRefund(PaymentRefund refund) =>
        new()
        {
            RefundId = refund.FinixRefundTransferId ?? $"pending-{refund.Id}",
            AmountInCents = refund.AmountInCents,
            Status = refund.Status.ToString(),
            FinixState = refund.FinixState,
            IdempotencyKey = refund.IdempotencyKey,
            CreatedAt = refund.CreatedAt,
            LastError = refund.LastError
        };

    private static Address ToAddress(PaymentAddress? address)
    {
        if (address is null ||
            string.IsNullOrWhiteSpace(address.Line1) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.PostalCode))
        {
            return new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        }

        return new Address(address.Line1.Trim(), address.City.Trim(), address.Region?.Trim() ?? "OH",
            address.Country.Trim(), address.PostalCode.Trim());
    }

    private static void ValidateCard(PaymentCardDetails card)
    {
        if (card is null)
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation, "Card details are required.");
        }

        var digits = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 12 || digits.Length > 19)
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                "The card number is not valid.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                "The card expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentOperationException(PaymentErrorKind.Validation,
                "The card has expired.");
        }
    }

    private static bool IsAuthorizedState(string? state) =>
        string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "PENDING", StringComparison.OrdinalIgnoreCase);

    private static bool IsHoldCapturable(FinixAuthorizationResult authorization)
    {
        if (authorization.IsVoid)
        {
            return false;
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return authorization.ExpiresAt is null || authorization.ExpiresAt > DateTimeOffset.UtcNow;
    }

    private static (string Message, string Action) DescribeUncapturableHold(
        FinixAuthorizationResult authorization)
    {
        var state = authorization.State ?? "UNKNOWN";
        var expires = authorization.ExpiresAt?.ToString("u", System.Globalization.CultureInfo.InvariantCulture);
        var action = "Ask the shopper to place a new order and authorize payment again; do not retry capture on this hold.";

        if (authorization.IsVoid)
        {
            return ($"Authorization {authorization.AuthorizationId} was voided and can no longer be captured. " +
                    "The shopper's funds are not captured.", action);
        }

        if (string.Equals(state, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            return ($"Authorization {authorization.AuthorizationId} is canceled and can no longer be captured. " +
                    "The shopper's funds are not captured.", action);
        }

        if (authorization.ExpiresAt is not null && authorization.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return ($"Authorization {authorization.AuthorizationId} expired at {expires} and can no longer be captured. " +
                    "The shopper's funds have been released.", action);
        }

        return ($"Authorization {authorization.AuthorizationId} is in state {state} and cannot be captured. " +
                "The shopper's funds are not captured.", action);
    }

    private static bool IsInRange(OrderPayment payment, DateTimeOffset from, DateTimeOffset to) =>
        IsBetween(payment.CreatedAt, from, to) ||
        (payment.AuthorizedAt is not null && IsBetween(payment.AuthorizedAt.Value, from, to)) ||
        (payment.CapturedAt is not null && IsBetween(payment.CapturedAt.Value, from, to)) ||
        (payment.CanceledAt is not null && IsBetween(payment.CanceledAt.Value, from, to));

    private static bool IsBetween(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;

    private static long ToCents(decimal amount) =>
        checked((long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero));

    private static async Task<IDisposable> AcquireLockAsync<TKey>(
        ConcurrentDictionary<TKey, SemaphoreSlim> locks, TKey key, CancellationToken cancellationToken)
        where TKey : notnull
    {
        var semaphore = locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new SemaphoreReleaser(semaphore);
    }

    private sealed class SemaphoreReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public SemaphoreReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _semaphore.Release();
        }
    }
}
