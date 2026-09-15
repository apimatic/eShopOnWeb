using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PaymentWorkflow
{
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;
    private readonly PaymentOperationLock _operationLock;

    public PaymentWorkflow(CatalogContext db, IPayPalClient payPal, IOptions<PayPalOptions> options,
        PaymentOperationLock operationLock)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
        _operationLock = operationLock;
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0) throw Problem(400, "EMPTY_ORDER", "At least one catalog item is required.");
        if (request.Items.GroupBy(x => x.CatalogItemId).Any(x => x.Count() > 1))
            throw Problem(400, "DUPLICATE_CATALOG_ITEM", "Each catalog item may appear only once.");

        var ids = request.Items.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id,
            cancellationToken);
        var missing = ids.Where(x => !catalogItems.ContainsKey(x)).ToArray();
        if (missing.Length > 0)
            throw Problem(400, "CATALOG_ITEM_NOT_FOUND", $"Unknown catalog item ids: {string.Join(", ", missing)}.");

        var items = request.Items.Select(x =>
        {
            var catalog = catalogItems[x.CatalogItemId];
            return new OrderItem(new CatalogItemOrdered(catalog.Id, catalog.Name, catalog.PictureUri),
                catalog.Price, x.Quantity);
        }).ToList();
        await using IDbContextTransaction? transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var address = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        var payment = new OrderPayment(order.Id, _options.Currency, order.Total());
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);

        return new PlaceOrderResponse(order.Id, payment.Amount, payment.Currency, order.PaymentStatus.ToString(),
            order.FulfilmentStatus.ToString());
    }

    public Task<OrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) => _operationLock.RunAsync($"order:{orderId}", async () =>
    {
        var (order, payment) = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (order.PaymentStatus == PaymentStatus.Authorized)
            return MapOrder(order, payment);
        if (order.PaymentStatus != PaymentStatus.AwaitingPayment ||
            order.FulfilmentStatus != FulfilmentStatus.Pending)
            throw Problem(409, "ORDER_NOT_PAYABLE", "The order is not awaiting payment.");

        var hasCard = request.Card is not null;
        var hasSavedMethod = request.PaymentMethodId.HasValue;
        if (hasCard == hasSavedMethod)
            throw Problem(400, "PAYMENT_SOURCE_REQUIRED",
                "Provide either card details or paymentMethodId, but not both.");

        PayPalPaymentSource source;
        int? savedMethodId = null;
        if (request.Card is not null)
        {
            source = PayPalPaymentSource.FromCard(ToPayPalCard(request.Card));
        }
        else
        {
            var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
                x.Id == request.PaymentMethodId && x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
            if (method is null) throw Problem(404, "PAYMENT_METHOD_NOT_FOUND", "Payment method not found.");
            source = PayPalPaymentSource.FromVault(method.PayPalVaultId);
            savedMethodId = method.Id;
        }

        var result = await _payPal.AuthorizeAsync(order.Id, payment.ExternalReference, payment.Amount,
            payment.Currency, source, RequestId("authorize", payment.ExternalReference), cancellationToken);
        EnsureAmount(result.Amount, result.Currency, payment.Amount, payment.Currency, "authorization");
        payment.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.CreatedAt,
            result.ExpiresAt, savedMethodId);
        if (!result.Status.Equals("CREATED", StringComparison.OrdinalIgnoreCase))
        {
            await _db.SaveChangesAsync(cancellationToken);
            throw Problem(409, "AUTHORIZATION_NOT_ACTIVE",
                $"PayPal returned authorization {result.AuthorizationId} with status {result.Status}.");
        }
        order.MarkAuthorized();
        await _db.SaveChangesAsync(cancellationToken);
        return MapOrder(order, payment);
    });

    public Task<OrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken) =>
        _operationLock.RunAsync($"order:{orderId}", async () =>
        {
            var (order, payment) = await AnyOrderAsync(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled &&
                order.PaymentStatus is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
                return MapOrder(order, payment);
            if (order.PaymentStatus != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
                throw Problem(409, "ORDER_NOT_FULFILLABLE", "The order does not have an active authorization.");

            var authorizationId = payment.AuthorizationId;
            var staleAt = payment.AuthorizationCreatedAt?.AddDays(3);
            if (staleAt.HasValue && staleAt <= DateTimeOffset.UtcNow)
            {
                try
                {
                    var renewed = await _payPal.ReauthorizeAsync(authorizationId, payment.Amount, payment.Currency,
                        RequestId("reauthorize", authorizationId), cancellationToken);
                    EnsureAmount(renewed.Amount, renewed.Currency, payment.Amount, payment.Currency,
                        "reauthorization");
                    payment.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.CreatedAt,
                        renewed.ExpiresAt);
                    await _db.SaveChangesAsync(cancellationToken);
                    authorizationId = renewed.AuthorizationId;
                }
                catch (PayPalApiException ex)
                {
                    throw Problem(409, "AUTHORIZATION_RENEWAL_FAILED",
                        $"PayPal could not renew the stale authorization. Ask the shopper to pay again. PayPal code: {ex.Code}" +
                        (ex.DebugId is null ? "." : $"; debug id: {ex.DebugId}."));
                }
            }

            var capture = await _payPal.CaptureAsync(authorizationId, payment.Amount, payment.Currency,
                RequestId("capture", authorizationId), cancellationToken);
            EnsureAmount(capture.Amount, capture.Currency, payment.Amount, payment.Currency, "capture");
            payment.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee,
                capture.NetAmount, capture.CreatedAt);
            if (!capture.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw Problem(409, "CAPTURE_NOT_COMPLETED",
                    $"PayPal created capture {capture.CaptureId} with status {capture.Status}; retry fulfilment after it completes.");
            }

            if (!capture.PayPalFee.HasValue || !capture.NetAmount.HasValue)
                throw Problem(502, "CAPTURE_BREAKDOWN_MISSING", "PayPal completed the capture without a fee/net breakdown.");
            order.MarkFulfilled();
            await _db.SaveChangesAsync(cancellationToken);
            return MapOrder(order, payment);
        });

    public Task<OrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken) =>
        _operationLock.RunAsync($"order:{orderId}", async () =>
        {
            var (order, payment) = await AnyOrderAsync(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Cancelled) return MapOrder(order, payment);
            if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled)
                throw Problem(409, "ORDER_ALREADY_FULFILLED", "A fulfilled order must be refunded, not cancelled.");
            if (order.PaymentStatus == PaymentStatus.Authorized)
            {
                if (string.IsNullOrWhiteSpace(payment.AuthorizationId))
                    throw Problem(409, "AUTHORIZATION_ID_MISSING", "The local authorization identifier is missing.");
                var status = await _payPal.VoidAsync(payment.AuthorizationId,
                    RequestId("void", payment.AuthorizationId), cancellationToken);
                payment.RecordVoid(status);
            }

            order.MarkCancelled();
            await _db.SaveChangesAsync(cancellationToken);
            return MapOrder(order, payment);
        });

    public Task<RefundResponse> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken) => _operationLock.RunAsync($"order:{orderId}", async () =>
    {
        var (order, payment) = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
        var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null)
        {
            if (request.Amount.HasValue && request.Amount.Value != existing.Amount)
                throw Problem(409, "IDEMPOTENCY_KEY_REUSED",
                    "That idempotency key was already used with a different refund amount.");
            return MapRefund(existing);
        }

        if (order.FulfilmentStatus != FulfilmentStatus.Fulfilled ||
            order.PaymentStatus is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded) ||
            string.IsNullOrWhiteSpace(payment.CaptureId) || !payment.CapturedAmount.HasValue)
            throw Problem(409, "ORDER_NOT_REFUNDABLE", "The order has no refundable captured payment.");
        var remaining = payment.CapturedAmount.Value - payment.RefundedAmount;
        var amount = request.Amount ?? remaining;
        if (amount <= 0 || amount > remaining)
            throw Problem(409, "REFUND_EXCEEDS_REMAINING",
                $"The maximum refundable amount is {remaining:0.00} {payment.Currency}.");

        var refund = await _payPal.RefundAsync(payment.CaptureId, amount, payment.Currency,
            RequestId("refund", $"{payment.CaptureId}:{request.IdempotencyKey}"), cancellationToken);
        EnsureAmount(refund.Amount, refund.Currency, amount, payment.Currency, "refund");
        var entity = payment.AddRefund(request.IdempotencyKey, refund.RefundId, refund.Status, refund.Amount);
        order.MarkRefunded(payment.RefundedAmount == payment.CapturedAmount.Value);
        await _db.SaveChangesAsync(cancellationToken);
        return MapRefund(entity);
    });

    public async Task<IReadOnlyList<OrderResponse>> MyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
            .OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var payments = await _db.OrderPayments.AsNoTracking().Where(x => orderIds.Contains(x.OrderId))
            .Include(x => x.Refunds).ToDictionaryAsync(x => x.OrderId, cancellationToken);
        return orders.Select(x => MapOrder(x, payments.GetValueOrDefault(x.Id))).ToList();
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId, CardRequest request,
        CancellationToken cancellationToken)
    {
        var existingCustomer = await _db.SavedPaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.PayPalCustomerId != string.Empty)
            .Select(x => x.PayPalCustomerId).FirstOrDefaultAsync(cancellationToken);
        var result = await _payPal.SaveCardAsync(ToPayPalCard(request), MerchantCustomerId(buyerId),
            existingCustomer, RequestId("vault", Guid.NewGuid().ToString("N")), cancellationToken);
        var method = new SavedPaymentMethod(buyerId, result.VaultId, result.CustomerId, result.Brand,
            result.LastFour, result.Expiry);
        _db.SavedPaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return MapPaymentMethod(method);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> PaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) => await _db.SavedPaymentMethods.AsNoTracking()
        .Where(x => x.BuyerId == buyerId && x.DeletedAt == null).OrderByDescending(x => x.CreatedAt)
        .Select(x => new PaymentMethodResponse(x.Id, x.Brand, x.LastFour, x.Expiry, x.CreatedAt))
        .ToListAsync(cancellationToken);

    public Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken) =>
        _operationLock.RunAsync($"method:{paymentMethodId}", async () =>
        {
            var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
                x.Id == paymentMethodId && x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
            if (method is null) throw Problem(404, "PAYMENT_METHOD_NOT_FOUND", "Payment method not found.");
            await _payPal.DeletePaymentTokenAsync(method.PayPalVaultId, cancellationToken);
            method.MarkDeleted();
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        });

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw Problem(400, "INVALID_DATE_RANGE", "from must be earlier than to.");
        if (to > DateTimeOffset.UtcNow.AddMinutes(5))
            throw Problem(400, "INVALID_DATE_RANGE", "to cannot be in the future.");

        // PayPal documents up to a three-hour reporting delay. Do not turn that normal delay into a failed report;
        // expose the freshness boundary and still report local records over the caller's complete range.
        var paypalDataThrough = DateTimeOffset.UtcNow.AddHours(-3);
        var paypalTo = to < paypalDataThrough ? to : paypalDataThrough;
        var paypal = from <= paypalTo
            ? await _payPal.ListTransactionsAsync(from, paypalTo, cancellationToken)
            : Array.Empty<PayPalTransaction>();
        var localPayments = await _db.OrderPayments.AsNoTracking()
            .Where(x => (x.CapturedAt >= from && x.CapturedAt <= to) ||
                        x.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to))
            .Include(x => x.Refunds).ToListAsync(cancellationToken);
        var local = new Dictionary<string, (string Type, int OrderId, decimal Amount, string Currency,
            DateTimeOffset? Date)>(StringComparer.Ordinal);
        foreach (var payment in localPayments)
        {
            if (payment.CaptureId is not null && payment.CapturedAmount.HasValue && payment.CapturedAt >= from &&
                payment.CapturedAt <= to)
                local[payment.CaptureId] = ("Capture", payment.OrderId, payment.CapturedAmount.Value,
                    payment.Currency, payment.CapturedAt);
            foreach (var refund in payment.Refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to))
                local[refund.PayPalRefundId] = ("Refund", payment.OrderId, refund.Amount, refund.Currency,
                    refund.CreatedAt);
        }

        var paypalById = paypal.ToDictionary(x => x.TransactionId, StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();
        foreach (var transaction in paypal)
        {
            var matched = local.TryGetValue(transaction.TransactionId, out var eShop);
            var amountsMatch = matched && Math.Abs(transaction.Amount) == eShop.Amount &&
                               transaction.Currency.Equals(eShop.Currency, StringComparison.OrdinalIgnoreCase);
            entries.Add(new ReconciliationEntry(matched ? (amountsMatch ? "Matched" : "Mismatch") : "PayPalOnly",
                matched ? eShop.Type : "PayPalTransaction", transaction.TransactionId,
                matched ? eShop.OrderId : null, matched ? eShop.Amount : null, transaction.Amount,
                transaction.Fee, transaction.Currency, transaction.Status, transaction.InitiatedAt,
                transaction.ReferenceId, transaction.InvoiceId));
        }

        foreach (var (id, eShop) in local.Where(x => !paypalById.ContainsKey(x.Key)))
            entries.Add(new ReconciliationEntry("EShopOnly", eShop.Type, id, eShop.OrderId, eShop.Amount, null,
                null, eShop.Currency, null, eShop.Date, null, null));

        return new ReconciliationResponse(from, to, paypalTo, entries.OrderBy(x => x.TransactionDate).ToList());
    }

    private async Task<(Order Order, OrderPayment Payment)> OwnedOrderAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        var pair = await AnyOrderAsync(orderId, cancellationToken, buyerId);
        return pair;
    }

    private async Task<(Order Order, OrderPayment Payment)> AnyOrderAsync(int orderId,
        CancellationToken cancellationToken, string? buyerId = null)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
            .SingleOrDefaultAsync(x => x.Id == orderId && (buyerId == null || x.BuyerId == buyerId),
                cancellationToken);
        if (order is null) throw Problem(404, "ORDER_NOT_FOUND", "Order not found.");
        var payment = await _db.OrderPayments.Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.OrderId == order.Id, cancellationToken);
        if (payment is null) throw Problem(409, "PAYMENT_RECORD_MISSING", "The order has no payment record.");
        return (order, payment);
    }

    private OrderResponse MapOrder(Order order, OrderPayment? payment) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = payment?.Currency ?? _options.Currency,
        PaymentStatus = order.PaymentStatus.ToString(),
        FulfilmentStatus = order.FulfilmentStatus.ToString(),
        Items = order.OrderItems.Select(x => new OrderItemResponse(x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
        Payment = payment is null ? null : new PaymentResponse
        {
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            RefundedAmount = payment.RefundedAmount,
            Refunds = payment.Refunds.Select(MapRefund).ToList()
        }
    };

    private static PaymentMethodResponse MapPaymentMethod(SavedPaymentMethod method) =>
        new(method.Id, method.Brand, method.LastFour, method.Expiry, method.CreatedAt);

    private static RefundResponse MapRefund(PaymentRefund refund) =>
        new(refund.PayPalRefundId, refund.Status, refund.Amount, refund.Currency, refund.IdempotencyKey);

    private static PayPalCard ToPayPalCard(CardRequest card) => new(card.Number, card.Expiry, card.SecurityCode,
        card.Name, new PayPalBillingAddress(card.BillingAddress.AddressLine1, card.BillingAddress.AddressLine2,
            card.BillingAddress.City, card.BillingAddress.State,
            card.BillingAddress.PostalCode, card.BillingAddress.CountryCode.ToUpperInvariant()));

    private static void EnsureAmount(decimal actualAmount, string actualCurrency, decimal expectedAmount,
        string expectedCurrency, string operation)
    {
        if (actualAmount != expectedAmount || !actualCurrency.Equals(expectedCurrency, StringComparison.OrdinalIgnoreCase))
            throw Problem(502, "PAYPAL_AMOUNT_MISMATCH",
                $"PayPal reported an unexpected {operation} amount or currency.");
    }

    private static string RequestId(string operation, string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"eshop:{operation}:{key}"));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static string MerchantCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return "eshop-" + Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static ApiProblemException Problem(int status, string code, string message) => new(status, code, message);
}
