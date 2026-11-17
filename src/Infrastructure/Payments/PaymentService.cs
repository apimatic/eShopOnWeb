using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OwnerLocks = new(StringComparer.Ordinal);

    private readonly CatalogContext _db;
    private readonly FinixGateway _gateway;
    private readonly FinixOptions _options;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        CatalogContext db,
        FinixGateway gateway,
        IOptions<FinixOptions> options,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _gateway = gateway;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OrderView> CreateOrderAsync(string buyerId, CreateOrderInput input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException("missing_buyer", "The caller identity is missing.", 401);
        }

        if (input.Items is null || input.Items.Count == 0)
        {
            throw new PaymentOperationException("empty_order", "An order must contain at least one catalog item.", 400);
        }

        var lines = input.Items
            .GroupBy(item => item.CatalogItemId)
            .Select(group => new OrderLineInput(group.Key, group.Sum(item => item.Quantity)))
            .ToList();

        if (lines.Any(line => line.CatalogItemId <= 0 || line.Quantity <= 0))
        {
            throw new PaymentOperationException("invalid_order_line", "Catalog item ids and quantities must be positive.", 400);
        }

        var catalogItemIds = lines.Select(line => line.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(item => catalogItemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        if (catalogItems.Count != catalogItemIds.Length)
        {
            var found = catalogItems.Select(item => item.Id).ToHashSet();
            var missing = catalogItemIds.Where(id => !found.Contains(id));
            throw new PaymentOperationException(
                "catalog_item_not_found",
                $"Catalog item(s) not found: {string.Join(", ", missing)}.",
                400);
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.Single(item => item.Id == line.CatalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : catalogItem.PictureUri;
            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri),
                catalogItem.Price,
                line.Quantity));
        }

        var order = new Order(buyerId, ToOrderAddress(input.ShippingAddress), orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var payment = new Payment(order.Id, buyerId, ToCents(order.Total()), _options.Currency);
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        return ToOrderView(order, payment);
    }

    public async Task<OrderView> PayOrderAsync(string buyerId, int orderId, PayOrderInput input, CancellationToken cancellationToken = default)
    {
        using var orderLock = await AcquireOrderLockAsync(orderId, cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException("order_not_found", "Order not found.", 404);
        }

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment is null)
        {
            throw new PaymentOperationException("payment_not_found", "The order has no payment record.", 409);
        }

        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return ToOrderView(order, payment);
        }

        if (payment.Status == PaymentStatus.Canceled)
        {
            throw new PaymentOperationException("order_canceled", "The order was canceled and cannot be paid.", 409);
        }

        if (payment.Status == PaymentStatus.Failed)
        {
            payment.ResetForRetry();
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (input.PaymentMethodId is null && input.Card is null)
        {
            throw new PaymentOperationException("payment_source_required", "Provide card details or a saved paymentMethodId.", 400);
        }

        string source;
        var usedSavedCard = false;

        if (input.PaymentMethodId is int paymentMethodId)
        {
            var savedCard = await _db.SavedCards.FirstOrDefaultAsync(
                card => card.Id == paymentMethodId && card.OwnerId == buyerId && card.IsActive,
                cancellationToken);

            if (savedCard is null)
            {
                throw new PaymentOperationException("payment_method_not_found", "Saved payment method not found.", 404);
            }

            source = savedCard.FinixPaymentInstrumentId;
            usedSavedCard = true;
        }
        else
        {
            var identityId = await GetOrCreateFinixIdentityAsync(buyerId, cancellationToken);
            var finixCard = await _gateway.CreateCardAsync(identityId, input.Card!, cancellationToken);
            source = finixCard.Id;
        }

        var amountCents = ToCents(order.Total());
        if (amountCents <= 0)
        {
            throw new PaymentOperationException("invalid_order_total", "The order total must be greater than zero.", 400);
        }

        var authorizationIdempotencyKey = payment.EnsureAuthorizationIdempotencyKey();
        await _db.SaveChangesAsync(cancellationToken);

        var authorization = await _gateway.AuthorizeAsync(
            amountCents,
            source,
            authorizationIdempotencyKey,
            cancellationToken);

        if (authorization.AmountCents is long authorizedAmount && authorizedAmount != amountCents)
        {
            payment.MarkFailed("authorization_amount_mismatch",
                $"Finix returned an authorization for {authorizedAmount} cents but the order total is {amountCents} cents.");
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(
                "authorization_amount_mismatch",
                "Finix returned an authorization for a different amount than the order total. Cancel the order and try again.",
                409,
                new Dictionary<string, object?>
                {
                    ["authorizationId"] = authorization.Id,
                    ["authorizedAmountCents"] = authorizedAmount,
                    ["orderAmountCents"] = amountCents
                });
        }

        if (string.Equals(authorization.State, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            payment.MarkFailed(authorization.FailureCode, authorization.FailureMessage);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(
                "authorization_declined",
                authorization.FailureMessage ?? "The card authorization was declined.",
                402,
                new Dictionary<string, object?>
                {
                    ["failureCode"] = authorization.FailureCode,
                    ["authorizationId"] = authorization.Id
                });
        }

        payment.MarkAuthorized(
            authorization.Id,
            authorization.State ?? "UNKNOWN",
            source,
            usedSavedCard);
        await _db.SaveChangesAsync(cancellationToken);

        return ToOrderView(order, payment);
    }

    public async Task<OrderView> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var orderLock = await AcquireOrderLockAsync(orderId, cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new PaymentOperationException("order_not_found", "Order not found.", 404);
        }

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment is null)
        {
            throw new PaymentOperationException("payment_not_found", "The order has no payment record.", 404);
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return ToOrderView(order, payment);
        }

        if (payment.Status == PaymentStatus.Canceled)
        {
            throw new PaymentOperationException("order_canceled", "The order was canceled and cannot be fulfilled.", 409);
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            throw new PaymentOperationException(
                "payment_not_authorized",
                "The order has no active authorization to capture. Pay for the order first.",
                409);
        }

        var authorization = await _gateway.GetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
        payment.MarkAuthorizationState(authorization.State ?? "UNKNOWN");

        if (!authorization.IsVoid && string.Equals(authorization.State, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(
                "authorization_pending",
                "Finix is still processing the authorization. Retry fulfilment shortly.",
                409,
                new Dictionary<string, object?>
                {
                    ["authorizationId"] = authorization.Id,
                    ["authorizationState"] = authorization.State
                });
        }

        if (authorization.IsVoid || !string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            var message =
                $"The authorization can no longer be captured (Finix state: {authorization.State ?? "UNKNOWN"}" +
                (authorization.IsVoid ? ", voided" : string.Empty) +
                "). Cancel the order and collect a new payment.";
            payment.MarkFailed("authorization_not_capturable", message);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(
                "authorization_not_capturable",
                message,
                409,
                new Dictionary<string, object?>
                {
                    ["authorizationId"] = authorization.Id,
                    ["authorizationState"] = authorization.State,
                    ["isVoid"] = authorization.IsVoid
                });
        }

        if (authorization.ExpiresAt is DateTimeOffset expiresAt && expiresAt <= DateTimeOffset.UtcNow)
        {
            var message =
                $"The authorization expired at {expiresAt:O} and can no longer be captured. Cancel the order and collect a new payment.";
            payment.MarkFailed("authorization_expired", message);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(
                "authorization_expired",
                message,
                409,
                new Dictionary<string, object?>
                {
                    ["authorizationId"] = authorization.Id,
                    ["expiresAt"] = expiresAt
                });
        }

        var captureTransferId = authorization.TransferId;
        if (string.IsNullOrWhiteSpace(captureTransferId))
        {
            var capture = await _gateway.CaptureAsync(payment.FinixAuthorizationId, payment.AmountCents, cancellationToken);
            captureTransferId = capture.TransferId;
            payment.MarkAuthorizationState(capture.State ?? authorization.State ?? "UNKNOWN");
        }

        if (string.IsNullOrWhiteSpace(captureTransferId))
        {
            throw new PaymentOperationException(
                "capture_missing_transfer",
                "Finix captured the authorization but did not return a transfer id. Contact Finix support before retrying.",
                502,
                new Dictionary<string, object?> { ["authorizationId"] = payment.FinixAuthorizationId });
        }

        var transfer = await _gateway.GetTransferAsync(captureTransferId, cancellationToken);
        if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(transfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(transfer.State, "RETURNED", StringComparison.OrdinalIgnoreCase))
        {
            var message = $"Finix reports the capture transfer is {transfer.State}. The payment was not taken; contact Finix support with transfer {transfer.Id}.";
            payment.MarkFailed("capture_failed", message);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(
                "capture_failed",
                message,
                409,
                new Dictionary<string, object?>
                {
                    ["transferId"] = transfer.Id,
                    ["transferState"] = transfer.State
                });
        }

        payment.MarkCaptured(
            transfer.Id,
            transfer.State ?? "UNKNOWN",
            transfer.AmountCents ?? payment.AmountCents,
            transfer.FeeCents);
        await _db.SaveChangesAsync(cancellationToken);

        return ToOrderView(order, payment);
    }

    public async Task<OrderView> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var orderLock = await AcquireOrderLockAsync(orderId, cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new PaymentOperationException("order_not_found", "Order not found.", 404);
        }

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment is null)
        {
            throw new PaymentOperationException("payment_not_found", "The order has no payment record.", 404);
        }

        if (payment.Status == PaymentStatus.Canceled)
        {
            return ToOrderView(order, payment);
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentOperationException(
                "payment_already_captured",
                "The payment was already captured. Use the refunds endpoint instead of cancel.",
                409);
        }

        if ((payment.Status == PaymentStatus.Authorized || payment.Status == PaymentStatus.Failed)
            && !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            var authorization = await _gateway.GetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
            if (!authorization.IsVoid && !string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                var voided = await _gateway.VoidAsync(payment.FinixAuthorizationId, cancellationToken);
                payment.MarkAuthorizationState(voided.State ?? "CANCELED");
            }
        }

        payment.MarkCanceled();
        await _db.SaveChangesAsync(cancellationToken);

        return ToOrderView(order, payment);
    }

    public async Task<RefundView> RefundOrderAsync(
        string callerId,
        bool isOperator,
        int orderId,
        RefundOrderInput input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            throw new PaymentOperationException("missing_idempotency_key", "An idempotencyKey is required for refunds.", 400);
        }

        using var orderLock = await AcquireOrderLockAsync(orderId, cancellationToken);

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment is null)
        {
            throw new PaymentOperationException("payment_not_found", "Payment not found for the order.", 404);
        }

        if (!isOperator && !string.Equals(payment.BuyerId, callerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException("payment_not_found", "Payment not found for the order.", 404);
        }

        var existing = payment.Refunds.FirstOrDefault(
            refund => string.Equals(refund.IdempotencyKey, input.IdempotencyKey, StringComparison.Ordinal));
        if (existing is not null)
        {
            var requestedAmount = ResolveRequestedAmount(input, existing.AmountCents);
            if (requestedAmount != existing.AmountCents)
            {
                throw new PaymentOperationException(
                    "idempotency_key_conflict",
                    "This idempotencyKey was already used for a different refund amount.",
                    409);
            }

            return ToRefundView(existing);
        }

        if (payment.Status == PaymentStatus.Refunded)
        {
            throw new PaymentOperationException(
                "nothing_to_refund",
                "The captured payment has already been fully refunded.",
                409);
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentOperationException(
                "payment_not_captured",
                "Only a captured payment can be refunded. Fulfil the order first.",
                409);
        }

        if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
        {
            throw new PaymentOperationException("capture_missing_transfer", "The captured payment has no Finix transfer id.", 409);
        }

        var remaining = payment.RefundableAmountCents();
        if (remaining <= 0)
        {
            throw new PaymentOperationException("nothing_to_refund", "The captured payment has already been fully refunded.", 409);
        }

        var amountCents = ResolveRequestedAmount(input, remaining);
        if (amountCents <= 0)
        {
            throw new PaymentOperationException("invalid_refund_amount", "The refund amount must be greater than zero.", 400);
        }

        if (amountCents > remaining)
        {
            throw new PaymentOperationException(
                "refund_exceeds_capture",
                $"The refund amount ({amountCents} cents) exceeds the refundable amount ({remaining} cents).",
                409,
                new Dictionary<string, object?>
                {
                    ["capturedAmountCents"] = payment.CapturedAmountCents,
                    ["refundedAmountCents"] = payment.RefundedAmountCents(),
                    ["refundableAmountCents"] = remaining
                });
        }

        var capturedAmount = payment.CapturedAmountCents ?? payment.AmountCents;
        var requiresSettledCapture = amountCents < capturedAmount;

        if (requiresSettledCapture)
        {
            await WaitForSettledTransferAsync(payment.FinixCaptureTransferId, cancellationToken);
        }

        FinixTransferResult refundTransfer;
        try
        {
            refundTransfer = await _gateway.RefundAsync(payment.FinixCaptureTransferId, amountCents, cancellationToken);
        }
        catch (PaymentOperationException exception) when (exception.Message.Contains("PENDING", StringComparison.OrdinalIgnoreCase))
        {
            await WaitForSettledTransferAsync(payment.FinixCaptureTransferId, cancellationToken);
            refundTransfer = await _gateway.RefundAsync(payment.FinixCaptureTransferId, amountCents, cancellationToken);
        }

        var refund = payment.AddRefund(
            input.IdempotencyKey.Trim(),
            amountCents,
            refundTransfer.Id,
            refundTransfer.State);

        await _db.SaveChangesAsync(cancellationToken);

        return ToRefundView(refund);
    }

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders
            .Include(order => order.OrderItems)
            .Where(order => order.BuyerId == buyerId)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

        var payments = await _db.Payments
            .Include(payment => payment.Refunds)
            .Where(payment => payment.BuyerId == buyerId)
            .ToListAsync(cancellationToken);

        var paymentsByOrder = payments.ToDictionary(payment => payment.OrderId);
        return orders
            .Select(order => ToOrderView(order, paymentsByOrder.GetValueOrDefault(order.Id)))
            .ToList();
    }

    public async Task<OrderView?> GetOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        return ToOrderView(order, payment);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentOperationException("invalid_date_range", "The from date must be before or equal to the to date.", 400);
        }

        var (authorizations, transfers) = await _gateway.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _db.Payments
            .Include(payment => payment.Refunds)
            .ToListAsync(cancellationToken);

        var paymentsById = payments.ToDictionary(payment => payment.Id);
        var paymentByAuthorization = payments
            .Where(payment => !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            .ToDictionary(payment => payment.FinixAuthorizationId!, payment => payment, StringComparer.Ordinal);
        var paymentByCapture = payments
            .Where(payment => !string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
            .ToDictionary(payment => payment.FinixCaptureTransferId!, payment => payment, StringComparer.Ordinal);
        var refundByTransfer = payments
            .SelectMany(payment => payment.Refunds)
            .Where(refund => !string.IsNullOrWhiteSpace(refund.FinixTransferId))
            .ToDictionary(refund => refund.FinixTransferId!, refund => refund, StringComparer.Ordinal);

        var finixTransactions = new List<FinixTransactionView>();
        var unmatchedFinix = new List<ReconciliationMismatch>();

        foreach (var authorization in authorizations)
        {
            if (paymentByAuthorization.TryGetValue(authorization.Id, out var payment))
            {
                finixTransactions.Add(ToFinixTransactionView(authorization, payment.OrderId, null));
            }
            else
            {
                finixTransactions.Add(ToFinixTransactionView(authorization, null, null));
                unmatchedFinix.Add(new ReconciliationMismatch(
                    authorization.RecordType,
                    authorization.Id,
                    null,
                    null,
                    authorization.AmountCents,
                    authorization.State,
                    "Finix has an authorization with no matching eShop payment."));
            }
        }

        foreach (var transfer in transfers)
        {
            if (paymentByCapture.TryGetValue(transfer.Id, out var capturedPayment))
            {
                finixTransactions.Add(ToFinixTransactionView(transfer, capturedPayment.OrderId, null));
                continue;
            }

            if (refundByTransfer.TryGetValue(transfer.Id, out var refund))
            {
                var payment = paymentsById.GetValueOrDefault(refund.PaymentId);
                finixTransactions.Add(ToFinixTransactionView(transfer, payment?.OrderId, refund.Id));
                continue;
            }

            finixTransactions.Add(ToFinixTransactionView(transfer, null, null));
            unmatchedFinix.Add(new ReconciliationMismatch(
                transfer.RecordType,
                transfer.Id,
                null,
                null,
                transfer.AmountCents,
                transfer.State,
                "Finix has a transfer with no matching eShop payment or refund."));
        }

        var finixAuthorizationIds = authorizations.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var finixTransferIds = transfers.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var unmatchedEshop = new List<ReconciliationMismatch>();

        foreach (var payment in payments.Where(payment => payment.CreatedAt >= from && payment.CreatedAt <= to))
        {
            if (!string.IsNullOrWhiteSpace(payment.FinixAuthorizationId)
                && !finixAuthorizationIds.Contains(payment.FinixAuthorizationId))
            {
                unmatchedEshop.Add(new ReconciliationMismatch(
                    "AUTHORIZATION",
                    payment.FinixAuthorizationId,
                    payment.OrderId,
                    null,
                    payment.AmountCents,
                    payment.FinixAuthorizationState,
                    "eShop has an authorization that Finix did not return for this range."));
            }

            if (!string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId)
                && !finixTransferIds.Contains(payment.FinixCaptureTransferId))
            {
                unmatchedEshop.Add(new ReconciliationMismatch(
                    "TRANSFER",
                    payment.FinixCaptureTransferId,
                    payment.OrderId,
                    null,
                    payment.CapturedAmountCents,
                    payment.FinixCaptureState,
                    "eShop has a capture transfer that Finix did not return for this range."));
            }

            foreach (var refund in payment.Refunds.Where(refund => refund.CreatedAt >= from && refund.CreatedAt <= to))
            {
                if (!string.IsNullOrWhiteSpace(refund.FinixTransferId)
                    && !finixTransferIds.Contains(refund.FinixTransferId))
                {
                    unmatchedEshop.Add(new ReconciliationMismatch(
                        "REFUND",
                        refund.FinixTransferId,
                        payment.OrderId,
                        refund.Id,
                        refund.AmountCents,
                        refund.FinixState,
                        "eShop has a refund transfer that Finix did not return for this range."));
                }
            }
        }

        return new ReconciliationReport(
            from,
            to,
            authorizations.Count + transfers.Count,
            payments.Count(payment => payment.CreatedAt >= from && payment.CreatedAt <= to),
            finixTransactions,
            unmatchedFinix,
            unmatchedEshop);
    }

    public async Task<SavedCardView> SaveCardAsync(string ownerId, SaveCardInput input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new PaymentOperationException("missing_buyer", "The caller identity is missing.", 401);
        }

        var identityId = await GetOrCreateFinixIdentityAsync(ownerId, cancellationToken);
        var finixCard = await _gateway.CreateCardAsync(identityId, input.Card, cancellationToken);

        var savedCard = new SavedCard(
            ownerId,
            finixCard.Id,
            finixCard.Brand,
            finixCard.LastFour,
            finixCard.ExpirationMonth,
            finixCard.ExpirationYear);

        _db.SavedCards.Add(savedCard);
        await _db.SaveChangesAsync(cancellationToken);

        return ToSavedCardView(savedCard);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var cards = await _db.SavedCards
            .Where(card => card.OwnerId == ownerId && card.IsActive)
            .OrderByDescending(card => card.CreatedAt)
            .ToListAsync(cancellationToken);

        return cards.Select(ToSavedCardView).ToList();
    }

    public async Task<SavedCardView> DeleteCardAsync(string ownerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var card = await _db.SavedCards.FirstOrDefaultAsync(
            item => item.Id == paymentMethodId && item.OwnerId == ownerId,
            cancellationToken);

        if (card is null)
        {
            throw new PaymentOperationException("payment_method_not_found", "Saved payment method not found.", 404);
        }

        if (!card.IsActive)
        {
            return ToSavedCardView(card);
        }

        card.Disable();
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _gateway.DisableCardAsync(card.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (PaymentOperationException exception)
        {
            _logger.LogWarning(exception, "The card was removed locally but Finix did not disable payment instrument {PaymentInstrumentId}.", card.FinixPaymentInstrumentId);
        }

        return ToSavedCardView(card);
    }

    private async Task<string> GetOrCreateFinixIdentityAsync(string ownerId, CancellationToken cancellationToken)
    {
        using var ownerLock = await AcquireOwnerLockAsync(ownerId, cancellationToken);

        var existing = await _db.FinixCustomers.FirstOrDefaultAsync(
            customer => customer.OwnerId == ownerId,
            cancellationToken);

        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        var identityId = await _gateway.CreateBuyerIdentityAsync(ownerId, cancellationToken);
        var customer = new FinixCustomer(ownerId, identityId);
        _db.FinixCustomers.Add(customer);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(customer).State = EntityState.Detached;
            var reloaded = await _db.FinixCustomers.FirstOrDefaultAsync(
                item => item.OwnerId == ownerId,
                cancellationToken);
            if (reloaded is not null)
            {
                return reloaded.FinixIdentityId;
            }

            throw;
        }

        return identityId;
    }

    private async Task WaitForSettledTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (true)
        {
            var transfer = await _gateway.GetTransferAsync(transferId, cancellationToken);
            if (string.Equals(transfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(transfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(transfer.State, "RETURNED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(
                    "capture_not_refundable",
                    $"Finix reports the capture transfer is {transfer.State} and cannot be partially refunded.",
                    409,
                    new Dictionary<string, object?>
                    {
                        ["transferId"] = transfer.Id,
                        ["transferState"] = transfer.State
                    });
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new PaymentOperationException(
                    "capture_still_pending",
                    "Finix is still settling the capture. Retry the partial refund in a few moments.",
                    409,
                    new Dictionary<string, object?>
                    {
                        ["transferId"] = transfer.Id,
                        ["transferState"] = transfer.State
                    });
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task<Order?> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _db.Orders
            .Include(order => order.OrderItems)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);
    }

    private async Task<Payment?> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _db.Payments
            .Include(payment => payment.Refunds)
            .FirstOrDefaultAsync(payment => payment.OrderId == orderId, cancellationToken);
    }

    private static Address ToOrderAddress(ShippingAddressInput? input)
    {
        if (input is null)
        {
            return new Address("1 Main St", "Springfield", "IL", "USA", "62701");
        }

        return new Address(
            string.IsNullOrWhiteSpace(input.Street) ? "1 Main St" : input.Street,
            string.IsNullOrWhiteSpace(input.City) ? "Springfield" : input.City,
            string.IsNullOrWhiteSpace(input.State) ? "IL" : input.State,
            string.IsNullOrWhiteSpace(input.Country) ? "USA" : input.Country,
            string.IsNullOrWhiteSpace(input.ZipCode) ? "62701" : input.ZipCode);
    }

    private static long ResolveRequestedAmount(RefundOrderInput input, long fallbackAmountCents)
    {
        if (input.AmountCents is long amountCents)
        {
            return amountCents;
        }

        if (input.Amount is decimal amount)
        {
            return ToCents(amount);
        }

        return fallbackAmountCents;
    }

    private static long ToCents(decimal amount) =>
        (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private OrderView ToOrderView(Order order, Payment? payment)
    {
        var items = order.OrderItems
            .Select(item => new OrderItemView(
                item.ItemOrdered.CatalogItemId,
                item.ItemOrdered.ProductName,
                item.UnitPrice,
                item.Units,
                item.UnitPrice * item.Units))
            .ToList();

        return new OrderView(
            order.Id,
            order.BuyerId,
            order.OrderDate,
            order.Total(),
            payment?.Currency ?? _options.Currency,
            items,
            payment is null ? null : ToPaymentView(payment));
    }

    private static string ToStatusText(PaymentStatus status) =>
        status == PaymentStatus.PendingPayment ? "AwaitingPayment" : status.ToString();

    private static PaymentView ToPaymentView(Payment payment)
    {
        return new PaymentView(
            payment.Id,
            ToStatusText(payment.Status),
            payment.AmountCents,
            payment.Currency,
            payment.FinixAuthorizationId,
            payment.FinixAuthorizationState,
            payment.FinixCaptureTransferId,
            payment.FinixCaptureState,
            payment.FinixPaymentInstrumentId,
            payment.UsedSavedCard,
            payment.CapturedAmountCents,
            payment.FinixFeeCents,
            payment.NetAmountCents,
            payment.RefundableAmountCents(),
            payment.FailureCode,
            payment.FailureMessage,
            payment.AuthorizedAt,
            payment.CapturedAt,
            payment.FulfilledAt,
            payment.CanceledAt,
            payment.Refunds
                .OrderBy(refund => refund.CreatedAt)
                .Select(ToRefundView)
                .ToList());
    }

    private static RefundView ToRefundView(PaymentRefund refund)
    {
        return new RefundView(
            refund.Id,
            refund.AmountCents,
            refund.IdempotencyKey,
            refund.FinixTransferId,
            refund.FinixState,
            refund.CreatedAt);
    }

    private static SavedCardView ToSavedCardView(SavedCard card)
    {
        return new SavedCardView(
            card.Id,
            card.Brand,
            card.LastFour,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.CreatedAt);
    }

    private static FinixTransactionView ToFinixTransactionView(
        FinixTransactionRecord record,
        int? matchedOrderId,
        int? matchedRefundId)
    {
        return new FinixTransactionView(
            record.RecordType,
            record.Id,
            record.AmountCents,
            record.Currency,
            record.State,
            record.Type,
            record.ParentTransferId,
            record.CreatedAt,
            matchedOrderId is not null || matchedRefundId is not null,
            matchedOrderId,
            matchedRefundId);
    }

    private static async Task<IDisposable> AcquireOrderLockAsync(int orderId, CancellationToken cancellationToken)
    {
        var semaphore = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new LockReleaser(semaphore);
    }

    private static async Task<IDisposable> AcquireOwnerLockAsync(string ownerId, CancellationToken cancellationToken)
    {
        var semaphore = OwnerLocks.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new LockReleaser(semaphore);
    }

    private sealed class LockReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public LockReleaser(SemaphoreSlim semaphore)
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
