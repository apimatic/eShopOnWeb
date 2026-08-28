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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PaymentWorkflowService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;

    public PaymentWorkflowService(CatalogContext db, IPayPalClient payPal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
    }

    public async Task<Order> CreateOrderAsync(string ownerId, CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0) throw ApiOperationException.BadRequest("At least one catalog item is required.");
        if (request.ShipToAddress is null) throw ApiOperationException.BadRequest("A shipping address is required.");
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw ApiOperationException.BadRequest("Catalog item IDs and quantities must be positive.");
        if (request.Items.GroupBy(x => x.CatalogItemId).Any(x => x.Count() > 1))
            throw ApiOperationException.BadRequest("Each catalog item may appear only once.");

        var ids = request.Items.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length) throw ApiOperationException.BadRequest("One or more catalog items do not exist.");
        var byId = catalogItems.ToDictionary(x => x.Id);
        var lines = request.Items.Select(x =>
        {
            var item = byId[x.CatalogItemId];
            var unitPrice = decimal.Round(item.Price, 2, MidpointRounding.AwayFromZero);
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), unitPrice, x.Quantity);
        }).ToList();
        var address = request.ShipToAddress;
        ValidateAddress(address);
        var order = new Order(ownerId, new Address(address.Street, address.City, address.State,
            address.Country, address.ZipCode), lines);
        order.InitializePayment(_options.Currency);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(int orderId, string ownerId, PayOrderRequest request, CancellationToken cancellationToken)
    {
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw ApiOperationException.BadRequest("Provide exactly one of card or paymentMethodId.");
        if (request.Card is not null) ValidateCard(request.Card);

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, ownerId);
            if (order.FulfilmentStatus != FulfilmentStatus.Pending)
                throw ApiOperationException.Conflict("Only a pending order can be paid.");
            var payment = order.Payment ?? throw ApiOperationException.Conflict("This order is not payment-enabled.");

            if (payment.PayPalAuthorizationId is not null)
            {
                if (payment.Status == PaymentStatus.AuthorizationPending)
                {
                    var refreshed = await _payPal.GetAuthorizationAsync(payment.PayPalAuthorizationId,
                        payment.PayPalOrderId!, cancellationToken);
                    RecordVerifiedAuthorization(payment, refreshed);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                if (payment.Status != PaymentStatus.Authorized)
                    throw ApiOperationException.Conflict($"The authorization is {payment.PayPalAuthorizationStatus}; retry when PayPal reports CREATED.");
                return order;
            }

            if (payment.PayPalOrderId is null)
            {
                var items = order.OrderItems.Select(x => new PayPalLineItem(x.ItemOrdered.CatalogItemId,
                    x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToArray();
                var paypalOrderId = await _payPal.CreateOrderAsync(order.ExternalId, payment.Amount,
                    payment.Currency, items, $"eshop-create-{order.ExternalId:N}", cancellationToken);
                payment.SetPayPalOrder(paypalOrderId);
                await _db.SaveChangesAsync(cancellationToken);
            }

            string? vaultId = null;
            if (request.PaymentMethodId is { } paymentMethodId)
            {
                var saved = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
                    x => x.Id == paymentMethodId && x.OwnerId == ownerId, cancellationToken);
                if (saved is null) throw ApiOperationException.NotFound("Saved payment method was not found.");
                vaultId = saved.PayPalPaymentTokenId;
            }

            var authorization = await _payPal.AuthorizeOrderAsync(payment.PayPalOrderId!,
                request.Card is null ? null : ToPayPalCard(request.Card), vaultId,
                $"eshop-authorize-{order.ExternalId:N}", cancellationToken);
            RecordVerifiedAuthorization(payment, authorization);
            await _db.SaveChangesAsync(cancellationToken);
            if (payment.Status != PaymentStatus.Authorized)
                throw ApiOperationException.Conflict($"PayPal authorization is {payment.PayPalAuthorizationStatus}; fulfilment cannot proceed yet.");
            return order;
        });
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled) return order;
            if (order.FulfilmentStatus == FulfilmentStatus.Cancelled)
                throw ApiOperationException.Conflict("A cancelled order cannot be fulfilled.");
            var payment = order.Payment ?? throw ApiOperationException.Conflict("This order is not payment-enabled.");

            if (payment.PayPalCaptureId is not null)
            {
                var refreshed = await _payPal.GetCaptureAsync(payment.PayPalCaptureId, cancellationToken);
                RecordVerifiedCapture(payment, refreshed);
                await _db.SaveChangesAsync(cancellationToken);
                if (refreshed.Status != "COMPLETED")
                    throw ApiOperationException.Conflict($"PayPal capture is {refreshed.Status}; do not dispatch until it is COMPLETED.");
                order.MarkFulfilled();
                await _db.SaveChangesAsync(cancellationToken);
                return order;
            }

            if (payment.Status != PaymentStatus.Authorized || payment.PayPalAuthorizationId is null)
                throw ApiOperationException.Conflict("The order needs a CREATED PayPal authorization before fulfilment.");

            var now = DateTimeOffset.UtcNow;
            if (payment.AuthorizationExpiresAt is { } expiresAt && expiresAt <= now)
                throw ApiOperationException.Conflict("The PayPal authorization has expired and cannot be renewed. Ask the shopper to pay the order again.");
            var honorStarted = payment.AuthorizationLastRenewedAt ?? payment.AuthorizedAt ?? now;
            if (honorStarted.AddDays(3) <= now)
            {
                try
                {
                    var renewed = await _payPal.ReauthorizeAsync(payment.PayPalAuthorizationId,
                        payment.PayPalOrderId!, payment.Amount, payment.Currency,
                        $"eshop-reauthorize-{order.ExternalId:N}-{honorStarted:yyyyMMdd}", cancellationToken);
                    RecordVerifiedAuthorization(payment, renewed);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (PayPalApiException ex)
                {
                    throw ApiOperationException.Conflict("PayPal could not renew the stale authorization. Ask the shopper to pay again. " +
                        Diagnostic(ex));
                }
            }

            var capture = await _payPal.CaptureAsync(payment.PayPalAuthorizationId, payment.Amount,
                payment.Currency, PayPalClient.InvoiceId(order.ExternalId),
                $"eshop-capture-{order.ExternalId:N}", cancellationToken);
            RecordVerifiedCapture(payment, capture);
            await _db.SaveChangesAsync(cancellationToken);
            if (capture.Status != "COMPLETED")
                throw ApiOperationException.Conflict($"PayPal capture is {capture.Status}; do not dispatch until it is COMPLETED.");
            order.MarkFulfilled();
            await _db.SaveChangesAsync(cancellationToken);
            return order;
        });
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Cancelled) return order;
            if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled)
                throw ApiOperationException.Conflict("A fulfilled order must be refunded, not cancelled.");
            var payment = order.Payment;
            if (payment?.PayPalCaptureId is not null)
                throw ApiOperationException.Conflict("Captured funds must be refunded, not cancelled.");
            if (payment?.PayPalAuthorizationId is not null && payment.Status != PaymentStatus.Voided)
            {
                var status = await _payPal.VoidAsync(payment.PayPalAuthorizationId,
                    $"eshop-void-{order.ExternalId:N}", cancellationToken);
                payment.RecordVoid(status);
            }
            order.MarkCancelled();
            await _db.SaveChangesAsync(cancellationToken);
            return order;
        });
    }

    public async Task<(PaymentRefund Refund, string Currency)> RefundAsync(int orderId, string ownerId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200)
            throw ApiOperationException.BadRequest("An idempotencyKey of 1 to 200 characters is required.");
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, ownerId);
            var payment = order.Payment ?? throw ApiOperationException.Conflict("This order is not payment-enabled.");
            var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
            if (existing is not null) return (existing, payment.Currency);
            if (order.FulfilmentStatus != FulfilmentStatus.Fulfilled || payment.PayPalCaptureId is null)
                throw ApiOperationException.Conflict("Only a fulfilled order with a captured payment can be refunded.");
            var amount = request.Amount ?? payment.RefundableAmount;
            if (amount <= 0 || decimal.Round(amount, 2) != amount)
                throw ApiOperationException.BadRequest("Refund amount must be positive with no more than two decimal places.");
            if (amount > payment.RefundableAmount)
                throw ApiOperationException.Conflict($"Only {payment.RefundableAmount:0.00} {payment.Currency} remains refundable.");
            var paypal = await _payPal.RefundAsync(payment.PayPalCaptureId, amount, payment.Currency,
                StableRequestId("refund", order.ExternalId, request.IdempotencyKey),
                order.ExternalId.ToString("N"), cancellationToken);
            if (paypal.Status is not ("COMPLETED" or "PENDING"))
                throw ApiOperationException.Conflict($"PayPal refund is {paypal.Status}; no refund was recorded in eShop.");
            VerifyMoney(paypal.Amount, paypal.Currency, amount, payment.Currency, "refund");
            var refund = payment.AddRefund(request.IdempotencyKey, paypal.Id, paypal.Status, paypal.Amount, paypal.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return (refund, payment.Currency);
        });
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(string ownerId, SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        ValidateCard(request.Card);
        var paypal = await _payPal.SaveCardAsync(PayPalCustomerReference(ownerId), ToPayPalCard(request.Card),
            "eshop-vault-" + Guid.NewGuid().ToString("N"), cancellationToken);
        var saved = new SavedPaymentMethod(ownerId, paypal.CustomerId, paypal.PaymentTokenId,
            paypal.Brand, paypal.Last4, paypal.Expiry);
        _db.SavedPaymentMethods.Add(saved);
        await _db.SaveChangesAsync(cancellationToken);
        return saved;
    }

    public Task<List<SavedPaymentMethod>> GetPaymentMethodsAsync(string ownerId, CancellationToken cancellationToken) =>
        _db.SavedPaymentMethods.Where(x => x.OwnerId == ownerId).OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(int id, string ownerId, CancellationToken cancellationToken)
    {
        var saved = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == ownerId,
            cancellationToken);
        if (saved is null) throw ApiOperationException.NotFound("Saved payment method was not found.");
        await _payPal.DeletePaymentTokenAsync(saved.PayPalPaymentTokenId, cancellationToken);
        _db.SavedPaymentMethods.Remove(saved);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<List<Order>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken) =>
        OrderQuery().Where(x => x.BuyerId == ownerId).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw ApiOperationException.BadRequest("from must be earlier than to.");
        var paypalTransactions = new List<PayPalTransaction>();
        var cursor = from;
        while (cursor < to)
        {
            var end = cursor.AddDays(31) < to ? cursor.AddDays(31) : to;
            paypalTransactions.AddRange(await _payPal.SearchTransactionsAsync(cursor, end, cancellationToken));
            cursor = end;
        }
        paypalTransactions = paypalTransactions.GroupBy(x => new
        {
            x.TransactionId, x.EventCode, x.InitiatedAt, x.Amount, x.Currency
        }).Select(x => x.First()).ToList();

        var orders = await OrderQuery().Where(x =>
                (x.OrderDate >= from && x.OrderDate <= to) ||
                (x.Payment != null && x.Payment.AuthorizedAt >= from && x.Payment.AuthorizedAt <= to) ||
                (x.Payment != null && x.Payment.CapturedAt >= from && x.Payment.CapturedAt <= to) ||
                (x.Payment != null && x.Payment.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to)))
            .ToListAsync(cancellationToken);
        var byInvoice = orders.ToDictionary(x => PayPalClient.InvoiceId(x.ExternalId), StringComparer.OrdinalIgnoreCase);
        var byPayPalId = orders.SelectMany(x => PaymentIds(x).Select(id => (id, order: x)))
            .GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.First().order, StringComparer.OrdinalIgnoreCase);
        var matchedOrders = new HashSet<int>();
        var report = new List<ReconciliationItem>();
        foreach (var transaction in paypalTransactions)
        {
            Order? order = null;
            if (transaction.InvoiceId is not null) byInvoice.TryGetValue(transaction.InvoiceId, out order);
            if (order is null) byPayPalId.TryGetValue(transaction.TransactionId, out order);
            if (order is null && transaction.ReferenceId is not null) byPayPalId.TryGetValue(transaction.ReferenceId, out order);
            if (order is not null) matchedOrders.Add(order.Id);
            report.Add(new ReconciliationItem(order is null ? "PayPalOnly" : "Matched", order?.Id,
                transaction.TransactionId, transaction.ReferenceId, transaction.EventCode, transaction.InitiatedAt,
                transaction.Amount, transaction.Currency, transaction.Fee, transaction.Status, transaction.InvoiceId));
        }
        foreach (var order in orders.Where(x => !matchedOrders.Contains(x.Id)))
        {
            report.Add(new ReconciliationItem("EshopOnly", order.Id, null, null, null, order.Payment?.CapturedAt ??
                order.Payment?.AuthorizedAt ?? order.OrderDate, order.Payment?.CapturedAmount ?? order.Payment?.Amount,
                order.Payment?.Currency, order.Payment?.PayPalFee, order.Payment?.PayPalCaptureStatus ??
                order.Payment?.PayPalAuthorizationStatus, PayPalClient.InvoiceId(order.ExternalId)));
        }
        return new ReconciliationResponse(from, to, report.OrderBy(x => x.InitiatedAt).ToArray());
    }

    private IQueryable<Order> OrderQuery() => _db.Orders.AsSplitQuery()
        .Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
        .Include(x => x.Payment).ThenInclude(x => x!.Refunds);

    private async Task<Order> GetOrderAsync(int id, CancellationToken cancellationToken)
    {
        return await OrderQuery().SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiOperationException.NotFound("Order was not found.");
    }

    private static async Task<T> WithOrderLock<T>(int orderId, Func<Task<T>> action)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { return await action(); }
        finally { gate.Release(); }
    }

    private static void RecordVerifiedAuthorization(OrderPayment payment, PayPalAuthorizationResult result)
    {
        VerifyMoney(result.Amount, result.Currency, payment.Amount, payment.Currency, "authorization");
        payment.RecordAuthorization(result.AuthorizationId, result.AuthorizationStatus, result.CreatedAt, result.ExpiresAt);
    }

    private static void RecordVerifiedCapture(OrderPayment payment, PayPalCaptureResult result)
    {
        VerifyMoney(result.Amount, result.Currency, payment.Amount, payment.Currency, "capture");
        payment.RecordCapture(result.Id, result.Status, result.Amount, result.Fee, result.NetAmount, result.CreatedAt);
    }

    private static void VerifyMoney(decimal actual, string actualCurrency, decimal expected, string expectedCurrency, string operation)
    {
        if (actual != expected || !actualCurrency.Equals(expectedCurrency, StringComparison.OrdinalIgnoreCase))
            throw ApiOperationException.Conflict($"PayPal {operation} amount {actual:0.00} {actualCurrency} does not match order total {expected:0.00} {expectedCurrency}.");
    }

    private static void EnsureOwner(Order order, string ownerId)
    {
        if (!order.BuyerId.Equals(ownerId, StringComparison.OrdinalIgnoreCase))
            throw ApiOperationException.NotFound("Order was not found.");
    }

    private static PayPalCard ToPayPalCard(CardRequest card) => new(card.Name, DigitsOnly(card.Number), card.Expiry,
        card.SecurityCode, new PayPalAddress(card.BillingAddress.AddressLine1, card.BillingAddress.AddressLine2,
            card.BillingAddress.City, card.BillingAddress.State, card.BillingAddress.PostalCode,
            card.BillingAddress.CountryCode.ToUpperInvariant()));

    private static void ValidateCard(CardRequest? card)
    {
        if (card is null) throw ApiOperationException.BadRequest("Card details are required.");
        if (string.IsNullOrWhiteSpace(card.Number) || card.Number.Any(x => !char.IsDigit(x) && x is not (' ' or '-')))
            throw ApiOperationException.BadRequest("Card number may contain only digits, spaces and hyphens.");
        var number = DigitsOnly(card.Number);
        if (number.Length is < 13 or > 19)
            throw ApiOperationException.BadRequest("Card number must contain 13 to 19 digits.");
        if (string.IsNullOrWhiteSpace(card.Name)) throw ApiOperationException.BadRequest("Cardholder name is required.");
        if (string.IsNullOrWhiteSpace(card.Expiry) ||
            !DateTime.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry.AddMonths(1) <= DateTime.UtcNow.Date)
            throw ApiOperationException.BadRequest("Card expiry must be a future month in YYYY-MM format.");
        if (string.IsNullOrWhiteSpace(card.SecurityCode) || card.SecurityCode.Length is < 3 or > 4 ||
            card.SecurityCode.Any(x => !char.IsDigit(x)))
            throw ApiOperationException.BadRequest("securityCode must contain 3 or 4 digits.");
        if (card.BillingAddress is null || card.BillingAddress.CountryCode.Length != 2)
            throw ApiOperationException.BadRequest("A billing address with a two-letter countryCode is required.");
        if (new[] { card.BillingAddress.AddressLine1, card.BillingAddress.City,
                card.BillingAddress.PostalCode, card.BillingAddress.CountryCode }.Any(string.IsNullOrWhiteSpace))
            throw ApiOperationException.BadRequest("Billing addressLine1, city, postalCode and countryCode are required.");
    }

    private static void ValidateAddress(AddressRequest address)
    {
        if (new[] { address.Street, address.City, address.Country, address.ZipCode }.Any(string.IsNullOrWhiteSpace))
            throw ApiOperationException.BadRequest("Street, city, country and zipCode are required.");
    }

    private static string DigitsOnly(string number) => new(number.Where(char.IsDigit).ToArray());
    private static string StableRequestId(string operation, Guid externalId, string callerKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(callerKey))).ToLowerInvariant();
        return $"eshop-{operation}-{externalId:N}-{hash[..24]}";
    }
    private static string PayPalCustomerReference(string ownerId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerId))).ToLowerInvariant();
    private static string Diagnostic(PayPalApiException ex) =>
        $"PayPal issues: {(ex.Issues.Count == 0 ? ex.ErrorName : string.Join(", ", ex.Issues))}; debug ID: {ex.DebugId ?? "not supplied"}.";
    private static IEnumerable<string> PaymentIds(Order order)
    {
        if (order.Payment?.PayPalOrderId is { } orderId) yield return orderId;
        if (order.Payment?.PayPalAuthorizationId is { } authorizationId) yield return authorizationId;
        if (order.Payment?.PayPalCaptureId is { } captureId) yield return captureId;
        if (order.Payment is not null)
            foreach (var refund in order.Payment.Refunds) yield return refund.PayPalRefundId;
    }
}

public sealed class ApiOperationException : Exception
{
    private ApiOperationException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    public int StatusCode { get; }
    public static ApiOperationException BadRequest(string message) => new(400, message);
    public static ApiOperationException NotFound(string message) => new(404, message);
    public static ApiOperationException Conflict(string message) => new(409, message);
}
