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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Finix;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class FinixPaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly CatalogContext _db;
    private readonly IFinixClient _finix;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixPaymentService> _logger;

    public FinixPaymentService(
        CatalogContext db,
        IFinixClient finix,
        IOptions<FinixOptions> options,
        ILogger<FinixPaymentService> logger)
    {
        _db = db;
        _finix = finix;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OrderDetails> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "A signed-in shopper is required to place an order.");
        }

        if (items == null || items.Count == 0)
        {
            throw new PaymentException(400, "At least one catalog item is required.");
        }

        if (shipToAddress == null ||
            string.IsNullOrWhiteSpace(shipToAddress.Street) ||
            string.IsNullOrWhiteSpace(shipToAddress.City) ||
            string.IsNullOrWhiteSpace(shipToAddress.Country) ||
            string.IsNullOrWhiteSpace(shipToAddress.ZipCode))
        {
            throw new PaymentException(400, "A shipping address with street, city, country and zip code is required.");
        }

        var requested = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (requested.Any(i => i.Quantity <= 0))
        {
            throw new PaymentException(400, "Item quantities must be greater than zero.");
        }

        var ids = requested.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);

        var missing = ids.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException(400, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = requested.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "eCatalog-item-default.png"
                : catalogItem.PictureUri;
            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri),
                catalogItem.Price,
                item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        return new OrderDetails(order, null);
    }

    public async Task<OrderDetails> PayOrderAsync(
        string buyerId,
        int orderId,
        CardPaymentInput? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken)
                        ?? throw new PaymentException(404, $"Order {orderId} was not found.");

            if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new PaymentException(404, $"Order {orderId} was not found.");
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
            {
                throw new PaymentException(409, $"Order {orderId} can no longer be paid because it is {order.Status}.");
            }

            var payment = await LoadPaymentAsync(orderId, cancellationToken);

            if (payment != null)
            {
                if (payment.Status == PaymentStatus.Authorized)
                {
                    return new OrderDetails(order, payment);
                }

                if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
                {
                    throw new PaymentException(409, $"Order {orderId} has already been captured and cannot be paid again.");
                }

                if (payment.Status == PaymentStatus.Voided)
                {
                    throw new PaymentException(409, $"The payment for order {orderId} was voided. The order must be cancelled and re-created.");
                }

                payment.ResetForRetry();
            }
            else
            {
                payment = new OrderPayment(order.Id, buyerId, _options.Currency);
                _db.OrderPayments.Add(payment);
            }

            if (card != null && savedPaymentMethodId.HasValue)
            {
                throw new PaymentException(400, "Supply either card details or a saved paymentMethodId, not both.");
            }

            if (card == null && !savedPaymentMethodId.HasValue)
            {
                throw new PaymentException(400, "Supply card details or a saved paymentMethodId.");
            }

            string sourcePaymentInstrumentId;

            if (savedPaymentMethodId.HasValue)
            {
                var savedCard = await _db.SavedCards.FirstOrDefaultAsync(
                    c => c.Id == savedPaymentMethodId.Value && c.BuyerId == buyerId && !c.IsDeleted,
                    cancellationToken);

                if (savedCard == null)
                {
                    throw new PaymentException(404, $"Saved payment method {savedPaymentMethodId.Value} was not found.");
                }

                var instrument = await _finix.GetPaymentInstrumentAsync(savedCard.FinixPaymentInstrumentId, cancellationToken);
                if (!instrument.Enabled)
                {
                    throw new PaymentException(400, "The selected saved card is no longer enabled. Remove it and save another card.");
                }

                sourcePaymentInstrumentId = savedCard.FinixPaymentInstrumentId;
            }
            else
            {
                ValidateCard(card!);
                var (firstName, lastName) = SplitName(card!.Name);
                var identity = await _finix.CreateBuyerIdentityAsync(
                    firstName,
                    lastName,
                    null,
                    null,
                    card.BillingAddress,
                    cancellationToken);

                var instrument = await _finix.CreatePaymentInstrumentAsync(identity.Id, ToFinixCard(card), cancellationToken);
                if (!instrument.Enabled)
                {
                    throw new PaymentException(402, "The card could not be used for payment.");
                }

                sourcePaymentInstrumentId = instrument.Id;
            }

            var amountCents = ToCents(order.Total());
            if (amountCents <= 0)
            {
                throw new PaymentException(400, "The order total must be greater than zero.");
            }

            await _db.SaveChangesAsync(cancellationToken);

            var idempotencyId = $"order-{order.PaymentReference}-authorize";

            FinixAuthorizationResult authorization;
            try
            {
                authorization = await _finix.CreateAuthorizationAsync(
                    amountCents,
                    _options.Currency,
                    sourcePaymentInstrumentId,
                    idempotencyId,
                    new Dictionary<string, string> { ["order_id"] = order.Id.ToString() },
                    cancellationToken);
            }
            catch (FinixApiException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            {
                authorization = await _finix.FindAuthorizationByIdempotencyIdAsync(idempotencyId, cancellationToken)
                                ?? throw ToPaymentException(ex, "Authorization");
            }
            catch (FinixApiException ex)
            {
                payment.MarkFailed(ex.Code, ex.Message);
                await _db.SaveChangesAsync(cancellationToken);
                throw ToPaymentException(ex, "Authorization");
            }

            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                payment.MarkFailed(authorization.FailureCode, authorization.FailureMessage ?? $"Finix authorization ended in state {authorization.State}.");
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentException(402, $"The payment could not be authorized: {authorization.FailureMessage ?? authorization.State}");
            }

            if (authorization.Amount != amountCents)
            {
                try
                {
                    await _finix.VoidAuthorizationAsync(authorization.Id, cancellationToken);
                }
                catch (FinixApiException ex)
                {
                    _logger.LogError(ex, "Failed to void mismatched authorization {AuthorizationId}", authorization.Id);
                }

                payment.MarkFailed("AMOUNT_MISMATCH", $"Finix authorized {authorization.Amount} cents but the order total is {amountCents} cents.");
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentException(502, "Finix authorized a different amount than the order total. The hold was released.");
            }

            payment.SetAuthorized(authorization.Id, authorization.State, authorization.ExpiresAt, authorization.Amount);
            order.MarkAuthorized();
            await _db.SaveChangesAsync(cancellationToken);

            return new OrderDetails(order, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderDetails> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken)
                        ?? throw new PaymentException(404, $"Order {orderId} was not found.");

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentException(409, $"Order {orderId} is cancelled and cannot be fulfilled.");
            }

            if (order.Status is OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
            {
                throw new PaymentException(409, $"Order {orderId} has already been refunded and cannot be fulfilled.");
            }

            var payment = await LoadPaymentAsync(orderId, cancellationToken)
                          ?? throw new PaymentException(409, $"Order {orderId} has no payment to capture. The shopper must pay before it can be fulfilled.");

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                if (order.Status != OrderStatus.Fulfilled)
                {
                    order.MarkFulfilled();
                    await _db.SaveChangesAsync(cancellationToken);
                }

                return new OrderDetails(order, payment);
            }

            if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            {
                throw new PaymentException(409, BuildNotCapturableMessage(orderId, payment));
            }

            FinixAuthorizationResult authorization;
            try
            {
                authorization = await _finix.GetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw ToPaymentException(ex, "Authorization lookup");
            }

            if (authorization.IsVoid ||
                !string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
                (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow))
            {
                payment.MarkAuthorizationExpired(BuildNotCapturableMessage(orderId, payment, authorization));
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentException(409, payment.FailureMessage!);
            }

            FinixAuthorizationResult capture;
            try
            {
                capture = await _finix.CaptureAuthorizationAsync(
                    authorization.Id,
                    payment.AuthorizedAmountCents,
                    cancellationToken);
            }
            catch (FinixApiException ex) when (ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            {
                var recovered = await TryRecoverCapturedPaymentAsync(order, payment, authorization.Id, cancellationToken);
                if (recovered != null)
                {
                    return recovered;
                }

                payment.MarkCaptureFailed(ex.Code, ex.Message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentException(409,
                    $"Capture failed for authorization {authorization.Id}: {ex.Message} " +
                    "Check the authorization in Finix, then cancel and re-create the order if the hold can no longer be captured.");
            }
            catch (FinixApiException ex)
            {
                throw ToPaymentException(ex, "Capture");
            }

            var transferId = capture.TransferId ?? authorization.TransferId;
            if (string.IsNullOrWhiteSpace(transferId))
            {
                var refreshed = await _finix.GetAuthorizationAsync(authorization.Id, cancellationToken);
                transferId = refreshed.TransferId;
            }

            if (string.IsNullOrWhiteSpace(transferId))
            {
                payment.MarkCaptureFailed("NO_TRANSFER", "Finix reported a successful capture but did not return a transfer id.");
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentException(502, $"Finix captured authorization {authorization.Id} but returned no transfer id. Contact Finix support with the authorization id.");
            }

            var transfer = await WaitForTransferAsync(transferId, cancellationToken);
            var feeCents = transfer.Fee;
            var netCents = transfer.Amount - feeCents;

            payment.MarkCaptured(transfer.Id, transfer.State, transfer.Amount, feeCents, netCents);
            order.MarkFulfilled();
            await _db.SaveChangesAsync(cancellationToken);

            return new OrderDetails(order, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderDetails> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken)
                        ?? throw new PaymentException(404, $"Order {orderId} was not found.");

            if (order.Status == OrderStatus.Cancelled)
            {
                return new OrderDetails(order, await LoadPaymentAsync(orderId, cancellationToken));
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
            {
                throw new PaymentException(409, $"Order {orderId} has already been fulfilled. Use the refunds endpoint instead of cancelling.");
            }

            var payment = await LoadPaymentAsync(orderId, cancellationToken);

            if (payment == null)
            {
                order.MarkCancelled();
                await _db.SaveChangesAsync(cancellationToken);
                return new OrderDetails(order, null);
            }

            if (payment.Status == PaymentStatus.Voided)
            {
                order.MarkCancelled();
                await _db.SaveChangesAsync(cancellationToken);
                return new OrderDetails(order, payment);
            }

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                throw new PaymentException(409, $"Order {orderId} has a captured payment. Use the refunds endpoint instead of cancelling.");
            }

            if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            {
                try
                {
                    var voided = await _finix.VoidAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
                    payment.MarkVoided(voided.State);
                }
                catch (FinixApiException ex)
                {
                    var current = await TryGetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
                    if (current?.IsVoid == true)
                    {
                        payment.MarkVoided(current.State);
                    }
                    else
                    {
                        throw new PaymentException(409,
                            $"The authorization {payment.FinixAuthorizationId} could not be released: {ex.Message} " +
                            "If it was already captured, use the refunds endpoint instead.");
                    }
                }
            }
            else
            {
                payment.MarkVoided();
            }

            order.MarkCancelled();
            await _db.SaveChangesAsync(cancellationToken);

            return new OrderDetails(order, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentRefund> RefundOrderAsync(
        int orderId,
        int? amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(400, "An idempotencyKey is required for refunds.");
        }

        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken)
                        ?? throw new PaymentException(404, $"Order {orderId} was not found.");

            var payment = await LoadPaymentAsync(orderId, cancellationToken)
                          ?? throw new PaymentException(409, $"Order {orderId} has no captured payment to refund.");

            if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded) ||
                string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
            {
                throw new PaymentException(409, $"Order {orderId} has not been fulfilled and captured, so it cannot be refunded.");
            }

            var existing = payment.Refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
            if (existing != null)
            {
                return existing;
            }

            var remaining = payment.CapturedAmountCents - payment.RefundedAmountCents;
            if (remaining <= 0)
            {
                throw new PaymentException(409, $"Order {orderId} has already been fully refunded.");
            }

            var refundCents = amountCents ?? remaining;
            if (refundCents <= 0)
            {
                throw new PaymentException(400, "The refund amount must be greater than zero.");
            }

            if (refundCents > remaining)
            {
                throw new PaymentException(400,
                    $"The refund amount {refundCents} cents exceeds the remaining refundable amount of {remaining} cents.");
            }

            var captureTransfer = await _finix.GetTransferAsync(payment.FinixCaptureTransferId, cancellationToken);
            if (string.Equals(captureTransfer.State, "PENDING", StringComparison.OrdinalIgnoreCase) &&
                refundCents < payment.CapturedAmountCents)
            {
                captureTransfer = await WaitForTransferAsync(payment.FinixCaptureTransferId, cancellationToken, TimeSpan.FromSeconds(30));
            }

            if (string.Equals(captureTransfer.State, "PENDING", StringComparison.OrdinalIgnoreCase) &&
                refundCents < payment.CapturedAmountCents)
            {
                throw new PaymentException(409,
                    $"Capture {payment.FinixCaptureTransferId} is still pending at Finix. Partial refunds can be retried once it reaches SUCCEEDED.");
            }

            var finixIdempotencyId = BuildRefundIdempotencyId(order.PaymentReference, idempotencyKey);

            FinixTransferResult reversal;
            try
            {
                reversal = await _finix.CreateReversalAsync(
                    payment.FinixCaptureTransferId,
                    refundCents,
                    finixIdempotencyId,
                    cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw ToPaymentException(ex, "Refund");
            }

            if (string.Equals(reversal.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reversal.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentException(402,
                    $"Finix rejected the refund: {reversal.FailureMessage ?? reversal.State}");
            }

            var refund = payment.AddRefund(reversal.Id, reversal.State, refundCents, idempotencyKey);
            order.MarkRefunded(payment.RefundedAmountCents >= payment.CapturedAmountCents);
            await _db.SaveChangesAsync(cancellationToken);

            return refund;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderDetails>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments
            .AsNoTracking()
            .Include(p => p.Refunds)
            .Where(p => orderIds.Contains(p.OrderId))
            .ToListAsync(cancellationToken);

        return orders
            .Select(o => new OrderDetails(o, payments.FirstOrDefault(p => p.OrderId == o.Id)))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new PaymentException(400, "'to' must be later than 'from'.");
        }

        var finixTransfers = await ListFinixTransfersAsync(from, to, cancellationToken);

        var payments = await _db.OrderPayments
            .AsNoTracking()
            .Include(p => p.Refunds)
            .ToListAsync(cancellationToken);

        var localTransactions = new List<LocalTransaction>();

        foreach (var payment in payments)
        {
            if (!string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId) &&
                payment.CapturedAt.HasValue &&
                payment.CapturedAt.Value >= from &&
                payment.CapturedAt.Value <= to)
            {
                localTransactions.Add(new LocalTransaction(
                    payment.FinixCaptureTransferId,
                    "DEBIT",
                    payment.FinixCaptureState ?? "UNKNOWN",
                    payment.CapturedAmountCents,
                    payment.FeeCents,
                    payment.Currency,
                    payment.CapturedAt.Value,
                    null,
                    payment.OrderId,
                    payment.Id));
            }

            foreach (var refund in payment.Refunds.Where(r => r.CreatedAt >= from && r.CreatedAt <= to))
            {
                localTransactions.Add(new LocalTransaction(
                    refund.FinixReversalId,
                    "REVERSAL",
                    refund.FinixState,
                    refund.AmountCents,
                    0,
                    payment.Currency,
                    refund.CreatedAt,
                    payment.FinixCaptureTransferId,
                    payment.OrderId,
                    payment.Id));
            }
        }

        var matchedLocalIds = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        // Listing can lag a moment behind creation, so confirm any local
        // transaction that the list did not return with a direct fetch.
        var allFinixTransfers = finixTransfers.ToList();
        var finixIds = new HashSet<string>(allFinixTransfers.Select(t => t.Id), StringComparer.Ordinal);
        foreach (var local in localTransactions.Where(t => !finixIds.Contains(t.FinixTransferId)))
        {
            try
            {
                var transfer = await _finix.GetTransferAsync(local.FinixTransferId, cancellationToken);
                allFinixTransfers.Add(transfer);
                finixIds.Add(transfer.Id);
            }
            catch (FinixApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
            }
        }

        foreach (var transfer in allFinixTransfers)
        {
            var local = localTransactions.FirstOrDefault(t => string.Equals(t.FinixTransferId, transfer.Id, StringComparison.Ordinal));
            if (local != null)
            {
                matchedLocalIds.Add(local.FinixTransferId);
            }

            entries.Add(new ReconciliationEntry(
                transfer.Id,
                transfer.Type,
                transfer.State,
                transfer.Amount,
                transfer.Fee,
                transfer.Currency ?? _options.Currency,
                transfer.CreatedAt,
                transfer.ParentTransfer,
                local?.OrderId,
                local?.PaymentId,
                local == null ? "MISSING_IN_ESHOP" : "MATCHED",
                local == null ? "Finix has this transaction but no matching eShop payment record exists." : null));
        }

        foreach (var local in localTransactions.Where(t => !matchedLocalIds.Contains(t.FinixTransferId)))
        {
            entries.Add(new ReconciliationEntry(
                local.FinixTransferId,
                local.Type,
                local.State,
                local.AmountCents,
                local.FeeCents,
                local.Currency,
                local.CreatedAt,
                local.ParentTransferId,
                local.OrderId,
                local.PaymentId,
                "MISSING_IN_FINIX",
                "eShop has this payment record but Finix returned no matching transaction in the requested range."));
        }

        return new ReconciliationReport(from, to, entries.OrderBy(e => e.CreatedAt).ToList());
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardPaymentInput card, CancellationToken cancellationToken = default)
    {
        ValidateCard(card);

        var (firstName, lastName) = SplitName(card.Name);
        var identity = await _finix.CreateBuyerIdentityAsync(
            firstName,
            lastName,
            null,
            null,
            card.BillingAddress,
            cancellationToken);

        var instrument = await _finix.CreatePaymentInstrumentAsync(identity.Id, ToFinixCard(card), cancellationToken);

        var savedCard = new SavedCard(
            buyerId,
            identity.Id,
            instrument.Id,
            instrument.Brand ?? "UNKNOWN",
            instrument.LastFour ?? LastFour(card.Number),
            instrument.ExpirationMonth > 0 ? instrument.ExpirationMonth : card.ExpirationMonth,
            instrument.ExpirationYear > 0 ? instrument.ExpirationYear : card.ExpirationYear,
            card.Name.Trim());

        _db.SavedCards.Add(savedCard);
        await _db.SaveChangesAsync(cancellationToken);

        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _db.SavedCards
            .AsNoTracking()
            .Where(c => c.BuyerId == buyerId && !c.IsDeleted)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _db.SavedCards.FirstOrDefaultAsync(
            c => c.Id == paymentMethodId && c.BuyerId == buyerId,
            cancellationToken);

        if (savedCard == null)
        {
            throw new PaymentException(404, $"Saved payment method {paymentMethodId} was not found.");
        }

        if (savedCard.IsDeleted)
        {
            return;
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(savedCard.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (FinixApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone at Finix; still remove it locally.
        }

        savedCard.MarkDeleted();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Order?> LoadOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    private async Task<OrderPayment?> LoadPaymentAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.OrderPayments
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    private async Task<OrderDetails?> TryRecoverCapturedPaymentAsync(
        Order order,
        OrderPayment payment,
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);
            if (string.IsNullOrWhiteSpace(authorization.TransferId))
            {
                return null;
            }

            var transfer = await WaitForTransferAsync(authorization.TransferId, cancellationToken);
            payment.MarkCaptured(
                transfer.Id,
                transfer.State,
                transfer.Amount,
                transfer.Fee,
                transfer.Amount - transfer.Fee);
            order.MarkFulfilled();
            await _db.SaveChangesAsync(cancellationToken);
            return new OrderDetails(order, payment);
        }
        catch (FinixApiException)
        {
            return null;
        }
    }

    private async Task<FinixAuthorizationResult?> TryGetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (FinixApiException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<FinixTransferResult>> ListFinixTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.ListTransfersAsync(from, to, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw ToPaymentException(ex, "Reconciliation");
        }
    }

    private async Task<FinixTransferResult> WaitForTransferAsync(
        string transferId,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(20));
        var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);

        while (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase) &&
               DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
            transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private static SemaphoreSlim GetOrderLock(int orderId) =>
        OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));

    private static int ToCents(decimal amount) =>
        (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static FinixCard ToFinixCard(CardPaymentInput card) =>
        new(
            card.Name,
            card.Number,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.SecurityCode,
            card.BillingAddress);

    private static string LastFour(string cardNumber)
    {
        var digits = new string(cardNumber.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? digits : digits[^4..];
    }

    private static (string FirstName, string LastName) SplitName(string? name)
    {
        var parts = (name ?? string.Empty)
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var first = parts.Length > 0 ? parts[0] : "eShop";
        var last = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : first;

        return (Truncate(first, 20), Truncate(last, 20));
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static void ValidateCard(CardPaymentInput card)
    {
        if (card == null)
        {
            throw new PaymentException(400, "Card details are required.");
        }

        var digits = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is < 12 or > 19)
        {
            throw new PaymentException(400, "The card number is invalid.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentException(400, "The card expiration month is invalid.");
        }

        var now = DateTimeOffset.UtcNow;
        if (card.ExpirationYear < now.Year ||
            (card.ExpirationYear == now.Year && card.ExpirationMonth < now.Month))
        {
            throw new PaymentException(400, "The card has expired.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentException(400, "The cardholder name is required.");
        }
    }

    private static string BuildNotCapturableMessage(
        int orderId,
        OrderPayment payment,
        FinixAuthorizationResult? authorization = null)
    {
        var state = authorization?.State ?? payment.FinixAuthorizationState ?? payment.Status.ToString();
        var expires = authorization?.ExpiresAt ?? payment.FinixAuthorizationExpiresAt;
        var reason = authorization?.IsVoid == true
            ? "the authorization was voided"
            : expires.HasValue && expires.Value <= DateTimeOffset.UtcNow
                ? $"the authorization expired at {expires.Value:O}"
                : $"the authorization is in state {state}";

        return $"Order {orderId} cannot be captured because {reason} (authorization {payment.FinixAuthorizationId ?? "none"}). " +
               "Cancel the order and have the shopper place/pay for a new order.";
    }

    private static string BuildRefundIdempotencyId(string paymentReference, string idempotencyKey)
    {
        var sanitized = new string(idempotencyKey.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (sanitized.Length == 0)
        {
            sanitized = "key";
        }

        return Truncate($"refund-{paymentReference}-{sanitized}", 100);
    }

    private static PaymentException ToPaymentException(FinixApiException ex, string action)
    {
        var status = ex.StatusCode switch
        {
            HttpStatusCode.BadRequest => 400,
            HttpStatusCode.Unauthorized => 502,
            HttpStatusCode.Forbidden => 502,
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.Conflict => 409,
            HttpStatusCode.UnprocessableEntity => 402,
            HttpStatusCode.PaymentRequired => 402,
            _ => 502
        };

        return new PaymentException(status, $"{action} failed: {ex.Message}");
    }

    private sealed record LocalTransaction(
        string FinixTransferId,
        string Type,
        string State,
        int AmountCents,
        int FeeCents,
        string Currency,
        DateTimeOffset CreatedAt,
        string? ParentTransferId,
        int OrderId,
        int PaymentId);
}
