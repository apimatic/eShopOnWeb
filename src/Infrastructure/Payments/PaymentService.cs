using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BuyerIdentityLocks = new();

    private readonly CatalogContext _db;
    private readonly FinixClient _finix;
    private readonly FinixOptions _options;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(CatalogContext db, FinixClient finix, IOptions<FinixOptions> options,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _finix = finix;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OrderSummary> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "The request is not authenticated.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentOperationException(400, "At least one order item is required.");
        }

        var requestedItems = request.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToList();

        if (requestedItems.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new PaymentOperationException(400, "Every order item needs a positive catalogItemId and quantity.");
        }

        var catalogIds = requestedItems.Select(i => i.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems
            .Where(i => catalogIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        var missingIds = catalogIds.Except(catalogItems.Select(i => i.Id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new PaymentOperationException(400,
                $"Unknown catalog item id(s): {string.Join(", ", missingIds)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var requestedItem in requestedItems)
        {
            var catalogItem = catalogItems.First(i => i.Id == requestedItem.CatalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "eCatalog-item-default.png"
                : catalogItem.PictureUri;
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, requestedItem.Quantity));
        }

        var total = orderItems.Sum(i => i.UnitPrice * i.Units);
        if (total <= 0m)
        {
            throw new PaymentOperationException(400, "The order total must be greater than zero.");
        }

        var shippingAddress = ToAddress(request.ShippingAddress, required: true);
        var order = new Order(buyerId, shippingAddress, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, ToCents(total), _options.Currency);
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        return MapOrder(order, payment);
    }

    public async Task<PaymentSummary> PayOrderAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _db.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, cancellationToken);
            if (order is null)
            {
                throw new PaymentOperationException(404, $"Order {orderId} was not found.");
            }

            var payment = await _db.OrderPayments
                .Include(p => p.Refunds)
                .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
            if (payment is null)
            {
                payment = new OrderPayment(order.Id, order.BuyerId, ToCents(order.Total()), _options.Currency);
                _db.OrderPayments.Add(payment);
                await _db.SaveChangesAsync(cancellationToken);
            }

            if (payment.Status == PaymentStatus.Cancelled)
            {
                throw new PaymentOperationException(409, "This order was cancelled and can no longer be paid.");
            }

            if (payment.Status != PaymentStatus.AwaitingPayment)
            {
                return MapPayment(order, payment);
            }

            if (request.Card is not null && request.PaymentMethodId.HasValue)
            {
                throw new PaymentOperationException(400,
                    "Send either card details or paymentMethodId, not both.");
            }

            string source;
            string? identityId = null;

            if (request.PaymentMethodId.HasValue)
            {
                var savedCard = await _db.SavedCards.FirstOrDefaultAsync(c =>
                    c.Id == request.PaymentMethodId.Value && c.BuyerId == buyerId && !c.IsDeleted,
                    cancellationToken);
                if (savedCard is null)
                {
                    throw new PaymentOperationException(404,
                        $"Saved payment method {request.PaymentMethodId.Value} was not found.");
                }

                source = savedCard.FinixPaymentInstrumentId;
            }
            else if (request.Card is not null)
            {
                var card = ValidateCard(request.Card);
                identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);
                FinixPaymentInstrument instrument;
                try
                {
                    instrument = await _finix.CreatePaymentInstrumentAsync(identityId, card.Number,
                        card.ExpirationMonth, card.ExpirationYear, card.SecurityCode, card.Name, card.Address,
                        cancellationToken);
                }
                catch (FinixApiException ex)
                {
                    throw new PaymentOperationException(422,
                        $"Finix could not accept the card details: {ex.Message}", ex.Code, ex);
                }

                source = instrument.Id;
            }
            else
            {
                throw new PaymentOperationException(400,
                    "Provide card details or the id of one of the caller's saved payment methods.");
            }

            if (identityId is not null)
            {
                payment.SetIdentity(identityId);
            }

            var idempotencyKey = !string.IsNullOrWhiteSpace(request.IdempotencyKey)
                ? $"order-{orderId}-{request.IdempotencyKey.Trim()}"
                : payment.AuthorizationIdempotencyKey;

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                payment.RotateAuthorizationIdempotencyKey();
                idempotencyKey = payment.AuthorizationIdempotencyKey!;
            }

            FinixAuthorization authorization;
            try
            {
                authorization = await _finix.CreateAuthorizationAsync(payment.AmountCents, payment.Currency,
                    source, idempotencyKey, orderId, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                if (IsDuplicateAuthorization(ex))
                {
                    var existingAuthorization = await _finix.FindAuthorizationByIdempotencyIdAsync(idempotencyKey,
                        cancellationToken);
                    if (existingAuthorization is not null
                        && string.Equals(existingAuthorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                    {
                        payment.MarkAuthorized(existingAuthorization.Id, existingAuthorization.State!,
                            existingAuthorization.ExpiresAt, existingAuthorization.Source ?? source, idempotencyKey);
                        order.MarkAuthorized();
                        await _db.SaveChangesAsync(cancellationToken);
                        return MapPayment(order, payment);
                    }

                    if (existingAuthorization is not null)
                    {
                        payment.MarkAuthorizationFailed(existingAuthorization.FailureCode,
                            existingAuthorization.FailureMessage);
                        await _db.SaveChangesAsync(cancellationToken);
                        throw new PaymentOperationException(402,
                            existingAuthorization.FailureMessage
                            ?? $"Finix did not approve the authorization (state: {existingAuthorization.State ?? "UNKNOWN"}).",
                            existingAuthorization.FailureCode);
                    }
                }

                payment.MarkAuthorizationFailed(ex.Code, ex.Message);
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    payment.RotateAuthorizationIdempotencyKey();
                }

                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(422,
                    $"Finix could not authorize the payment: {ex.Message}", ex.Code, ex);
            }

            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                payment.MarkAuthorizationFailed(authorization.FailureCode, authorization.FailureMessage);
                if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    payment.RotateAuthorizationIdempotencyKey();
                }

                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(402,
                    authorization.FailureMessage
                    ?? $"Finix did not approve the authorization (state: {authorization.State ?? "UNKNOWN"}).",
                    authorization.FailureCode);
            }

            payment.MarkAuthorized(authorization.Id, authorization.State ?? "SUCCEEDED",
                authorization.ExpiresAt, source, idempotencyKey);
            order.MarkAuthorized();
            await _db.SaveChangesAsync(cancellationToken);

            return MapPayment(order, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentSummary> FulfilOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _db.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
            if (order is null)
            {
                throw new PaymentOperationException(404, $"Order {orderId} was not found.");
            }

            var payment = await _db.OrderPayments
                .Include(p => p.Refunds)
                .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
            if (payment is null)
            {
                throw new PaymentOperationException(409, "The order has no payment to fulfil.");
            }

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Refunded)
            {
                return MapPayment(order, payment);
            }

            if (payment.Status == PaymentStatus.Cancelled)
            {
                throw new PaymentOperationException(409,
                    "The order was cancelled, so its authorization hold was released and cannot be captured.");
            }

            if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            {
                throw new PaymentOperationException(409,
                    "The order is awaiting payment. Authorize it with POST /api/orders/{orderId}/pay before fulfilling it.");
            }

            if (payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow)
            {
                throw new PaymentOperationException(409,
                    $"The authorization hold expired at {payment.AuthorizationExpiresAt.Value:O}. "
                    + "Cancel the order to release any remaining hold, then ask the shopper to pay again.");
            }

            FinixAuthorization captured;
            try
            {
                captured = await _finix.CaptureAuthorizationAsync(payment.FinixAuthorizationId,
                    payment.AmountCents, cancellationToken);
            }
            catch (FinixApiException ex) when (IsAlreadyCaptured(ex))
            {
                var existing = await _finix.GetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
                if (string.IsNullOrWhiteSpace(existing.Transfer))
                {
                    throw new PaymentOperationException(409,
                        $"Finix reports authorization {payment.FinixAuthorizationId} as already captured, "
                        + "but did not return the capture transfer id.",
                        ex.Code, ex);
                }

                captured = existing;
            }
            catch (FinixApiException ex)
            {
                throw new PaymentOperationException(409,
                    $"The authorization {payment.FinixAuthorizationId} could not be captured: {ex.Message} "
                    + "The order remains authorized. If the hold has expired, cancel the order to release it and "
                    + "ask the shopper to pay again.",
                    ex.Code, ex);
            }

            if (string.IsNullOrWhiteSpace(captured.Transfer))
            {
                throw new PaymentOperationException(502,
                    "Finix reported a successful capture but did not return the capture transfer id.");
            }

            FinixTransfer transfer;
            try
            {
                transfer = await _finix.GetTransferAsync(captured.Transfer, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw new PaymentOperationException(502,
                    $"Finix captured the payment as transfer {captured.Transfer}, but its details could not be read: {ex.Message}",
                    ex.Code, ex);
            }

            if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(409,
                    $"Finix reported the capture transfer {transfer.Id} as FAILED"
                    + (string.IsNullOrWhiteSpace(transfer.FailureMessage) ? "." : $": {transfer.FailureMessage}")
                    + " The order remains authorized; cancel it to release any remaining hold.");
            }

            decimal feeCents = 0m;
            var feeKnown = false;
            try
            {
                feeCents = await _finix.GetFeesForTransferAsync(captured.Transfer, TimeSpan.FromSeconds(10),
                    cancellationToken);
                feeKnown = feeCents > 0m;
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning("Could not read Finix fees for transfer {TransferId}: {Message}",
                    captured.Transfer, ex.Message);
            }

            var feeAmountCents = feeKnown ? FeeToCents(feeCents) : (int?)null;
            var netAmountCents = feeKnown ? payment.AmountCents - feeAmountCents!.Value : (int?)null;

            payment.MarkCaptured(captured.Transfer, transfer.State ?? "SUCCEEDED", payment.AmountCents,
                feeAmountCents, netAmountCents);
            order.MarkFulfilled();
            await _db.SaveChangesAsync(cancellationToken);

            return MapPayment(order, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentSummary> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _db.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
            if (order is null)
            {
                throw new PaymentOperationException(404, $"Order {orderId} was not found.");
            }

            var payment = await _db.OrderPayments
                .Include(p => p.Refunds)
                .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
            if (payment is null)
            {
                throw new PaymentOperationException(409, "The order has no payment hold to release.");
            }

            if (payment.Status == PaymentStatus.Cancelled)
            {
                return MapPayment(order, payment);
            }

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Refunded)
            {
                throw new PaymentOperationException(409,
                    "The payment has already been captured. Use POST /api/orders/{orderId}/refunds to return the money.");
            }

            if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            {
                throw new PaymentOperationException(409,
                    "The order has no active authorization hold to release.");
            }

            FinixAuthorization voided;
            try
            {
                voided = await _finix.VoidAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
            }
            catch (FinixApiException ex) when (IsAlreadyVoided(ex))
            {
                var existing = await _finix.GetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
                if (existing.IsVoid != true
                    && !string.Equals(existing.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PaymentOperationException(409,
                        $"Finix reports authorization {payment.FinixAuthorizationId} as already voided, "
                        + "but its void state is not successful.",
                        ex.Code, ex);
                }

                voided = existing;
            }
            catch (FinixApiException ex)
            {
                throw new PaymentOperationException(409,
                    $"The authorization hold {payment.FinixAuthorizationId} could not be released: {ex.Message}",
                    ex.Code, ex);
            }

            var released = voided.IsVoid == true
                || string.Equals(voided.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(voided.VoidState, "PENDING", StringComparison.OrdinalIgnoreCase);

            if (!released)
            {
                throw new PaymentOperationException(409,
                    $"Finix did not release the authorization hold (void state: {voided.VoidState ?? "UNKNOWN"}).");
            }

            payment.MarkCancelled(voided.VoidState ?? (voided.IsVoid == true ? "SUCCEEDED" : "PENDING"));
            order.MarkCancelled();
            await _db.SaveChangesAsync(cancellationToken);

            return MapPayment(order, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundSummary> RefundOrderAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AmountCents <= 0)
        {
            throw new PaymentOperationException(400, "The refund amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentOperationException(400, "An idempotencyKey is required for refunds.");
        }

        var idempotencyKey = request.IdempotencyKey.Trim();
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _db.Orders
                .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, cancellationToken);
            if (order is null)
            {
                throw new PaymentOperationException(404, $"Order {orderId} was not found.");
            }

            var payment = await _db.OrderPayments
                .Include(p => p.Refunds)
                .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
            if (payment is null)
            {
                throw new PaymentOperationException(404, $"Order {orderId} has no payment.");
            }

            if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Refunded))
            {
                throw new PaymentOperationException(409,
                    "Refunds are only available after the order has been fulfilled and its payment captured.");
            }

            if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
            {
                throw new PaymentOperationException(409, "The order has no captured Finix transfer to refund.");
            }

            var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
            if (existing is not null)
            {
                if (existing.AmountCents != request.AmountCents)
                {
                    throw new PaymentOperationException(409,
                        $"Idempotency key '{idempotencyKey}' was already used for a refund of "
                        + $"{existing.AmountCents} cents, not {request.AmountCents} cents.");
                }

                return MapRefund(existing);
            }

            var capturedAmount = payment.CapturedAmountCents ?? payment.AmountCents;
            var remaining = capturedAmount - payment.RefundedAmountCents;
            if (request.AmountCents > remaining)
            {
                throw new PaymentOperationException(409,
                    $"Refund amount {request.AmountCents} cents exceeds the remaining refundable amount "
                    + $"{remaining} cents of the captured {capturedAmount} cents.");
            }

            if (request.AmountCents < capturedAmount)
            {
                await WaitForTransferToSettleAsync(payment.FinixCaptureTransferId, capturedAmount,
                    cancellationToken);
            }

            var refund = new PaymentRefund(payment.Id, orderId, buyerId, request.AmountCents, idempotencyKey);
            _db.PaymentRefunds.Add(refund);
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                var reversal = await _finix.CreateReversalAsync(payment.FinixCaptureTransferId,
                    request.AmountCents, cancellationToken);

                if (string.Equals(reversal.State, "FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    refund.MarkFailed(reversal.FailureCode, reversal.FailureMessage);
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new PaymentOperationException(422,
                        reversal.FailureMessage ?? "Finix reported the refund transfer as FAILED.",
                        reversal.FailureCode);
                }

                refund.MarkSucceeded(reversal.Id, reversal.State ?? "SUCCEEDED");
                payment.AddRefund(refund);
                order.MarkRefunded(payment.RefundedAmountCents, capturedAmount);
                await _db.SaveChangesAsync(cancellationToken);

                return MapRefund(refund);
            }
            catch (FinixApiException ex) when (IsPendingTransferConflict(ex))
            {
                _db.PaymentRefunds.Remove(refund);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(409,
                    $"Finix still reports the capture transfer {payment.FinixCaptureTransferId} as PENDING. "
                    + "Partial refunds are only allowed after the capture transfer reaches SUCCEEDED. "
                    + $"Retry this request with the same idempotencyKey in a few moments, or refund the full "
                    + $"{capturedAmount} cents now.",
                    ex.Code, ex);
            }
            catch (FinixApiException ex)
            {
                refund.MarkFailed(ex.Code, ex.Message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(422,
                    $"Finix could not create the refund: {ex.Message}", ex.Code, ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .Include(o => o.OrderItems)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var payments = await _db.OrderPayments
            .Include(p => p.Refunds)
            .Where(p => p.BuyerId == buyerId)
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var payment in payments)
        {
            changed |= await TryRefreshFeesAsync(payment, cancellationToken);
            changed |= await TryRefreshTransferStatesAsync(payment, cancellationToken);
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);
        return orders
            .Select(o => MapOrder(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<SavedCardSummary> SaveCardAsync(string buyerId, CardRequest request,
        CancellationToken cancellationToken)
    {
        var card = ValidateCard(request);
        var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);

        FinixPaymentInstrument instrument;
        try
        {
            instrument = await _finix.CreatePaymentInstrumentAsync(identityId, card.Number,
                card.ExpirationMonth, card.ExpirationYear, card.SecurityCode, card.Name, card.Address,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(422,
                $"Finix could not save the card: {ex.Message}", ex.Code, ex);
        }

        var savedCard = new SavedCard(buyerId, instrument.Id, instrument.Brand, instrument.LastFour,
            instrument.ExpirationMonth, instrument.ExpirationYear, instrument.CardType, instrument.Name ?? card.Name);
        _db.SavedCards.Add(savedCard);
        await _db.SaveChangesAsync(cancellationToken);

        return MapSavedCard(savedCard);
    }

    public async Task<IReadOnlyList<SavedCardSummary>> GetCardsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var cards = await _db.SavedCards
            .Where(c => c.BuyerId == buyerId && !c.IsDeleted)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);

        return cards.Select(MapSavedCard).ToList();
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var savedCard = await _db.SavedCards.FirstOrDefaultAsync(c =>
            c.Id == paymentMethodId && c.BuyerId == buyerId && !c.IsDeleted, cancellationToken);
        if (savedCard is null)
        {
            throw new PaymentOperationException(404, $"Saved payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(savedCard.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (FinixApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Finix payment instrument {InstrumentId} was already absent while deleting saved card {CardId}",
                savedCard.FinixPaymentInstrumentId, savedCard.Id);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(409,
                $"Finix could not disable the saved card: {ex.Message}", ex.Code, ex);
        }

        savedCard.MarkDeleted();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentOperationException(400, "The 'from' date-time must be on or before the 'to' date-time.");
        }

        IReadOnlyList<FinixTransfer> finixTransfers;
        try
        {
            finixTransfers = await _finix.ListTransfersAsync(from, to, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(502,
                $"Finix could not list transactions for the requested range: {ex.Message}", ex.Code, ex);
        }

        var payments = await _db.OrderPayments
            .Include(p => p.Refunds)
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var payment in payments)
        {
            changed |= await TryRefreshFeesAsync(payment, cancellationToken);
            changed |= await TryRefreshTransferStatesAsync(payment, cancellationToken);
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        var localByFinixTransferId = new Dictionary<string, (int OrderId, string Kind)>();
        foreach (var payment in payments)
        {
            if (!string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
            {
                localByFinixTransferId[payment.FinixCaptureTransferId] = (payment.OrderId, "CAPTURE");
            }

            foreach (var refund in payment.Refunds)
            {
                if (!string.IsNullOrWhiteSpace(refund.FinixTransferId))
                {
                    localByFinixTransferId[refund.FinixTransferId] = (payment.OrderId, "REFUND");
                }
            }
        }

        var transactions = new List<ReconciliationTransaction>();
        var unmatchedFinixIds = new List<string>();
        var matchedFinixCount = 0;

        var finixById = finixTransfers.ToDictionary(t => t.Id);
        var supplementalTransfers = new List<FinixTransfer>();
        foreach (var transferId in localByFinixTransferId.Keys)
        {
            if (finixById.ContainsKey(transferId))
            {
                continue;
            }

            try
            {
                var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
                supplementalTransfers.Add(transfer);
                finixById[transfer.Id] = transfer;
            }
            catch (FinixApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Finix has no transfer {TransferId} referenced by an eShop payment", transferId);
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning("Could not read Finix transfer {TransferId} during reconciliation: {Message}",
                    transferId, ex.Message);
            }
        }

        if (supplementalTransfers.Count > 0)
        {
            finixTransfers = finixTransfers.Concat(supplementalTransfers).ToList();
        }

        foreach (var transfer in finixTransfers)
        {
            int? matchedOrderId = null;
            var matchStatus = "FINIX_ONLY";

            if (localByFinixTransferId.TryGetValue(transfer.Id, out var localMatch))
            {
                matchedOrderId = localMatch.OrderId;
                matchStatus = "MATCHED";
                matchedFinixCount++;
            }
            else
            {
                unmatchedFinixIds.Add(transfer.Id);
            }

            transactions.Add(new ReconciliationTransaction(
                transfer.Id,
                transfer.Type,
                transfer.Subtype,
                transfer.State,
                (int)(transfer.Amount ?? 0),
                transfer.Currency,
                transfer.CreatedAt,
                transfer.ParentTransfer,
                transfer.Source,
                transfer.TraceId,
                transfer.FailureCode,
                transfer.FailureMessage,
                matchedOrderId,
                matchStatus));
        }

        var relevantPayments = payments
            .Where(p => InRange(p.CreatedAt, from, to)
                || InRange(p.CapturedAt, from, to)
                || p.Refunds.Any(r => InRange(r.CreatedAt, from, to)))
            .OrderBy(p => p.CreatedAt)
            .ToList();

        var eshopPayments = new List<ReconciliationEshopPayment>();
        var unmatchedEshopOrderIds = new List<int>();

        foreach (var payment in relevantPayments)
        {
            var missing = false;
            if (!string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId)
                && !finixById.ContainsKey(payment.FinixCaptureTransferId))
            {
                missing = true;
            }

            if (payment.Refunds.Any(r => !string.IsNullOrWhiteSpace(r.FinixTransferId)
                && !finixById.ContainsKey(r.FinixTransferId!)))
            {
                missing = true;
            }

            var matchStatus = missing
                ? "MISSING_IN_FINIX"
                : string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId)
                    ? "NOT_CAPTURED"
                    : "MATCHED";

            if (missing)
            {
                unmatchedEshopOrderIds.Add(payment.OrderId);
            }

            eshopPayments.Add(new ReconciliationEshopPayment(
                payment.OrderId,
                payment.Status.ToString(),
                payment.FinixAuthorizationId,
                payment.FinixCaptureTransferId,
                payment.AmountCents,
                payment.CapturedAmountCents,
                payment.RefundedAmountCents,
                payment.CreatedAt,
                payment.CapturedAt,
                payment.Refunds.OrderBy(r => r.Id).Select(MapRefund).ToList(),
                matchStatus));
        }

        return new ReconciliationReport(
            from,
            to,
            transactions,
            eshopPayments,
            unmatchedFinixIds,
            unmatchedEshopOrderIds,
            matchedFinixCount,
            transactions.Count,
            eshopPayments.Count);
    }

    private async Task<bool> TryRefreshTransferStatesAsync(OrderPayment payment,
        CancellationToken cancellationToken)
    {
        var changed = false;

        if (string.Equals(payment.CaptureState, "PENDING", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
        {
            try
            {
                var transfer = await _finix.GetTransferAsync(payment.FinixCaptureTransferId, cancellationToken);
                if (!string.Equals(transfer.State, payment.CaptureState, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(transfer.State))
                {
                    payment.UpdateCaptureState(transfer.State);
                    changed = true;
                }
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning("Could not refresh Finix capture state for transfer {TransferId}: {Message}",
                    payment.FinixCaptureTransferId, ex.Message);
            }
        }

        foreach (var refund in payment.Refunds.Where(r =>
            string.Equals(r.State, "PENDING", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(r.FinixTransferId)))
        {
            try
            {
                var transfer = await _finix.GetTransferAsync(refund.FinixTransferId!, cancellationToken);
                if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    refund.MarkFailed(transfer.FailureCode, transfer.FailureMessage);
                    changed = true;
                }
                else if (!string.Equals(transfer.State, refund.State, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(transfer.State))
                {
                    refund.UpdateState(transfer.State);
                    changed = true;
                }
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning("Could not refresh Finix refund state for transfer {TransferId}: {Message}",
                    refund.FinixTransferId, ex.Message);
            }
        }

        return changed;
    }

    private async Task<bool> TryRefreshFeesAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        if (!payment.FeesPending || string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
        {
            return false;
        }

        try
        {
            var feeCents = await _finix.GetFeesForTransferAsync(payment.FinixCaptureTransferId,
                TimeSpan.FromSeconds(2), cancellationToken);
            if (feeCents > 0m)
            {
                var feeAmountCents = FeeToCents(feeCents);
                payment.UpdateFees(feeAmountCents,
                    (payment.CapturedAmountCents ?? payment.AmountCents) - feeAmountCents);
                return true;
            }

            var capturedAt = payment.CapturedAt ?? payment.CreatedAt;
            if (DateTimeOffset.UtcNow - capturedAt > TimeSpan.FromMinutes(2))
            {
                payment.UpdateFees(0, payment.CapturedAmountCents ?? payment.AmountCents);
                return true;
            }
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning("Could not refresh Finix fees for transfer {TransferId}: {Message}",
                payment.FinixCaptureTransferId, ex.Message);
        }

        return false;
    }

    private async Task WaitForTransferToSettleAsync(string transferId, int capturedAmount,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(60);
        string? state;
        try
        {
            var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
            state = transfer.State;
            while (!string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(state, "CANCELED", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(state, "RETURNED", StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(1000, cancellationToken);
                transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
                state = transfer.State;
            }
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(409,
                $"Could not read the capture transfer {transferId} before refunding: {ex.Message}",
                ex.Code, ex);
        }

        if (!string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentOperationException(409,
                $"Finix reports the capture transfer {transferId} as {state ?? "UNKNOWN"}. "
                + "Partial refunds are only allowed after the capture transfer reaches SUCCEEDED. "
                + $"Retry this request with the same idempotencyKey in a few moments, or refund the full "
                + $"{capturedAmount} cents now.");
        }
    }

    private static bool IsPendingTransferConflict(FinixApiException ex)
    {
        return string.Equals(ex.Code, "CONFLICT", StringComparison.OrdinalIgnoreCase)
            && ex.Message.Contains("PENDING", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDuplicateAuthorization(FinixApiException ex)
    {
        return ex.Message.Contains("Duplicate authorization", StringComparison.OrdinalIgnoreCase)
            || (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                && ex.Message.Contains("idempotency", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAlreadyCaptured(FinixApiException ex)
    {
        return ex.Message.Contains("already captured", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlreadyVoided(FinixApiException ex)
    {
        return ex.Message.Contains("already void", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("is void", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GetOrCreateIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        var existing = await _db.ShopperPaymentProfiles
            .FirstOrDefaultAsync(p => p.BuyerId == buyerId, cancellationToken);
        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        var gate = BuyerIdentityLocks.GetOrAdd(buyerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            existing = await _db.ShopperPaymentProfiles
                .FirstOrDefaultAsync(p => p.BuyerId == buyerId, cancellationToken);
            if (existing is not null)
            {
                return existing.FinixIdentityId;
            }

            FinixIdentity identity;
            try
            {
                identity = await _finix.CreateIdentityAsync(cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw new PaymentOperationException(502,
                    $"Finix could not create a buyer identity: {ex.Message}", ex.Code, ex);
            }

            var profile = new ShopperPaymentProfile(buyerId, identity.Id);
            _db.ShopperPaymentProfiles.Add(profile);
            await _db.SaveChangesAsync(cancellationToken);
            return identity.Id;
        }
        finally
        {
            gate.Release();
        }
    }

    private static (string Number, int ExpirationMonth, int ExpirationYear, string? SecurityCode, string Name,
        FinixAddressRequest? Address) ValidateCard(CardRequest? request)
    {
        if (request is null)
        {
            throw new PaymentOperationException(400, "Card details are required.");
        }

        var number = DigitsOnly(request.Number ?? string.Empty);
        if (number.Length is < 12 or > 19)
        {
            throw new PaymentOperationException(400, "The card number is not valid.");
        }

        if (request.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentOperationException(400, "expirationMonth must be between 1 and 12.");
        }

        var year = request.ExpirationYear < 100 ? request.ExpirationYear + 2000 : request.ExpirationYear;
        if (year < DateTime.UtcNow.Year)
        {
            throw new PaymentOperationException(400, "The card expiration year is in the past.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new PaymentOperationException(400, "The cardholder name is required.");
        }

        FinixAddressRequest? address = null;
        if (request.Address is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Address.Street)
                || string.IsNullOrWhiteSpace(request.Address.City)
                || string.IsNullOrWhiteSpace(request.Address.State)
                || string.IsNullOrWhiteSpace(request.Address.ZipCode))
            {
                throw new PaymentOperationException(400,
                    "A supplied billing address needs street, city, state and zipCode.");
            }

            address = new FinixAddressRequest
            {
                Line1 = request.Address.Street.Trim(),
                City = request.Address.City.Trim(),
                Region = request.Address.State.Trim(),
                PostalCode = request.Address.ZipCode.Trim(),
                Country = string.IsNullOrWhiteSpace(request.Address.Country)
                    ? "USA"
                    : request.Address.Country.Trim()
            };
        }

        return (number, request.ExpirationMonth, year,
            string.IsNullOrWhiteSpace(request.SecurityCode) ? null : request.SecurityCode.Trim(),
            request.Name.Trim(), address);
    }

    private static Address ToAddress(AddressRequest? request, bool required)
    {
        if (request is null)
        {
            if (required)
            {
                throw new PaymentOperationException(400, "A shipping address is required.");
            }

            throw new PaymentOperationException(400, "An address is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Street)
            || string.IsNullOrWhiteSpace(request.City)
            || string.IsNullOrWhiteSpace(request.State)
            || string.IsNullOrWhiteSpace(request.Country)
            || string.IsNullOrWhiteSpace(request.ZipCode))
        {
            throw new PaymentOperationException(400,
                "The shipping address needs street, city, state, country and zipCode.");
        }

        return new Address(request.Street.Trim(), request.City.Trim(), request.State.Trim(),
            request.Country.Trim(), request.ZipCode.Trim());
    }

    private OrderSummary MapOrder(Order order, OrderPayment? payment)
    {
        var items = order.OrderItems
            .Select(i => new OrderItemSummary(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName,
                i.UnitPrice, i.Units, i.UnitPrice * i.Units))
            .ToList();

        var total = order.Total();
        var totalCents = ToCents(total);
        var currency = payment?.Currency ?? _options.Currency;

        var paymentSummary = payment is null
            ? new PaymentSummary(order.Id, order.Status.ToString(), PaymentStatus.AwaitingPayment.ToString(),
                totalCents, currency, null, null, null, null, null, null, null, null, false, 0, 0, null, null,
                Array.Empty<RefundSummary>())
            : MapPayment(order, payment);

        return new OrderSummary(order.Id, order.OrderDate, order.Status.ToString(), total, totalCents,
            currency, items, paymentSummary);
    }

    private static PaymentSummary MapPayment(Order order, OrderPayment payment)
    {
        var refunds = payment.Refunds.OrderBy(r => r.Id).Select(MapRefund).ToList();
        var captured = payment.CapturedAmountCents ?? 0;
        var refundable = Math.Max(0, captured - payment.RefundedAmountCents);

        return new PaymentSummary(
            payment.OrderId,
            order.Status.ToString(),
            payment.Status.ToString(),
            payment.AmountCents,
            payment.Currency,
            payment.FinixAuthorizationId,
            payment.AuthorizationState,
            payment.AuthorizationExpiresAt,
            payment.FinixCaptureTransferId,
            payment.CaptureState,
            payment.CapturedAmountCents,
            payment.FeeAmountCents,
            payment.NetAmountCents,
            payment.FeesPending,
            payment.RefundedAmountCents,
            refundable,
            payment.FailureCode,
            payment.FailureMessage,
            refunds);
    }

    private static RefundSummary MapRefund(PaymentRefund refund)
    {
        return new RefundSummary(refund.Id, refund.AmountCents, refund.State ?? "UNKNOWN",
            refund.IdempotencyKey, refund.FinixTransferId, refund.CreatedAt, refund.FailureCode,
            refund.FailureMessage);
    }

    private static SavedCardSummary MapSavedCard(SavedCard card)
    {
        return new SavedCardSummary(card.Id, card.Brand ?? "UNKNOWN", card.LastFour ?? "****",
            card.ExpirationMonth, card.ExpirationYear, card.CardType, card.Name, card.CreatedAt);
    }

    private static bool InRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to)
    {
        return value.HasValue && value.Value >= from && value.Value <= to;
    }

    private static int ToCents(decimal amount)
    {
        return (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    }

    private static int FeeToCents(decimal feeCents)
    {
        return (int)Math.Round(feeCents, MidpointRounding.AwayFromZero);
    }

    private static string DigitsOnly(string value)
    {
        return new string(value.Where(char.IsDigit).ToArray());
    }
}
