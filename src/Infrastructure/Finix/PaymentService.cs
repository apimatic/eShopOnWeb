using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payments;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public sealed class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OperationLocks = new(StringComparer.Ordinal);

    private readonly CatalogContext _db;
    private readonly IFinixPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly FinixOptions _finixOptions;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        CatalogContext db,
        IFinixPaymentGateway gateway,
        IUriComposer uriComposer,
        IOptions<FinixOptions> finixOptions,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _finixOptions = finixOptions.Value;
        _logger = logger;
    }

    public async Task<OrderPlacementResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderItemRequest> items,
        ShippingAddressDetails? shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "The caller identity could not be determined.");
        }

        var requestedItems = (items ?? Array.Empty<OrderItemRequest>())
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new OrderItemRequest(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (requestedItems.Count == 0)
        {
            throw new PaymentException(400, "At least one catalog item with a positive quantity is required.");
        }

        var catalogItemIds = requestedItems.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(i => catalogItemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        if (catalogItems.Count != catalogItemIds.Length)
        {
            var found = catalogItems.Select(i => i.Id).ToHashSet();
            var missing = catalogItemIds.Where(id => !found.Contains(id));
            throw new PaymentException(400, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var requested in requestedItems)
        {
            var catalogItem = catalogItems.First(i => i.Id == requested.CatalogItemId);
            var orderedItem = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(orderedItem, catalogItem.Price, requested.Quantity));
        }

        var address = ToAddress(shippingAddress, buyerId);
        var order = new Order(buyerId, address, orderItems);

        var total = order.Total();
        if (total <= 0)
        {
            throw new PaymentException(400, "The order total must be greater than zero.");
        }

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var currency = ResolveCurrency();
        var payment = new OrderPayment(order.Id, ToCents(total), currency);
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created order {OrderId} for {BuyerId} with total {Total} {Currency}",
            order.Id,
            buyerId,
            total,
            currency);

        return new OrderPlacementResult(order.Id, total, currency, MapPayment(payment));
    }

    public async Task<PaymentOperationResult> PayAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        using var lease = await AcquireLockAsync($"order:{orderId}", cancellationToken);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment is null)
        {
            throw new PaymentException(409, $"Order {orderId} has no payment record.");
        }

        if (payment.Status is PaymentStatus.Authorized
            or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            return new PaymentOperationResult(orderId, MapPayment(payment));
        }

        if (payment.Status == PaymentStatus.Canceled)
        {
            throw new PaymentException(409, $"Order {orderId} was canceled and can no longer be paid.");
        }

        string sourcePaymentInstrumentId;
        string? finixIdentityId = null;

        if (savedPaymentMethodId.HasValue)
        {
            var savedCard = await _db.SavedPaymentMethods
                .FirstOrDefaultAsync(
                    p => p.Id == savedPaymentMethodId.Value && p.BuyerId == buyerId && p.IsEnabled,
                    cancellationToken);

            if (savedCard is null)
            {
                throw new PaymentException(404, "Saved payment method not found.");
            }

            sourcePaymentInstrumentId = savedCard.FinixPaymentInstrumentId;
            finixIdentityId = savedCard.FinixIdentityId;
        }
        else
        {
            if (card is null)
            {
                throw new PaymentException(400, "Provide card details or a saved paymentMethodId.");
            }

            ValidateCard(card);
            FinixPaymentInstrumentInfo instrument;
            try
            {
                instrument = await _gateway.CreateCardPaymentInstrumentAsync(
                    order.BuyerId,
                    card,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is ApiException or SdkException)
            {
                _logger.LogError(ex, "Finix rejected the card details for order {OrderId}", orderId);
                throw new PaymentException(402, $"Finix could not use the supplied card: {ex.Message}");
            }

            sourcePaymentInstrumentId = instrument.Id;
            finixIdentityId = instrument.IdentityId;
        }

        FinixAuthorizationInfo authorization;
        try
        {
            authorization = await _gateway.CreateAuthorizationAsync(
                payment.AmountAuthorizedCents,
                payment.Currency,
                sourcePaymentInstrumentId,
                $"eshop-{payment.FinixOperationId}-authorization",
                orderId.ToString(),
                payment.FinixOperationId,
                card?.SecurityCode,
                cancellationToken);
        }
        catch (Exception ex) when (ex is ApiException or SdkException)
        {
            _logger.LogError(ex, "Finix authorization failed for order {OrderId}", orderId);
            payment.MarkAuthorizationFailed(null, ex.Message, DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentException(402, $"Finix authorization failed: {ex.Message}");
        }

        var now = DateTimeOffset.UtcNow;
        if (string.Equals(authorization.State, "FAILED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            payment.MarkAuthorizationFailed(authorization.FailureCode, authorization.FailureMessage, now);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentException(
                402,
                $"Finix declined the authorization: {authorization.FailureMessage ?? authorization.FailureCode ?? authorization.State}.");
        }

        payment.MarkAuthorized(
            authorization.Id,
            authorization.State,
            authorization.Amount > 0 ? authorization.Amount : payment.AmountAuthorizedCents,
            authorization.ExpiresAt,
            sourcePaymentInstrumentId,
            finixIdentityId,
            now);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Authorized order {OrderId} with Finix authorization {AuthorizationId} in state {State}",
            orderId,
            authorization.Id,
            authorization.State);

        return new PaymentOperationResult(orderId, MapPayment(payment));
    }

    public async Task<PaymentOperationResult> FulfilAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        using var lease = await AcquireLockAsync($"order:{orderId}", cancellationToken);

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment is null)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return new PaymentOperationResult(orderId, MapPayment(payment));
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            throw new PaymentException(
                409,
                $"Order {orderId} is in payment state {payment.Status} and has no capturable authorization. " +
                "Pay for the order before fulfilling it.");
        }

        FinixAuthorizationInfo authorization;
        try
        {
            authorization = await _gateway.GetAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
        }
        catch (Exception ex) when (ex is ApiException or SdkException)
        {
            _logger.LogError(ex, "Could not read Finix authorization {AuthorizationId}", payment.FinixAuthorizationId);
            throw new PaymentException(502, $"Could not read the Finix authorization: {ex.Message}");
        }

        var now = DateTimeOffset.UtcNow;
        payment.UpdateAuthorizationState(authorization.State, authorization.IsVoid, authorization.ExpiresAt, now);
        await _db.SaveChangesAsync(cancellationToken);

        var notCapturableReason = GetNotCapturableReason(authorization, now);
        if (notCapturableReason is not null)
        {
            throw new PaymentException(
                409,
                $"Order {orderId} cannot be fulfilled: {notCapturableReason} " +
                "Cancel the order or collect a new payment before trying again.");
        }

        FinixAuthorizationInfo capture;
        try
        {
            capture = await _gateway.CaptureAuthorizationAsync(
                payment.FinixAuthorizationId!,
                payment.AmountAuthorizedCents,
                $"eshop-{payment.FinixOperationId}-capture",
                cancellationToken);
        }
        catch (Exception ex) when (ex is ApiException or SdkException)
        {
            _logger.LogError(ex, "Finix capture failed for order {OrderId}", orderId);
            throw new PaymentException(
                409,
                $"Finix could not capture order {orderId}: {ex.Message}. " +
                "The order is still unfulfilled; cancel it or collect a new payment before trying again.");
        }

        if (string.Equals(capture.State, "FAILED", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(capture.TransferId))
        {
            payment.UpdateAuthorizationState(capture.State, false, capture.ExpiresAt, now);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentException(
                409,
                $"Finix could not capture order {orderId}: " +
                $"{capture.FailureMessage ?? capture.FailureCode ?? capture.State ?? "no transfer was created"}. " +
                "The order is still unfulfilled; cancel it or collect a new payment before trying again.");
        }

        var transfer = await _gateway.GetTransferAsync(capture.TransferId!, cancellationToken);
        var feeCents = await _gateway.GetFeeCentsAsync(capture.TransferId!, cancellationToken);

        // Finix can create the capture transfer before it has finalized the fee entries.
        // Give it a bounded window to reach SUCCEEDED and report the fee so the operator sees the real net.
        var feeDeadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while ((feeCents <= 0 || !string.Equals(transfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
               && !string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase)
               && DateTimeOffset.UtcNow < feeDeadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            transfer = await _gateway.GetTransferAsync(capture.TransferId!, cancellationToken);
            feeCents = await _gateway.GetFeeCentsAsync(capture.TransferId!, cancellationToken);
        }

        var capturedCents = transfer.Amount ?? payment.AmountAuthorizedCents;

        payment.MarkCaptured(
            capture.TransferId!,
            transfer.State ?? capture.State,
            capturedCents,
            feeCents,
            DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Captured order {OrderId}: transfer {TransferId}, captured {Captured} cents, fee {Fee} cents",
            orderId,
            capture.TransferId,
            capturedCents,
            feeCents);

        return new PaymentOperationResult(orderId, MapPayment(payment));
    }

    public async Task<PaymentOperationResult> CancelAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        using var lease = await AcquireLockAsync($"order:{orderId}", cancellationToken);

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment is null)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        if (payment.Status == PaymentStatus.Canceled)
        {
            return new PaymentOperationResult(orderId, MapPayment(payment));
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentException(
                409,
                $"Order {orderId} has already been fulfilled. Use the refunds endpoint instead of canceling it.");
        }

        if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            try
            {
                var authorization = await _gateway.VoidAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
                payment.UpdateAuthorizationState(
                    authorization.State,
                    authorization.IsVoid || string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase),
                    authorization.ExpiresAt,
                    DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is ApiException or SdkException)
            {
                _logger.LogError(ex, "Finix void failed for order {OrderId}", orderId);

                try
                {
                    var current = await _gateway.GetAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
                    if (current.IsVoid || string.Equals(current.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
                    {
                        payment.UpdateAuthorizationState(current.State, true, current.ExpiresAt, DateTimeOffset.UtcNow);
                    }
                    else if (!string.IsNullOrWhiteSpace(current.TransferId))
                    {
                        throw new PaymentException(
                            409,
                            $"Order {orderId} has already been captured at Finix. Use the refunds endpoint instead of canceling it.");
                    }
                    else
                    {
                        throw new PaymentException(
                            409,
                            $"Finix could not release the hold for order {orderId}: {ex.Message}. " +
                            "The authorization is still in state " + (current.State ?? "unknown") + ".");
                    }
                }
                catch (PaymentException)
                {
                    throw;
                }
                catch (Exception readException)
                {
                    _logger.LogError(readException, "Could not read authorization after void failure for order {OrderId}", orderId);
                    throw new PaymentException(
                        409,
                        $"Finix could not release the hold for order {orderId}: {ex.Message}. " +
                        "If the payment was already captured, use the refunds endpoint instead.");
                }
            }
        }

        payment.MarkCanceled(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Canceled order {OrderId} and released any held funds", orderId);
        return new PaymentOperationResult(orderId, MapPayment(payment));
    }

    public async Task<RefundOperationResult> RefundAsync(
        int orderId,
        string? buyerId,
        bool isOperator,
        long? amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(400, "An idempotencyKey is required for refunds.");
        }

        if (idempotencyKey.Length > 200)
        {
            throw new PaymentException(400, "idempotencyKey must be 200 characters or fewer.");
        }

        using var lease = await AcquireLockAsync($"order:{orderId}", cancellationToken);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null || (!isOperator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal)))
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment is null)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        var existing = payment.Refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
        if (existing is not null)
        {
            return new RefundOperationResult(
                existing.Id,
                existing.FinixRefundId,
                orderId,
                existing.AmountCents,
                existing.State,
                MapPayment(payment));
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            || string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            throw new PaymentException(
                409,
                $"Order {orderId} has not been captured. Only a fulfilled order can be refunded.");
        }

        var refundable = payment.RefundableCents;
        if (refundable <= 0)
        {
            throw new PaymentException(409, $"Order {orderId} has already been fully refunded.");
        }

        var amount = amountCents ?? refundable;
        if (amount <= 0)
        {
            throw new PaymentException(400, "The refund amount must be greater than zero.");
        }

        if (amount > refundable)
        {
            throw new PaymentException(
                409,
                $"Refund amount {amount} cents exceeds the remaining captured amount of {refundable} cents for order {orderId}.");
        }

        var captureTransfer = await _gateway.GetTransferAsync(payment.CaptureTransferId!, cancellationToken);
        var captureDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (!string.Equals(captureTransfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(captureTransfer.State, "FAILED", StringComparison.OrdinalIgnoreCase)
               && DateTimeOffset.UtcNow < captureDeadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            captureTransfer = await _gateway.GetTransferAsync(payment.CaptureTransferId!, cancellationToken);
        }

        payment.UpdateCaptureState(captureTransfer.State, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        if (!string.Equals(captureTransfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                409,
                $"The Finix capture {payment.CaptureTransferId} for order {orderId} is still in state " +
                $"{captureTransfer.State ?? "unknown"}. Retry the refund once the capture has succeeded.");
        }

        FinixRefundInfo refund;
        try
        {
            refund = await _gateway.RefundTransferAsync(
                payment.CaptureTransferId!,
                amount,
                idempotencyKey,
                cancellationToken);
        }
        catch (Exception ex) when (ex is ApiException or SdkException)
        {
            _logger.LogError(ex, "Finix refund failed for order {OrderId}", orderId);
            throw new PaymentException(402, $"Finix could not refund order {orderId}: {ex.Message}");
        }

        if (string.Equals(refund.State, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                402,
                $"Finix rejected the refund for order {orderId}: " +
                $"{refund.FailureMessage ?? refund.FailureCode ?? refund.State}.");
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new PaymentRefund(
            refund.Id,
            amount,
            idempotencyKey,
            refund.State,
            refund.FailureCode,
            refund.FailureMessage,
            now);
        payment.AddRefund(entity, now);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Refunded {Amount} cents for order {OrderId} with Finix refund {RefundId}",
            amount,
            orderId,
            refund.Id);

        return new RefundOperationResult(
            entity.Id,
            refund.Id,
            orderId,
            amount,
            refund.State,
            MapPayment(payment));
    }

    public async Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = await _db.OrderPayments
            .Where(p => orderIds.Contains(p.OrderId))
            .Include(p => p.Refunds)
            .ToDictionaryAsync(p => p.OrderId, cancellationToken);

        var refreshed = false;
        foreach (var payment in payments.Values)
        {
            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded
                && !string.IsNullOrWhiteSpace(payment.CaptureTransferId)
                && (!string.Equals(payment.CaptureState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) || payment.FeeCents <= 0))
            {
                try
                {
                    var transfer = await _gateway.GetTransferAsync(payment.CaptureTransferId!, cancellationToken);
                    var feeCents = await _gateway.GetFeeCentsAsync(payment.CaptureTransferId!, cancellationToken);
                    payment.UpdateCaptureDetails(
                        transfer.Amount ?? payment.AmountCapturedCents,
                        feeCents,
                        transfer.State,
                        DateTimeOffset.UtcNow);
                    refreshed = true;
                }
                catch (Exception ex) when (ex is ApiException or SdkException)
                {
                    _logger.LogWarning(
                        ex,
                        "Could not refresh capture details for order payment {OrderPaymentId}",
                        payment.Id);
                }
            }
        }

        if (refreshed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return orders
            .Select(o => new OrderSummary(
                o.Id,
                o.OrderDate,
                o.Total(),
                payments.TryGetValue(o.Id, out var payment) ? payment.Currency : ResolveCurrency(),
                MapPayment(payment)))
            .ToList();
    }

    public async Task<ReconciliationResult> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentException(400, "The 'from' date must be before or equal to the 'to' date.");
        }

        var finixTransactions = await _gateway.ListTransactionsAsync(from, to, cancellationToken);

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .OrderBy(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = await _db.OrderPayments
            .AsNoTracking()
            .Where(p => orderIds.Contains(p.OrderId))
            .Include(p => p.Refunds)
            .ToDictionaryAsync(p => p.OrderId, cancellationToken);

        var matchedFinixIdsByOrder = orders.ToDictionary(o => o.Id, _ => new List<string>());
        var matchedTransactions = new List<ReconciliationFinixTransaction>();
        var discrepancies = new List<ReconciliationDiscrepancy>();

        foreach (var transaction in finixTransactions)
        {
            var match = MatchTransaction(transaction, payments);
            if (match.OrderId.HasValue)
            {
                matchedFinixIdsByOrder[match.OrderId.Value].Add(transaction.Id);
            }
            else
            {
                discrepancies.Add(new ReconciliationDiscrepancy(
                    "FinixOnly",
                    $"Finix {transaction.Kind.ToLowerInvariant()} {transaction.Id} " +
                    $"({transaction.Type ?? "unknown type"}, {transaction.Amount ?? 0} cents, state {transaction.State ?? "unknown"}) " +
                    "has no matching eShop order.",
                    null,
                    transaction.Id));
            }

            matchedTransactions.Add(new ReconciliationFinixTransaction(
                transaction.Id,
                transaction.Kind,
                transaction.State,
                transaction.Amount,
                transaction.Type,
                transaction.ParentId,
                transaction.CreatedAt,
                match.OrderId,
                match.Reason,
                transaction.FailureCode,
                transaction.FailureMessage));
        }

        var eshopOrders = new List<ReconciliationEshopOrder>();
        foreach (var order in orders)
        {
            payments.TryGetValue(order.Id, out var payment);
            var matchedIds = matchedFinixIdsByOrder[order.Id];
            eshopOrders.Add(new ReconciliationEshopOrder(
                order.Id,
                order.OrderDate,
                order.Total(),
                payment?.Currency ?? ResolveCurrency(),
                payment?.Status.ToString() ?? "NoPayment",
                payment?.FinixAuthorizationId,
                payment?.CaptureTransferId,
                matchedIds));

            if (payment is not null
                && (payment.FinixAuthorizationId is not null || payment.CaptureTransferId is not null)
                && matchedIds.Count == 0)
            {
                discrepancies.Add(new ReconciliationDiscrepancy(
                    "EshopOnly",
                    $"eShop order {order.Id} has payment state {payment.Status} " +
                    $"but Finix returned no matching transaction in the requested range.",
                    order.Id,
                    null));
            }
        }

        _logger.LogInformation(
            "Reconciled {FinixCount} Finix transactions against {OrderCount} eShop orders with {DiscrepancyCount} discrepancies",
            finixTransactions.Count,
            orders.Count,
            discrepancies.Count);

        return new ReconciliationResult(from, to, matchedTransactions, eshopOrders, discrepancies);
    }

    public async Task<SavedCardResult> SaveCardAsync(
        string buyerId,
        string? buyerEmail,
        CardDetails card,
        CancellationToken cancellationToken = default)
    {
        ValidateCard(card);

        FinixPaymentInstrumentInfo instrument;
        try
        {
            instrument = await _gateway.CreateCardPaymentInstrumentAsync(
                buyerEmail ?? buyerId,
                card,
                cancellationToken);
        }
        catch (Exception ex) when (ex is ApiException or SdkException)
        {
            _logger.LogError(ex, "Finix rejected the saved card for {BuyerId}", buyerId);
            throw new PaymentException(402, $"Finix could not save the card: {ex.Message}");
        }

        var saved = new SavedPaymentMethod(
            buyerId,
            instrument.Id,
            instrument.IdentityId,
            instrument.Brand,
            instrument.Last4,
            instrument.ExpirationMonth,
            instrument.ExpirationYear,
            instrument.CardType,
            DateTimeOffset.UtcNow);

        _db.SavedPaymentMethods.Add(saved);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Saved Finix payment instrument {PaymentInstrumentId} for {BuyerId}",
            instrument.Id,
            buyerId);

        return MapSavedCard(saved);
    }

    public async Task<IReadOnlyList<SavedCardResult>> ListSavedCardsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var cards = await _db.SavedPaymentMethods
            .AsNoTracking()
            .Where(p => p.BuyerId == buyerId && p.IsEnabled)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return cards.Select(MapSavedCard).ToList();
    }

    public async Task DeleteSavedCardAsync(
        int paymentMethodId,
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var card = await _db.SavedPaymentMethods
            .FirstOrDefaultAsync(p => p.Id == paymentMethodId && p.BuyerId == buyerId, cancellationToken);

        if (card is null)
        {
            throw new PaymentException(404, "Saved payment method not found.");
        }

        card.Disable();
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _gateway.DisablePaymentInstrumentAsync(card.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (Exception ex) when (ex is ApiException or SdkException)
        {
            _logger.LogWarning(
                ex,
                "The saved card {PaymentMethodId} was disabled locally but Finix could not disable payment instrument {PaymentInstrumentId}",
                paymentMethodId,
                card.FinixPaymentInstrumentId);
        }

        _logger.LogInformation("Deleted saved payment method {PaymentMethodId} for {BuyerId}", paymentMethodId, buyerId);
    }

    private async Task<OrderPayment?> LoadPaymentAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.OrderPayments
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    private static (int? OrderId, string? Reason) MatchTransaction(
        FinixTransactionRecord transaction,
        IReadOnlyDictionary<int, OrderPayment> payments)
    {
        foreach (var pair in payments)
        {
            var payment = pair.Value;
            if (string.Equals(payment.FinixAuthorizationId, transaction.Id, StringComparison.Ordinal))
            {
                return (pair.Key, "authorizationId");
            }

            if (string.Equals(payment.CaptureTransferId, transaction.Id, StringComparison.Ordinal))
            {
                return (pair.Key, "captureTransferId");
            }

            if (payment.Refunds.Any(r => string.Equals(r.FinixRefundId, transaction.Id, StringComparison.Ordinal)))
            {
                return (pair.Key, "refundId");
            }
        }

        if (transaction.Tags.TryGetValue("eshopOperationId", out var operationId))
        {
            foreach (var pair in payments)
            {
                if (string.Equals(pair.Value.FinixOperationId, operationId, StringComparison.Ordinal))
                {
                    return (pair.Key, "eshopOperationId");
                }
            }
        }

        return (null, null);
    }

    private static string? GetNotCapturableReason(FinixAuthorizationInfo authorization, DateTimeOffset now)
    {
        if (authorization.IsVoid)
        {
            return $"authorization {authorization.Id} was voided.";
        }

        if (string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            return $"authorization {authorization.Id} was canceled.";
        }

        if (string.Equals(authorization.State, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return $"authorization {authorization.Id} failed with " +
                   $"{authorization.FailureMessage ?? authorization.FailureCode ?? "no failure detail"}.";
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= now)
        {
            return $"authorization {authorization.Id} expired at {authorization.ExpiresAt.Value:O}.";
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            return $"authorization {authorization.Id} is in state {authorization.State ?? "unknown"}.";
        }

        return null;
    }

    private static void ValidateCard(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || card.Number.Count(char.IsDigit) < 12)
        {
            throw new PaymentException(400, "A valid card number is required.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentException(400, "expirationMonth must be between 1 and 12.");
        }

        if (card.ExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentException(400, "The card expiration year has already passed.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentException(400, "The card security code is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentException(400, "The cardholder name is required.");
        }
    }

    private string ResolveCurrency()
    {
        var currency = _finixOptions.Currency?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new PaymentException(500, "Finix:Currency is not configured.");
        }

        return currency;
    }

    private static Address ToAddress(ShippingAddressDetails? address, string buyerId)
    {
        if (address is null)
        {
            return new Address("Not provided", "Not provided", "NA", "USA", "00000");
        }

        return new Address(
            string.IsNullOrWhiteSpace(address.Street) ? "Not provided" : address.Street,
            string.IsNullOrWhiteSpace(address.City) ? "Not provided" : address.City,
            string.IsNullOrWhiteSpace(address.State) ? "NA" : address.State,
            string.IsNullOrWhiteSpace(address.Country) ? "USA" : address.Country,
            string.IsNullOrWhiteSpace(address.ZipCode) ? "00000" : address.ZipCode);
    }

    private static long ToCents(decimal amount) =>
        checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    private static PaymentView MapPayment(OrderPayment? payment)
    {
        if (payment is null)
        {
            return new PaymentView(
                PaymentStatus.AwaitingPayment.ToString(),
                "USD",
                0,
                0,
                0,
                0,
                0,
                0,
                null,
                null,
                false,
                null,
                null,
                null,
                null,
                null,
                Array.Empty<RefundView>());
        }

        return new PaymentView(
            payment.Status.ToString(),
            payment.Currency,
            payment.AmountAuthorizedCents,
            payment.AmountCapturedCents,
            payment.AmountRefundedCents,
            payment.RefundableCents,
            payment.FeeCents,
            payment.NetCents,
            payment.FinixAuthorizationId,
            payment.AuthorizationState,
            payment.AuthorizationVoided,
            payment.AuthorizationExpiresAt,
            payment.CaptureTransferId,
            payment.CaptureState,
            payment.FailureCode,
            payment.FailureMessage,
            payment.Refunds
                .OrderBy(r => r.CreatedAt)
                .Select(r => new RefundView(
                    r.Id,
                    r.FinixRefundId,
                    r.AmountCents,
                    r.State,
                    r.IdempotencyKey,
                    r.CreatedAt))
                .ToList());
    }

    private static SavedCardResult MapSavedCard(SavedPaymentMethod card) =>
        new(
            card.Id,
            card.Brand,
            card.Last4,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.CardType,
            card.CreatedAt);

    private static async Task<IDisposable> AcquireLockAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = OperationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public Releaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose() => _semaphore.Release();
    }
}
