using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class CommercePaymentService : ICommercePaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalPaymentsClient _payPal;
    private readonly string _currency;

    public CommercePaymentService(CatalogContext db, IPayPalPaymentsClient payPal,
        IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _currency = options.Value.Currency;
    }

    public async Task<OrderView> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput address, CancellationToken cancellationToken)
    {
        if (lines.Count == 0) throw Validation("empty_order", "At least one catalog item is required.");
        if (lines.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw Validation("invalid_quantity", "Catalog item IDs and quantities must be positive.");
        if (lines.GroupBy(x => x.CatalogItemId).Any(x => x.Count() > 1))
            throw Validation("duplicate_catalog_item", "Each catalog item may appear only once.");
        ValidateAddress(address);

        var ids = lines.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var missing = ids.Where(id => !catalogItems.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw Validation("catalog_item_not_found", $"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var orderItems = lines.Select(x =>
        {
            var item = catalogItems[x.CatalogItemId];
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, x.Quantity);
        }).ToList();
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return View(order);
    }

    public async Task<OrderView> PayAsync(int orderId, string buyerId, CardDetails? card,
        int? paymentMethodId, CancellationToken cancellationToken)
    {
        if ((card is null) == (paymentMethodId is null))
            throw Validation("payment_source_required", "Supply exactly one of card or paymentMethodId.");
        if (card is not null) ValidateCard(card);

        return await LockedAsync(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            if (order.Status == OrderStatus.Authorized) return View(order);
            if (order.Status != OrderStatus.AwaitingPayment)
                throw Conflict("order_not_payable", $"An order in {order.Status} state cannot be paid.");

            string? vaultId = null;
            if (paymentMethodId is not null)
            {
                var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId,
                    cancellationToken);
                if (method is null || !method.IsActive) throw NotFound("payment_method_not_found", "Payment method not found.");
                if (!string.Equals(method.BuyerId, buyerId, StringComparison.Ordinal))
                    throw Forbidden("payment_method_forbidden", "That payment method belongs to another shopper.");
                vaultId = method.VaultId;
            }

            var paymentReference = order.EnsurePaymentReference();
            await _db.SaveChangesAsync(cancellationToken);
            var authorization = await _payPal.AuthorizeAsync(paymentReference, order.Total(), card, vaultId,
                RequestId($"authorize:{paymentReference}"), cancellationToken);
            if (!string.Equals(authorization.Currency, _currency, StringComparison.OrdinalIgnoreCase))
                throw Upstream("currency_mismatch", "PayPal authorized the payment in an unexpected currency.");
            order.RecordAuthorization(authorization.Currency, authorization.PayPalOrderId,
                authorization.OrderStatus, authorization.AuthorizationId, authorization.AuthorizationStatus,
                authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
            return View(order);
        });
    }

    public async Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        return await LockedAsync(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
                return View(order);
            if (order.Status != OrderStatus.Authorized || order.AuthorizationId is null || order.Currency is null)
                throw Conflict("order_not_fulfillable", $"An order in {order.Status} state cannot be fulfilled.");

            if (IsOutsideHonorPeriod(order))
            {
                try
                {
                    var renewed = await _payPal.ReauthorizeAsync(order.AuthorizationId,
                        RequestId($"reauthorize:{order.Id}:{order.AuthorizationId}"), cancellationToken);
                    order.RecordReauthorization(renewed.AuthorizationId, renewed.AuthorizationStatus,
                        renewed.Amount, renewed.CreatedAt, renewed.ExpiresAt);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (PayPalApiException ex)
                {
                    throw Conflict("authorization_cannot_be_renewed",
                        $"The authorization is stale and PayPal could not renew it. Ask the shopper to pay again. PayPal issue: {ex.Issue ?? "unknown"}; debug ID: {ex.DebugId ?? "not supplied"}.");
                }
            }

            var paymentReference = order.EnsurePaymentReference();
            var capture = await _payPal.CaptureAsync(order.AuthorizationId!, paymentReference, order.Total(),
                order.Currency, RequestId($"capture:{paymentReference}"), cancellationToken);
            if (!string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                throw Conflict("capture_not_completed", $"PayPal capture is {capture.Status}; retry fulfilment after it reaches COMPLETED.");
            order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.NetAmount,
                capture.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return View(order);
        });
    }

    public async Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        return await LockedAsync(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (order.Status == OrderStatus.Cancelled) return View(order);
            if (order.Status == OrderStatus.AwaitingPayment)
            {
                order.RecordUnpaidCancellation();
            }
            else if (order.Status == OrderStatus.Authorized && order.AuthorizationId is not null)
            {
                var status = await _payPal.VoidAsync(order.AuthorizationId,
                    RequestId($"void:{order.Id}"), cancellationToken);
                order.RecordCancellation(status);
            }
            else throw Conflict("order_not_cancellable", $"An order in {order.Status} state cannot be cancelled.");
            await _db.SaveChangesAsync(cancellationToken);
            return View(order);
        });
    }

    public async Task<RefundView> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw Validation("invalid_idempotency_key", "idempotencyKey is required and must be at most 128 characters.");
        return await LockedAsync(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
            if (existing is not null)
            {
                if (amount is not null && decimal.Round(amount.Value, 2) != decimal.Round(existing.Amount, 2))
                    throw Conflict("idempotency_key_reused", "That idempotency key was already used with a different amount.");
                return View(existing);
            }
            if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded) ||
                order.CaptureId is null || order.CapturedAmount is null || order.Currency is null)
                throw Conflict("order_not_refundable", $"An order in {order.Status} state cannot be refunded.");
            var remaining = order.CapturedAmount.Value - order.RefundedAmount;
            var requested = amount ?? remaining;
            if (requested <= 0 || decimal.Round(requested, 2) != requested || requested > remaining)
                throw Validation("invalid_refund_amount", $"Refund amount must be positive, have at most two decimals, and not exceed {remaining:0.00}.");

            var paymentReference = order.EnsurePaymentReference();
            var payPalRefund = await _payPal.RefundAsync(order.CaptureId, paymentReference, requested,
                order.Currency, idempotencyKey, RequestId($"refund:{paymentReference}:{idempotencyKey}"),
                cancellationToken);
            var refund = order.RecordRefund(idempotencyKey, payPalRefund.Id, payPalRefund.Status,
                payPalRefund.Amount, payPalRefund.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return View(refund);
        });
    }

    public async Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking().Include(x => x.OrderItems).Include(x => x.Refunds)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(View).ToList();
    }

    public async Task<PaymentMethodView> SavePaymentMethodAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken)
    {
        ValidateCard(card);
        var customerId = await _db.PaymentMethods.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .Select(x => x.PayPalCustomerId).FirstOrDefaultAsync(cancellationToken);
        var vaulted = await _payPal.SaveCardAsync(buyerId, customerId, card,
            RequestId($"vault:{buyerId}:{Guid.NewGuid():N}"), cancellationToken);
        var method = new PaymentMethod(buyerId, vaulted.VaultId, vaulted.CustomerId, vaulted.Brand,
            vaulted.Last4, vaulted.Expiry, vaulted.CardholderName);
        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return View(method);
    }

    public async Task<IReadOnlyList<PaymentMethodView>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var methods = await _db.PaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.IsActive).OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return methods.Select(View).ToList();
    }

    public async Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken)
    {
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId,
            cancellationToken);
        if (method is null || !method.IsActive) throw NotFound("payment_method_not_found", "Payment method not found.");
        if (method.BuyerId != buyerId) throw Forbidden("payment_method_forbidden", "That payment method belongs to another shopper.");
        await _payPal.DeletePaymentTokenAsync(method.VaultId, cancellationToken);
        method.Deactivate();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw Validation("invalid_range", "from must be earlier than to.");
        IReadOnlyList<PayPalTransaction> payPal;
        try
        {
            payPal = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.Message.Contains(
            "Data for the given start date is not available", StringComparison.OrdinalIgnoreCase))
        {
            // Transaction Search trails live activity in the sandbox. A range newer than the
            // reporting watermark has no PayPal-side rows yet, but local-only rows are still useful.
            payPal = Array.Empty<PayPalTransaction>();
        }
        var orders = await _db.Orders.AsNoTracking().Include(x => x.Refunds).ToListAsync(cancellationToken);
        var lines = new List<ReconciliationLine>();
        var matchedCaptureIds = new HashSet<string>(StringComparer.Ordinal);
        var matchedRefundIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transaction in payPal)
        {
            var order = orders.FirstOrDefault(x => Matches(x, transaction));
            if (order?.CaptureId == transaction.TransactionId) matchedCaptureIds.Add(transaction.TransactionId);
            var refund = order?.Refunds.FirstOrDefault(x => x.PayPalRefundId == transaction.TransactionId);
            if (refund is not null) matchedRefundIds.Add(transaction.TransactionId);
            lines.Add(new ReconciliationLine(order is null ? "PayPalOnly" : "Matched", order?.Id,
                "PayPal", transaction.TransactionId, transaction.PayPalReferenceId,
                transaction.EventCode, transaction.Status, transaction.Amount, transaction.Fee,
                transaction.Currency, transaction.UpdatedAt));
        }

        foreach (var order in orders.Where(x => x.CaptureId is not null && x.CapturedAt >= from && x.CapturedAt <= to && !matchedCaptureIds.Contains(x.CaptureId)))
            lines.Add(new ReconciliationLine("EShopOnly", order.Id, "eShop", order.CaptureId!,
                order.AuthorizationId, null, order.CaptureStatus ?? string.Empty, order.CapturedAmount ?? 0,
                order.PayPalFee ?? 0, order.Currency ?? string.Empty, order.CapturedAt!.Value));
        foreach (var order in orders)
            foreach (var refund in order.Refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to && !matchedRefundIds.Contains(x.PayPalRefundId)))
                lines.Add(new ReconciliationLine("EShopOnly", order.Id, "eShop", refund.PayPalRefundId,
                    order.CaptureId, null, refund.PayPalStatus, -refund.Amount, 0,
                    order.Currency ?? string.Empty, refund.CreatedAt));

        return new ReconciliationView(from, to, lines.OrderBy(x => x.Timestamp).ToList());
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
        ?? throw NotFound("order_not_found", "Order not found.");

    private static bool IsOutsideHonorPeriod(Order order) =>
        order.AuthorizationCreatedAt is not null && DateTimeOffset.UtcNow >= order.AuthorizationCreatedAt.Value.AddDays(3);
    private static bool Matches(Order order, PayPalTransaction transaction) =>
        transaction.TransactionId == order.CaptureId || transaction.TransactionId == order.AuthorizationId ||
        transaction.TransactionId == order.PayPalOrderId || transaction.PayPalReferenceId == order.CaptureId ||
        order.Refunds.Any(x => x.PayPalRefundId == transaction.TransactionId) ||
        (order.PaymentReference is not null && transaction.InvoiceId?.StartsWith(order.PaymentReference, StringComparison.Ordinal) == true) ||
        (order.PaymentReference is not null && transaction.CustomField?.StartsWith(order.PaymentReference, StringComparison.Ordinal) == true);

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw Forbidden("order_forbidden", "That order belongs to another shopper.");
    }
    private static void ValidateAddress(ShippingAddressInput x)
    {
        if (string.IsNullOrWhiteSpace(x.Street) || string.IsNullOrWhiteSpace(x.City) ||
            string.IsNullOrWhiteSpace(x.Country) || string.IsNullOrWhiteSpace(x.ZipCode))
            throw Validation("invalid_shipping_address", "Street, city, country, and zipCode are required.");
    }
    private static void ValidateCard(CardDetails card)
    {
        if (card.Number.Length is < 13 or > 19 || card.Number.Any(c => !char.IsDigit(c)) ||
            card.SecurityCode.Length is < 3 or > 4 || card.SecurityCode.Any(c => !char.IsDigit(c)) ||
            !DateTime.TryParseExact(card.Expiry, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _) || string.IsNullOrWhiteSpace(card.Name))
            throw Validation("invalid_card", "Card number, expiry (YYYY-MM), security code, and name are required and invalid.");
        if (string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1) || string.IsNullOrWhiteSpace(card.BillingAddress.City) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.PostalCode) || card.BillingAddress.CountryCode.Length != 2)
            throw Validation("invalid_billing_address", "A complete billing address with a two-letter country code is required.");
    }
    private static string RequestId(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes[..16]).ToString();
    }
    private static OrderView View(Order x) => new(x.Id, x.OrderDate, x.Status.ToString(),
        x.PaymentStatus.ToString(), x.Total(), x.Currency, x.PayPalOrderId, x.AuthorizationId,
        x.AuthorizationStatus, x.AuthorizationExpiresAt, x.CaptureId, x.CaptureStatus,
        x.CapturedAmount, x.PayPalFee, x.NetAmount, x.RefundedAmount, x.Refunds.Select(View).ToList());
    private static RefundView View(OrderRefund x) => new(x.Id, x.PayPalRefundId, x.PayPalStatus,
        x.Amount, x.CreatedAt, x.IdempotencyKey);
    private static PaymentMethodView View(PaymentMethod x) => new(x.Id, x.Brand, x.Last4, x.Expiry, x.CardholderName);

    private static CommerceException Validation(string code, string message) => new(CommerceErrorKind.Validation, code, message);
    private static CommerceException NotFound(string code, string message) => new(CommerceErrorKind.NotFound, code, message);
    private static CommerceException Forbidden(string code, string message) => new(CommerceErrorKind.Forbidden, code, message);
    private static CommerceException Conflict(string code, string message) => new(CommerceErrorKind.Conflict, code, message);
    private static CommerceException Upstream(string code, string message) => new(CommerceErrorKind.Upstream, code, message);

    private static async Task<T> LockedAsync<T>(int orderId, Func<Task<T>> action)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { return await action(); }
        finally { gate.Release(); }
    }
}
