using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Finix;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly CatalogContext _context;
    private readonly IFinixClient _finix;
    private readonly FinixOptions _options;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        CatalogContext context,
        IFinixClient finix,
        IOptions<FinixOptions> options,
        ILogger<PaymentService> logger)
    {
        _context = context;
        _finix = finix;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CreatedOrderResult> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineInput> lines,
        AddressInput? shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentForbiddenException("A signed-in shopper is required to place an order.");
        }

        if (lines is null || lines.Count == 0)
        {
            throw new PaymentValidationException("At least one catalog item is required.");
        }

        var requested = lines
            .GroupBy(l => l.CatalogItemId)
            .Select(g => new OrderLineInput { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (requested.Any(l => l.CatalogItemId <= 0))
        {
            throw new PaymentValidationException("Every order line must reference a valid catalog item id.");
        }

        if (requested.Any(l => l.Quantity <= 0))
        {
            throw new PaymentValidationException("Every order line must have a quantity greater than zero.");
        }

        var catalogIds = requested.Select(l => l.CatalogItemId).ToArray();
        var catalogItems = await _context.CatalogItems
            .Where(i => catalogIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        var missing = catalogIds.Where(id => catalogItems.All(i => i.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentValidationException(
                $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = requested.Select(line =>
        {
            var catalogItem = catalogItems.First(i => i.Id == line.CatalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = BuildAddress(shippingAddress);
        var order = new Order(buyerId, address, orderItems);
        order.SetCurrency(_options.RequireCurrency());

        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created order {OrderId} for shopper {BuyerId}.", order.Id, buyerId);

        return new CreatedOrderResult
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus,
            Total = order.Total(),
            TotalCents = OrderTotalCents(order),
            Currency = order.Currency,
            OrderDate = order.OrderDate,
            Items = MapOrderLines(order)
        };
    }

    public async Task<PaymentActionResult> PayOrderAsync(
        string buyerId,
        int orderId,
        CardDetailsInput? card,
        string? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var gate = OrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order is null)
            {
                throw new PaymentNotFoundException($"Order {orderId} was not found.");
            }

            if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new PaymentForbiddenException("This order belongs to another shopper.");
            }

            if (order.PaymentStatus is OrderPaymentStatuses.Authorized
                or OrderPaymentStatuses.Fulfilled
                or OrderPaymentStatuses.PartiallyRefunded
                or OrderPaymentStatuses.Refunded)
            {
                return ToPaymentActionResult(order, "Order was already authorized; no additional hold was placed.");
            }

            if (order.PaymentStatus == OrderPaymentStatuses.Canceled)
            {
                throw new PaymentConflictException(
                    $"Order {orderId} was canceled and can no longer be paid. Place a new order instead.");
            }

            var amountCents = OrderTotalCents(order);
            if (amountCents <= 0)
            {
                throw new PaymentValidationException("The order total must be greater than zero.");
            }

            string source;
            int? savedPaymentMethodId = null;

            if (!string.IsNullOrWhiteSpace(paymentMethodId))
            {
                var saved = await ResolveSavedPaymentMethodAsync(buyerId, paymentMethodId!, cancellationToken);
                source = saved.FinixPaymentInstrumentId;
                savedPaymentMethodId = saved.Id;
            }
            else
            {
                if (card is null)
                {
                    throw new PaymentValidationException(
                        "Provide card details or the id of one of the shopper's saved payment methods.");
                }

                var identity = await _finix.GetMerchantIdentityAsync(cancellationToken);
                var instrument = await _finix.CreatePaymentInstrumentAsync(identity, card, cancellationToken);
                source = instrument.Id;
            }

            var idempotencyId = $"eshop-{order.PaymentReference}-authorize";
            FinixAuthorization authorization;
            try
            {
                authorization = await _finix.CreateAuthorizationAsync(
                    amountCents,
                    order.Currency,
                    source,
                    idempotencyId,
                    new Dictionary<string, string>
                    {
                        ["order_id"] = order.Id.ToString(),
                        ["order_reference"] = order.PaymentReference,
                        ["source"] = "eshoponweb"
                    },
                    cancellationToken);
            }
            catch (PaymentGatewayException ex) when (!string.IsNullOrWhiteSpace(ex.RelatedResourceId)
                && (ex.Message.Contains("idempotency", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)))
            {
                // Finix reports the existing authorization when the idempotency
                // key has already been used. Reuse it instead of holding twice.
                authorization = await _finix.GetAuthorizationAsync(ex.RelatedResourceId!, cancellationToken);
            }

            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                var message = BuildAuthorizationFailureMessage(authorization);
                order.RecordPaymentFailure(authorization.Id, authorization.State, message);
                await _context.SaveChangesAsync(cancellationToken);
                throw new PaymentDeclinedException(message);
            }

            order.MarkAuthorized(
                authorization.Id,
                authorization.State ?? "SUCCEEDED",
                amountCents,
                source,
                savedPaymentMethodId);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Authorized order {OrderId} with Finix authorization {AuthorizationId} for {AmountCents} {Currency}.",
                order.Id,
                authorization.Id,
                amountCents,
                order.Currency);

            return ToPaymentActionResult(order, null);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<FulfilmentResult> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = OrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order is null)
            {
                throw new PaymentNotFoundException($"Order {orderId} was not found.");
            }

            if (order.PaymentStatus is OrderPaymentStatuses.Fulfilled
                or OrderPaymentStatuses.PartiallyRefunded
                or OrderPaymentStatuses.Refunded)
            {
                return ToFulfilmentResult(order, "Order was already fulfilled; the payment was not captured again.");
            }

            if (order.PaymentStatus != OrderPaymentStatuses.Authorized)
            {
                throw new PaymentConflictException(
                    $"Order {orderId} is in state '{order.PaymentStatus}' and cannot be fulfilled. " +
                    "Only an authorized order can be fulfilled.");
            }

            if (string.IsNullOrWhiteSpace(order.PaymentAuthorizationId))
            {
                throw new PaymentConflictException(
                    $"Order {orderId} has no Finix authorization to capture. Authorize the order first.");
            }

            var authorizationId = order.PaymentAuthorizationId!;
            var authorization = await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);

            if (authorization.IsVoid
                || !string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentConflictException(
                    $"Authorization {authorizationId} can no longer be captured " +
                    $"(state '{authorization.State}', void state '{authorization.VoidState}'). " +
                    "The hold may have expired or been released; cancel the order or collect a new payment.");
            }

            if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
            {
                throw new PaymentConflictException(
                    $"Authorization {authorizationId} expired at {authorization.ExpiresAt:u} and can no longer be captured. " +
                    "Cancel the order or collect a new payment.");
            }

            string captureTransferId;
            FinixTransfer captureTransfer;

            if (!string.IsNullOrWhiteSpace(authorization.Transfer))
            {
                captureTransferId = authorization.Transfer!;
                captureTransfer = await WaitForSettledTransferAsync(captureTransferId, cancellationToken);
            }
            else
            {
                var authorizedAmount = order.AuthorizedAmountCents > 0
                    ? order.AuthorizedAmountCents
                    : authorization.Amount;
                var captureAmount = Math.Min(authorizedAmount, authorization.Amount);
                if (captureAmount <= 0)
                {
                    throw new PaymentConflictException(
                        $"Authorization {authorizationId} has no capturable amount.");
                }

                FinixAuthorization capture;
                try
                {
                    capture = await _finix.CaptureAuthorizationAsync(
                        authorizationId,
                        captureAmount,
                        cancellationToken);
                }
                catch (PaymentGatewayException ex)
                {
                    throw new PaymentConflictException(
                        $"Finix could not capture authorization {authorizationId}: {ex.Message} " +
                        "The hold may have expired or already been captured; cancel the order or collect a new payment.");
                }

                if (string.IsNullOrWhiteSpace(capture.Transfer))
                {
                    throw new PaymentConflictException(
                        $"Finix accepted the capture for authorization {authorizationId} but returned no transfer id. " +
                        "Retry the fulfilment request or check the authorization in Finix.");
                }

                captureTransferId = capture.Transfer!;
                captureTransfer = await WaitForSettledTransferAsync(captureTransferId, cancellationToken);
            }

            if (string.Equals(captureTransfer.State, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentConflictException(
                    $"Finix capture transfer {captureTransferId} failed: {captureTransfer.FailureMessage}. " +
                    "The order was not fulfilled; cancel it or collect a new payment.");
            }

            var feeCents = captureTransfer.Fee.HasValue
                ? (long)Math.Round(captureTransfer.Fee.Value, MidpointRounding.AwayFromZero)
                : 0L;
            var netCents = captureTransfer.Amount - feeCents;

            order.MarkFulfilled(
                captureTransferId,
                captureTransfer.State ?? "UNKNOWN",
                captureTransfer.Amount,
                feeCents,
                netCents);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Captured order {OrderId} via Finix transfer {TransferId}: amount {Amount}, fee {Fee}, net {Net}.",
                order.Id,
                captureTransferId,
                captureTransfer.Amount,
                feeCents,
                netCents);

            return ToFulfilmentResult(order, null);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<CancelResult> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = OrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order is null)
            {
                throw new PaymentNotFoundException($"Order {orderId} was not found.");
            }

            if (order.PaymentStatus == OrderPaymentStatuses.Canceled)
            {
                return ToCancelResult(order, "Order was already canceled.");
            }

            if (order.PaymentStatus is OrderPaymentStatuses.Fulfilled
                or OrderPaymentStatuses.PartiallyRefunded
                or OrderPaymentStatuses.Refunded)
            {
                throw new PaymentConflictException(
                    $"Order {orderId} was already fulfilled and cannot be canceled. Use the refunds endpoint instead.");
            }

            if (order.PaymentStatus != OrderPaymentStatuses.Authorized
                || string.IsNullOrWhiteSpace(order.PaymentAuthorizationId))
            {
                order.MarkCanceled(null);
                await _context.SaveChangesAsync(cancellationToken);
                return ToCancelResult(order, "Order canceled before any funds were authorized.");
            }

            var authorizationId = order.PaymentAuthorizationId!;
            try
            {
                var authorization = await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);
                if (authorization.IsVoid
                    || string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    order.MarkCanceled(authorization.VoidState ?? "SUCCEEDED");
                    await _context.SaveChangesAsync(cancellationToken);
                    return ToCancelResult(order, "The authorization was already voided; the hold has been released.");
                }

                var voided = await _finix.VoidAuthorizationAsync(authorizationId, cancellationToken);
                order.MarkCanceled(voided.VoidState ?? voided.State);
                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Canceled order {OrderId} and voided Finix authorization {AuthorizationId}.",
                    order.Id,
                    authorizationId);

                return ToCancelResult(order, "The authorization was voided; the shopper's held funds were released.");
            }
            catch (PaymentGatewayException ex)
            {
                throw new PaymentConflictException(
                    $"Finix could not void authorization {authorizationId}: {ex.Message} " +
                    "The order was not canceled; retry or resolve the authorization in Finix.");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundResult> RefundOrderAsync(
        string callerId,
        bool isAdministrator,
        int orderId,
        decimal? amount,
        long? amountInCents,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var gate = OrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _context.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order is null)
            {
                throw new PaymentNotFoundException($"Order {orderId} was not found.");
            }

            if (!isAdministrator && !string.Equals(order.BuyerId, callerId, StringComparison.Ordinal))
            {
                throw new PaymentForbiddenException("This order belongs to another shopper.");
            }

            if (order.PaymentStatus is not (OrderPaymentStatuses.Fulfilled
                or OrderPaymentStatuses.PartiallyRefunded
                or OrderPaymentStatuses.Refunded))
            {
                throw new PaymentConflictException(
                    $"Order {orderId} is in state '{order.PaymentStatus}'. Refunds are only available after fulfilment.");
            }

            if (string.IsNullOrWhiteSpace(order.PaymentCaptureTransferId))
            {
                throw new PaymentConflictException(
                    $"Order {orderId} has no captured payment to refund.");
            }

            var key = string.IsNullOrWhiteSpace(idempotencyKey)
                ? Guid.NewGuid().ToString("N")
                : idempotencyKey!.Trim();

            if (key.Length > 255)
            {
                throw new PaymentValidationException("The refund idempotency key must be 255 characters or fewer.");
            }

            var existing = await _context.OrderRefunds
                .FirstOrDefaultAsync(r => r.OrderId == order.Id && r.IdempotencyKey == key, cancellationToken);
            if (existing is not null)
            {
                return ToRefundResult(order, existing, "This idempotency key was already used; the original refund is returned.");
            }

            var remaining = order.RemainingRefundableCents;
            if (remaining <= 0)
            {
                throw new PaymentConflictException(
                    $"Order {orderId} has already been refunded in full ({order.CapturedAmountCents} cents captured).");
            }

            long refundCents;
            if (amountInCents.HasValue)
            {
                refundCents = amountInCents.Value;
            }
            else if (amount.HasValue)
            {
                refundCents = ToCents(amount.Value);
            }
            else
            {
                refundCents = remaining;
            }

            if (refundCents <= 0)
            {
                throw new PaymentValidationException("The refund amount must be greater than zero.");
            }

            if (refundCents > remaining)
            {
                throw new PaymentValidationException(
                    $"The refund amount ({FromCents(refundCents):0.00}) exceeds the remaining refundable amount " +
                    $"({FromCents(remaining):0.00}) for order {orderId}.");
            }

            var captureTransferId = order.PaymentCaptureTransferId!;
            var captureTransfer = await WaitForSettledTransferAsync(captureTransferId, cancellationToken);

            if (string.Equals(captureTransfer.State, "PENDING", StringComparison.OrdinalIgnoreCase)
                && refundCents < remaining)
            {
                throw new PaymentConflictException(
                    $"Finix capture transfer {captureTransferId} is still pending settlement. " +
                    "A partial refund can be retried once the capture has succeeded; a full refund may be possible now.");
            }

            if (captureTransfer.State is "FAILED" or "CANCELED" or "RETURNED")
            {
                throw new PaymentConflictException(
                    $"Finix capture transfer {captureTransferId} is in state '{captureTransfer.State}' and cannot be refunded.");
            }

            FinixTransfer refund;
            try
            {
                refund = await _finix.CreateRefundAsync(captureTransferId, refundCents, cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                throw new PaymentConflictException(
                    $"Finix could not refund order {orderId} against transfer {captureTransferId}: {ex.Message}");
            }

            if (string.Equals(refund.State, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentConflictException(
                    $"Finix refund {refund.Id} failed: {refund.FailureMessage}. No refund was recorded.");
            }

            var record = new OrderRefund(
                order.Id,
                refund.Id,
                refundCents,
                key,
                refund.State ?? "PENDING",
                order.Currency);

            _context.OrderRefunds.Add(record);
            order.ApplyRefund(refundCents);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Refunded order {OrderId} by {RefundCents} {Currency} via Finix transfer {RefundTransferId}.",
                order.Id,
                refundCents,
                order.Currency,
                refund.Id);

            return ToRefundResult(order, record, null);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderSummaryResult>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentForbiddenException("A signed-in shopper is required.");
        }

        var orders = await _context.Orders
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var refunds = await _context.OrderRefunds
            .Where(r => orderIds.Contains(r.OrderId))
            .ToListAsync(cancellationToken);

        return orders
            .Select(o => ToOrderSummary(o, refunds.Where(r => r.OrderId == o.Id)))
            .ToList();
    }

    public async Task<SavedPaymentMethodResult> SavePaymentMethodAsync(
        string buyerId,
        CardDetailsInput card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentForbiddenException("A signed-in shopper is required.");
        }

        var identity = await _finix.GetMerchantIdentityAsync(cancellationToken);
        var instrument = await _finix.CreatePaymentInstrumentAsync(identity, card, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            instrument.Id,
            instrument.Brand,
            instrument.LastFour,
            instrument.ExpirationMonth,
            instrument.ExpirationYear,
            instrument.Name,
            instrument.Fingerprint);

        _context.SavedPaymentMethods.Add(saved);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Shopper {BuyerId} saved Finix payment instrument {PaymentInstrumentId}.",
            buyerId,
            instrument.Id);

        return ToSavedPaymentMethodResult(saved);
    }

    public async Task<IReadOnlyList<SavedPaymentMethodResult>> ListPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentForbiddenException("A signed-in shopper is required.");
        }

        var methods = await _context.SavedPaymentMethods
            .Where(m => m.OwnerId == buyerId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        return methods.Select(ToSavedPaymentMethodResult).ToList();
    }

    public async Task<bool> DeletePaymentMethodAsync(
        string buyerId,
        string paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentForbiddenException("A signed-in shopper is required.");
        }

        var saved = await ResolveSavedPaymentMethodAsync(buyerId, paymentMethodId, cancellationToken);

        try
        {
            await _finix.DisablePaymentInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning(
                ex,
                "Could not disable Finix payment instrument {PaymentInstrumentId}; removing the local saved-card reference anyway.",
                saved.FinixPaymentInstrumentId);
        }

        _context.SavedPaymentMethods.Remove(saved);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Shopper {BuyerId} deleted saved payment method {PaymentMethodId}.",
            buyerId,
            saved.Id);

        return true;
    }

    public async Task<ReconciliationResult> GetReconciliationAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentValidationException("The 'from' date-time must be before or equal to 'to'.");
        }

        var transfers = await _finix.ListTransfersAsync(from, to, cancellationToken);
        var authorizations = await _finix.ListAuthorizationsAsync(from, to, cancellationToken);

        var orders = await _context.Orders
            .Include(o => o.OrderItems)
                .ThenInclude(i => i.ItemOrdered)
            .ToListAsync(cancellationToken);

        var refunds = await _context.OrderRefunds.ToListAsync(cancellationToken);

        var transferIds = transfers.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var authorizationIds = authorizations.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        var transferRecords = new List<FinixTransactionRecord>(transfers.Count);
        foreach (var transfer in transfers)
        {
            var captureOrder = orders.FirstOrDefault(o => o.PaymentCaptureTransferId == transfer.Id);
            var refund = refunds.FirstOrDefault(r => r.FinixTransferId == transfer.Id);
            var order = captureOrder ?? (refund is null
                ? null
                : orders.FirstOrDefault(o => o.Id == refund.OrderId));

            transferRecords.Add(new FinixTransactionRecord
            {
                Id = transfer.Id,
                Type = transfer.Type,
                State = transfer.State,
                AmountCents = transfer.Amount,
                Amount = FromCents(transfer.Amount),
                Currency = transfer.Currency,
                CreatedAt = transfer.CreatedAt,
                Merchant = transfer.Merchant,
                OperationKey = transfer.OperationKey,
                ParentTransfer = transfer.ParentTransfer,
                OrderId = order?.Id,
                MatchType = captureOrder is not null ? "Capture" : refund is not null ? "Refund" : null,
                Matched = order is not null
            });
        }

        var authorizationRecords = new List<FinixAuthorizationRecord>(authorizations.Count);
        foreach (var authorization in authorizations)
        {
            var order = orders.FirstOrDefault(o => o.PaymentAuthorizationId == authorization.Id);
            authorizationRecords.Add(new FinixAuthorizationRecord
            {
                Id = authorization.Id,
                State = authorization.State,
                AmountCents = authorization.Amount,
                Amount = FromCents(authorization.Amount),
                Currency = authorization.Currency,
                CreatedAt = authorization.CreatedAt,
                IsVoid = authorization.IsVoid,
                VoidState = authorization.VoidState,
                TransferId = authorization.Transfer,
                OrderId = order?.Id,
                Matched = order is not null
            });
        }

        var orderRecords = new List<ReconciliationOrderRecord>();
        foreach (var order in orders)
        {
            var orderTransfers = transfers
                .Where(t => t.Id == order.PaymentCaptureTransferId
                    || refunds.Any(r => r.OrderId == order.Id && r.FinixTransferId == t.Id))
                .Select(t => t.Id)
                .ToList();

            var inRange = order.OrderDate >= from && order.OrderDate <= to;
            var hasAuthorization = order.PaymentAuthorizationId is not null
                && authorizationIds.Contains(order.PaymentAuthorizationId);
            var hasTransfer = orderTransfers.Any(transferIds.Contains);

            if (!inRange && !hasAuthorization && !hasTransfer)
            {
                continue;
            }

            orderRecords.Add(new ReconciliationOrderRecord
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Currency = order.Currency,
                PaymentStatus = order.PaymentStatus,
                AuthorizationId = order.PaymentAuthorizationId,
                CaptureTransferId = order.PaymentCaptureTransferId,
                FinixTransferIds = orderTransfers,
                Matched = hasAuthorization || hasTransfer
            });
        }

        var unmatchedTransfers = transferRecords.Where(t => !t.Matched).ToList();
        var unmatchedOrders = orderRecords
            .Where(o => !o.Matched
                && o.PaymentStatus is OrderPaymentStatuses.Authorized
                    or OrderPaymentStatuses.Fulfilled
                    or OrderPaymentStatuses.PartiallyRefunded
                    or OrderPaymentStatuses.Refunded)
            .ToList();

        return new ReconciliationResult
        {
            From = from,
            To = to,
            FinixTransactionCount = transferRecords.Count,
            FinixAuthorizationCount = authorizationRecords.Count,
            OrderCount = orderRecords.Count,
            UnmatchedFinixTransactionCount = unmatchedTransfers.Count,
            UnmatchedOrderCount = unmatchedOrders.Count,
            FinixTransactions = transferRecords,
            FinixAuthorizations = authorizationRecords,
            Orders = orderRecords,
            UnmatchedFinixTransactions = unmatchedTransfers,
            UnmatchedOrders = unmatchedOrders
        };
    }

    private async Task<SavedPaymentMethod> ResolveSavedPaymentMethodAsync(
        string buyerId,
        string paymentMethodId,
        CancellationToken cancellationToken)
    {
        var query = _context.SavedPaymentMethods.Where(m => m.OwnerId == buyerId);

        SavedPaymentMethod? saved;
        if (int.TryParse(paymentMethodId, out var localId))
        {
            saved = await query.FirstOrDefaultAsync(m => m.Id == localId, cancellationToken);
        }
        else
        {
            saved = await query.FirstOrDefaultAsync(
                m => m.FinixPaymentInstrumentId == paymentMethodId,
                cancellationToken);
        }

        if (saved is null)
        {
            throw new PaymentNotFoundException(
                $"Saved payment method '{paymentMethodId}' was not found for this shopper.");
        }

        return saved;
    }

    private async Task<FinixTransfer> WaitForSettledTransferAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);

        while (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase)
            && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private static SemaphoreSlim OrderLock(int orderId) =>
        OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));

    private static Address BuildAddress(AddressInput? input)
    {
        input ??= new AddressInput();
        return new Address(
            string.IsNullOrWhiteSpace(input.Line1) ? "123 Main St." : input.Line1!,
            string.IsNullOrWhiteSpace(input.City) ? "Kent" : input.City!,
            string.IsNullOrWhiteSpace(input.Region) ? "OH" : input.Region!,
            string.IsNullOrWhiteSpace(input.Country) ? "United States" : input.Country!,
            string.IsNullOrWhiteSpace(input.PostalCode) ? "44240" : input.PostalCode!);
    }

    private static long OrderTotalCents(Order order) =>
        order.OrderItems.Sum(item => ToCents(item.UnitPrice * item.Units));

    private static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static decimal FromCents(long cents) => cents / 100m;

    private static string BuildAuthorizationFailureMessage(FinixAuthorization authorization)
    {
        var details = string.IsNullOrWhiteSpace(authorization.FailureMessage)
            ? $"state '{authorization.State}'"
            : authorization.FailureMessage;
        return $"Finix declined authorization {authorization.Id}: {details}";
    }

    private static IReadOnlyList<OrderLineResult> MapOrderLines(Order order) =>
        order.OrderItems.Select(item => new OrderLineResult
        {
            CatalogItemId = item.ItemOrdered.CatalogItemId,
            ProductName = item.ItemOrdered.ProductName,
            UnitPrice = item.UnitPrice,
            Quantity = item.Units,
            LineTotal = item.UnitPrice * item.Units
        }).ToList();

    private static PaymentActionResult ToPaymentActionResult(Order order, string? message) => new()
    {
        OrderId = order.Id,
        PaymentStatus = order.PaymentStatus,
        AuthorizationId = order.PaymentAuthorizationId,
        AuthorizationState = order.PaymentAuthorizationState,
        PaymentInstrumentId = order.PaymentInstrumentId,
        AmountCents = order.AuthorizedAmountCents,
        Amount = FromCents(order.AuthorizedAmountCents),
        Currency = order.Currency,
        Message = message
    };

    private static FulfilmentResult ToFulfilmentResult(Order order, string? message) => new()
    {
        OrderId = order.Id,
        PaymentStatus = order.PaymentStatus,
        AuthorizationId = order.PaymentAuthorizationId,
        CaptureTransferId = order.PaymentCaptureTransferId,
        CaptureState = order.PaymentCaptureState,
        CapturedAmountCents = order.CapturedAmountCents,
        CapturedAmount = FromCents(order.CapturedAmountCents),
        FinixFeeCents = order.FinixFeeCents,
        FinixFee = FromCents(order.FinixFeeCents),
        NetProceedsCents = order.NetProceedsCents,
        NetProceeds = FromCents(order.NetProceedsCents),
        Currency = order.Currency,
        Message = message
    };

    private static CancelResult ToCancelResult(Order order, string? message) => new()
    {
        OrderId = order.Id,
        PaymentStatus = order.PaymentStatus,
        AuthorizationId = order.PaymentAuthorizationId,
        VoidState = order.PaymentAuthorizationState,
        Message = message
    };

    private static RefundResult ToRefundResult(Order order, OrderRefund refund, string? message) => new()
    {
        RefundId = refund.Id,
        OrderId = order.Id,
        FinixRefundTransferId = refund.FinixTransferId,
        RefundedAmountCents = refund.AmountCents,
        RefundedAmount = FromCents(refund.AmountCents),
        TotalRefundedCents = order.RefundedAmountCents,
        TotalRefunded = FromCents(order.RefundedAmountCents),
        RemainingRefundableCents = order.RemainingRefundableCents,
        RemainingRefundable = FromCents(order.RemainingRefundableCents),
        Currency = order.Currency,
        State = refund.State,
        IdempotencyKey = refund.IdempotencyKey,
        PaymentStatus = order.PaymentStatus,
        Message = message
    };

    private static OrderSummaryResult ToOrderSummary(Order order, IEnumerable<OrderRefund> refunds) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        TotalCents = OrderTotalCents(order),
        Currency = order.Currency,
        PaymentStatus = order.PaymentStatus,
        AuthorizationId = order.PaymentAuthorizationId,
        AuthorizationState = order.PaymentAuthorizationState,
        CaptureTransferId = order.PaymentCaptureTransferId,
        CaptureState = order.PaymentCaptureState,
        CapturedAmountCents = order.CapturedAmountCents,
        CapturedAmount = FromCents(order.CapturedAmountCents),
        FinixFeeCents = order.FinixFeeCents,
        FinixFee = FromCents(order.FinixFeeCents),
        NetProceedsCents = order.NetProceedsCents,
        NetProceeds = FromCents(order.NetProceedsCents),
        RefundedAmountCents = order.RefundedAmountCents,
        RefundedAmount = FromCents(order.RefundedAmountCents),
        RemainingRefundableCents = order.RemainingRefundableCents,
        RemainingRefundable = FromCents(order.RemainingRefundableCents),
        PaymentFailureMessage = order.PaymentFailureMessage,
        Items = MapOrderLines(order),
        Refunds = refunds
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new OrderRefundSummary
            {
                RefundId = r.Id,
                FinixRefundTransferId = r.FinixTransferId,
                Amount = FromCents(r.AmountCents),
                AmountCents = r.AmountCents,
                State = r.State,
                IdempotencyKey = r.IdempotencyKey,
                CreatedAt = r.CreatedAt
            })
            .ToList()
    };

    private static SavedPaymentMethodResult ToSavedPaymentMethodResult(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        PaymentInstrumentId = method.FinixPaymentInstrumentId,
        Brand = method.Brand,
        Last4 = method.LastFour,
        ExpirationMonth = method.ExpirationMonth,
        ExpirationYear = method.ExpirationYear,
        Name = method.CardholderName,
        CreatedAt = method.CreatedAt
    };
}


