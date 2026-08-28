using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OperationLocks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalGateway _gateway;

    public PaymentService(CatalogContext db, IPayPalGateway gateway)
    {
        _db = db;
        _gateway = gateway;
    }

    public async Task<OrderView> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items,
        ShippingAddressInput? shippingAddress, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (items.Count == 0) throw Problem("empty_order", "At least one catalog item is required.", 400);
        if (items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw Problem("invalid_order_item", "Catalog item ids and quantities must be positive.", 400);
        if (items.GroupBy(x => x.CatalogItemId).Any(g => g.Count() > 1))
            throw Problem("duplicate_order_item", "Each catalog item may appear only once.", 400);

        var ids = items.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
            throw Problem("catalog_item_not_found", "One or more catalog items do not exist.", 404);

        var lines = items.Select(requested =>
        {
            var catalog = catalogItems.Single(x => x.Id == requested.CatalogItemId);
            return new OrderItem(
                new CatalogItemOrdered(catalog.Id, catalog.Name, catalog.PictureUri),
                decimal.Round(catalog.Price, 2, MidpointRounding.AwayFromZero),
                requested.Quantity);
        }).ToList();

        var address = shippingAddress is null
            ? new Address("Not supplied", "Not supplied", string.Empty, "Not supplied", "Not supplied")
            : new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State,
                shippingAddress.Country, shippingAddress.ZipCode);
        var order = new Order(buyerId, address, lines);
        order.InitializePayment(_gateway.Currency);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return ToView(order);
    }

    public async Task<OrderView> AuthorizeAsync(int orderId, string buyerId, AuthorizeOrderInput input,
        CancellationToken cancellationToken)
    {
        await using var operationLock = await LockAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);
        var payment = RequirePayment(order);

        if (payment.Status is OrderPaymentStatus.Authorized or OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
            return ToView(order);
        if (payment.Status == OrderPaymentStatus.Cancelled)
            throw Problem("order_cancelled", "A cancelled order cannot be paid.");
        if ((input.Card is null) == (input.PaymentMethodId is null))
            throw Problem("payment_source_required", "Supply either card details or one saved payment method.", 400);

        PaymentMethod? method = null;
        if (input.PaymentMethodId is int paymentMethodId)
        {
            method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId,
                cancellationToken);
            if (method is null || !method.IsActive || !string.Equals(method.BuyerId, buyerId, StringComparison.Ordinal))
                throw Problem("payment_method_not_found", "The saved payment method was not found.", 404);
        }

        var total = Money(order.Total());
        if (payment.ProviderOrderId is null)
        {
            var providerOrder = await _gateway.CreateOrderAsync(new GatewayCreateOrderRequest(
                order.Id, total, payment.Currency, StableKey($"{payment.OperationId}:create")), cancellationToken);
            payment.RecordProviderOrder(providerOrder.PayPalOrderId, providerOrder.Status);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var authorization = await _gateway.AuthorizeAsync(new GatewayAuthorizeRequest(
            order.Id,
            payment.ProviderOrderId!,
            total,
            payment.Currency,
            input.Card,
            method?.ProviderPaymentTokenId,
            StableKey($"{payment.OperationId}:authorize")), cancellationToken);
        EnsureMoney(total, payment.Currency, authorization.Amount, authorization.Currency, "authorization");
        payment.RecordProviderOrder(authorization.PayPalOrderId, authorization.PayPalOrderStatus);
        payment.RecordAuthorization(authorization.AuthorizationId, authorization.AuthorizationStatus,
            authorization.StatusReason, authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt,
            method?.Id);
        await _db.SaveChangesAsync(cancellationToken);
        return ToView(order);
    }

    public async Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var operationLock = await LockAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = RequirePayment(order);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Fulfilled) return ToView(order);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled)
            throw Problem("order_cancelled", "A cancelled order cannot be fulfilled.");

        if (payment.CaptureId is not null)
        {
            var currentCapture = await _gateway.GetCaptureAsync(payment.CaptureId, cancellationToken);
            RecordCapture(payment, currentCapture);
            if (!string.Equals(currentCapture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw Problem("capture_not_complete",
                    $"PayPal capture {currentCapture.CaptureId} is {currentCapture.Status}; retry fulfilment after it completes.");
            }

            order.MarkFulfilled();
            await _db.SaveChangesAsync(cancellationToken);
            return ToView(order);
        }

        if (payment.Status != OrderPaymentStatus.Authorized || payment.AuthorizationId is null
            || payment.ProviderOrderId is null || payment.AuthorizedAmount is null)
            throw Problem("payment_not_authorized", "The order must have an active authorization before fulfilment.");

        var current = await _gateway.GetAuthorizationAsync(payment.ProviderOrderId, payment.AuthorizationId,
            cancellationToken);
        EnsureMoney(payment.AuthorizedAmount.Value, payment.Currency, current.Amount, current.Currency, "authorization");
        if (current.AuthorizationStatus is "DENIED" or "VOIDED")
            throw Problem("authorization_not_renewable",
                $"PayPal authorization {current.AuthorizationId} is {current.AuthorizationStatus}; ask the shopper to pay again.");

        var now = DateTimeOffset.UtcNow;
        var originalCreated = payment.AuthorizationCreatedAt ?? current.CreatedAt;
        var absoluteExpiry = payment.AuthorizationExpiresAt ?? current.ExpiresAt ?? originalCreated?.AddDays(29);
        if (absoluteExpiry is not null && now >= absoluteExpiry.Value)
            throw Problem("authorization_not_renewable",
                $"PayPal authorization {current.AuthorizationId} has expired and can no longer be renewed; ask the shopper to pay again.");

        var outsideHonor = originalCreated is not null && now >= originalCreated.Value.AddDays(3);
        if (outsideHonor)
        {
            if (payment.AuthorizationRenewedAt is not null && now >= payment.AuthorizationRenewedAt.Value.AddDays(3))
                throw Problem("authorization_not_renewable",
                    $"PayPal authorization {current.AuthorizationId} is outside its renewed honor period; ask the shopper to pay again.");

            if (payment.AuthorizationRenewedAt is null)
            {
                current = await _gateway.ReauthorizeAsync(payment.ProviderOrderId, current.AuthorizationId,
                    payment.AuthorizedAmount.Value, payment.Currency,
                    StableKey($"{payment.OperationId}:reauthorize"), cancellationToken);
                EnsureMoney(payment.AuthorizedAmount.Value, payment.Currency, current.Amount, current.Currency,
                    "reauthorization");
                payment.RecordAuthorization(current.AuthorizationId, current.AuthorizationStatus,
                    current.StatusReason, current.Amount, current.CreatedAt, current.ExpiresAt,
                    payment.PaymentMethodId, renewed: true);
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        var capture = await _gateway.CaptureAsync(current.AuthorizationId,
            payment.AuthorizedAmount.Value, payment.Currency, StableKey($"{payment.OperationId}:capture"),
            cancellationToken);
        EnsureMoney(payment.AuthorizedAmount.Value, payment.Currency, capture.Amount, capture.Currency, "capture");
        RecordCapture(payment, capture);
        if (!string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            await _db.SaveChangesAsync(cancellationToken);
            throw Problem("capture_not_complete",
                $"PayPal capture {capture.CaptureId} is {capture.Status}; retry fulfilment after it completes.");
        }

        order.MarkFulfilled();
        await _db.SaveChangesAsync(cancellationToken);
        return ToView(order);
    }

    public async Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var operationLock = await LockAsync($"order:{orderId}", cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = RequirePayment(order);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled) return ToView(order);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Fulfilled || payment.CaptureId is not null)
            throw Problem("already_captured", "A captured order cannot be cancelled; refund it instead.");

        if (payment.AuthorizationId is not null && payment.Status == OrderPaymentStatus.Authorized)
        {
            var status = await _gateway.VoidAsync(payment.AuthorizationId,
                StableKey($"{payment.OperationId}:void"), cancellationToken);
            payment.RecordVoid(status);
        }
        else
        {
            payment.RecordVoid("VOIDED");
        }

        order.MarkCancelled();
        await _db.SaveChangesAsync(cancellationToken);
        return ToView(order);
    }

    public async Task<RefundView> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        await using var operationLock = await LockAsync($"order:{orderId}", cancellationToken);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw Problem("idempotency_key_required", "An idempotency key is required.", 400);
        if (amount is <= 0) throw Problem("invalid_refund_amount", "Refund amount must be positive.", 400);

        var order = await LoadOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);
        var payment = RequirePayment(order);
        if (payment.CaptureId is null || payment.CapturedAmount is null)
            throw Problem("payment_not_captured", "Only a captured payment can be refunded.");

        var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            if (amount is not null && Money(amount.Value) != existing.Amount)
                throw Problem("idempotency_key_conflict", "This idempotency key was used with a different amount.");
            if (existing.ProviderRefundId is not null || existing.Status == "FAILED") return ToView(existing);
        }

        var remaining = Money(payment.CapturedAmount.Value - payment.RefundedAmount);
        var refundAmount = Money(amount ?? remaining);
        if (refundAmount <= 0 || refundAmount > remaining)
            throw Problem("refund_exceeds_capture", $"At most {remaining.ToString("F2", CultureInfo.InvariantCulture)} {payment.Currency} remains refundable.");

        var refund = existing ?? payment.ReserveRefund(idempotencyKey, refundAmount);
        if (existing is null) await _db.SaveChangesAsync(cancellationToken);

        try
        {
            decimal? sendAmount = amount is null && payment.Refunds.Count == 1 && refundAmount == payment.CapturedAmount
                ? null : refundAmount;
            var provider = await _gateway.RefundAsync(payment.CaptureId, sendAmount, payment.Currency,
                idempotencyKey, cancellationToken);
            EnsureMoney(refundAmount, payment.Currency, provider.Amount, provider.Currency, "refund");
            refund.RecordProviderResult(provider.RefundId, provider.Status, provider.StatusReason);
            payment.RefreshRefundState();
            await _db.SaveChangesAsync(cancellationToken);
            return ToView(refund);
        }
        catch (PaymentOperationException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            refund.MarkFailed(ex.Message);
            payment.RefreshRefundState();
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var orders = await _db.Orders.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .Include(x => x.Payment).ThenInclude(x => x!.Refunds)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(ToView).ToList();
    }

    public async Task<SavedPaymentMethodView> SavePaymentMethodAsync(string buyerId, CardInput card,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        await using var operationLock = await LockAsync($"buyer:{buyerId}:vault", cancellationToken);
        var saved = await _gateway.SaveCardAsync(buyerId, card, Guid.NewGuid().ToString(), cancellationToken);
        var existing = await _db.PaymentMethods.SingleOrDefaultAsync(
            x => x.BuyerId == buyerId && x.ProviderPaymentTokenId == saved.PaymentTokenId,
            cancellationToken);
        if (existing is not null) return ToView(existing);

        var method = new PaymentMethod(buyerId, saved.PaymentTokenId, saved.CustomerId, saved.Brand,
            saved.LastDigits, saved.Expiry, saved.CardholderName, saved.CardType);
        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return ToView(method);
    }

    public async Task<IReadOnlyList<SavedPaymentMethodView>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        return await _db.PaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.IsActive)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new SavedPaymentMethodView(x.Id, x.Brand, x.LastDigits, x.Expiry,
                x.CardholderName, x.CardType))
            .ToListAsync(cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        await using var operationLock = await LockAsync($"payment-method:{paymentMethodId}", cancellationToken);
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId,
            cancellationToken);
        if (method is null || !method.IsActive || method.BuyerId != buyerId)
            throw Problem("payment_method_not_found", "The saved payment method was not found.", 404);
        await _gateway.DeleteCardAsync(method.ProviderPaymentTokenId, cancellationToken);
        method.Deactivate();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReconciliationEntry>> ReconcileAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from >= to) throw Problem("invalid_date_range", "from must be earlier than to.", 400);
        var provider = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _db.Orders.AsNoTracking()
            .Where(x => x.OrderDate <= to && x.Payment != null)
            .Include(x => x.Payment).ThenInclude(x => x!.Refunds)
            .ToListAsync(cancellationToken);

        var local = new List<(int OrderId, string Type, string Id, decimal? Amount, DateTimeOffset? Date)>();
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            if (payment.AuthorizationId is not null)
                local.Add((order.Id, "authorization", payment.AuthorizationId, payment.AuthorizedAmount,
                    payment.AuthorizationCreatedAt));
            if (payment.CaptureId is not null)
                local.Add((order.Id, "capture", payment.CaptureId, payment.CapturedAmount, payment.CapturedAt));
            local.AddRange(payment.Refunds.Where(x => x.ProviderRefundId is not null)
                .Select(x => (order.Id, "refund", x.ProviderRefundId!, (decimal?)x.Amount, (DateTimeOffset?)x.UpdatedAt)));
        }

        local = local.Where(x => x.Date is null || (x.Date >= from && x.Date <= to)).ToList();
        var entries = new List<ReconciliationEntry>();
        var matchedLocal = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transaction in provider)
        {
            var match = local.FirstOrDefault(x => x.Id == transaction.TransactionId
                || x.Id == transaction.PayPalReferenceId);
            if (!string.IsNullOrEmpty(match.Id))
            {
                matchedLocal.Add(match.Id);
                entries.Add(new ReconciliationEntry("Matched", match.OrderId, match.Type, match.Id,
                    match.Amount, transaction));
            }
            else
            {
                entries.Add(new ReconciliationEntry("PayPalOnly", null, null, null, null, transaction));
            }
        }

        var lagCutoff = DateTimeOffset.UtcNow.AddHours(-3);
        entries.AddRange(local.Where(x => !matchedLocal.Contains(x.Id)).Select(x =>
            new ReconciliationEntry(x.Date >= lagCutoff ? "PendingPayPalReporting" : "eShopOnly",
                x.OrderId, x.Type, x.Id, x.Amount, null)));
        return entries;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(x => x.OrderItems)
            .Include(x => x.Payment).ThenInclude(x => x!.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return order ?? throw Problem("order_not_found", "The order was not found.", 404);
    }

    private static OrderPayment RequirePayment(Order order) => order.Payment
        ?? throw Problem("payment_not_available", "This legacy order has no payment record.");
    private static void EnsureOwner(Order order, string buyerId)
    {
        RequireBuyer(buyerId);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw Problem("order_not_found", "The order was not found.", 404);
    }

    private static void RequireBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw Problem("unauthenticated", "Authentication is required.", 401);
    }

    private static void EnsureMoney(decimal expectedAmount, string expectedCurrency, decimal actualAmount,
        string actualCurrency, string operation)
    {
        if (Money(expectedAmount) != Money(actualAmount)
            || !string.Equals(expectedCurrency, actualCurrency, StringComparison.OrdinalIgnoreCase))
            throw Problem("provider_amount_mismatch",
                $"PayPal returned an unexpected amount or currency for the {operation}.", 502);
    }

    private static void RecordCapture(OrderPayment payment, GatewayCapture capture) =>
        payment.RecordCapture(capture.CaptureId, capture.Status, capture.StatusReason, capture.Amount,
            capture.PayPalFee, capture.NetAmount, capture.CapturedAt);

    private static decimal Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    private static string StableKey(string value) => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16]).ToString();
    private static PaymentOperationException Problem(string code, string message, int status = 409) =>
        new(code, message, status);

    private static OrderView ToView(Order order)
    {
        var payment = RequirePayment(order);
        return new OrderView(order.Id, order.OrderDate, Money(order.Total()), payment.Currency, payment.Status,
            order.FulfilmentStatus, payment.ProviderOrderId, payment.AuthorizationId,
            payment.AuthorizationStatus, payment.AuthorizedAmount, payment.AuthorizationExpiresAt,
            payment.CaptureId, payment.CaptureStatus, payment.CapturedAmount, payment.PayPalFee,
            payment.NetAmount, payment.RefundedAmount, payment.Refunds.Select(ToView).ToList());
    }

    private static RefundView ToView(OrderRefund refund) => new(refund.Id, refund.IdempotencyKey,
        refund.ProviderRefundId, refund.Status, refund.Amount, refund.Currency, refund.StatusReason);
    private static SavedPaymentMethodView ToView(PaymentMethod method) => new(method.Id, method.Brand,
        method.LastDigits, method.Expiry, method.CardholderName, method.CardType);

    private static async Task<AsyncLockReleaser> LockAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = OperationLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new AsyncLockReleaser(semaphore);
    }

    private sealed class AsyncLockReleaser : IAsyncDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public AsyncLockReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public ValueTask DisposeAsync()
        {
            _semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
