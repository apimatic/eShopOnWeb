using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IPaymentSettings _settings;

    // Serialise money-moving operations per order so a double-click can never authorize,
    // capture or refund twice within this process. PayPal's own idempotency keys guard the
    // remote side; this guards the local read-modify-write.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalGateway payPal,
        IPaymentSettings settings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _settings = settings;
    }

    public async Task<OrderPayment> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, ShippingAddressInput? shipTo, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentException("Every order line must have a quantity of at least one.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _settings.CurrencyCode);
        return await _paymentRepository.AddAsync(payment, ct);
    }

    public async Task<OrderPayment> AuthorizeAsync(string buyerId, int orderId, PaymentSourceInstruction source, CancellationToken ct = default)
    {
        var gate = await AcquireAsync(orderId, ct);
        try
        {
            var order = await LoadOwnedOrderAsync(buyerId, orderId, ct);
            var payment = await LoadPaymentAsync(orderId, ct);

            // Idempotent in effect: an existing hold is returned, never re-placed.
            if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
                return payment;
            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
                throw new PaymentException($"Order {orderId} has already been paid and cannot be authorized again.");
            if (payment.Status == PaymentStatus.Voided)
                throw new PaymentException($"Order {orderId} was cancelled and can no longer be paid.");

            // A named saved card must belong to the caller.
            var resolvedSource = source;
            if (source.VaultId is not null)
            {
                var card = (await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct))
                    .FirstOrDefault(c => c.PayPalVaultId == source.VaultId);
                if (card is null)
                    throw new PaymentException("The requested saved card was not found for this shopper.");
                resolvedSource = PaymentSourceInstruction.FromVault(card.PayPalVaultId);
            }
            else if (source.Card is null)
            {
                throw new PaymentException("Provide either card details or a saved card id to pay with.");
            }

            var instruction = new AuthorizeInstruction(
                PayPalRequestId: NewRequestId("auth"),
                Amount: payment.Amount,
                CurrencyCode: payment.CurrencyCode,
                OrderReference: orderId.ToString(CultureInfo.InvariantCulture),
                Source: resolvedSource);

            var result = await _payPal.AuthorizeAsync(instruction, ct);

            switch (result.Status)
            {
                case AuthorizeStatus.Authorized:
                    payment.RecordAuthorization(result.PayPalOrderId!, result.AuthorizationId!, result.AuthorizationStatus ?? "CREATED", result.ExpiresAt);
                    await _paymentRepository.UpdateAsync(payment, ct);
                    return payment;

                case AuthorizeStatus.ChallengeRequired:
                    throw new PaymentChallengeRequiredException(
                        $"PayPal requires the shopper to approve this card payment in a browser (3-D Secure challenge). " +
                        $"Order {orderId} was not authorized. A browser approval flow is out of scope for this API.");

                default:
                    payment.MarkFailed();
                    await _paymentRepository.UpdateAsync(payment, ct);
                    throw new PaymentException($"PayPal declined the card for order {orderId}: {result.FailureReason ?? "payment failed"}.");
            }
        }
        finally
        {
            Release(gate);
        }
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var gate = await AcquireAsync(orderId, ct);
        try
        {
            var payment = await LoadPaymentAsync(orderId, ct);

            // Idempotent: an already-captured order is returned unchanged.
            if (payment.CaptureId is not null)
                return payment;
            if (payment.Status == PaymentStatus.Voided)
                throw new PaymentException($"Order {orderId} was cancelled and cannot be fulfilled.");
            if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
                throw new PaymentException($"Order {orderId} is not authorized. Authorize payment before fulfilling.");

            var authId = payment.AuthorizationId;

            // Renew a hold that has already gone stale rather than failing the capture.
            if (payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
                authId = await ReauthorizeAsync(payment, orderId, ct);

            CaptureResult capture;
            try
            {
                capture = await _payPal.CaptureAsync(authId, payment.Amount, payment.CurrencyCode,
                    payment.InvoiceNumber, NewRequestId("capture"), ct);
            }
            catch (PayPalApiException ex) when (IsStaleAuthorization(ex))
            {
                authId = await ReauthorizeAsync(payment, orderId, ct);
                capture = await _payPal.CaptureAsync(authId, payment.Amount, payment.CurrencyCode,
                    payment.InvoiceNumber, NewRequestId("capture"), ct);
            }

            payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            await _paymentRepository.UpdateAsync(payment, ct);
            return payment;
        }
        finally
        {
            Release(gate);
        }
    }

    private async Task<string> ReauthorizeAsync(OrderPayment payment, int orderId, CancellationToken ct)
    {
        try
        {
            var info = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, NewRequestId("reauth"), ct);
            payment.RecordReauthorization(info.AuthorizationId, info.Status, info.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            return info.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationUnrenewableException(
                $"The authorization for order {orderId} has expired and can no longer be renewed ({ex.Issue ?? ex.Name ?? "not renewable"}). " +
                $"Ask the shopper to pay for the order again before it can be fulfilled.");
        }
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var gate = await AcquireAsync(orderId, ct);
        try
        {
            var payment = await LoadPaymentAsync(orderId, ct);

            if (payment.Status == PaymentStatus.Voided)
                return payment; // idempotent
            if (payment.CaptureId is not null || payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
                throw new PaymentException($"Order {orderId} has already been fulfilled; refund it instead of cancelling.");

            if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
            {
                try
                {
                    await _payPal.VoidAsync(payment.AuthorizationId, NewRequestId("void"), ct);
                }
                catch (PayPalApiException ex) when (IsAlreadyVoided(ex))
                {
                    // Already released at PayPal — treat as success.
                }
            }

            payment.RecordVoid();
            await _paymentRepository.UpdateAsync(payment, ct);
            return payment;
        }
        finally
        {
            Release(gate);
        }
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var gate = await AcquireAsync(orderId, ct);
        try
        {
            await LoadOwnedOrderAsync(buyerId, orderId, ct);
            var payment = await LoadPaymentAsync(orderId, ct);

            // Idempotent by caller key: repeating a request returns the same refund.
            var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
                return existing;

            if (payment.CaptureId is null)
                throw new PaymentException($"Order {orderId} has not been captured; there is nothing to refund.");

            var refundable = payment.RefundableAmount();
            if (refundable <= 0m)
                throw new PaymentException($"Order {orderId} has already been fully refunded.");

            var refundAmount = amount ?? refundable;
            if (refundAmount <= 0m)
                throw new PaymentException("Refund amount must be greater than zero.");
            if (refundAmount > refundable)
                throw new PaymentException($"Refund of {refundAmount:0.00} exceeds the {refundable:0.00} still refundable on order {orderId}.");

            // The PayPal-Request-Id is derived from the (globally unique) capture id and the
            // caller's key, so repeating the caller's key returns the same refund at PayPal too,
            // while the key stays unique across app restarts (which reset local order ids).
            // No invoice_id on refunds — PayPal enforces invoice-id uniqueness per merchant and
            // the capture already carries this order's invoice number. Correlation is by refund id.
            var result = await _payPal.RefundAsync(payment.CaptureId, refundAmount, payment.CurrencyCode,
                string.Empty, $"refund-{payment.CaptureId}-{idempotencyKey}", ct);

            var refund = new PaymentRefund(idempotencyKey, refundAmount, payment.CurrencyCode);
            refund.MarkCompleted(result.RefundId, result.Status);
            payment.AddRefund(refund);
            payment.RecomputeRefundStatus();
            await _paymentRepository.UpdateAsync(payment, ct);
            return refund;
        }
        finally
        {
            Release(gate);
        }
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), ct);
        var byOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.Id)
            .Select(o => new OrderWithPayment(o, byOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
            throw new PaymentException("The reconciliation 'to' date must not be earlier than 'from'.");

        var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(new AllOrderPaymentsSpecification(), ct);

        // eShop's own record of every PayPal-side id we expect to see, mapped back to its order.
        var eShopIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in payments)
        {
            if (p.CaptureId is not null) eShopIds[p.CaptureId] = p.OrderId;
            if (p.AuthorizationId is not null) eShopIds[p.AuthorizationId] = p.OrderId;
            foreach (var r in p.Refunds)
                if (r.PayPalRefundId is not null) eShopIds[r.PayPalRefundId] = p.OrderId;
        }

        var lines = new List<ReconciliationLine>();
        var matchedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in transactions)
        {
            int? orderId = null;
            var matchState = "PayPalOnly";
            if (t.TransactionId is not null && eShopIds.TryGetValue(t.TransactionId, out var oid))
            {
                orderId = oid;
                matchState = "Matched";
                matchedIds.Add(t.TransactionId);
            }
            else if (t.ReferenceId is not null && eShopIds.TryGetValue(t.ReferenceId, out var oid2))
            {
                orderId = oid2;
                matchState = "Matched";
                matchedIds.Add(t.ReferenceId);
            }

            lines.Add(new ReconciliationLine(
                t.TransactionId, t.EventCode, t.Status, t.Amount, t.CurrencyCode, t.FeeAmount, t.InitiationDate, orderId, matchState));
        }

        // eShop-side ids PayPal's report does not (yet) know about.
        foreach (var kvp in eShopIds)
        {
            if (matchedIds.Contains(kvp.Key)) continue;
            lines.Add(new ReconciliationLine(kvp.Key, null, null, null, null, null, null, kvp.Value, "EShopOnly"));
        }

        return new ReconciliationReport(
            from, to,
            PayPalTransactionCount: transactions.Count,
            MatchedCount: lines.Count(l => l.MatchState == "Matched"),
            PayPalOnlyCount: lines.Count(l => l.MatchState == "PayPalOnly"),
            EShopOnlyCount: lines.Count(l => l.MatchState == "EShopOnly"),
            Lines: lines);
    }

    // ----- helpers -----

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        // A shopper must never see or act on another's order; report as not-found either way.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new OrderNotFoundException(orderId);
        return order;
    }

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
            throw new OrderNotFoundException(orderId);
        return payment;
    }

    // A capture failed because the hold is no longer usable and should be renewed. PayPal
    // signals this with issues such as AUTHORIZATION_EXPIRED.
    private static bool IsStaleAuthorization(PayPalApiException ex)
    {
        var issue = ex.Issue?.ToUpperInvariant() ?? string.Empty;
        return issue.Contains("EXPIRED");
    }

    private static bool IsAlreadyVoided(PayPalApiException ex)
    {
        var issue = ex.Issue?.ToUpperInvariant() ?? string.Empty;
        return issue.Contains("VOIDED")
            || issue.Contains("ALREADY_CAPTURED")
            || issue.Contains("PREVIOUSLY_VOIDED");
    }

    // A fresh, unique PayPal idempotency key per attempt. Double-submit within a run is
    // already prevented by the per-order lock and the persisted payment state, so the key must
    // be unique per attempt — a value that stays constant across app restarts (which reset the
    // in-memory order ids) would collide with PayPal's 45-day key cache from an earlier run.
    private static string NewRequestId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static async Task<SemaphoreSlim> AcquireAsync(int orderId, CancellationToken ct)
    {
        var sem = _locks.GetOrAdd($"order-{orderId}", _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return sem;
    }

    private static void Release(SemaphoreSlim sem) => sem.Release();
}
