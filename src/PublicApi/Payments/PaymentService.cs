using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference.Models;
using FinixApiReference.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Application-level payment orchestration: orders, holds, captures, releases, refunds and the
/// reconciliation report. All provider access goes through <see cref="FinixPaymentGateway"/>.
/// </summary>
public sealed class PaymentService
{
    private const string FulfilAction = "FULFIL";
    private const string CancelAction = "CANCEL";

    private readonly CatalogContext _db;
    private readonly FinixPaymentGateway _gateway;
    private readonly PaymentGate _gate;
    private readonly FinixSettings _settings;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        CatalogContext db,
        FinixPaymentGateway gateway,
        PaymentGate gate,
        FinixSettings settings,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _gateway = gateway;
        _gate = gate;
        _settings = settings;
        _logger = logger;
    }

    // --------------------------------------------------------------------- orders

    public async Task<(int OrderId, OrderPaymentView Payment)> CreateOrderAsync(
        string buyerId, IReadOnlyList<OrderItemInput> items, AddressInput? address, CancellationToken ct)
    {
        if (items is null || items.Count == 0)
        {
            throw new PaymentOperationException(400, "At least one catalog item is required.");
        }

        var quantities = new Dictionary<int, int>();
        foreach (var item in items)
        {
            if (item.CatalogItemId <= 0 || item.Quantity <= 0)
            {
                throw new PaymentOperationException(400, "Each item needs a positive catalogItemId and quantity.");
            }
            quantities[item.CatalogItemId] = quantities.TryGetValue(item.CatalogItemId, out var q)
                ? q + item.Quantity
                : item.Quantity;
        }

        var ids = quantities.Keys.ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(ct);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentOperationException(400, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var (catalogItemId, quantity) in quantities)
        {
            var catalogItem = catalogItems.First(c => c.Id == catalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : catalogItem.PictureUri;
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, quantity));
        }

        var shipTo = new Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address(
            ValueOr(address?.Street, "N/A"),
            ValueOr(address?.City, "N/A"),
            ValueOr(address?.State, "N/A"),
            ValueOr(address?.Country, "N/A"),
            ValueOr(address?.ZipCode, "N/A"));

        var order = new Order(buyerId, shipTo, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        var cents = Money.ToCents(order.Total());
        var now = DateTimeOffset.UtcNow;
        var payment = new OrderPayment
        {
            OrderId = order.Id,
            BuyerId = buyerId,
            Currency = _settings.Currency.ToUpperInvariant(),
            Status = PaymentStatus.AwaitingPayment,
            AmountCents = cents,
            IdempotencyId = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(ct);

        return (order.Id, ToView(payment));
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _db.Orders
            .Include(o => o.OrderItems)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(ct);

        var payments = await _db.OrderPayments
            .Where(p => p.BuyerId == buyerId)
            .ToListAsync(ct);

        var reservedRefunds = await _db.RefundRecords
            .Where(r => r.State != "FAILED" && r.State != "CANCELED")
            .GroupBy(r => r.OrderId)
            .Select(g => new { OrderId = g.Key, Amount = g.Sum(r => r.AmountCents) })
            .ToDictionaryAsync(x => x.OrderId, x => x.Amount, ct);

        var byOrder = payments.ToDictionary(p => p.OrderId);
        var result = new List<OrderPaymentView>();

        foreach (var order in orders)
        {
            if (byOrder.TryGetValue(order.Id, out var payment))
            {
                var view = ToView(payment);
                if (reservedRefunds.TryGetValue(payment.OrderId, out var refunded) && refunded > view.RefundedAmountCents)
                {
                    view = view with { RefundedAmountCents = refunded };
                }
                result.Add(view);
            }
            else
            {
                result.Add(new OrderPaymentView(
                    order.Id,
                    _settings.Currency.ToUpperInvariant(),
                    PaymentStatus.AwaitingPayment,
                    PaymentStatus.AwaitingPayment.ToString(),
                    Money.ToCents(order.Total()),
                    null, null, null, null, null, null, null, null, null, 0, null, null, null,
                    order.OrderDate));
            }
        }

        return result;
    }

    // --------------------------------------------------------------------- pay

    public async Task<OrderPaymentView> PayAsync(
        string buyerId, int orderId, CardInput? card, string? savedCardId, CancellationToken ct)
    {
        using var _ = await _gate.LockAsync(orderId, ct);

        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(404, $"Order {orderId} was not found.");
        }

        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is null)
        {
            throw new PaymentOperationException(404, $"Order {orderId} has no payment record.");
        }

        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return ToView(payment);
        }

        if (payment.Status is PaymentStatus.Authorizing or PaymentStatus.Capturing or PaymentStatus.Cancelling)
        {
            throw new PaymentOperationException(409, "A payment operation is already in progress for this order.");
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new PaymentOperationException(409, "This order was cancelled and can no longer be paid.");
        }

        if (payment.Status == PaymentStatus.Unknown)
        {
            if (await TrySettleAuthorizationAsync(payment, ct))
            {
                return ToView(payment);
            }
        }

        var cents = Money.ToCents(order.Total());
        var retryAfterFailure = payment.Status == PaymentStatus.Failed;
        payment.AmountCents = cents;
        payment.Currency = _settings.Currency.ToUpperInvariant();
        payment.FailureCode = null;
        payment.FailureMessage = null;
        payment.LastActionError = null;

        string source;
        if (!string.IsNullOrWhiteSpace(savedCardId))
        {
            var saved = await _db.SavedCards.FirstOrDefaultAsync(
                c => c.Id == savedCardId && c.BuyerId == buyerId, ct);

            if (saved is null || saved.Deleted)
            {
                throw new PaymentOperationException(404, "The saved card was not found.");
            }
            if (!saved.Enabled)
            {
                throw new PaymentOperationException(409, "The saved card is disabled and cannot be used.");
            }

            var instrument = await _gateway.GetCardAsync(saved.FinixPaymentInstrumentId, ct);
            if (instrument.Enabled != true)
            {
                throw new PaymentOperationException(409, "Finix reports that this saved card is no longer usable.");
            }
            if (!string.IsNullOrWhiteSpace(saved.BuyerIdentityId)
                && !string.IsNullOrWhiteSpace(instrument.Identity)
                && !string.Equals(instrument.Identity, saved.BuyerIdentityId, StringComparison.Ordinal))
            {
                throw new PaymentOperationException(409, "The saved card does not belong to this shopper.");
            }

            source = saved.FinixPaymentInstrumentId;
            payment.SavedCardId = saved.Id;
        }
        else if (card is not null)
        {
            ValidateCardInput(card);
            var identityId = await EnsureBuyerIdentityAsync(buyerId, ct);
            var instrument = await _gateway.CreateCardAsync(identityId, card, buyerId, ct);
            source = instrument.Id!;
            payment.SavedCardId = null;
        }
        else
        {
            throw new PaymentOperationException(400, "Supply card details or a saved paymentMethodId.");
        }

        payment.Status = PaymentStatus.Authorizing;
        // Reuse the persisted key for a retry of the same attempt; a previously declined
        // attempt gets a fresh key so the shopper can try again.
        if (string.IsNullOrWhiteSpace(payment.IdempotencyId) || retryAfterFailure)
        {
            payment.IdempotencyId = Guid.NewGuid().ToString("N");
        }
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        Authorization authorization;
        try
        {
            authorization = await _gateway.CreateAuthorizationAsync(
                source, cents, payment.IdempotencyId!, orderId, buyerId, ct);
        }
        catch (PaymentOperationException ex)
        {
            if (ex.OutcomeUnknown)
            {
                payment.Status = PaymentStatus.Unknown;
                payment.LastActionError = ex.Message;
                payment.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);

                if (await TrySettleAuthorizationAsync(payment, ct))
                {
                    return ToView(payment);
                }
            }
            else
            {
                payment.Status = PaymentStatus.Failed;
                payment.FailureCode = ex.ProviderCode;
                payment.FailureMessage = ex.Message;
                payment.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
            throw;
        }

        await ApplyAuthorizationAsync(payment, authorization, cents, ct);
        await _db.SaveChangesAsync(ct);
        return ToView(payment);
    }

    // --------------------------------------------------------------------- fulfil

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        using var _ = await _gate.LockAsync(orderId, ct);

        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is null)
        {
            throw new PaymentOperationException(404, $"Order {orderId} was not found.");
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return ToView(payment);
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new PaymentOperationException(409, "A cancelled order cannot be fulfilled.");
        }

        if (payment.Status == PaymentStatus.Capturing)
        {
            await SettlePendingCaptureAsync(payment, ct);
            return ToView(payment);
        }

        if (payment.Status == PaymentStatus.Unknown)
        {
            if (!string.IsNullOrWhiteSpace(payment.CaptureTransferId))
            {
                await SettlePendingCaptureAsync(payment, ct);
                return ToView(payment);
            }

            if (!string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                var rechecked = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, ct);
                if (!string.IsNullOrWhiteSpace(rechecked.Transfer))
                {
                    await ApplyCaptureAsync(
                        payment, rechecked, payment.AuthorizedAmountCents ?? payment.AmountCents, ct);
                    await _db.SaveChangesAsync(ct);
                    return ToView(payment);
                }
            }

            await ReleaseActionAsync(orderId, FulfilAction, ct);
            payment.Status = PaymentStatus.Authorized;
            payment.LastActionError = null;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw new PaymentOperationException(
                409, $"Order {orderId} is not authorized (current payment state: {payment.Status}). Pay for the order before fulfilling it.");
        }

        await ClaimActionAsync(orderId, FulfilAction, ct);
        payment.Status = PaymentStatus.Capturing;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        Authorization current;
        try
        {
            current = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, ct);
        }
        catch
        {
            await ReleaseActionAsync(orderId, FulfilAction, ct);
            payment.Status = PaymentStatus.Authorized;
            await _db.SaveChangesAsync(ct);
            throw;
        }

        var unusable = DescribeUnusableHold(payment, current);
        if (unusable is not null)
        {
            await ReleaseActionAsync(orderId, FulfilAction, ct);
            payment.Status = PaymentStatus.Authorized;
            payment.LastActionError = unusable;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            throw new PaymentOperationException(409, unusable);
        }

        var captureAmount = current.Amount ?? payment.AuthorizedAmountCents ?? payment.AmountCents;
        Authorization captured;
        try
        {
            captured = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId!, captureAmount, ct);
        }
        catch (PaymentOperationException ex)
        {
            if (ex.OutcomeUnknown)
            {
                payment.Status = PaymentStatus.Unknown;
                payment.LastActionError = ex.Message;
                payment.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);

                try
                {
                    var reread = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, ct);
                    if (!string.IsNullOrWhiteSpace(reread.Transfer))
                    {
                        captured = reread;
                        await ApplyCaptureAsync(payment, captured, captureAmount, ct);
                        await _db.SaveChangesAsync(ct);
                        return ToView(payment);
                    }
                }
                catch (PaymentOperationException)
                {
                    // leave as Unknown; the report exposes it for an operator sweep
                }
                throw;
            }

            await ReleaseActionAsync(orderId, FulfilAction, ct);
            payment.Status = PaymentStatus.Authorized;
            payment.LastActionError = ex.Message;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            throw;
        }

        await ApplyCaptureAsync(payment, captured, captureAmount, ct);
        await _db.SaveChangesAsync(ct);
        return ToView(payment);
    }

    // --------------------------------------------------------------------- cancel

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        using var _ = await _gate.LockAsync(orderId, ct);

        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is null)
        {
            throw new PaymentOperationException(404, $"Order {orderId} was not found.");
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return ToView(payment);
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentOperationException(
                409, "This order has already been fulfilled; use the refunds endpoint to return the money.");
        }

        if (payment.Status is PaymentStatus.AwaitingPayment or PaymentStatus.Failed)
        {
            payment.Status = PaymentStatus.Cancelled;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return ToView(payment);
        }

        if (payment.Status == PaymentStatus.Cancelling)
        {
            await SettlePendingCancelAsync(payment, ct);
            return ToView(payment);
        }

        if (payment.Status == PaymentStatus.Unknown)
        {
            if (!string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                var current = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, ct);
                if (current.IsVoid == true || current.VoidState == VoidState.Succeeded)
                {
                    payment.Status = PaymentStatus.Cancelled;
                    payment.LastActionError = null;
                    payment.UpdatedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync(ct);
                    return ToView(payment);
                }
            }

            await ReleaseActionAsync(orderId, CancelAction, ct);
            payment.Status = PaymentStatus.Authorized;
            payment.LastActionError = null;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw new PaymentOperationException(
                409, $"Order {orderId} has no held funds to release (current payment state: {payment.Status}).");
        }

        await ClaimActionAsync(orderId, CancelAction, ct);
        payment.Status = PaymentStatus.Cancelling;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        try
        {
            await _gateway.VoidAuthorizationAsync(payment.AuthorizationId!, ct);
        }
        catch (PaymentOperationException ex)
        {
            if (!ex.OutcomeUnknown)
            {
                await ReleaseActionAsync(orderId, CancelAction, ct);
                payment.Status = PaymentStatus.Authorized;
                payment.LastActionError = ex.Message;
                payment.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                throw;
            }

            try
            {
                var reread = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, ct);
                if (reread.IsVoid == true || reread.VoidState == VoidState.Succeeded)
                {
                    payment.Status = PaymentStatus.Cancelled;
                    payment.UpdatedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync(ct);
                    return ToView(payment);
                }
            }
            catch (PaymentOperationException)
            {
                // outcome stays unknown; report it rather than pretending the funds were released
            }

            payment.Status = PaymentStatus.Unknown;
            payment.LastActionError = ex.Message;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            throw;
        }

        await SettlePendingCancelAsync(payment, ct);
        return ToView(payment);
    }

    // --------------------------------------------------------------------- refunds

    public async Task<RefundView> RefundAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentOperationException(400, "An idempotency key is required for refunds.");
        }
        idempotencyKey = idempotencyKey.Trim();

        using var _ = await _gate.LockAsync(orderId, ct);

        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == orderId, ct);
        if (payment is null || !string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(404, $"Order {orderId} was not found.");
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
        {
            throw new PaymentOperationException(
                409, $"Only a fulfilled (captured) order can be refunded (current payment state: {payment.Status}).");
        }

        if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            throw new PaymentOperationException(409, "The captured transfer id is missing for this order.");
        }

        var existing = await _db.RefundRecords.FirstOrDefaultAsync(
            r => r.OrderId == orderId && r.IdempotencyKey == idempotencyKey, ct);

        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(existing.RefundId))
            {
                return ToRefundView(existing);
            }

            if (existing.State is "PENDING" or "UNKNOWN")
            {
                if (await TrySettleRefundAsync(existing, payment, ct))
                {
                    await _db.SaveChangesAsync(ct);
                    return ToRefundView(existing);
                }
            }

            throw new PaymentOperationException(
                409, $"A refund under this idempotency key already exists in state {existing.State}.");
        }

        var captured = payment.CapturedAmountCents ?? payment.AmountCents;
        var already = await RefundedOrReservedAsync(payment, ct);
        var remaining = captured - already;

        long requestedCents;
        if (amount is null)
        {
            if (remaining <= 0)
            {
                throw new PaymentOperationException(400, "This order has already been fully refunded.");
            }
            requestedCents = remaining;
        }
        else
        {
            requestedCents = Money.ToCents(amount.Value);
        }

        if (requestedCents > remaining)
        {
            throw new PaymentOperationException(
                400, $"Refund of {requestedCents} cents exceeds the refundable balance of {remaining} cents.");
        }

        if (requestedCents < captured)
        {
            // Finix accepts a full reversal while the capture transfer is still PENDING, but a
            // partial reversal only once it has succeeded. Surface that as an actionable message
            // rather than a raw provider rejection.
            var transfer = await _gateway.GetTransferAsync(payment.CaptureTransferId!, ct);
            payment.CaptureState = transfer.State?.Value;

            if (transfer.State == State11.Pending)
            {
                throw new PaymentOperationException(
                    409,
                    "The capture is still settling at Finix (state PENDING). Partial refunds become available once it succeeds; " +
                    "you can refund the full captured amount now.");
            }

            if (transfer.State != State11.Succeeded)
            {
                throw new PaymentOperationException(
                    409, $"The capture is in state {transfer.State?.Value ?? "unknown"} and cannot be partially refunded.");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var record = new RefundRecord
        {
            OrderId = orderId,
            IdempotencyKey = idempotencyKey,
            CaptureTransferId = payment.CaptureTransferId!,
            AmountCents = requestedCents,
            State = "PENDING",
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.RefundRecords.Add(record);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.Entry(record).State = EntityState.Detached;
            var raced = await _db.RefundRecords.FirstOrDefaultAsync(
                r => r.OrderId == orderId && r.IdempotencyKey == idempotencyKey, ct);
            if (raced is not null)
            {
                return ToRefundView(raced);
            }
            throw new PaymentOperationException(409, "A concurrent refund under this idempotency key was detected.");
        }

        Transfer reversal;
        try
        {
            reversal = await _gateway.CreateReversalAsync(payment.CaptureTransferId!, requestedCents, ct);
        }
        catch (PaymentOperationException ex)
        {
            if (ex.OutcomeUnknown)
            {
                record.State = "UNKNOWN";
                record.FailureMessage = ex.Message;
                record.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);

                if (await TrySettleRefundAsync(record, payment, ct))
                {
                    await _db.SaveChangesAsync(ct);
                    return ToRefundView(record);
                }
            }
            else
            {
                record.State = "FAILED";
                record.FailureMessage = ex.Message;
                record.UpdatedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
            throw;
        }

        await ApplyReversalAsync(record, payment, reversal, requestedCents, ct);
        await _db.SaveChangesAsync(ct);
        return ToRefundView(record);
    }

    // --------------------------------------------------------------------- saved cards

    public async Task<SavedCardView> CreateSavedCardAsync(string buyerId, CardInput card, CancellationToken ct)
    {
        ValidateCardInput(card);
        var identityId = await EnsureBuyerIdentityAsync(buyerId, ct);
        var instrument = await _gateway.CreateCardAsync(identityId, card, buyerId, ct);

        var saved = new SavedCard
        {
            Id = Guid.NewGuid().ToString("N"),
            BuyerId = buyerId,
            FinixPaymentInstrumentId = instrument.Id!,
            BuyerIdentityId = identityId,
            Brand = instrument.Brand?.Value,
            LastFour = instrument.LastFour,
            ExpirationMonth = instrument.ExpirationMonth ?? card.ExpirationMonth,
            ExpirationYear = instrument.ExpirationYear ?? card.ExpirationYear,
            Name = instrument.Name ?? card.Name,
            Enabled = instrument.Enabled ?? true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.SavedCards.Add(saved);
        await _db.SaveChangesAsync(ct);
        return ToCardView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken ct)
    {
        var cards = await _db.SavedCards
            .Where(c => c.BuyerId == buyerId && !c.Deleted)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return cards.Select(ToCardView).ToList();
    }

    public async Task DeleteSavedCardAsync(string buyerId, string paymentMethodId, CancellationToken ct)
    {
        var card = await _db.SavedCards.FirstOrDefaultAsync(
            c => c.Id == paymentMethodId && c.BuyerId == buyerId, ct);

        if (card is null || card.Deleted)
        {
            throw new PaymentOperationException(404, "The saved card was not found.");
        }

        try
        {
            await _gateway.DisableCardAsync(card.FinixPaymentInstrumentId, ct);
        }
        catch (PaymentOperationException ex)
        {
            // There is no provider-side delete operation. The local tombstone below is what makes
            // the card unusable here; record the provider failure for operators.
            _logger.LogWarning(ex, "Finix could not disable saved card {PaymentMethodId}; tombstoning locally.", card.Id);
        }

        card.Deleted = true;
        card.Enabled = false;
        card.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // --------------------------------------------------------------------- reconciliation

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
        {
            throw new PaymentOperationException(400, "'to' must be on or after 'from'.");
        }

        var (authorizations, authTruncated) = await _gateway.ListAuthorizationsAsync(from, to, ct);
        var (transfers, transferTruncated) = await _gateway.ListTransfersAsync(from, to, null, ct);

        var payments = await _db.OrderPayments.ToListAsync(ct);
        var refunds = await _db.RefundRecords.ToListAsync(ct);

        var finixEntries = new List<ReconciliationEntry>();

        foreach (var authorization in authorizations)
        {
            var orderId = ResolveOrderId(authorization, payments);
            finixEntries.Add(new ReconciliationEntry(
                "AUTHORIZATION",
                authorization.Id ?? string.Empty,
                orderId,
                authorization.Amount,
                authorization.State?.Value,
                authorization.CreatedAt,
                orderId is null ? "FINIX_ONLY" : "MATCHED",
                authorization.FailureMessage));
        }

        foreach (var transfer in transfers)
        {
            var orderId = ResolveOrderId(transfer, payments, refunds);
            finixEntries.Add(new ReconciliationEntry(
                transfer.Type?.Value ?? "TRANSFER",
                transfer.Id ?? string.Empty,
                orderId,
                transfer.Amount,
                transfer.State?.Value,
                transfer.CreatedAt,
                orderId is null ? "FINIX_ONLY" : "MATCHED",
                transfer.FailureMessage));
        }

        var matchedOrderIds = finixEntries
            .Where(e => e.OrderId is not null)
            .Select(e => e.OrderId!)
            .ToHashSet(StringComparer.Ordinal);

        var eshopEntries = payments
            .Where(p => p.CreatedAt >= from && p.CreatedAt <= to)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ReconciliationEntry(
                "ESHOP_ORDER",
                p.AuthorizationId ?? p.CaptureTransferId ?? string.Empty,
                p.OrderId.ToString(),
                p.AmountCents,
                p.Status.ToString(),
                p.CreatedAt,
                matchedOrderIds.Contains(p.OrderId.ToString()) ? "MATCHED" : "ESHOP_ONLY",
                p.FailureMessage ?? p.LastActionError))
            .ToList();

        return new ReconciliationReport(
            from,
            to,
            authTruncated || transferTruncated,
            finixEntries,
            eshopEntries);
    }

    // --------------------------------------------------------------------- internals

    private async Task<Authorization> ApplyAuthorizationAsync(
        OrderPayment payment, Authorization authorization, long expectedCents, CancellationToken ct)
    {
        payment.AuthorizationId = authorization.Id;
        payment.AuthorizationState = authorization.State?.Value;
        payment.AuthorizationExpiresAt = authorization.ExpiresAt;
        payment.AuthorizedAmountCents = authorization.Amount ?? expectedCents;
        payment.UpdatedAt = DateTimeOffset.UtcNow;

        if (authorization.Amount is { } held && held != expectedCents)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = "AMOUNT_MISMATCH";
            payment.FailureMessage =
                $"Finix held {held} cents but the order total is {expectedCents} cents.";

            if (!string.IsNullOrWhiteSpace(authorization.Id))
            {
                try
                {
                    await _gateway.VoidAuthorizationAsync(authorization.Id!, ct);
                }
                catch (PaymentOperationException ex)
                {
                    _logger.LogWarning(ex, "Could not release mismatched authorization {AuthorizationId}.", authorization.Id);
                }
            }

            throw new PaymentOperationException(502, payment.FailureMessage);
        }

        if (authorization.State == State1.Failed || authorization.State == State1.Canceled)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = authorization.FailureCode;
            payment.FailureMessage = authorization.FailureMessage ?? "Finix declined the authorization.";
            throw new PaymentOperationException(
                402, payment.FailureMessage, providerCode: payment.FailureCode);
        }

        payment.Status = PaymentStatus.Authorized;
        payment.FailureCode = authorization.FailureCode;
        payment.FailureMessage = authorization.FailureMessage;
        return authorization;
    }

    private async Task ApplyCaptureAsync(
        OrderPayment payment, Authorization captured, long requestedCents, CancellationToken ct)
    {
        var transferId = captured.Transfer;
        if (string.IsNullOrWhiteSpace(transferId))
        {
            var refreshed = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, ct);
            transferId = refreshed.Transfer;
        }

        if (string.IsNullOrWhiteSpace(transferId))
        {
            payment.Status = PaymentStatus.Unknown;
            payment.LastActionError = "Finix did not return the capture transfer id.";
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            throw new PaymentOperationException(
                502, "Finix captured the authorization but did not return a transfer id; the outcome is unknown.",
                outcomeUnknown: true);
        }

        var transfer = await _gateway.GetTransferAsync(transferId, ct);
        payment.CaptureTransferId = transferId;
        payment.CaptureState = transfer.State?.Value;
        payment.CapturedAmountCents = transfer.Amount ?? requestedCents;
        payment.FeeCents = transfer.Fee;
        payment.NetCents = payment.CapturedAmountCents - (transfer.Fee ?? 0);
        payment.UpdatedAt = DateTimeOffset.UtcNow;

        if (transfer.State == State11.Succeeded || transfer.State == State11.Pending)
        {
            // Finix has accepted and captured the transfer. A PENDING transfer is awaiting
            // settlement, not awaiting capture, so the order is fulfilled either way; the
            // provider state stays visible in CaptureState.
            payment.Status = PaymentStatus.Captured;
            payment.LastActionError = null;
            return;
        }

        payment.Status = PaymentStatus.Failed;
        payment.FailureCode = transfer.FailureCode;
        payment.FailureMessage = transfer.FailureMessage ?? $"Capture transfer is {transfer.State?.Value ?? "unknown"}.";
        throw new PaymentOperationException(
            402, payment.FailureMessage, providerCode: payment.FailureCode);
    }

    private async Task SettlePendingCaptureAsync(OrderPayment payment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payment.CaptureTransferId)) return;

        var transfer = await _gateway.GetTransferAsync(payment.CaptureTransferId, ct);
        payment.CaptureState = transfer.State?.Value;
        payment.CapturedAmountCents = transfer.Amount ?? payment.CapturedAmountCents;
        payment.FeeCents = transfer.Fee;
        payment.NetCents = (payment.CapturedAmountCents ?? payment.AmountCents) - (transfer.Fee ?? 0);
        payment.UpdatedAt = DateTimeOffset.UtcNow;

        if (transfer.State == State11.Succeeded || transfer.State == State11.Pending)
        {
            payment.Status = PaymentStatus.Captured;
            payment.LastActionError = null;
        }
        else if (transfer.State == State11.Failed || transfer.State == State11.Canceled)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = transfer.FailureCode;
            payment.FailureMessage = transfer.FailureMessage;
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task SettlePendingCancelAsync(OrderPayment payment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payment.AuthorizationId)) return;

        var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, ct);
        payment.AuthorizationState = authorization.State?.Value;
        payment.UpdatedAt = DateTimeOffset.UtcNow;

        if (authorization.IsVoid == true || authorization.VoidState == VoidState.Succeeded)
        {
            payment.Status = PaymentStatus.Cancelled;
            payment.LastActionError = null;
            await _db.SaveChangesAsync(ct);
            return;
        }

        payment.Status = PaymentStatus.Authorized;
        payment.LastActionError =
            $"Finix has not confirmed the release (void state: {authorization.VoidState?.Value ?? "unknown"}).";
        await _db.SaveChangesAsync(ct);
        throw new PaymentOperationException(409, payment.LastActionError);
    }

    private async Task ApplyReversalAsync(
        RefundRecord record, OrderPayment payment, Transfer reversal, long requestedCents, CancellationToken ct)
    {
        record.RefundId = reversal.Id ?? string.Empty;
        record.State = reversal.State?.Value ?? "UNKNOWN";
        record.FailureMessage = reversal.FailureMessage;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(record.RefundId))
        {
            record.State = "UNKNOWN";
            throw new PaymentOperationException(
                502, "Finix accepted the refund but returned no reversal id; the outcome is unknown.",
                outcomeUnknown: true);
        }

        if (reversal.State == State11.Succeeded)
        {
            var refunded = reversal.Amount ?? requestedCents;
            payment.RefundedAmountCents += refunded;
            var captured = payment.CapturedAmountCents ?? payment.AmountCents;
            payment.Status = payment.RefundedAmountCents >= captured
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            return;
        }

        if (reversal.State == State11.Failed || reversal.State == State11.Canceled)
        {
            record.State = "FAILED";
            payment.LastActionError = record.FailureMessage ?? "Finix rejected the refund.";
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            throw new PaymentOperationException(
                402, payment.LastActionError, providerCode: reversal.FailureCode);
        }

        // Pending: the refund is reserved but not yet counted as returned money.
        payment.LastActionError = null;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<bool> TrySettleAuthorizationAsync(OrderPayment payment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payment.IdempotencyId)) return false;

        var authorization = await _gateway.FindAuthorizationByIdempotencyIdAsync(payment.IdempotencyId!, ct);
        if (authorization is null) return false;

        try
        {
            await ApplyAuthorizationAsync(payment, authorization, payment.AmountCents, ct);
        }
        catch (PaymentOperationException)
        {
            await _db.SaveChangesAsync(ct);
            throw;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> TrySettleRefundAsync(
        RefundRecord record, OrderPayment payment, CancellationToken ct)
    {
        var (reversals, _) = await _gateway.ListReversalsAsync(record.CaptureTransferId, ct);
        var claimed = await _db.RefundRecords
            .Where(r => r.OrderId == record.OrderId && r.IdempotencyKey != record.IdempotencyKey)
            .Select(r => r.RefundId)
            .ToListAsync(ct);

        var match = reversals
            .Where(t => t.Amount == record.AmountCents)
            .Where(t => t.CreatedAt is null || t.CreatedAt >= record.CreatedAt.AddMinutes(-10))
            .Where(t => !claimed.Contains(t.Id ?? string.Empty))
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefault();

        if (match is null) return false;

        record.RefundId = match.Id ?? string.Empty;
        record.State = match.State?.Value ?? "UNKNOWN";
        record.FailureMessage = match.FailureMessage;
        record.UpdatedAt = DateTimeOffset.UtcNow;

        if (match.State == State11.Succeeded)
        {
            payment.RefundedAmountCents += match.Amount ?? record.AmountCents;
            var captured = payment.CapturedAmountCents ?? payment.AmountCents;
            payment.Status = payment.RefundedAmountCents >= captured
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return true;
    }

    private async Task<long> RefundedOrReservedAsync(OrderPayment payment, CancellationToken ct)
    {
        var persisted = await _db.RefundRecords
            .Where(r => r.OrderId == payment.OrderId && r.State != "FAILED" && r.State != "CANCELED")
            .SumAsync(r => (long?)r.AmountCents, ct) ?? 0L;

        var (reversals, reversalsTruncated) = await _gateway.ListReversalsAsync(payment.CaptureTransferId!, ct);
        if (reversalsTruncated)
        {
            throw new PaymentOperationException(
                503, "Could not read the full refund history from Finix, so the refundable balance is unknown. Try again.");
        }
        var atFinix = reversals
            .Where(t => t.State == State11.Succeeded || t.State == State11.Pending)
            .Sum(t => t.Amount ?? 0);

        return Math.Max(persisted, atFinix);
    }

    private async Task<string> EnsureBuyerIdentityAsync(string buyerId, CancellationToken ct)
    {
        var existing = await _db.ShopperIdentities.FirstOrDefaultAsync(i => i.BuyerId == buyerId, ct);
        if (existing is not null) return existing.FinixIdentityId;

        var (found, identitiesTruncated) = await _gateway.FindBuyerIdentitiesAsync(buyerId, ct);
        var identityId = found.FirstOrDefault()?.Id;

        if (string.IsNullOrWhiteSpace(identityId) && identitiesTruncated)
        {
            throw new PaymentOperationException(
                503, "Could not determine whether this shopper already has a Finix identity. Try again.");
        }

        if (string.IsNullOrWhiteSpace(identityId))
        {
            var created = await _gateway.CreateBuyerIdentityAsync(buyerId, EmailFor(buyerId), ct);
            identityId = created.Id;
        }

        if (string.IsNullOrWhiteSpace(identityId))
        {
            throw new PaymentOperationException(502, "Finix did not return an identity id.");
        }

        _db.ShopperIdentities.Add(new ShopperIdentity
        {
            BuyerId = buyerId,
            FinixIdentityId = identityId!,
            CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            var raced = await _db.ShopperIdentities.AsNoTracking()
                .FirstOrDefaultAsync(i => i.BuyerId == buyerId, ct);
            if (raced is not null) return raced.FinixIdentityId;
            throw;
        }

        return identityId!;
    }

    private async Task ClaimActionAsync(int orderId, string action, CancellationToken ct)
    {
        var existing = await _db.ActionClaims
            .FirstOrDefaultAsync(c => c.OrderId == orderId && c.Action == action, ct);
        if (existing is not null)
        {
            throw new PaymentOperationException(409, $"A {action.ToLowerInvariant()} request is already in progress.");
        }

        _db.ActionClaims.Add(new ActionClaim
        {
            OrderId = orderId,
            Action = action,
            CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new PaymentOperationException(409, $"A {action.ToLowerInvariant()} request is already in progress.");
        }
    }

    private async Task ReleaseActionAsync(int orderId, string action, CancellationToken ct)
    {
        var claim = await _db.ActionClaims
            .FirstOrDefaultAsync(c => c.OrderId == orderId && c.Action == action, ct);
        if (claim is null) return;

        _db.ActionClaims.Remove(claim);
        await _db.SaveChangesAsync(ct);
    }

    private static string? DescribeUnusableHold(OrderPayment payment, Authorization authorization)
    {
        if (authorization.IsVoid == true)
        {
            return $"The authorization {authorization.Id} was voided and can no longer be captured. Cancel the order or take a new payment.";
        }

        if (authorization.State != State1.Succeeded)
        {
            return $"The authorization {authorization.Id} is in state {authorization.State?.Value ?? "unknown"} and can no longer be captured. Cancel the order or take a new payment.";
        }

        if (authorization.ExpiresAt is { } expires && expires < DateTimeOffset.UtcNow)
        {
            return $"The authorization {authorization.Id} expired at {expires:O} and can no longer be captured. Cancel the order or take a new payment.";
        }

        return null;
    }

    private static string? ResolveOrderId(
        Authorization authorization, IReadOnlyList<OrderPayment> payments)
    {
        var tag = GetTag(authorization.Tags, "order_id");
        if (tag is not null) return tag;

        return payments.FirstOrDefault(p =>
            string.Equals(p.AuthorizationId, authorization.Id, StringComparison.Ordinal))?.OrderId.ToString();
    }

    private static string? ResolveOrderId(
        Transfer transfer, IReadOnlyList<OrderPayment> payments, IReadOnlyList<RefundRecord> refunds)
    {
        var direct = payments.FirstOrDefault(p =>
            string.Equals(p.CaptureTransferId, transfer.Id, StringComparison.Ordinal));
        if (direct is not null) return direct.OrderId.ToString();

        var refund = refunds.FirstOrDefault(r =>
            string.Equals(r.RefundId, transfer.Id, StringComparison.Ordinal));
        if (refund is not null) return refund.OrderId.ToString();

        if (!string.IsNullOrWhiteSpace(transfer.ParentTransfer))
        {
            var parent = payments.FirstOrDefault(p =>
                string.Equals(p.CaptureTransferId, transfer.ParentTransfer, StringComparison.Ordinal));
            if (parent is not null) return parent.OrderId.ToString();

            var parentRefund = refunds.FirstOrDefault(r =>
                string.Equals(r.CaptureTransferId, transfer.ParentTransfer, StringComparison.Ordinal));
            if (parentRefund is not null) return parentRefund.OrderId.ToString();
        }

        return GetTag(transfer.Tags, "order_id");
    }

    private static string? GetTag(object? tags, string key)
    {
        if (tags is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(key, out var value))
            {
                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            }
        }

        if (tags is IReadOnlyDictionary<string, string> map && map.TryGetValue(key, out var text))
        {
            return text;
        }

        return null;
    }

    private static void ValidateCardInput(CardInput card)
    {
        if (string.IsNullOrWhiteSpace(card.Number))
        {
            throw new PaymentOperationException(400, "Card number is required.");
        }
        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentOperationException(400, "Cardholder name is required.");
        }
        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentOperationException(400, "Expiration month must be between 1 and 12.");
        }
        if (card.ExpirationYear < 2000)
        {
            throw new PaymentOperationException(400, "Expiration year is invalid.");
        }
    }

    private static string EmailFor(string buyerId)
        => buyerId.Contains('@', StringComparison.Ordinal)
            ? buyerId
            : $"{buyerId}@shoppers.eshoponweb.local";

    private static string ValueOr(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value!;

    private static OrderPaymentView ToView(OrderPayment p) => new(
        p.OrderId,
        p.Currency,
        p.Status,
        p.Status.ToString(),
        p.AmountCents,
        p.AuthorizationId,
        p.AuthorizationState,
        p.AuthorizedAmountCents,
        p.AuthorizationExpiresAt,
        p.CaptureTransferId,
        p.CaptureState,
        p.CapturedAmountCents,
        p.FeeCents,
        p.NetCents,
        p.RefundedAmountCents,
        p.SavedCardId,
        p.FailureCode,
        p.FailureMessage,
        p.CreatedAt);

    private static RefundView ToRefundView(RefundRecord r) => new(
        r.RefundId,
        r.OrderId,
        r.AmountCents,
        r.State,
        r.CreatedAt);

    private static SavedCardView ToCardView(SavedCard c) => new(
        c.Id,
        c.Brand,
        c.LastFour,
        c.ExpirationMonth,
        c.ExpirationYear,
        c.Name,
        c.Enabled);
}
