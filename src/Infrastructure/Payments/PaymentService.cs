using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments.Finix;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PaymentService : IPaymentService
{
    private readonly CatalogContext _db;
    private readonly IFinixClient _finix;
    private readonly FinixOptions _finixOptions;
    private readonly IPaymentLockProvider _locks;

    public PaymentService(CatalogContext db, IFinixClient finix, IOptions<FinixOptions> finixOptions,
        IPaymentLockProvider locks)
    {
        _db = db;
        _finix = finix;
        _finixOptions = finixOptions.Value;
        _locks = locks;
    }

    public async Task<Order> CreateOrderAsync(CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.BuyerId))
        {
            throw PaymentException.BadRequest("The caller identity is missing from the token.");
        }

        if (command.Items is null || command.Items.Count == 0)
        {
            throw PaymentException.BadRequest("At least one catalog item is required.");
        }

        if (command.Items.Any(i => i.Quantity <= 0))
        {
            throw PaymentException.BadRequest("Every order line must have a quantity greater than zero.");
        }

        var lines = command.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new OrderLineCommand(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        var catalogItemIds = lines.Select(l => l.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems
            .Where(i => catalogItemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        var missing = catalogItemIds.Except(catalogItems.Select(i => i.Id)).ToList();
        if (missing.Count > 0)
        {
            throw PaymentException.NotFound(
                $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.First(i => i.Id == line.CatalogItemId);
            var picture = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.svg"
                : catalogItem.PictureUri;
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, picture);
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, line.Quantity));
        }

        var address = BuildAddress(command.ShippingAddress);
        var order = new Order(command.BuyerId, address, orderItems);
        var amountCents = ToCents(order.Total());
        if (amountCents <= 0)
        {
            throw PaymentException.BadRequest("The order total must be greater than zero.");
        }

        IDbContextTransaction? transaction = null;
        if (_db.Database.IsRelational())
        {
            transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            _db.Orders.Add(order);
            await _db.SaveChangesAsync(cancellationToken);

            var payment = new Payment(order.Id, command.BuyerId, amountCents, _finixOptions.ResolveCurrency());
            _db.Payments.Add(payment);
            await _db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        return order;
    }

    public async Task<Payment> PayOrderAsync(string buyerId, int orderId, PayOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw PaymentException.BadRequest("The caller identity is missing from the token.");
        }

        if (command.PaymentMethodId.HasValue == (command.Card is not null))
        {
            throw PaymentException.BadRequest(
                "Provide exactly one payment source: either card details for a one-off payment or paymentMethodId for a saved card.");
        }

        using var handle = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw PaymentException.NotFound($"Order {orderId} was not found.");
        }

        var payment = order.Payment;
        if (payment is null)
        {
            var amount = ToCents(order.Total());
            payment = new Payment(order.Id, order.BuyerId, amount, _finixOptions.ResolveCurrency());
            _db.Payments.Add(payment);
        }

        if (order.Status == OrderStatus.Cancelled || payment.Status == PaymentStatus.Cancelled)
        {
            throw PaymentException.Conflict(
                $"Order {orderId} was cancelled and can no longer be paid. Place a new order instead.");
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            return payment;
        }

        if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            FinixAuthorization? existingAuthorization = null;
            try
            {
                existingAuthorization = await _finix.GetAuthorizationAsync(payment.AuthorizationId,
                    cancellationToken);
            }
            catch (PaymentException ex) when (ex.StatusCode is 400 or 404 or 409 or 422)
            {
                existingAuthorization = null;
            }

            if (!string.IsNullOrWhiteSpace(existingAuthorization?.Transfer))
            {
                // The capture already exists at Finix; fulfilment can finish it.
                return payment;
            }

            var expired = payment.AuthorizationExpiresAt.HasValue &&
                          payment.AuthorizationExpiresAt.Value < DateTimeOffset.UtcNow;

            if (existingAuthorization is not null &&
                !IsAuthorizationVoided(existingAuthorization) &&
                string.Equals(existingAuthorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) &&
                !expired)
            {
                return payment;
            }

            // The old hold is gone. Record that and continue with a fresh authorization below.
            payment.MarkFailed("AUTHORIZATION_INVALID",
                "The previous Finix authorization is no longer capturable and a new authorization is required.");
            order.MarkPaymentFailed();
            await _db.SaveChangesAsync(cancellationToken);
        }

        string sourcePaymentInstrumentId;
        string? securityCode = null;
        int? savedPaymentMethodId = null;

        if (command.PaymentMethodId.HasValue)
        {
            var saved = await _db.SavedPaymentMethods.FirstOrDefaultAsync(
                m => m.Id == command.PaymentMethodId.Value && m.BuyerId == buyerId && !m.IsDeleted,
                cancellationToken);

            if (saved is null)
            {
                throw PaymentException.NotFound(
                    $"Saved payment method {command.PaymentMethodId.Value} was not found.");
            }

            sourcePaymentInstrumentId = saved.FinixPaymentInstrumentId;
            savedPaymentMethodId = saved.Id;
        }
        else
        {
            var card = NormalizeAndValidateCard(command.Card!);
            securityCode = card.SecurityCode;

            try
            {
                var identityId = await GetOrCreateBuyerIdentityAsync(buyerId, cancellationToken);
                var instrument = await _finix.CreatePaymentInstrumentAsync(identityId, buyerId, card,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(instrument.Id))
                {
                    throw PaymentException.Upstream("Finix did not return a payment instrument id.");
                }

                sourcePaymentInstrumentId = instrument.Id;
            }
            catch (PaymentException ex) when (ex.StatusCode != 502)
            {
                payment.MarkFailed(ex.Code, ex.Message);
                order.MarkPaymentFailed();
                await _db.SaveChangesAsync(cancellationToken);
                throw;
            }
        }

        try
        {
            var idempotencyId = $"eshop-order-{order.Id}-authorize-{payment.Status}-{payment.UpdatedAt.UtcTicks}";
            var tags = new Dictionary<string, string>
            {
                ["eshop_order_id"] = order.Id.ToString(),
                ["eshop_buyer_id"] = buyerId
            };

            var authorization = await _finix.CreateAuthorizationAsync(sourcePaymentInstrumentId,
                payment.AmountCents, securityCode, idempotencyId, tags, cancellationToken);

            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(authorization.FailureCode))
            {
                var detail = authorization.FailureMessage ?? authorization.FailureCode ?? authorization.State ??
                    "UNKNOWN";
                throw PaymentException.Conflict(
                    $"Finix did not authorize the payment for order {orderId}: {detail}.",
                    "AUTHORIZATION_FAILED");
            }

            payment.MarkAuthorized(authorization.Id, authorization.State ?? "SUCCEEDED",
                authorization.ExpiresAt, sourcePaymentInstrumentId, savedPaymentMethodId);
            order.MarkAuthorized();
            await _db.SaveChangesAsync(cancellationToken);
            return payment;
        }
        catch (PaymentException ex) when (ex.StatusCode != 502)
        {
            payment.MarkFailed(ex.Code, ex.Message);
            order.MarkPaymentFailed();
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var handle = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw PaymentException.NotFound($"Order {orderId} was not found.");
        }

        var payment = order.Payment;
        if (payment is null)
        {
            throw PaymentException.Conflict(
                $"Order {orderId} has no payment. The shopper must pay before it can be fulfilled.");
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            if (payment.Status == PaymentStatus.Captured &&
                string.Equals(payment.CaptureStatus, "PENDING", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(payment.CaptureTransferId))
            {
                var latest = await _finix.GetTransferAsync(payment.CaptureTransferId, cancellationToken);
                if (string.Equals(latest.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    var captured = latest.Amount ?? payment.CapturedAmountCents ?? payment.AmountCents;
                    var fee = await GetFeeCentsAsync(payment.CaptureTransferId, cancellationToken);
                    payment.MarkCaptured(payment.CaptureTransferId, latest.State, captured, fee,
                        captured - fee, latest.CreatedAt ?? payment.CapturedAt ?? DateTimeOffset.UtcNow);
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }

            return payment;
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw PaymentException.Conflict(
                $"Order {orderId} was cancelled; there is no authorization to capture.");
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw PaymentException.Conflict(
                $"Order {orderId} is not authorized. The shopper must pay with POST /api/orders/{orderId}/pay before fulfilment.");
        }

        var authorization = await _finix.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        EnsureAuthorizationCapturable(orderId, authorization, payment);
        payment.SetAuthorizationStatus(authorization.State);

        var captureTransferId = await CaptureOrGetExistingTransferAsync(orderId, payment, authorization,
            cancellationToken);
        var transfer = await WaitForTransferAsync(captureTransferId, cancellationToken);

        var capturedAmount = transfer.Amount ?? payment.AmountCents;
        var feeCents = await GetFeeCentsAsync(captureTransferId, cancellationToken);
        var netProceeds = capturedAmount - feeCents;

        payment.MarkCaptured(captureTransferId, transfer.State, capturedAmount, feeCents, netProceeds,
            transfer.CreatedAt ?? DateTimeOffset.UtcNow);
        order.MarkFulfilled();
        await _db.SaveChangesAsync(cancellationToken);
        return payment;
    }

    public async Task<Payment> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var handle = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw PaymentException.NotFound($"Order {orderId} was not found.");
        }

        var payment = order.Payment;
        if (payment is null)
        {
            payment = new Payment(order.Id, order.BuyerId, ToCents(order.Total()), _finixOptions.ResolveCurrency());
            _db.Payments.Add(payment);
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment;
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            throw PaymentException.Conflict(
                $"Order {orderId} has already been fulfilled. Use POST /api/orders/{orderId}/refunds to return the money.");
        }

        if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            var authorization = await TryGetAuthorizationAsync(payment.AuthorizationId, cancellationToken);

            if (!string.IsNullOrWhiteSpace(authorization?.Transfer))
            {
                throw PaymentException.Conflict(
                    $"Order {orderId} has already been captured by Finix and can no longer be cancelled. Use POST /api/orders/{orderId}/refunds instead.");
            }

            if (authorization is not null && IsAuthorizationVoided(authorization))
            {
                payment.MarkCancelled("VOIDED");
                order.MarkCancelled();
                await _db.SaveChangesAsync(cancellationToken);
                return payment;
            }

            try
            {
                var voided = await _finix.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                payment.MarkCancelled(voided.IsVoid == true ? "VOIDED" : "CANCELED");
                order.MarkCancelled();
                await _db.SaveChangesAsync(cancellationToken);
                return payment;
            }
            catch (PaymentException ex) when (ex.StatusCode is 400 or 402 or 404 or 409 or 422)
            {
                var latest = await TryGetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                if (latest is not null && IsAuthorizationVoided(latest))
                {
                    payment.MarkCancelled("VOIDED");
                    order.MarkCancelled();
                    await _db.SaveChangesAsync(cancellationToken);
                    return payment;
                }

                payment.SetAuthorizationStatus(latest?.State);
                await _db.SaveChangesAsync(cancellationToken);
                throw PaymentException.Conflict(
                    $"Order {orderId} could not be cancelled because Finix could not release authorization {payment.AuthorizationId}. {ex.Message}",
                    "AUTHORIZATION_NOT_VOIDABLE");
            }
        }

        payment.MarkCancelled(payment.AuthorizationStatus);
        order.MarkCancelled();
        await _db.SaveChangesAsync(cancellationToken);
        return payment;
    }

    public async Task<Refund> RefundOrderAsync(string buyerId, int orderId, long? amountCents,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw PaymentException.BadRequest("The caller identity is missing from the token.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw PaymentException.BadRequest("idempotencyKey is required for refunds.");
        }

        idempotencyKey = idempotencyKey.Trim();
        if (idempotencyKey.Length > 200)
        {
            throw PaymentException.BadRequest("idempotencyKey must be 200 characters or fewer.");
        }

        using var handle = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw PaymentException.NotFound($"Order {orderId} was not found.");
        }

        var payment = order.Payment;
        if (payment is null)
        {
            throw PaymentException.Conflict($"Order {orderId} has no captured payment to refund.");
        }

        var existing = payment.Refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing;
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded))
        {
            throw PaymentException.Conflict(
                $"Order {orderId} has not been fulfilled. Only a captured payment can be refunded.");
        }

        var capturedAmount = payment.CapturedAmountCents ?? 0;
        var remaining = capturedAmount - payment.RefundedAmountCents;
        if (remaining <= 0)
        {
            throw PaymentException.Conflict($"Order {orderId} has already been fully refunded.");
        }

        var refundAmount = amountCents ?? remaining;
        if (refundAmount <= 0)
        {
            throw PaymentException.BadRequest("The refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw PaymentException.BadRequest(
                $"The refund amount ({refundAmount} cents) exceeds the refundable balance ({remaining} cents).");
        }

        if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            throw PaymentException.Conflict(
                $"Order {orderId} has no Finix capture transfer id, so it cannot be refunded.");
        }

        var reversal = await _finix.CreateRefundAsync(payment.CaptureTransferId, refundAmount, cancellationToken);

        if (string.Equals(reversal.State, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            throw PaymentException.Conflict(
                $"Finix could not refund order {orderId}: {reversal.FailureMessage ?? reversal.FailureCode ?? "FAILED"}.",
                "REFUND_FAILED");
        }

        if (string.IsNullOrWhiteSpace(reversal.Id))
        {
            throw PaymentException.Upstream("Finix did not return a refund transfer id.");
        }

        var refund = new Refund(payment.Id, order.Id, reversal.Id, refundAmount,
            reversal.State ?? "PENDING", idempotencyKey, DateTimeOffset.UtcNow);

        payment.ApplyRefund(refund);
        order.MarkRefunded(payment.Status == PaymentStatus.Refunded);
        await _db.SaveChangesAsync(cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw PaymentException.BadRequest("The caller identity is missing from the token.");
        }

        return await _db.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
            .ThenInclude(p => p!.Refunds)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw PaymentException.BadRequest("'from' must be before or equal to 'to'.");
        }

        var transfersTask = _finix.ListTransfersAsync(from, to, cancellationToken);
        var authorizationsTask = _finix.ListAuthorizationsAsync(from, to, cancellationToken);
        await Task.WhenAll(transfersTask, authorizationsTask);

        var transfers = await transfersTask;
        var authorizations = await authorizationsTask;

        var payments = await _db.Payments
            .AsNoTracking()
            .Include(p => p.Refunds)
            .ToListAsync(cancellationToken);

        var byAuthorization = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.AuthorizationId))
            .ToDictionary(p => p.AuthorizationId!, p => p, StringComparer.Ordinal);

        var byTransfer = new Dictionary<string, (Payment Payment, Refund? Refund)>(StringComparer.Ordinal);
        foreach (var payment in payments)
        {
            if (!string.IsNullOrWhiteSpace(payment.CaptureTransferId))
            {
                byTransfer[payment.CaptureTransferId!] = (payment, null);
            }

            foreach (var refund in payment.Refunds)
            {
                byTransfer[refund.FinixTransferId] = (payment, refund);
            }
        }

        var transferIds = transfers.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var authorizationIds = authorizations.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        var entries = new List<ReconciliationEntry>();
        var matchedFinixIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transfer in transfers)
        {
            var matched = false;
            string? note = null;
            Payment? payment = null;
            Refund? refund = null;

            if (byTransfer.TryGetValue(transfer.Id, out var hit))
            {
                payment = hit.Payment;
                refund = hit.Refund;
                matched = true;
                note = refund is null ? "Matched eShop capture" : "Matched eShop refund";
            }

            if (matched)
            {
                matchedFinixIds.Add(transfer.Id);
            }

            entries.Add(new ReconciliationEntry(transfer.Id, "transfer", transfer.Type, transfer.State,
                transfer.Amount, transfer.Currency, transfer.CreatedAt, payment?.OrderId, payment?.BuyerId,
                matched, note ?? "Finix transaction has no matching eShop payment"));
        }

        foreach (var authorization in authorizations)
        {
            var matched = false;
            string? note = null;
            Payment? payment = null;

            if (byAuthorization.TryGetValue(authorization.Id, out var hit))
            {
                payment = hit;
                matched = true;
                note = "Matched eShop authorization";
            }

            if (matched)
            {
                matchedFinixIds.Add(authorization.Id);
            }

            entries.Add(new ReconciliationEntry(authorization.Id, "authorization", "AUTHORIZATION",
                authorization.State, authorization.Amount, authorization.Currency, authorization.CreatedAt,
                payment?.OrderId, payment?.BuyerId, matched,
                note ?? "Finix authorization has no matching eShop payment"));
        }

        var eshopOnly = 0;
        foreach (var payment in payments)
        {
            var hasFinixMatch =
                (!string.IsNullOrWhiteSpace(payment.AuthorizationId) &&
                 authorizationIds.Contains(payment.AuthorizationId!)) ||
                (!string.IsNullOrWhiteSpace(payment.CaptureTransferId) &&
                 transferIds.Contains(payment.CaptureTransferId!)) ||
                payment.Refunds.Any(r => transferIds.Contains(r.FinixTransferId));

            if (hasFinixMatch || !IsPaymentInRange(payment, from, to))
            {
                continue;
            }

            eshopOnly++;
            entries.Add(new ReconciliationEntry(
                payment.AuthorizationId ?? payment.CaptureTransferId ?? $"order-{payment.OrderId}",
                "eshop", "ORDER_PAYMENT", payment.Status.ToString(), payment.AmountCents, payment.Currency,
                payment.CreatedAt, payment.OrderId, payment.BuyerId, false,
                "Present in eShop but not returned by Finix for this range"));
        }

        var finixCount = transfers.Count + authorizations.Count;
        var matchedCount = matchedFinixIds.Count;
        var finixOnly = entries.Count(e => e.Kind != "eshop" && !e.Matched);

        return new ReconciliationReport(from, to, finixCount, matchedCount, finixOnly, eshopOnly, entries);
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw PaymentException.BadRequest("The caller identity is missing from the token.");
        }

        var normalized = NormalizeAndValidateCard(card);

        using var handle = await _locks.AcquireAsync($"identity:{buyerId}", cancellationToken);
        var identityId = await GetOrCreateBuyerIdentityAsync(buyerId, cancellationToken);
        var instrument = await _finix.CreatePaymentInstrumentAsync(identityId, buyerId, normalized,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(instrument.Id))
        {
            throw PaymentException.Upstream("Finix did not return a payment instrument id.");
        }

        var lastFour = !string.IsNullOrWhiteSpace(instrument.LastFour)
            ? instrument.LastFour!
            : normalized.Number.Length >= 4
                ? normalized.Number[^4..]
                : string.Empty;

        var saved = new SavedPaymentMethod(buyerId, instrument.Id, instrument.Brand ?? "UNKNOWN",
            lastFour, instrument.ExpirationMonth ?? normalized.ExpirationMonth,
            instrument.ExpirationYear ?? normalized.ExpirationYear, instrument.Name ?? normalized.Name,
            DateTimeOffset.UtcNow);

        _db.SavedPaymentMethods.Add(saved);
        await _db.SaveChangesAsync(cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw PaymentException.BadRequest("The caller identity is missing from the token.");
        }

        return await _db.SavedPaymentMethods
            .AsNoTracking()
            .Where(m => m.BuyerId == buyerId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw PaymentException.BadRequest("The caller identity is missing from the token.");
        }

        var saved = await _db.SavedPaymentMethods.FirstOrDefaultAsync(
            m => m.Id == paymentMethodId && m.BuyerId == buyerId && !m.IsDeleted,
            cancellationToken);

        if (saved is null)
        {
            throw PaymentException.NotFound($"Saved payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            // Already gone at Finix; still remove it locally.
        }

        saved.MarkDeleted();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Order?> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment)
            .ThenInclude(p => p!.Refunds)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    private async Task<string> GetOrCreateBuyerIdentityAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var existing = await _db.BuyerFinixIdentities
            .FirstOrDefaultAsync(i => i.BuyerId == buyerId, cancellationToken);

        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        var identity = await _finix.CreateBuyerIdentityAsync(buyerId, cancellationToken);
        if (string.IsNullOrWhiteSpace(identity.Id))
        {
            throw PaymentException.Upstream("Finix did not return an identity id.");
        }

        var record = new BuyerFinixIdentity(buyerId, identity.Id);
        _db.BuyerFinixIdentities.Add(record);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return identity.Id;
        }
        catch (DbUpdateException)
        {
            _db.Entry(record).State = EntityState.Detached;
            var raced = await _db.BuyerFinixIdentities
                .FirstOrDefaultAsync(i => i.BuyerId == buyerId, cancellationToken);
            if (raced is not null)
            {
                return raced.FinixIdentityId;
            }

            throw;
        }
    }

    private async Task<string> CaptureOrGetExistingTransferAsync(int orderId, Payment payment,
        FinixAuthorization authorization, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(authorization.Transfer))
        {
            return authorization.Transfer!;
        }

        try
        {
            var capture = await _finix.CaptureAuthorizationAsync(authorization.Id, payment.AmountCents,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(capture.Transfer))
            {
                return capture.Transfer!;
            }

            if (string.Equals(capture.State, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                throw PaymentException.Conflict(
                    $"Finix could not capture authorization {authorization.Id}: {capture.FailureMessage ?? capture.FailureCode ?? "FAILED"}.",
                    "CAPTURE_FAILED");
            }
        }
        catch (PaymentException ex) when (ex.StatusCode is 400 or 402 or 404 or 409 or 422)
        {
            var latest = await TryGetAuthorizationAsync(authorization.Id, cancellationToken);
            if (!string.IsNullOrWhiteSpace(latest?.Transfer))
            {
                return latest!.Transfer!;
            }

            payment.SetAuthorizationStatus(latest?.State);
            throw PaymentException.Conflict(
                $"Order {orderId} cannot be fulfilled: Finix authorization {authorization.Id} is " +
                $"{latest?.State ?? "no longer capturable"}{(latest is not null && IsAuthorizationVoided(latest) ? " (voided)" : "")}. " +
                $"The shopper must authorize payment again with POST /api/orders/{orderId}/pay before fulfilment can be retried. {ex.Message}",
                "AUTHORIZATION_NOT_CAPTURABLE");
        }

        var afterCapture = await TryGetAuthorizationAsync(authorization.Id, cancellationToken);
        if (!string.IsNullOrWhiteSpace(afterCapture?.Transfer))
        {
            return afterCapture!.Transfer!;
        }

        throw PaymentException.Conflict(
            $"Finix accepted the capture for order {orderId} but did not return a transfer id. Retry fulfilment; if the problem persists contact Finix support.",
            "CAPTURE_TRANSFER_MISSING");
    }

    private async Task<FinixTransfer> WaitForTransferAsync(string transferId,
        CancellationToken cancellationToken)
    {
        FinixTransfer? last = null;

        for (var attempt = 0; attempt < 75; attempt++)
        {
            last = await _finix.GetTransferAsync(transferId, cancellationToken);

            if (string.Equals(last.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                return last;
            }

            if (string.Equals(last.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(last.State, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(last.State, "RETURNED", StringComparison.OrdinalIgnoreCase))
            {
                var detail = last.FailureMessage ?? last.FailureCode ?? last.State;
                throw PaymentException.Conflict(
                    $"Finix capture transfer {transferId} is {detail}. The order was not marked fulfilled; the authorization may no longer be capturable.",
                    "CAPTURE_NOT_COMPLETED");
            }

            if (attempt < 74)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }

        // PENDING is an accepted capture that is still settling at Finix. Report the transfer's
        // current state and amount rather than pretending the capture did not happen.
        return last!;
    }

    private async Task<decimal> GetFeeCentsAsync(string transferId, CancellationToken cancellationToken)
    {
        try
        {
            var fees = await _finix.ListFeesForTransferAsync(transferId, cancellationToken);
            return fees.Sum(f => f.Amount ?? 0m);
        }
        catch (PaymentException)
        {
            // The capture is already final. Do not fail fulfilment because the fee listing was
            // temporarily unavailable; report zero rather than inventing a fee.
            return 0m;
        }
    }

    private async Task<FinixAuthorization?> TryGetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException)
        {
            return null;
        }
    }

    private static void EnsureAuthorizationCapturable(int orderId, FinixAuthorization authorization,
        Payment payment)
    {
        if (IsAuthorizationVoided(authorization))
        {
            throw PaymentException.Conflict(
                $"Order {orderId} cannot be fulfilled: Finix authorization {authorization.Id} is voided. " +
                $"The shopper must authorize payment again with POST /api/orders/{orderId}/pay before fulfilment can be retried.",
                "AUTHORIZATION_NOT_CAPTURABLE");
        }

        if (!string.IsNullOrWhiteSpace(authorization.Transfer))
        {
            return;
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            throw PaymentException.Conflict(
                $"Order {orderId} cannot be fulfilled: Finix authorization {authorization.Id} is {authorization.State ?? "UNKNOWN"}. " +
                $"The shopper must authorize payment again with POST /api/orders/{orderId}/pay before fulfilment can be retried.",
                "AUTHORIZATION_NOT_CAPTURABLE");
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            throw PaymentException.Conflict(
                $"Order {orderId} cannot be fulfilled: Finix authorization {authorization.Id} expired at {authorization.ExpiresAt:O}. " +
                $"The shopper must authorize payment again with POST /api/orders/{orderId}/pay before fulfilment can be retried.",
                "AUTHORIZATION_EXPIRED");
        }

        payment.SetAuthorizationStatus(authorization.State);
    }

    private static bool IsAuthorizationVoided(FinixAuthorization authorization) =>
        authorization.IsVoid == true ||
        string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase);

    private static bool IsPaymentInRange(Payment payment, DateTimeOffset from, DateTimeOffset to)
    {
        return InRange(payment.CreatedAt, from, to) ||
               (payment.CapturedAt.HasValue && InRange(payment.CapturedAt.Value, from, to)) ||
               payment.Refunds.Any(r => InRange(r.CreatedAt, from, to));
    }

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;

    private static Address BuildAddress(ShippingAddressCommand? address)
    {
        if (address is null)
        {
            return new Address("1 Main Street", "Seattle", "WA", "USA", "98101");
        }

        if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw PaymentException.BadRequest(
                "ShippingAddress requires street, city, country and zipCode.");
        }

        return new Address(address.Street, address.City, address.State ?? string.Empty, address.Country,
            address.ZipCode);
    }

    private static CardDetails NormalizeAndValidateCard(CardDetails card)
    {
        var number = new string(card.Number.Where(char.IsDigit).ToArray());
        if (number.Length < 12 || number.Length > 19)
        {
            throw PaymentException.BadRequest("The card number is not valid.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw PaymentException.BadRequest("expirationMonth must be between 1 and 12.");
        }

        if (card.ExpirationYear < 2000 || card.ExpirationYear > 9999)
        {
            throw PaymentException.BadRequest("expirationYear must be a four-digit year.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw PaymentException.BadRequest("The cardholder name is required.");
        }

        if (!string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            var securityCode = new string(card.SecurityCode.Where(char.IsDigit).ToArray());
            if (securityCode.Length is < 3 or > 4)
            {
                throw PaymentException.BadRequest("securityCode must be 3 or 4 digits.");
            }

            card = card with { SecurityCode = securityCode };
        }

        return card with { Number = number, Name = card.Name.Trim() };
    }

    private static long ToCents(decimal dollars) =>
        checked((long)decimal.Round(dollars * 100m, 0, MidpointRounding.AwayFromZero));
}
