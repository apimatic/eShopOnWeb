using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalGateway _payPal;
    private readonly PayPalOptions _options;

    public PaymentService(CatalogContext db, IPayPalGateway payPal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(string ownerId, PlaceOrderRequest request,
        CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new PaymentOperationException(400, "An order must contain at least one catalog item.");
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw new PaymentOperationException(400, "Catalog item ids and quantities must be positive.");

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await _db.CatalogItems
            .Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(ct);
        if (catalogItems.Count != requested.Count)
            throw new PaymentOperationException(400, "One or more catalog items do not exist.");

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            requested[item.Id])).ToList();
        var address = new Address("Not supplied", "Not supplied", string.Empty, "Not supplied", "00000");
        var order = new Order(ownerId, address, orderItems, awaitingPayment: true);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        var payment = new PaymentRecord(order.Id, _options.Currency.ToUpperInvariant(), order.Total());
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);
        return new PlaceOrderResponse(order.Id, order.Status.ToString(), order.Total(), payment.Currency);
    }

    public async Task<PaymentStateResponse> PayAsync(string ownerId, int orderId, PayOrderRequest request,
        CancellationToken ct)
    {
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw new PaymentOperationException(400, "Supply exactly one of card or paymentMethodId.");
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var (order, payment) = await OwnedOrderAsync(ownerId, orderId, ct);
            if (payment.State == PaymentStates.Authorized || payment.State == PaymentStates.Captured)
                return Project(order, payment);
            if (order.Status is OrderStatus.Cancelled or OrderStatus.Fulfilled)
                throw new PaymentOperationException(409, "This order can no longer be authorized.");

            ProviderCard providerCard;
            if (request.PaymentMethodId is int methodId)
            {
                var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
                    x => x.Id == methodId && x.OwnerId == ownerId && x.DeletedAt == null && x.PayPalTokenId != null, ct)
                    ?? throw new PaymentOperationException(404, "The saved payment method was not found.");
                providerCard = new ProviderCard(string.Empty, string.Empty, string.Empty, string.Empty,
                    new BillingAddressInput(string.Empty, null, string.Empty, string.Empty, string.Empty, "US"),
                    method.PayPalTokenId);
            }
            else
            {
                ValidateCard(request.Card!);
                providerCard = ToProvider(request.Card!);
            }

            AuthorizationResult result;
            try
            {
                result = await _payPal.AuthorizeAsync(order.Id, payment.OrderAmount, payment.Currency,
                    providerCard, payment.CreateRequestId, payment.AuthorizeRequestId, ct);
            }
            catch (PaymentProviderException ex)
            {
                payment.RecordFailure(ex.Message);
                await _db.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            payment.RecordPayPalOrder(result.PayPalOrderId, result.OrderStatus);
            if (result.PayerActionRequired)
            {
                payment.RecordChallenge();
                await _db.SaveChangesAsync(ct);
                throw new PaymentOperationException(409,
                    "PayPal requires browser approval for this card; the headless payment flow has stopped.");
            }
            if (result.AuthorizationId is null || result.Amount is null)
                throw new PaymentProviderException("PayPal returned an incomplete authorization.");
            payment.RecordAuthorization(result.AuthorizationId, result.AuthorizationStatus, result.Amount.Value,
                result.CreatedAt, result.ExpiresAt);
            order.MarkAuthorized();
            await _db.SaveChangesAsync(ct);
            return Project(order, payment);
        }
        finally { gate.Release(); }
    }

    public async Task<PaymentStateResponse> FulfilAsync(int orderId, CancellationToken ct)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await _db.Orders.Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.Id == orderId, ct)
                ?? throw new PaymentOperationException(404, "The order was not found.");
            var payment = await PaymentAsync(orderId, ct);
            if (payment.State == PaymentStates.Captured)
                return Project(order, payment);
            if (order.Status != OrderStatus.Authorized || payment.AuthorizationId is null)
                throw new PaymentOperationException(409, "The order must have an active authorization before fulfilment.");

            var current = await _payPal.GetAuthorizationAsync(payment.AuthorizationId, ct);
            var created = payment.AuthorizationCreatedAt ?? current.CreatedAt ?? DateTimeOffset.UtcNow;
            var age = DateTimeOffset.UtcNow - created;
            if (age >= TimeSpan.FromDays(29))
                throw RenewalRequired();
            if (age >= TimeSpan.FromDays(3) ||
                (current.ExpiresAt is not null && current.ExpiresAt <= DateTimeOffset.UtcNow))
            {
                try
                {
                    current = await _payPal.ReauthorizeAsync(current.Id, payment.OrderAmount, payment.Currency,
                        payment.ReserveReauthorization(), ct);
                    payment.RecordAuthorization(current.Id, current.Status, current.Amount,
                        current.CreatedAt, current.ExpiresAt, renewed: true);
                    await _db.SaveChangesAsync(ct);
                }
                catch (PaymentProviderException)
                {
                    payment.RecordFailure("The authorization can no longer be renewed; ask the shopper to pay again.");
                    await _db.SaveChangesAsync(CancellationToken.None);
                    throw RenewalRequired();
                }
            }

            ProviderCapture capture;
            try
            {
                capture = await _payPal.CaptureAsync(current.Id, payment.OrderAmount, payment.Currency,
                    payment.CaptureRequestId, ct);
            }
            catch (PaymentProviderException) when (age >= TimeSpan.FromDays(3))
            {
                throw RenewalRequired();
            }
            if (capture.Amount != payment.OrderAmount)
                throw new PaymentProviderException("PayPal returned a capture amount that does not match the order.");
            payment.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.Net,
                capture.CreatedAt);
            order.MarkFulfilled();
            await _db.SaveChangesAsync(ct);
            return Project(order, payment);
        }
        finally { gate.Release(); }
    }

    public async Task<PaymentStateResponse> CancelAsync(int orderId, CancellationToken ct)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await _db.Orders.Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.Id == orderId, ct)
                ?? throw new PaymentOperationException(404, "The order was not found.");
            var payment = await PaymentAsync(orderId, ct);
            if (order.Status == OrderStatus.Cancelled) return Project(order, payment);
            if (order.Status == OrderStatus.Fulfilled || payment.CaptureId is not null)
                throw new PaymentOperationException(409, "A fulfilled order must be refunded, not cancelled.");
            if (payment.AuthorizationId is not null)
            {
                var status = await _payPal.VoidAsync(payment.AuthorizationId, payment.VoidRequestId, ct);
                if (!string.Equals(status, "VOIDED", StringComparison.OrdinalIgnoreCase))
                    throw new PaymentProviderException("PayPal did not confirm that the authorization was voided.");
                payment.RecordVoid(status);
            }
            order.MarkCancelled();
            await _db.SaveChangesAsync(ct);
            return Project(order, payment);
        }
        finally { gate.Release(); }
    }

    public async Task<RefundResponse> RefundAsync(string ownerId, int orderId, RefundOrderRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 108)
            throw new PaymentOperationException(400, "A non-empty idempotencyKey of at most 108 characters is required.");
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var (order, payment) = await OwnedOrderAsync(ownerId, orderId, ct);
            if (order.Status != OrderStatus.Fulfilled || payment.CaptureId is null || payment.CapturedAmount is null)
                throw new PaymentOperationException(409, "Only a fulfilled, captured order can be refunded.");
            var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
            if (existing is not null)
            {
                if (existing.State == "Completed")
                    return new RefundResponse(existing.Id, existing.PayPalStatus ?? existing.State,
                        existing.RefundedAmount ?? existing.RequestedAmount);
                if (existing.State == "Failed")
                    throw new PaymentOperationException(409, "The prior refund under this idempotency key failed.");
            }

            var reserved = payment.Refunds.Where(x => x.State is "Pending" or "Completed")
                .Sum(x => x.RequestedAmount);
            var remaining = payment.CapturedAmount.Value - reserved;
            var amount = request.Amount ?? remaining;
            if (amount <= 0 || amount > remaining)
                throw new PaymentOperationException(409, $"The maximum refundable amount is {remaining:0.00} {payment.Currency}.");
            var refund = existing ?? new PaymentRefund(payment.Id, request.IdempotencyKey, amount);
            if (existing is null)
            {
                _db.PaymentRefunds.Add(refund);
                payment.ReserveRefund();
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    throw new PaymentOperationException(409,
                        "Another refund changed the available balance; retry with a new idempotency key.");
                }
            }

            try
            {
                var provider = await _payPal.RefundAsync(payment.CaptureId,
                    amount == remaining && reserved == 0 ? null : amount,
                    payment.Currency, request.IdempotencyKey, ct);
                refund.Complete(provider.Id, provider.Status, provider.Amount, provider.CreatedAt);
                await _db.SaveChangesAsync(ct);
                return new RefundResponse(refund.Id, provider.Status ?? "UNKNOWN", provider.Amount);
            }
            catch (PaymentProviderException)
            {
                // Keep the reservation pending: the write may have reached PayPal and a replay uses the same key.
                await _db.SaveChangesAsync(CancellationToken.None);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<PaymentStateResponse>> MyOrdersAsync(string ownerId, CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking().Include(x => x.OrderItems)
            .Where(x => x.BuyerId == ownerId).OrderByDescending(x => x.OrderDate).ToListAsync(ct);
        var ids = orders.Select(x => x.Id).ToList();
        var payments = await _db.Payments.AsNoTracking().Include(x => x.Refunds)
            .Where(x => ids.Contains(x.OrderId)).ToDictionaryAsync(x => x.OrderId, ct);
        return orders.Where(x => payments.ContainsKey(x.Id)).Select(x => Project(x, payments[x.Id])).ToList();
    }

    public async Task<PaymentMethodResponse> SaveMethodAsync(string ownerId, SavePaymentMethodRequest request,
        CancellationToken ct)
    {
        ValidateCard(request.Card);
        var method = new SavedPaymentMethod(ownerId, Guid.NewGuid().ToString("N"));
        _db.SavedPaymentMethods.Add(method);
        await _db.SaveChangesAsync(ct);
        var provider = await _payPal.SaveMethodAsync(ownerId, ToProvider(request.Card), method.CreateRequestId, ct);
        method.Activate(provider.TokenId, provider.CustomerId, provider.Brand, provider.LastDigits,
            provider.Expiry, provider.CardType, provider.VerificationStatus);
        await _db.SaveChangesAsync(ct);
        return Project(method);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> ListMethodsAsync(string ownerId, CancellationToken ct)
    {
        var methods = await _db.SavedPaymentMethods
            .Where(x => x.OwnerId == ownerId && x.DeletedAt == null && x.PayPalTokenId != null)
            .ToListAsync(ct);
        foreach (var group in methods.Where(x => x.PayPalCustomerId != null).GroupBy(x => x.PayPalCustomerId!))
        {
            var remote = await _payPal.ListMethodsAsync(group.Key, ct);
            foreach (var local in group)
            {
                var current = remote.SingleOrDefault(x => x.TokenId == local.PayPalTokenId);
                if (current is not null)
                    local.Activate(current.TokenId, current.CustomerId, current.Brand, current.LastDigits,
                        current.Expiry, current.CardType, current.VerificationStatus);
            }
        }
        await _db.SaveChangesAsync(ct);
        return methods.Where(x => x.IsActive).Select(Project).ToList();
    }

    public async Task DeleteMethodAsync(string ownerId, int methodId, CancellationToken ct)
    {
        var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
            x => x.Id == methodId && x.OwnerId == ownerId, ct)
            ?? throw new PaymentOperationException(404, "The saved payment method was not found.");
        if (method.DeletedAt is not null) return;
        if (method.PayPalTokenId is not null) await _payPal.DeleteMethodAsync(method.PayPalTokenId, ct);
        method.Delete();
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        if (from > to) throw new PaymentOperationException(400, "from must be earlier than or equal to to.");
        var provider = await _payPal.SearchTransactionsAsync(from, to, ct);
        var payments = await _db.Payments.AsNoTracking().Include(x => x.Refunds)
            .Where(x => (x.AuthorizationCreatedAt >= from && x.AuthorizationCreatedAt <= to) ||
                        (x.CaptureCreatedAt >= from && x.CaptureCreatedAt <= to) ||
                        x.Refunds.Any(r => r.ProviderCreatedAt >= from && r.ProviderCreatedAt <= to))
            .ToListAsync(ct);
        var local = new Dictionary<string, (int OrderId, string? Status, decimal? Amount, DateTimeOffset? Time)>();
        foreach (var p in payments)
        {
            if (p.AuthorizationId is not null && p.AuthorizationCreatedAt >= from && p.AuthorizationCreatedAt <= to)
                local[p.AuthorizationId] = (p.OrderId, p.AuthorizationStatus, p.AuthorizedAmount, p.AuthorizationCreatedAt);
            if (p.CaptureId is not null && p.CaptureCreatedAt >= from && p.CaptureCreatedAt <= to)
                local[p.CaptureId] = (p.OrderId, p.CaptureStatus, p.CapturedAmount, p.CaptureCreatedAt);
            foreach (var refund in p.Refunds.Where(x => x.PayPalRefundId != null &&
                         x.ProviderCreatedAt >= from && x.ProviderCreatedAt <= to))
                local[refund.PayPalRefundId!] = (p.OrderId, refund.PayPalStatus, refund.RefundedAmount,
                    refund.ProviderCreatedAt);
        }

        var lines = new List<ReconciliationLine>();
        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transaction in provider)
        {
            var key = local.ContainsKey(transaction.Id) ? transaction.Id :
                transaction.ReferenceId is not null && local.ContainsKey(transaction.ReferenceId) ? transaction.ReferenceId : null;
            if (key is not null)
            {
                matched.Add(key);
                lines.Add(new ReconciliationLine("Matched", transaction.Id, local[key].OrderId,
                    transaction.Status, transaction.Amount, transaction.Fee, transaction.Currency, transaction.TransactionTime));
            }
            else
                lines.Add(new ReconciliationLine("PayPalOnly", transaction.Id, null, transaction.Status,
                    transaction.Amount, transaction.Fee, transaction.Currency, transaction.TransactionTime));
        }
        foreach (var pair in local.Where(x => !matched.Contains(x.Key)))
        {
            var classification = pair.Value.Time >= DateTimeOffset.UtcNow.AddHours(-3)
                ? "PendingPayPalReporting" : "EShopOnly";
            lines.Add(new ReconciliationLine(classification, pair.Key, pair.Value.OrderId,
                pair.Value.Status, pair.Value.Amount, null, _options.Currency, pair.Value.Time));
        }
        return new ReconciliationResponse(from, to, lines);
    }

    private async Task<(Order Order, PaymentRecord Payment)> OwnedOrderAsync(string ownerId, int orderId,
        CancellationToken ct)
    {
        var order = await _db.Orders.Include(x => x.OrderItems)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == ownerId, ct)
            ?? throw new PaymentOperationException(404, "The order was not found.");
        return (order, await PaymentAsync(orderId, ct));
    }

    private async Task<PaymentRecord> PaymentAsync(int orderId, CancellationToken ct) =>
        await _db.Payments.Include(x => x.Refunds).SingleOrDefaultAsync(x => x.OrderId == orderId, ct)
        ?? throw new PaymentOperationException(404, "The payment record was not found.");

    private static PaymentStateResponse Project(Order order, PaymentRecord payment) => new(
        order.Id, order.Status.ToString(), payment.State, payment.OrderAmount, payment.Currency,
        payment.PayPalOrderId, payment.AuthorizationId, payment.AuthorizationStatus,
        payment.CaptureId, payment.CaptureStatus, payment.CapturedAmount, payment.PayPalFee,
        payment.MerchantNet,
        payment.Refunds.Where(x => x.State == "Completed").Sum(x => x.RefundedAmount ?? 0m),
        payment.LastProviderError);

    private static PaymentMethodResponse Project(SavedPaymentMethod method) => new(
        method.Id, method.Brand, method.LastDigits, method.Expiry, method.CardType, method.VerificationStatus);

    private static ProviderCard ToProvider(CardInput card) => new(card.Name, card.Number.Replace(" ", string.Empty),
        card.Expiry, card.SecurityCode, card.BillingAddress);

    private static void ValidateCard(CardInput card)
    {
        if (string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode) ||
            card.BillingAddress is null || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
            throw new PaymentOperationException(400, "Complete card and billing-address details are required.");
    }

    private static PaymentOperationException RenewalRequired() => new(409,
        "The authorization can no longer be renewed. Ask the shopper to pay again before fulfilment.");
}

public sealed class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    public int StatusCode { get; }
}
