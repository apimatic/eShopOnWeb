using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class OrderPaymentService
{
    private readonly CatalogContext _context;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;
    private readonly PaymentOperationLocks _locks;

    public OrderPaymentService(CatalogContext context, IPayPalClient payPal,
        IOptions<PayPalOptions> options, PaymentOperationLocks locks)
    {
        _context = context;
        _payPal = payPal;
        _options = options.Value;
        _locks = locks;
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "At least one catalog item is required.");
        var quantities = request.Items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(i => i.Quantity));
        if (quantities.Values.Any(x => x is < 1 or > 100))
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "Each item quantity must be between 1 and 100.");
        var ids = quantities.Keys.ToArray();
        var catalogItems = await _context.CatalogItems.Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            var missing = ids.Except(catalogItems.Select(x => x.Id));
            throw new PaymentOperationException(HttpStatusCode.BadRequest,
                $"Unknown catalog item IDs: {string.Join(", ", missing)}.");
        }

        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, quantities[item.Id])).ToList();
        var address = request.ShipToAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), items);
        EnsureCentAmount(order.Total());
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
        return new PlaceOrderResponse(order.Id, order.Total(), Currency(), order.PaymentStatus.ToString());
    }

    public async Task<OrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var operationLock = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (order.FulfilmentStatus != FulfilmentStatus.Unfulfilled)
            throw new PaymentOperationException(HttpStatusCode.Conflict, "This order can no longer be paid.");
        if (order.PaymentStatus != PaymentStatus.AwaitingPayment) return ToResponse(order);
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw new PaymentOperationException(HttpStatusCode.BadRequest,
                "Specify exactly one of card or paymentMethodId.");

        PayPalCard? card = null;
        string? vaultId = null;
        if (request.Card is not null)
        {
            card = ToPayPalCard(request.Card);
        }
        else
        {
            var method = await _context.SavedPaymentMethods.SingleOrDefaultAsync(x =>
                x.Id == request.PaymentMethodId && x.BuyerId == buyerId && x.DeletedAt == null,
                cancellationToken);
            if (method is null) throw new PaymentOperationException(HttpStatusCode.NotFound, "Payment method not found.");
            vaultId = method.PayPalPaymentTokenId;
        }

        var total = EnsureCentAmount(order.Total());
        var currency = Currency();
        var authorization = await _payPal.AuthorizeAsync(new PayPalAuthorizeCommand(
            order.PaymentCorrelationId, order.Id, total, currency, InvoiceId(order), card, vaultId),
            cancellationToken);
        VerifyMoney(total, currency, authorization.Amount, authorization.Currency, "authorized");
        if (authorization.Status is not "CREATED" and not "PENDING")
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                $"PayPal returned authorization status {authorization.Status}; use another payment method.");
        order.RecordAuthorization(authorization.OrderId, authorization.AuthorizationId, authorization.Status,
            authorization.Amount, authorization.Currency, authorization.CreatedAt, authorization.UpdatedAt,
            authorization.ExpirationTime);
        await _context.SaveChangesAsync(cancellationToken);
        return ToResponse(order);
    }

    public async Task<OrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operationLock = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await AnyOrderAsync(orderId, cancellationToken);
        if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled) return ToResponse(order);
        if (order.FulfilmentStatus == FulfilmentStatus.Cancelled)
            throw new PaymentOperationException(HttpStatusCode.Conflict, "A cancelled order cannot be fulfilled.");
        if (order.PayPalAuthorizationId is null)
            throw new PaymentOperationException(HttpStatusCode.Conflict, "The order is awaiting payment authorization.");

        if (order.PayPalCaptureId is not null)
        {
            var existingCapture = await _payPal.GetCaptureAsync(order.PayPalCaptureId, cancellationToken);
            ApplyCapture(order, existingCapture);
            if (existingCapture.Status == "COMPLETED") order.MarkFulfilled(DateTimeOffset.UtcNow);
            await _context.SaveChangesAsync(cancellationToken);
            if (existingCapture.Status != "COMPLETED")
                throw new PaymentOperationException(HttpStatusCode.Conflict,
                    $"PayPal capture {existingCapture.CaptureId} is {existingCapture.Status}; retry fulfilment after it completes.");
            return ToResponse(order);
        }

        var authorization = await _payPal.GetAuthorizationAsync(order.PayPalAuthorizationId, cancellationToken);
        order.RefreshAuthorization(authorization.AuthorizationId, authorization.Status, authorization.CreatedAt,
            authorization.UpdatedAt, authorization.ExpirationTime);
        if (authorization.Status != "CREATED")
        {
            await _context.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                $"PayPal authorization {authorization.AuthorizationId} is {authorization.Status}; ask the shopper to place and pay a new order.");
        }

        var now = DateTimeOffset.UtcNow;
        if (authorization.CreatedAt.AddDays(3) <= now)
        {
            var original = order.OriginalAuthorizationCreatedAt ?? authorization.CreatedAt;
            if (original.AddDays(30) <= now)
                throw new PaymentOperationException(HttpStatusCode.Conflict,
                    "The PayPal authorization is at least 30 days old and cannot be renewed. Ask the shopper to place and pay a new order.");
            try
            {
                authorization = await _payPal.ReauthorizeAsync(authorization.AuthorizationId, order.Total(),
                    Currency(), $"{order.PaymentCorrelationId}:{authorization.AuthorizationId}", cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                throw new PaymentOperationException(HttpStatusCode.Conflict,
                    $"PayPal could not renew the stale authorization. Ask the shopper to place and pay a new order. {ex.Message}");
            }
            if (authorization.Status != "CREATED")
                throw new PaymentOperationException(HttpStatusCode.Conflict,
                    $"The renewed PayPal authorization is {authorization.Status}; ask the shopper to place and pay a new order.");
            order.RefreshAuthorization(authorization.AuthorizationId, authorization.Status, authorization.CreatedAt,
                authorization.UpdatedAt, authorization.ExpirationTime);
        }

        var capture = await _payPal.CaptureAsync(authorization.AuthorizationId, order.Total(), Currency(),
            InvoiceId(order), order.PaymentCorrelationId, cancellationToken);
        VerifyMoney(order.Total(), Currency(), capture.Amount, capture.Currency, "captured");
        ApplyCapture(order, capture);
        if (capture.Status == "COMPLETED") order.MarkFulfilled(now);
        await _context.SaveChangesAsync(cancellationToken);
        if (capture.Status != "COMPLETED")
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                $"PayPal capture {capture.CaptureId} is {capture.Status}; retry fulfilment after it completes.");
        return ToResponse(order);
    }

    public async Task<OrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operationLock = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await AnyOrderAsync(orderId, cancellationToken);
        if (order.FulfilmentStatus == FulfilmentStatus.Cancelled) return ToResponse(order);
        if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled || order.PayPalCaptureId is not null)
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                "A captured or fulfilled order cannot be cancelled; refund it instead.");
        if (order.PayPalAuthorizationId is not null)
            await _payPal.VoidAsync(order.PayPalAuthorizationId, order.PaymentCorrelationId, cancellationToken);
        order.MarkCancelled(DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return ToResponse(order);
    }

    public async Task<RefundResponse> RefundAsync(string buyerId, int orderId, string idempotencyKey,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 64)
            throw new PaymentOperationException(HttpStatusCode.BadRequest,
                "Idempotency-Key is required and must be at most 64 characters.");
        using var operationLock = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
        var existing = order.FindRefund(idempotencyKey);
        if (existing is not null)
        {
            if (request.Amount is not null && Money(request.Amount.Value) != existing.Amount)
                throw new PaymentOperationException(HttpStatusCode.Conflict,
                    "This Idempotency-Key was already used with a different refund amount.");
            return new RefundResponse(existing.PayPalRefundId, existing.Amount, existing.Currency, existing.Status);
        }
        if (order.FulfilmentStatus != FulfilmentStatus.Fulfilled || order.PayPalCaptureId is null ||
            order.CapturedAmount is null)
            throw new PaymentOperationException(HttpStatusCode.Conflict, "Only a fulfilled, captured order can be refunded.");

        var remaining = Money(order.CapturedAmount.Value - order.RefundedAmount());
        var amount = Money(request.Amount ?? remaining);
        if (amount <= 0 || amount > remaining)
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                $"Refund amount must be positive and no more than the remaining captured amount {remaining:0.00} {order.PaymentCurrency}.");
        var result = await _payPal.RefundAsync(order.PayPalCaptureId, amount,
            order.PaymentCurrency ?? Currency(), request.Note,
            $"{order.PaymentCorrelationId}:{idempotencyKey}", cancellationToken);
        VerifyMoney(amount, order.PaymentCurrency ?? Currency(), result.Amount, result.Currency, "refunded");
        var refund = new PaymentRefund(result.RefundId, idempotencyKey, result.Amount, result.Currency,
            result.Status, result.CreatedAt);
        order.AddRefund(refund);
        await _context.SaveChangesAsync(cancellationToken);
        return new RefundResponse(result.RefundId, result.Amount, result.Currency, result.Status);
    }

    public async Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _context.Orders.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .Include(x => x.Refunds)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(ToResponse).ToList();
    }

    public async Task<SavePaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var saved = await _payPal.SaveCardAsync(ToPayPalCard(request.Card), MerchantCustomerId(buyerId),
            operationId, cancellationToken);
        var entity = new SavedPaymentMethod(buyerId, saved.PaymentTokenId, saved.CustomerId, saved.Brand,
            saved.Last4, saved.Expiry, DateTimeOffset.UtcNow);
        _context.SavedPaymentMethods.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);
        return new SavePaymentMethodResponse(entity.Id, entity.Brand, entity.Last4, entity.Expiry);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) => await _context.SavedPaymentMethods.AsNoTracking()
        .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
        .OrderBy(x => x.Id)
        .Select(x => new PaymentMethodResponse(x.Id, x.Brand, x.Last4, x.Expiry))
        .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        using var operationLock = await _locks.AcquireAsync($"method:{paymentMethodId}", cancellationToken);
        var method = await _context.SavedPaymentMethods.SingleOrDefaultAsync(x =>
            x.Id == paymentMethodId && x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
        if (method is null) throw new PaymentOperationException(HttpStatusCode.NotFound, "Payment method not found.");
        await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken);
        method.MarkDeleted(DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw new PaymentOperationException(HttpStatusCode.BadRequest, "from must be earlier than to.");
        var payPal = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _context.Orders.AsNoTracking()
            .Where(x => (x.AuthorizationCreatedAt >= from && x.AuthorizationCreatedAt <= to) ||
                        (x.CapturedAt >= from && x.CapturedAt <= to) ||
                        x.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to))
            .Include(x => x.Refunds)
            .ToListAsync(cancellationToken);
        var local = orders.SelectMany(order => LocalTransactions(order, from, to)).ToList();
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();
        foreach (var transaction in payPal)
        {
            var localMatch = local.FirstOrDefault(x => x.TransactionId == transaction.TransactionId);
            localMatch ??= local.FirstOrDefault(x => !matched.Contains(x.TransactionId) &&
                !string.IsNullOrWhiteSpace(transaction.InvoiceId) && x.InvoiceId == transaction.InvoiceId &&
                transaction.Amount is not null && x.Amount is not null &&
                Math.Abs(transaction.Amount.Value) == Math.Abs(x.Amount.Value));
            if (localMatch is not null) matched.Add(localMatch.TransactionId);
            entries.Add(new ReconciliationEntry(
                localMatch is null ? "PayPal" : "Both",
                transaction.TransactionId,
                transaction.ReferenceId,
                localMatch?.TransactionId,
                localMatch?.OrderId,
                transaction.EventCode,
                transaction.Status,
                transaction.Amount,
                transaction.Fee,
                transaction.Currency,
                transaction.InitiationDate,
                localMatch is null ? "PayPalOnly" : "Matched"));
        }
        entries.AddRange(local.Where(x => !matched.Contains(x.TransactionId)).Select(x =>
            new ReconciliationEntry("eShop", null, null, x.TransactionId, x.OrderId, null, x.Status,
                x.Amount, x.Fee, x.Currency, x.Date, "EShopOnly")));
        return new ReconciliationResponse(from, to, entries.OrderBy(x => x.TransactionDate).ToList());
    }

    private async Task<Order> OwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Where(x => x.Id == orderId && x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .Include(x => x.Refunds)
            .SingleOrDefaultAsync(cancellationToken);
        return order ?? throw new PaymentOperationException(HttpStatusCode.NotFound, "Order not found.");
    }

    private async Task<Order> AnyOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .Where(x => x.Id == orderId)
            .Include(x => x.OrderItems)
            .Include(x => x.Refunds)
            .SingleOrDefaultAsync(cancellationToken);
        return order ?? throw new PaymentOperationException(HttpStatusCode.NotFound, "Order not found.");
    }

    private static void ApplyCapture(Order order, PayPalCaptureResult capture) => order.RecordCapture(
        capture.CaptureId, capture.Status, capture.Amount, capture.Currency, capture.Fee,
        capture.NetAmount, capture.CreatedAt);

    private static OrderResponse ToResponse(Order order) => new(
        order.Id, order.OrderDate, order.Total(), order.PaymentCurrency,
        order.PaymentStatus.ToString(), order.FulfilmentStatus.ToString(), order.PayPalOrderId,
        order.PayPalAuthorizationId, order.PayPalAuthorizationStatus, order.AuthorizationExpiresAt,
        order.PayPalCaptureId, order.PayPalCaptureStatus, order.CapturedAmount, order.PayPalFee,
        order.NetProceeds, order.RefundedAmount(),
        order.OrderItems.Select(x => new OrderItemResponse(x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
        order.Refunds.Select(x => new RefundDetailsResponse(x.PayPalRefundId, x.Amount, x.Currency,
            x.Status, x.CreatedAt)).ToList());

    private PayPalCard ToPayPalCard(CardInput card)
    {
        var number = Regex.Replace(card.Number ?? string.Empty, "[ -]", string.Empty);
        var securityCode = card.SecurityCode ?? string.Empty;
        var expiry = card.Expiry ?? string.Empty;
        var countryCode = card.BillingAddress.CountryCode ?? string.Empty;
        if (!Regex.IsMatch(number, "^[0-9]{13,19}$"))
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "Card number format is invalid.");
        if (!Regex.IsMatch(securityCode, "^[0-9]{3,4}$"))
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "Card security code format is invalid.");
        if (!Regex.IsMatch(expiry, "^[0-9]{4}-(0[1-9]|1[0-2])$"))
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "Card expiry must use YYYY-MM format.");
        if (!Regex.IsMatch(countryCode, "^[A-Za-z]{2}$"))
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "Billing countryCode must be a two-letter code.");
        return new PayPalCard(number, expiry, securityCode, card.Name,
            countryCode.ToUpperInvariant(), card.BillingAddress.AddressLine1,
            card.BillingAddress.AddressLine2, card.BillingAddress.AdminArea1,
            card.BillingAddress.AdminArea2, card.BillingAddress.PostalCode);
    }

    private string Currency()
    {
        var currency = _options.Currency ?? string.Empty;
        if (!Regex.IsMatch(currency, "^[A-Za-z]{3}$"))
            throw new InvalidOperationException("PayPal:Currency must be a three-letter ISO-4217 code.");
        return currency.ToUpperInvariant();
    }

    private static decimal EnsureCentAmount(decimal amount)
    {
        if (amount <= 0 || Money(amount) != amount)
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                "The order total must be positive and have no more than two decimal places.");
        return amount;
    }

    private static decimal Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static void VerifyMoney(decimal expectedAmount, string expectedCurrency, decimal actualAmount,
        string actualCurrency, string operation)
    {
        if (Money(expectedAmount) != Money(actualAmount) ||
            !expectedCurrency.Equals(actualCurrency, StringComparison.OrdinalIgnoreCase))
            throw new PaymentOperationException(HttpStatusCode.BadGateway,
                $"PayPal {operation} {actualAmount.ToString("0.00", CultureInfo.InvariantCulture)} {actualCurrency}, " +
                $"but the order requires {expectedAmount.ToString("0.00", CultureInfo.InvariantCulture)} {expectedCurrency}.");
    }

    private static string InvoiceId(Order order) => $"ESHOP-{order.Id}-{order.PaymentCorrelationId}";

    private static string MerchantCustomerId(string buyerId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant();
        return $"eshop-{hash[..32]}";
    }

    private static IEnumerable<LocalTransaction> LocalTransactions(Order order, DateTimeOffset from,
        DateTimeOffset to)
    {
        if (order.PayPalAuthorizationId is not null && order.AuthorizationCreatedAt >= from &&
            order.AuthorizationCreatedAt <= to)
            yield return new LocalTransaction(order.PayPalAuthorizationId, order.Id, InvoiceId(order),
                order.PayPalAuthorizationStatus, order.Total(), null, order.PaymentCurrency,
                order.AuthorizationCreatedAt);
        if (order.PayPalCaptureId is not null && order.CapturedAt >= from && order.CapturedAt <= to)
            yield return new LocalTransaction(order.PayPalCaptureId, order.Id, InvoiceId(order),
                order.PayPalCaptureStatus, order.CapturedAmount, order.PayPalFee, order.PaymentCurrency,
                order.CapturedAt);
        foreach (var refund in order.Refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to))
            yield return new LocalTransaction(refund.PayPalRefundId, order.Id, InvoiceId(order),
                refund.Status, refund.Amount, null, refund.Currency, refund.CreatedAt);
    }

    private sealed record LocalTransaction(string TransactionId, int OrderId, string InvoiceId,
        string? Status, decimal? Amount, decimal? Fee, string? Currency, DateTimeOffset? Date);
}
