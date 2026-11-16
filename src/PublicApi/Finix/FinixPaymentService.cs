using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Errors;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinixAddressModel = FinixApiReference.Models.Address;
using FinixCountryEnum = FinixApiReference.Models.Enums.Country;
using FinixCurrencyEnum = FinixApiReference.Models.Enums.Currency;

namespace Microsoft.eShopWeb.PublicApi.Finix;

/// <summary>
/// Owns the Finix interactions and the payment state that survives between requests.
/// Endpoints are thin; every money-moving operation and its idempotency guard lives here.
/// </summary>
public class FinixPaymentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ClaimGates = new();
    private static readonly TimeSpan ClaimLifetime = TimeSpan.FromMinutes(15);

    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _settings;
    private readonly CatalogContext _db;
    private readonly IDbContextFactory<CatalogContext> _dbFactory;
    private readonly ILogger<FinixPaymentService> _logger;

    public FinixPaymentService(FinixApiReferenceClient client, IOptions<FinixOptions> settings,
        CatalogContext db, IDbContextFactory<CatalogContext> dbFactory,
        ILogger<FinixPaymentService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _db = db;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public static long ToCents(decimal amount) =>
        checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    // ---------------------------------------------------------------------
    // Orders
    // ---------------------------------------------------------------------

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines,
        AddressDetails? shipTo, CancellationToken ct)
    {
        if (lines.Count == 0)
        {
            throw new PaymentException("An order needs at least one catalog item.", 400);
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least 1.", 400);
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToList();
        var catalogItems = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        if (catalogItems.Count != ids.Count)
        {
            throw new PaymentException("One or more catalog items were not found.", 400);
        }

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.Single(i => i.Id == line.CatalogItemId);
            var picture = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : catalogItem.PictureUri;
            items.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, picture),
                catalogItem.Price,
                line.Quantity));
        }

        var address = shipTo == null
            ? new Address("1 Main St", "Seattle", "WA", "USA", "98101")
            : new Address(shipTo.Line1, shipTo.City, shipTo.Region, shipTo.Country, shipTo.PostalCode);

        var order = new Order(buyerId, address, items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        _db.OrderPayments.Add(new OrderPayment(order.Id, buyerId, _settings.Currency, ToCents(order.Total())));
        await _db.SaveChangesAsync(ct);

        return order;
    }

    public async Task<Order> GetOrderAsync(int orderId, string? requiredBuyerId, CancellationToken ct)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order == null || (requiredBuyerId != null &&
                              !string.Equals(order.BuyerId, requiredBuyerId, StringComparison.Ordinal)))
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    public async Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _db.Orders
            .Include(o => o.OrderItems)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(ct);

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments
            .Where(p => orderIds.Contains(p.OrderId))
            .ToDictionaryAsync(p => p.OrderId, ct);
        var refunds = await _db.PaymentRefunds
            .Where(r => orderIds.Contains(r.OrderId))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        return orders.Select(order =>
        {
            payments.TryGetValue(order.Id, out var payment);
            var orderRefunds = refunds.Where(r => r.OrderId == order.Id).ToList();
            return new MyOrderView(
                order.Id,
                order.OrderDate,
                order.Status,
                order.Total(),
                payment?.Currency ?? _settings.Currency,
                order.OrderItems
                    .Select(i => new MyOrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName,
                        i.UnitPrice, i.Units))
                    .ToList(),
                payment == null ? null : ToView(payment, orderRefunds));
        }).ToList();
    }

    // ---------------------------------------------------------------------
    // Saved cards
    // ---------------------------------------------------------------------

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        var profile = await EnsureIdentityAsync(buyerId, ct);
        var instrument = await CreatePaymentInstrumentAsync(profile.FinixIdentityId, card, ct);

        var saved = new SavedCard(instrument.Id!, buyerId, profile.FinixIdentityId,
            instrument.LastFour, instrument.Brand?.Value,
            instrument.ExpirationMonth, instrument.ExpirationYear);

        _db.SavedCards.Add(saved);
        await _db.SaveChangesAsync(ct);

        return ToView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> ListCardsAsync(string buyerId, CancellationToken ct)
    {
        var cards = await _db.SavedCards
            .Where(c => c.BuyerId == buyerId && c.Enabled)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return cards.Select(ToView).ToList();
    }

    public async Task DeleteCardAsync(string buyerId, string paymentMethodId, CancellationToken ct)
    {
        var card = await _db.SavedCards
            .FirstOrDefaultAsync(c => c.PaymentInstrumentId == paymentMethodId && c.BuyerId == buyerId, ct);

        if (card == null)
        {
            throw new PaymentException($"Payment method {paymentMethodId} was not found.", 404);
        }

        if (!card.Enabled)
        {
            return;
        }

        var lease = await TryAcquireClaimAsync($"card-delete:{buyerId}:{paymentMethodId}", ct);
        if (lease == null)
        {
            throw new PaymentException("A request to remove this card is already in progress.", 409);
        }

        try
        {
            await DisablePaymentInstrumentAsync(paymentMethodId, ct);
            card.Disable();
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            lease.ReleaseClaim();
            throw;
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------
    // Pay (authorize)
    // ---------------------------------------------------------------------

    public async Task<OrderPayment> PayAsync(Order order, string buyerId, string? savedPaymentMethodId,
        CardDetails? card, CancellationToken ct)
    {
        var payment = await GetOrCreatePaymentAsync(order, buyerId, ct);

        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }

        if (payment.Status == PaymentStatus.Cancelled || order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("This order was cancelled and can no longer be paid.", 409);
        }

        if (payment.Status == PaymentStatus.Unknown)
        {
            throw new PaymentException(
                "A previous payment attempt for this order has an unknown outcome. Reconcile the payment before retrying.",
                409);
        }

        var lease = await TryAcquireClaimAsync($"pay:{order.Id}", ct);
        if (lease == null)
        {
            var existing = await _db.OrderPayments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == order.Id, ct);
            if (existing != null && existing.Status is PaymentStatus.Authorized or PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return existing;
            }

            throw new PaymentException("A payment for this order is already in progress.", 409);
        }

        try
        {
            var profile = await EnsureIdentityAsync(buyerId, ct);

            string sourceInstrumentId;
            if (!string.IsNullOrWhiteSpace(savedPaymentMethodId))
            {
                sourceInstrumentId = await ResolveSavedCardAsync(buyerId, profile.FinixIdentityId,
                    savedPaymentMethodId, ct);
            }
            else if (card != null)
            {
                var instrument = await CreatePaymentInstrumentAsync(profile.FinixIdentityId, card, ct);
                sourceInstrumentId = instrument.Id!;
            }
            else
            {
                throw new PaymentException(
                    "Supply either card details or the id of one of the shopper's saved cards.", 400);
            }

            var amountCents = ToCents(order.Total());
            var idempotencyKey = $"eshop-pay-{order.Id}-{Guid.NewGuid():N}";
            payment.SetPendingAttempt(profile.FinixIdentityId, sourceInstrumentId, idempotencyKey);
            await _db.SaveChangesAsync(ct);

            var authorization = await AuthorizeAsync(amountCents, sourceInstrumentId, idempotencyKey, ct);

            if (authorization.State == FinixApiReference.Models.Enums.State1.Succeeded)
            {
                payment.SetAuthorized(profile.FinixIdentityId, sourceInstrumentId, authorization.Id!,
                    authorization.State?.Value, authorization.Amount ?? amountCents,
                    authorization.IsVoid, authorization.VoidState?.Value,
                    authorization.ExpiresAt, idempotencyKey);
                order.MarkAuthorized();
                await _db.SaveChangesAsync(ct);
                return payment;
            }

            payment.SetAuthorizationFailed(authorization.Id, authorization.State?.Value,
                authorization.FailureCode, authorization.FailureMessage);
            order.MarkAwaitingPayment();
            await _db.SaveChangesAsync(ct);
            lease.ReleaseClaim();

            throw new PaymentException(
                authorization.FailureMessage ??
                $"The card was not authorized (state {authorization.State?.Value ?? "UNKNOWN"}).",
                402, authorization.FailureCode);
        }
        catch (PaymentException ex) when (ex.OutcomeUnknown)
        {
            payment.SetUnknown(ex.Message);
            await _db.SaveChangesAsync(ct);
            throw;
        }
        catch (PaymentException)
        {
            lease.ReleaseClaim();
            throw;
        }
        catch
        {
            lease.ReleaseClaim();
            throw;
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------
    // Fulfil (capture)
    // ---------------------------------------------------------------------

    public async Task<OrderPayment> FulfilAsync(Order order, CancellationToken ct)
    {
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == order.Id, ct);
        if (payment == null)
        {
            throw new PaymentException("This order has no payment record.", 409);
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            await RefreshCaptureDetailsAsync(payment, ct);
            return payment;
        }

        if (payment.Status == PaymentStatus.Cancelled || order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("This order was cancelled; there is nothing to capture.", 409);
        }

        if (payment.Status == PaymentStatus.Unknown)
        {
            throw new PaymentException(
                "A previous capture attempt has an unknown outcome. Reconcile the payment before retrying.", 409);
        }

        if (string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw new PaymentException("This order has not been paid yet; there is no hold to capture.", 409);
        }

        var lease = await TryAcquireClaimAsync($"fulfil:{order.Id}", ct);
        if (lease == null)
        {
            var existing = await _db.OrderPayments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == order.Id, ct);
            if (existing != null && existing.Status is PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return existing;
            }

            throw new PaymentException("A fulfilment for this order is already in progress.", 409);
        }

        try
        {
            var authorization = await GetAuthorizationAsync(payment.AuthorizationId, ct);
            payment.RefreshAuthorization(authorization.State?.Value, authorization.IsVoid,
                authorization.VoidState?.Value, authorization.Amount, authorization.ExpiresAt,
                authorization.FailureCode, authorization.FailureMessage);

            var notCapturable = CaptureBlocker(authorization);
            if (notCapturable != null)
            {
                await _db.SaveChangesAsync(ct);
                throw new PaymentException(notCapturable, 409, authorization.FailureCode);
            }

            var captureAmount = payment.AuthorizedAmountCents ?? authorization.Amount ?? payment.AmountCents;
            var captured = await CaptureAsync(payment.AuthorizationId, captureAmount, ct);

            var transferId = captured.Transfer;
            if (string.IsNullOrWhiteSpace(transferId))
            {
                var refreshed = await GetAuthorizationAsync(payment.AuthorizationId, ct);
                transferId = refreshed.Transfer;
            }

            if (string.IsNullOrWhiteSpace(transferId))
            {
                throw new PaymentException(
                    "Finix captured the authorization but did not report a capture transfer id. Reconcile before retrying.",
                    502);
            }

            var transfer = await GetTransferAsync(transferId, ct);
            var capturedAmount = transfer.Amount ?? captureAmount;
            var fee = transfer.Fee;
            var net = fee.HasValue ? capturedAmount - fee.Value : (long?)null;

            payment.SetCaptured(transferId, capturedAmount, fee, net, transfer.State?.Value);
            order.MarkFulfilled();
            await _db.SaveChangesAsync(ct);

            return payment;
        }
        catch (PaymentException ex) when (ex.OutcomeUnknown)
        {
            payment.SetUnknown(ex.Message);
            await _db.SaveChangesAsync(ct);
            throw;
        }
        catch (PaymentException)
        {
            lease.ReleaseClaim();
            throw;
        }
        catch
        {
            lease.ReleaseClaim();
            throw;
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------
    // Cancel (void)
    // ---------------------------------------------------------------------

    public async Task<OrderPayment> CancelAsync(Order order, CancellationToken ct)
    {
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == order.Id, ct);
        if (payment == null)
        {
            payment = new OrderPayment(order.Id, order.BuyerId, _settings.Currency, ToCents(order.Total()));
            _db.OrderPayments.Add(payment);
        }

        if (payment.Status == PaymentStatus.Cancelled || order.Status == OrderStatus.Cancelled)
        {
            return payment;
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentException(
                "This payment has already been captured. Use the refunds endpoint instead of cancelling.", 409);
        }

        if (payment.Status == PaymentStatus.Unknown)
        {
            throw new PaymentException(
                "A previous cancellation has an unknown outcome. Reconcile the payment before retrying.", 409);
        }

        if (string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            payment.SetStatus(PaymentStatus.Cancelled);
            order.MarkCancelled();
            await _db.SaveChangesAsync(ct);
            return payment;
        }

        var lease = await TryAcquireClaimAsync($"cancel:{order.Id}", ct);
        if (lease == null)
        {
            var existing = await _db.OrderPayments.AsNoTracking().FirstOrDefaultAsync(p => p.OrderId == order.Id, ct);
            if (existing != null && existing.Status == PaymentStatus.Cancelled)
            {
                return existing;
            }

            throw new PaymentException("A cancellation for this order is already in progress.", 409);
        }

        try
        {
            var authorization = await GetAuthorizationAsync(payment.AuthorizationId, ct);
            if (authorization.IsVoid == true ||
                authorization.VoidState == FinixApiReference.Models.Enums.VoidState.Succeeded)
            {
                payment.SetVoided(authorization.IsVoid, authorization.VoidState?.Value);
                order.MarkCancelled();
                await _db.SaveChangesAsync(ct);
                return payment;
            }

            if (payment.CaptureTransferId != null ||
                authorization.Transfer != null && payment.Status == PaymentStatus.Captured)
            {
                throw new PaymentException(
                    "This payment has already been captured. Use the refunds endpoint instead of cancelling.", 409);
            }

            var voided = await VoidAsync(payment.AuthorizationId, ct);
            payment.SetVoided(voided.IsVoid, voided.VoidState?.Value);
            order.MarkCancelled();
            await _db.SaveChangesAsync(ct);
            return payment;
        }
        catch (PaymentException ex) when (ex.OutcomeUnknown)
        {
            payment.SetUnknown(ex.Message);
            await _db.SaveChangesAsync(ct);
            throw;
        }
        catch (PaymentException)
        {
            lease.ReleaseClaim();
            throw;
        }
        catch
        {
            lease.ReleaseClaim();
            throw;
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------
    // Refunds
    // ---------------------------------------------------------------------

    public async Task<PaymentRefund> RefundAsync(Order order, string buyerId, long? amountCents,
        string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("An idempotencyKey is required for a refund.", 400);
        }

        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == order.Id, ct);
        if (payment == null || string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            throw new PaymentException("Only a captured payment can be refunded.", 409);
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded))
        {
            throw new PaymentException("Only a captured payment can be refunded.", 409);
        }

        var existingRefund = await _db.PaymentRefunds
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);
        if (existingRefund != null)
        {
            return existingRefund;
        }

        var lease = await TryAcquireClaimAsync($"refund:{idempotencyKey}", ct);
        if (lease == null)
        {
            var raced = await _db.PaymentRefunds.AsNoTracking()
                .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);
            if (raced != null)
            {
                return raced;
            }

            throw new PaymentException(
                "A refund with this idempotencyKey is already in progress or has an unknown outcome.", 409);
        }

        try
        {
            existingRefund = await _db.PaymentRefunds
                .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);
            if (existingRefund != null)
            {
                return existingRefund;
            }

            var storedRefunded = await _db.PaymentRefunds
                .Where(r => r.OrderId == order.Id)
                .SumAsync(r => r.AmountCents, ct);

            var reversals = await ListReversalsAsync(payment.CaptureTransferId, ct);
            if (reversals.HasMore)
            {
                throw new PaymentException(
                    "Finix returned more refunds than this service can page through safely; the refund was not attempted.",
                    503);
            }

            var finixRefunded = reversals.Items.Sum(t => t.Amount ?? 0);
            var alreadyRefunded = Math.Max(storedRefunded, finixRefunded);
            var captured = payment.CapturedAmountCents ?? payment.AuthorizedAmountCents ?? payment.AmountCents;
            var remaining = captured - alreadyRefunded;

            if (remaining <= 0)
            {
                throw new PaymentException("This payment has already been fully refunded.", 409);
            }

            var requested = amountCents ?? remaining;
            if (requested <= 0)
            {
                throw new PaymentException("The refund amount must be greater than zero.", 400);
            }

            if (requested > remaining)
            {
                throw new PaymentException(
                    $"The refund of {requested} cents exceeds the {remaining} cents still refundable on this capture.",
                    409);
            }

            if (requested > int.MaxValue)
            {
                throw new PaymentException("The refund amount is too large.", 400);
            }

            var reversal = await CreateReversalAsync(payment.CaptureTransferId, (int)requested, ct);
            if (string.IsNullOrWhiteSpace(reversal.Id))
            {
                throw new PaymentException(
                    "Finix created a refund but did not report its transfer id. Reconcile before retrying.", 502);
            }

            var refund = new PaymentRefund(order.Id, buyerId, reversal.Id!, idempotencyKey,
                requested, reversal.State?.Value);
            _db.PaymentRefunds.Add(refund);

            var totalRefunded = alreadyRefunded + requested;
            order.MarkRefunded(totalRefunded >= captured);
            payment.SetStatus(totalRefunded >= captured ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded);
            await _db.SaveChangesAsync(ct);

            return refund;
        }
        catch (PaymentException ex) when (ex.OutcomeUnknown)
        {
            // The reversal may have landed. Settle it by re-reading Finix before reporting unknown.
            var settled = await TryFindReversalAsync(payment.CaptureTransferId!, amountCents, ct);
            if (settled != null)
            {
                var refund = new PaymentRefund(order.Id, buyerId, settled.Id!, idempotencyKey,
                    settled.Amount ?? 0, settled.State?.Value);
                _db.PaymentRefunds.Add(refund);
                await _db.SaveChangesAsync(ct);
                return refund;
            }

            throw;
        }
        catch (PaymentException)
        {
            lease.ReleaseClaim();
            throw;
        }
        catch
        {
            lease.ReleaseClaim();
            throw;
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    // ---------------------------------------------------------------------
    // Reconciliation
    // ---------------------------------------------------------------------

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        if (to < from)
        {
            throw new PaymentException("The 'to' date must not be earlier than the 'from' date.", 400);
        }

        var finix = await ListTransfersAsync(from, to, ct);
        var payments = await _db.OrderPayments
            .Where(p => p.CaptureTransferId != null)
            .ToListAsync(ct);
        var refunds = await _db.PaymentRefunds.ToListAsync(ct);

        var captureByTransfer = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.CaptureTransferId))
            .GroupBy(p => p.CaptureTransferId!)
            .ToDictionary(g => g.Key, g => g.First());
        var refundByTransfer = refunds
            .Where(r => !string.IsNullOrWhiteSpace(r.RefundTransferId))
            .GroupBy(r => r.RefundTransferId)
            .ToDictionary(g => g.Key, g => g.First());

        var seenCaptureIds = new HashSet<string>(StringComparer.Ordinal);
        var seenRefundIds = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<ReconciliationTransferView>();

        foreach (var transfer in finix.Items)
        {
            if (string.IsNullOrWhiteSpace(transfer.Id))
            {
                continue;
            }

            int? matchedOrderId = null;
            string? matchKind = null;
            string? mismatch = null;

            if (captureByTransfer.TryGetValue(transfer.Id, out var capture))
            {
                matchedOrderId = capture.OrderId;
                matchKind = "capture";
                seenCaptureIds.Add(transfer.Id);
                if (capture.CapturedAmountCents.HasValue && transfer.Amount.HasValue &&
                    capture.CapturedAmountCents.Value != transfer.Amount.Value)
                {
                    mismatch = $"eShop recorded {capture.CapturedAmountCents.Value} cents but Finix reports {transfer.Amount.Value}.";
                }
            }
            else if (refundByTransfer.TryGetValue(transfer.Id, out var refund))
            {
                matchedOrderId = refund.OrderId;
                matchKind = "refund";
                seenRefundIds.Add(transfer.Id);
                if (transfer.Amount.HasValue && refund.AmountCents != transfer.Amount.Value)
                {
                    mismatch = $"eShop recorded {refund.AmountCents} cents but Finix reports {transfer.Amount.Value}.";
                }
            }

            rows.Add(new ReconciliationTransferView(
                transfer.Id,
                transfer.Type?.Value,
                transfer.State?.Value,
                transfer.Amount,
                transfer.Fee,
                transfer.CreatedAt,
                matchedOrderId,
                matchKind,
                matchedOrderId.HasValue,
                mismatch));
        }

        var appOnly = new List<ReconciliationAppOnlyView>();
        foreach (var payment in payments)
        {
            if (payment.CaptureTransferId == null || seenCaptureIds.Contains(payment.CaptureTransferId))
            {
                continue;
            }

            if (payment.UpdatedAt < from || payment.UpdatedAt > to)
            {
                continue;
            }

            appOnly.Add(new ReconciliationAppOnlyView(payment.OrderId, "capture", payment.CaptureTransferId,
                payment.CapturedAmountCents ?? payment.AmountCents, payment.Status));
        }

        foreach (var refund in refunds)
        {
            if (seenRefundIds.Contains(refund.RefundTransferId))
            {
                continue;
            }

            if (refund.CreatedAt < from || refund.CreatedAt > to)
            {
                continue;
            }

            appOnly.Add(new ReconciliationAppOnlyView(refund.OrderId, "refund", refund.RefundTransferId,
                refund.AmountCents, refund.State ?? "UNKNOWN"));
        }

        return new ReconciliationResult(rows, appOnly, finix.HasMore, from, to);
    }

    // ---------------------------------------------------------------------
    // Identity / instrument helpers
    // ---------------------------------------------------------------------

    private async Task<ShopperProfile> EnsureIdentityAsync(string buyerId, CancellationToken ct)
    {
        var profile = await _db.ShopperProfiles.FindAsync(new object[] { buyerId }, ct);
        if (profile != null)
        {
            return profile;
        }

        var lease = await TryAcquireClaimAsync($"identity:{buyerId}", ct);
        if (lease == null)
        {
            profile = await _db.ShopperProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.BuyerId == buyerId, ct);
            if (profile != null)
            {
                return profile;
            }

            throw new PaymentException("Creating the shopper's Finix identity is already in progress.", 409);
        }

        try
        {
            profile = await _db.ShopperProfiles.FindAsync(new object[] { buyerId }, ct);
            if (profile != null)
            {
                return profile;
            }

            var email = LooksLikeEmail(buyerId) ? buyerId : null;
            var identity = await CreateIdentityAsync(email, ct);
            if (string.IsNullOrWhiteSpace(identity.Id))
            {
                throw new PaymentException("Finix did not return an identity id.", 502);
            }

            profile = new ShopperProfile(buyerId, identity.Id!, email);
            _db.ShopperProfiles.Add(profile);
            await _db.SaveChangesAsync(ct);
            return profile;
        }
        catch
        {
            lease.ReleaseClaim();
            throw;
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    private async Task<Identity> CreateIdentityAsync(string? email, CancellationToken ct)
    {
        var body = IdentitiesRequest.Buyer(new Buyer
        {
            Entity = new Entity5 { Email = email }
        });

        var since = DateTimeOffset.UtcNow.AddMinutes(-5);
        try
        {
            return await _client.Identities.CreateIdentity(
                new CreateIdentityRequest { Body = body }, cancellationToken: ct);
        }
        catch (ApiException<CreateIdentityError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Could not create the Finix buyer identity");
        }
        catch (SdkConnectionException ex)
        {
            if (email != null)
            {
                var found = await TryFindIdentityAsync(email, since, ct);
                if (found != null)
                {
                    return found;
                }
            }

            throw Unknown("Creating the Finix buyer identity", ex);
        }
    }

    private async Task<Identity?> TryFindIdentityAsync(string email, DateTimeOffset since, CancellationToken ct)
    {
        try
        {
            var page = await _client.Identities.ListIdentities(new ListIdentitiesRequest
            {
                Email = email,
                CreatedAtGte = since,
                Limit = 100
            }, cancellationToken: ct);

            return page.Embedded?.Identities?.FirstOrDefault();
        }
        catch (Exception ex) when (ex is SdkException or ApiException)
        {
            _logger.LogWarning(ex, "Could not re-read Finix identities after an ambiguous create.");
            return null;
        }
    }

    private async Task<PaymentInstrumentCard> CreatePaymentInstrumentAsync(string identityId,
        CardDetails card, CancellationToken ct)
    {
        var body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
        {
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Identity = identityId,
            Name = card.Name,
            Number = NormalizeCardNumber(card.Number),
            SecurityCode = card.SecurityCode,
            Address = ToFinixAddress(card.Address)
        });

        var since = DateTimeOffset.UtcNow.AddMinutes(-5);
        var last4 = LastFour(card.Number);
        try
        {
            var created = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(
                new CreatePaymentInstrumentRequest { Body = body }, cancellationToken: ct);

            if (!created.TryGetPaymentInstrumentCard(out var instrument) || instrument?.Id == null)
            {
                throw new PaymentException("Finix did not return a payment instrument.", 502);
            }

            return instrument;
        }
        catch (ApiException<CreatePaymentInstrumentError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Could not save the card with Finix");
        }
        catch (SdkConnectionException ex)
        {
            var found = await TryFindInstrumentAsync(identityId, last4, since, ct);
            if (found != null)
            {
                return found;
            }

            throw Unknown("Saving the card with Finix", ex);
        }
    }

    private async Task<PaymentInstrumentCard?> TryFindInstrumentAsync(string identityId, string? last4,
        DateTimeOffset since, CancellationToken ct)
    {
        try
        {
            var page = await _client.PaymentInstrumentsApi.ListPaymentInstruments(
                new ListPaymentInstrumentsRequest
                {
                    OwnerIdentityId = identityId,
                    Last4 = last4,
                    CreatedAtGte = since,
                    Limit = 100
                }, cancellationToken: ct);

            return page.Embedded?.PaymentInstruments?
                .Select(i => i.TryGetPaymentInstrumentCard(out var c) ? c : null)
                .FirstOrDefault(c => c != null);
        }
        catch (Exception ex) when (ex is SdkException or ApiException)
        {
            _logger.LogWarning(ex, "Could not re-read Finix payment instruments after an ambiguous create.");
            return null;
        }
    }

    private async Task<string> ResolveSavedCardAsync(string buyerId, string identityId, string paymentMethodId,
        CancellationToken ct)
    {
        var card = await _db.SavedCards
            .FirstOrDefaultAsync(c => c.PaymentInstrumentId == paymentMethodId && c.BuyerId == buyerId, ct);
        if (card == null || !card.Enabled)
        {
            throw new PaymentException($"Payment method {paymentMethodId} was not found.", 404);
        }

        var page = await ListInstrumentsAsync(identityId, ct);
        var live = page.Items
            .Select(i => i.TryGetPaymentInstrumentCard(out var c) ? c : null)
            .FirstOrDefault(c => c?.Id == paymentMethodId);

        if (live == null || live.Enabled == false)
        {
            throw new PaymentException(
                $"Payment method {paymentMethodId} is no longer enabled at Finix and cannot be used.", 400);
        }

        return paymentMethodId;
    }

    private async Task<FinixPage<PaymentInstruments3>> ListInstrumentsAsync(string identityId, CancellationToken ct)
    {
        try
        {
            var (items, hasMore) = await CollectPagesAsync<PaymentInstruments3>(async (cursor, token) =>
            {
                var page = await _client.PaymentInstrumentsApi.ListPaymentInstruments(
                    new ListPaymentInstrumentsRequest
                    {
                        OwnerIdentityId = identityId,
                        AfterCursor = cursor,
                        Limit = 100
                    }, cancellationToken: token);
                return (page.Embedded?.PaymentInstruments, page.Page?.NextCursor);
            }, 50, ct);

            return new FinixPage<PaymentInstruments3>(items, hasMore);
        }
        catch (ApiException<ListPaymentInstrumentsError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Could not read the shopper's cards from Finix");
        }
        catch (SdkConnectionException ex)
        {
            throw Unavailable("Reading the shopper's cards from Finix", ex);
        }
    }

    private async Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken ct)
    {
        try
        {
            await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(new UpdatePaymentInstrumentRequest
            {
                PaymentInstrumentId = paymentInstrumentId,
                Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                    new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
            }, cancellationToken: ct);

            var instrument = await GetPaymentInstrumentAsync(paymentInstrumentId, ct);
            if (instrument.Enabled != false)
            {
                throw new PaymentException("Finix did not disable the card.", 502);
            }
        }
        catch (ApiException<UpdatePaymentInstrumentError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Could not remove the saved card at Finix");
        }
        catch (SdkConnectionException ex)
        {
            var instrument = await TryGetPaymentInstrumentAsync(paymentInstrumentId, ct);
            if (instrument?.Enabled == false)
            {
                return;
            }

            throw Unknown("Removing the saved card at Finix", ex);
        }
    }

    private async Task<PaymentInstrumentCard> GetPaymentInstrumentAsync(string id, CancellationToken ct)
    {
        try
        {
            var response = await _client.PaymentInstrumentsApi.GetPaymentInstrument(
                new GetPaymentInstrumentRequest { PaymentInstrumentId = id }, cancellationToken: ct);

            if (!response.TryGetPaymentInstrumentCard(out var card) || card == null)
            {
                throw new PaymentException($"Payment method {id} was not found at Finix.", 404);
            }

            return card;
        }
        catch (ApiException<GetPaymentInstrumentError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Could not read the saved card from Finix");
        }
        catch (SdkConnectionException ex)
        {
            throw Unavailable("Reading the saved card from Finix", ex);
        }
    }

    private async Task<PaymentInstrumentCard?> TryGetPaymentInstrumentAsync(string id, CancellationToken ct)
    {
        try
        {
            var response = await _client.PaymentInstrumentsApi.GetPaymentInstrument(
                new GetPaymentInstrumentRequest { PaymentInstrumentId = id }, cancellationToken: ct);
            return response.TryGetPaymentInstrumentCard(out var card) ? card : null;
        }
        catch (Exception ex) when (ex is SdkException or ApiException)
        {
            _logger.LogWarning(ex, "Could not re-read Finix payment instrument {PaymentInstrumentId}.", id);
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // Authorization / transfer helpers
    // ---------------------------------------------------------------------

    private async Task<Authorization> AuthorizeAsync(long amountCents, string source, string idempotencyKey,
        CancellationToken ct)
    {
        var body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
        {
            Amount = amountCents,
            Currency = _settings.CurrencyValue,
            Merchant = _settings.MerchantId,
            Source = source,
            IdempotencyId = idempotencyKey,
            StatementDescriptor = $"ESHOP ORDER"
        });

        try
        {
            return await _client.AuthorizationsApi.CreateAuthorization(
                new CreateAuthorizationRequest { Body = body }, cancellationToken: ct);
        }
        catch (ApiException<CreateAuthorizationError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Finix declined the authorization", 402);
        }
        catch (SdkConnectionException ex)
        {
            var found = await TryFindAuthorizationByIdempotencyKeyAsync(idempotencyKey, ct);
            if (found != null)
            {
                return found;
            }

            throw Unknown("Authorizing the payment with Finix", ex);
        }
    }

    private async Task<Authorization?> TryFindAuthorizationByIdempotencyKeyAsync(string idempotencyKey,
        CancellationToken ct)
    {
        try
        {
            var page = await _client.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
            {
                IdempotencyId = idempotencyKey,
                Limit = 100
            }, cancellationToken: ct);

            return page.Embedded?.Authorizations?.FirstOrDefault();
        }
        catch (Exception ex) when (ex is SdkException or ApiException)
        {
            _logger.LogWarning(ex, "Could not re-read Finix authorizations after an ambiguous create.");
            return null;
        }
    }

    private async Task<Authorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            return await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId }, cancellationToken: ct);
        }
        catch (ApiException<GetAuthorizationError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Could not read the authorization from Finix");
        }
        catch (SdkConnectionException ex)
        {
            throw Unavailable("Reading the authorization from Finix", ex);
        }
    }

    private async Task<Authorization> CaptureAsync(string authorizationId, long amountCents, CancellationToken ct)
    {
        try
        {
            return await _client.AuthorizationsApi.CaptureAuthorization(new CaptureAuthorizationRequest
            {
                AuthorizationId = authorizationId,
                Body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
                {
                    CaptureAmount = amountCents
                })
            }, cancellationToken: ct);
        }
        catch (ApiException<CaptureAuthorizationError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Finix could not capture the authorization");
        }
        catch (SdkConnectionException ex)
        {
            var found = await TryGetAuthorizationAsync(authorizationId, ct);
            if (found?.Transfer != null)
            {
                return found;
            }

            throw Unknown("Capturing the payment with Finix", ex);
        }
    }

    private async Task<Authorization> VoidAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            return await _client.AuthorizationsApi.VoidAuthorization(new VoidAuthorizationRequest
            {
                AuthorizationIdVoidTo = authorizationId,
                Body = new VoidAnAuthorization { VoidMe = true }
            }, cancellationToken: ct);
        }
        catch (ApiException<VoidAuthorizationError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Finix could not void the authorization");
        }
        catch (SdkConnectionException ex)
        {
            var found = await TryGetAuthorizationAsync(authorizationId, ct);
            if (found?.IsVoid == true ||
                found?.VoidState == FinixApiReference.Models.Enums.VoidState.Succeeded)
            {
                return found;
            }

            throw Unknown("Releasing the authorization with Finix", ex);
        }
    }

    private async Task<Authorization?> TryGetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            return await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId }, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is SdkException or ApiException)
        {
            _logger.LogWarning(ex, "Could not re-read Finix authorization {AuthorizationId}.", authorizationId);
            return null;
        }
    }

    private async Task<Transfer> GetTransferAsync(string transferId, CancellationToken ct)
    {
        try
        {
            return await _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId }, cancellationToken: ct);
        }
        catch (ApiException<GetTransferError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Could not read the capture from Finix");
        }
        catch (SdkConnectionException ex)
        {
            throw Unavailable("Reading the capture from Finix", ex);
        }
    }

    private async Task<FinixPage<Transfer>> ListReversalsAsync(string captureTransferId, CancellationToken ct)
    {
        try
        {
            var (items, hasMore) = await CollectPagesAsync<Transfer>(async (cursor, token) =>
            {
                var page = await _client.TransfersApi.ListTransferReversals(new ListTransferReversalsRequest
                {
                    TransferId = captureTransferId,
                    AfterCursor = cursor,
                    Limit = 100
                }, cancellationToken: token);
                return (page.Embedded?.Transfers, page.Page?.NextCursor);
            }, 50, ct);

            return new FinixPage<Transfer>(items, hasMore);
        }
        catch (ApiException<ListTransferReversalsError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Could not read the refunds from Finix");
        }
        catch (SdkConnectionException ex)
        {
            throw Unavailable("Reading the refunds from Finix", ex);
        }
    }

    private async Task<FinixPage<Transfer>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        try
        {
            var (items, hasMore) = await CollectPagesAsync<Transfer>(async (cursor, token) =>
            {
                var page = await _client.TransfersApi.ListTransfers(new ListTransfersRequest
                {
                    CreatedAtGte = from,
                    CreatedAtLte = to,
                    Merchant = _settings.MerchantId,
                    AfterCursor = cursor,
                    Limit = 100
                }, cancellationToken: token);
                return (page.Embedded?.Transfers, page.Page?.NextCursor);
            }, 100, ct);

            return new FinixPage<Transfer>(items, hasMore);
        }
        catch (ApiException<ListTransfersError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Could not read the transactions from Finix");
        }
        catch (SdkConnectionException ex)
        {
            throw Unavailable("Reading the transactions from Finix", ex);
        }
    }

    private async Task<Transfer> CreateReversalAsync(string captureTransferId, int amountCents, CancellationToken ct)
    {
        try
        {
            return await _client.TransfersApi.CreateTransferReversal(new CreateTransferReversalRequest
            {
                TransferId = captureTransferId,
                Body = TransfersReversalsRequest.RefundTransfer(new RefundTransfer
                {
                    RefundAmount = amountCents
                })
            }, cancellationToken: ct);
        }
        catch (ApiException<CreateTransferReversalError> ex)
        {
            throw Translate(ex, e => e.TryGetGenericError(out var g) ? g : null,
                "Finix could not create the refund");
        }
        catch (SdkConnectionException ex)
        {
            var found = await TryFindReversalAsync(captureTransferId, amountCents, ct);
            if (found != null)
            {
                return found;
            }

            throw Unknown("Creating the refund with Finix", ex);
        }
    }

    private async Task<Transfer?> TryFindReversalAsync(string captureTransferId, long? amountCents,
        CancellationToken ct)
    {
        try
        {
            var page = await ListReversalsAsync(captureTransferId, ct);
            var since = DateTimeOffset.UtcNow.AddMinutes(-10);
            return page.Items
                .Where(t => t.CreatedAt == null || t.CreatedAt >= since)
                .Where(t => amountCents == null || t.Amount == amountCents)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is SdkException or PaymentException)
        {
            _logger.LogWarning(ex, "Could not re-read Finix reversals after an ambiguous refund.");
            return null;
        }
    }

    private async Task RefreshCaptureDetailsAsync(OrderPayment payment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payment.CaptureTransferId) || payment.FeeCents.HasValue)
        {
            return;
        }

        try
        {
            var transfer = await GetTransferAsync(payment.CaptureTransferId, ct);
            var capturedAmount = transfer.Amount ?? payment.CapturedAmountCents ?? payment.AmountCents;
            var fee = transfer.Fee;
            payment.SetCaptured(payment.CaptureTransferId, capturedAmount, fee,
                fee.HasValue ? capturedAmount - fee.Value : null, transfer.State?.Value);
            await _db.SaveChangesAsync(ct);
        }
        catch (PaymentException ex)
        {
            _logger.LogWarning(ex, "Could not refresh capture details for order {OrderId}.", payment.OrderId);
        }
    }

    private static string? CaptureBlocker(Authorization authorization)
    {
        if (authorization.IsVoid == true ||
            authorization.VoidState == FinixApiReference.Models.Enums.VoidState.Succeeded)
        {
            return "The authorization was voided, so the hold is gone and cannot be captured. " +
                   "Ask the shopper to pay again or cancel the order.";
        }

        if (authorization.State != FinixApiReference.Models.Enums.State1.Succeeded)
        {
            var reason = string.IsNullOrWhiteSpace(authorization.FailureMessage)
                ? $"Finix reports the authorization state as {authorization.State?.Value ?? "UNKNOWN"}."
                : authorization.FailureMessage;
            return $"The authorization can no longer be captured: {reason} " +
                   "Ask the shopper to pay again or cancel the order.";
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            return $"The authorization expired at {authorization.ExpiresAt.Value:O}. The hold can no longer be captured; " +
                   "ask the shopper to pay again or cancel the order.";
        }

        return null;
    }

    private async Task<OrderPayment> GetOrCreatePaymentAsync(Order order, string buyerId, CancellationToken ct)
    {
        var payment = await _db.OrderPayments.FirstOrDefaultAsync(p => p.OrderId == order.Id, ct);
        if (payment != null)
        {
            return payment;
        }

        payment = new OrderPayment(order.Id, buyerId, _settings.Currency, ToCents(order.Total()));
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(ct);
        return payment;
    }

    private static async Task<(List<T> Items, bool HasMore)> CollectPagesAsync<T>(
        Func<string?, CancellationToken, Task<(IReadOnlyList<T>? Items, string? NextCursor)>> fetch,
        int maxPages, CancellationToken ct)
    {
        var items = new List<T>();
        string? cursor = null;
        var pages = 0;

        while (true)
        {
            var (pageItems, nextCursor) = await fetch(cursor, ct);
            if (pageItems != null)
            {
                items.AddRange(pageItems);
            }

            pages++;
            if (string.IsNullOrWhiteSpace(nextCursor))
            {
                return (items, false);
            }

            if (pages >= maxPages)
            {
                return (items, true);
            }

            cursor = nextCursor;
        }
    }

    // ---------------------------------------------------------------------
    // Claims
    // ---------------------------------------------------------------------

    private async Task<ClaimLease?> TryAcquireClaimAsync(string key, CancellationToken ct)
    {
        var gate = ClaimGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var existing = await db.PaymentClaims.FindAsync(new object[] { key }, ct);

            if (existing != null)
            {
                if (DateTimeOffset.UtcNow - existing.CreatedAt > ClaimLifetime)
                {
                    db.PaymentClaims.Remove(existing);
                    await db.SaveChangesAsync(ct);
                }
                else
                {
                    gate.Release();
                    return null;
                }
            }

            db.PaymentClaims.Add(new PaymentClaim(key));
            await db.SaveChangesAsync(ct);
            return new ClaimLease(this, key, gate);
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    private async Task RemoveClaimAsync(string key)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var claim = await db.PaymentClaims.FindAsync(new object[] { key });
            if (claim != null)
            {
                db.PaymentClaims.Remove(claim);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not release payment claim {ClaimKey}.", key);
        }
    }

    private sealed class ClaimLease : IAsyncDisposable
    {
        private readonly FinixPaymentService _owner;
        private readonly string _key;
        private readonly SemaphoreSlim _gate;
        private bool _releaseClaim;

        public ClaimLease(FinixPaymentService owner, string key, SemaphoreSlim gate)
        {
            _owner = owner;
            _key = key;
            _gate = gate;
        }

        public void ReleaseClaim() => _releaseClaim = true;

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_releaseClaim)
                {
                    await _owner.RemoveClaimAsync(_key);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    // ---------------------------------------------------------------------
    // Error translation
    // ---------------------------------------------------------------------

    private static PaymentException Translate<TError>(ApiException<TError> ex,
        Func<TError, GenericError?> extract, string operation, int fallbackStatus = 502)
        where TError : ApiError
    {
        var generic = extract(ex.Error);
        var first = generic?.Embedded?.Errors?.FirstOrDefault();
        var detail = first?.Message;
        var code = first?.Code;
        var message = string.IsNullOrWhiteSpace(detail) ? operation : $"{operation}: {detail}";

        var status = (int)ex.StatusCode switch
        {
            400 or 422 => 400,
            402 => 402,
            404 => 404,
            409 => 409,
            429 => 503,
            _ => fallbackStatus
        };

        return new PaymentException(message, status, code, false, ex);
    }

    private static PaymentException Unavailable(string operation, Exception ex) =>
        new($"{operation} failed because Finix could not be reached. Please retry.", 502,
            innerException: ex);

    private static PaymentException Unknown(string operation, Exception ex) =>
        new($"{operation} could not be confirmed because the connection to Finix failed. " +
            "The operation may have succeeded; reconcile before retrying.", 503,
            outcomeUnknown: true, innerException: ex);

    // ---------------------------------------------------------------------
    // Mapping
    // ---------------------------------------------------------------------

    private static SavedCardView ToView(SavedCard card) =>
        new(card.PaymentMethodId, card.Last4, card.Brand, card.ExpirationMonth, card.ExpirationYear,
            card.Enabled, card.CreatedAt);

    private static OrderPaymentView ToView(OrderPayment payment, IReadOnlyList<PaymentRefund> refunds) =>
        new(payment.OrderId, payment.Status, payment.Currency, payment.AmountCents,
            payment.AuthorizationId, payment.AuthorizationState, payment.AuthorizedAmountCents,
            payment.IsVoid, payment.VoidState, payment.AuthorizationExpiresAt,
            payment.CaptureTransferId, payment.CapturedAmountCents, payment.FeeCents, payment.NetCents,
            payment.CaptureState, payment.FailureCode, payment.FailureMessage,
            refunds.Select(r => new RefundView(r.Id, r.RefundTransferId, r.AmountCents, r.State, r.CreatedAt))
                .ToList());

    private static FinixAddressModel? ToFinixAddress(AddressDetails? address)
    {
        if (address == null)
        {
            return null;
        }

        return new FinixAddressModel
        {
            Line1 = address.Line1,
            Line2 = address.Line2,
            City = address.City,
            Region = address.Region,
            PostalCode = address.PostalCode,
            Country = FinixCountryEnum.TryGetKnownValue(address.Country, out var country)
                ? country
                : FinixCountryEnum.Usa
        };
    }

    private static string NormalizeCardNumber(string number) =>
        new(number.Where(char.IsDigit).ToArray());

    private static string? LastFour(string number)
    {
        var digits = NormalizeCardNumber(number);
        return digits.Length >= 4 ? digits[^4..] : null;
    }

    private static bool LooksLikeEmail(string value) =>
        value.Contains('@', StringComparison.Ordinal);
}

