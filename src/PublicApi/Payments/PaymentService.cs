using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private readonly CatalogContext _db;
    private readonly PayPalClient _payPal;
    private readonly PayPalOptions _options;

    public PaymentService(CatalogContext db, PayPalClient payPal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
    }

    public async Task<OrderCreatedResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw BadRequest("An order must contain at least one catalog item.");
        if (request.ShippingAddress is null)
            throw BadRequest("A shipping address is required.");
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw BadRequest("Catalog item ids and quantities must be positive.");

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        var catalogItems = await _db.CatalogItems
            .Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = requested.Keys.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0)
            throw BadRequest($"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price, requested[item.Id])).ToList();
        var address = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return new OrderCreatedResponse(order.Id, order.Status.ToString(), order.Total(), Currency);
    }

    public async Task<AuthorizationResponse> PayAsync(string buyerId, int orderId,
        PayOrderRequest request, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
            if (order.Status == OrderStatus.Authorized)
                return AuthorizationDto(order);
            if (order.Status != OrderStatus.AwaitingPayment)
                throw Conflict($"Order {orderId} cannot be paid while it is {order.Status}.");
            if ((request.Card is null) == (request.PaymentMethodId is null))
                throw BadRequest("Supply either card or paymentMethodId, but not both.");

            string? vaultId = null;
            if (request.PaymentMethodId is int paymentMethodId)
            {
                var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
                    x.Id == paymentMethodId && x.BuyerId == buyerId && !x.IsDeleted, cancellationToken);
                if (method is null)
                    throw new PaymentApiException(StatusCodes.Status404NotFound,
                        $"Payment method {paymentMethodId} was not found.");
                vaultId = method.PayPalTokenId;
            }
            else
            {
                ValidateCard(request.Card!);
            }

            var total = Decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
            var result = await _payPal.CreateAndAuthorizeOrderAsync(orderId, order.PaymentReference, total, Currency,
                request.Card, vaultId, cancellationToken);
            EnsureMoney(result.Amount, result.Currency, total);
            order.RecordAuthorization(Currency, result.PayPalOrderId, result.PayPalOrderStatus,
                result.AuthorizationId, result.AuthorizationStatus, result.Amount,
                result.CreatedAt, result.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
            return AuthorizationDto(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<CaptureResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await OrderAsync(orderId, cancellationToken);
            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
                return CaptureDto(order);
            if (order.Status != OrderStatus.Authorized || order.AuthorizationId is null)
                throw Conflict($"Order {orderId} must be authorized before it can be fulfilled.");

            var current = await _payPal.GetAuthorizationAsync(order.PayPalOrderId!,
                order.AuthorizationId, cancellationToken);
            var honorPeriodEnded = DateTimeOffset.UtcNow >= current.CreatedAt.AddDays(3);
            if (current.AuthorizationStatus == "EXPIRED" || honorPeriodEnded)
            {
                var originalCreated = order.AuthorizationCreatedAt ?? current.CreatedAt;
                if (order.AuthorizationReauthorized || DateTimeOffset.UtcNow >= originalCreated.AddDays(29))
                    throw Conflict($"Authorization {order.AuthorizationId} can no longer be renewed. " +
                        "Ask the shopper to authorize the order again with a valid payment method.");
                current = await _payPal.ReauthorizeAsync(order.PayPalOrderId!, order.AuthorizationId,
                    order.Total(), Currency, order.PaymentReference, cancellationToken);
                EnsureMoney(current.Amount, current.Currency, order.Total());
                order.RecordAuthorization(Currency, current.PayPalOrderId, current.PayPalOrderStatus,
                    current.AuthorizationId, current.AuthorizationStatus, current.Amount,
                    current.CreatedAt, current.ExpiresAt, reauthorized: true);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else if (current.AuthorizationStatus is "DENIED" or "VOIDED")
            {
                throw Conflict($"Authorization {order.AuthorizationId} is {current.AuthorizationStatus}. " +
                    "Ask the shopper to authorize the order again with a valid payment method.");
            }

            var capture = await _payPal.CaptureAsync(order.AuthorizationId!, order.Total(),
                Currency, order.PaymentReference, cancellationToken);
            EnsureMoney(capture.Amount, capture.Currency, order.Total());
            if (capture.Status != "COMPLETED")
                throw Conflict($"PayPal capture {capture.Id} is {capture.Status}; do not ship until it is COMPLETED.");
            order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.Net);
            await _db.SaveChangesAsync(cancellationToken);
            return CaptureDto(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<CancellationResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await OrderAsync(orderId, cancellationToken);
            if (order.Status == OrderStatus.Cancelled)
                return new CancellationResponse(order.Id, order.Status.ToString(), order.AuthorizationStatus!);
            if (order.Status != OrderStatus.Authorized || order.AuthorizationId is null)
                throw Conflict($"Only an authorized, unfulfilled order can be cancelled; order {orderId} is {order.Status}.");
            await _payPal.VoidAsync(order.AuthorizationId, order.PaymentReference, cancellationToken);
            order.RecordCancellation("VOIDED");
            await _db.SaveChangesAsync(cancellationToken);
            return new CancellationResponse(order.Id, order.Status.ToString(), order.AuthorizationStatus!);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundResponse> RefundAsync(string buyerId, int orderId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 108)
            throw BadRequest("idempotencyKey is required and cannot exceed 108 characters.");
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
            var previous = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
            if (previous is not null) return RefundDto(order, previous);
            if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded) ||
                order.CaptureId is null || order.CapturedAmount is null)
                throw Conflict($"Order {orderId} has no refundable captured payment.");

            var alreadyRefunded = order.Refunds.Sum(x => x.Amount);
            var remaining = order.CapturedAmount.Value - alreadyRefunded;
            var amount = request.Amount ?? remaining;
            amount = Decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
            if (amount <= 0 || amount > remaining)
                throw BadRequest($"Refund amount must be positive and no more than {remaining:0.00} {Currency}.");

            var result = await _payPal.RefundAsync(order.CaptureId, amount, Currency,
                ProcessorRefundKey(order.PaymentReference, request.IdempotencyKey),
                request.IdempotencyKey, request.Note, cancellationToken);
            EnsureMoney(result.Amount, result.Currency, amount);
            var refund = order.RecordRefund(result.Id, request.IdempotencyKey, result.Status, result.Amount);
            await _db.SaveChangesAsync(cancellationToken);
            return RefundDto(order, refund);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderSummaryResponse>> MyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .Include(x => x.Refunds)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(OrderSummary).ToList();
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        ValidateCard(request.Card);
        var result = await _payPal.VaultCardAsync(buyerId, request.Card,
            "eshop-vault-" + Guid.NewGuid().ToString("N"), cancellationToken);
        var method = new SavedPaymentMethod(buyerId, result.TokenId, result.CustomerId,
            result.Brand, result.LastFour, result.Expiry);
        _db.SavedPaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return PaymentMethodDto(method);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> PaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) =>
        await _db.SavedPaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PaymentMethodResponse(x.Id, x.Brand, x.LastFour, x.Expiry, x.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
            x.Id == paymentMethodId && x.BuyerId == buyerId && !x.IsDeleted, cancellationToken);
        if (method is null)
            throw new PaymentApiException(StatusCodes.Status404NotFound,
                $"Payment method {paymentMethodId} was not found.");
        await _payPal.DeletePaymentTokenAsync(method.PayPalTokenId, cancellationToken);
        method.Delete();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw BadRequest("from must be earlier than to.");
        if (to - from > TimeSpan.FromDays(31))
            throw BadRequest("PayPal Transaction Search supports a maximum range of 31 days.");
        var paypal = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _db.Orders.AsNoTracking().Include(x => x.Refunds)
            .Where(x => x.AuthorizationCreatedAt <= to || x.FulfilledAt <= to || x.OrderDate <= to)
            .ToListAsync(cancellationToken);
        var local = BuildLocalPaymentIndex(orders, from, to);
        var entries = new List<ReconciliationEntry>();
        var matchedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transaction in paypal)
        {
            var match = MatchLocal(transaction, local);
            if (match is not null) matchedIds.Add(match.Id);
            entries.Add(new ReconciliationEntry("PayPal", transaction.Id, transaction.ReferenceId,
                transaction.EventCode, transaction.Status, transaction.Date, transaction.Amount,
                transaction.Fee, transaction.Currency, match?.OrderId, match?.Type,
                match is null ? "MissingFromEShop" : "Matched"));
        }
        foreach (var payment in local.Where(x => !matchedIds.Contains(x.Id)))
        {
            entries.Add(new ReconciliationEntry("EShop", payment.Id, null, null, payment.Status,
                payment.Date, payment.Amount, null, payment.Currency, payment.OrderId, payment.Type,
                "MissingFromPayPal"));
        }
        return new ReconciliationResponse(from, to, paypal.Count, entries);
    }

    private async Task<Order> OwnedOrderAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        return order ?? throw new PaymentApiException(StatusCodes.Status404NotFound,
            $"Order {orderId} was not found.");
    }

    private async Task<Order> OrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return order ?? throw new PaymentApiException(StatusCodes.Status404NotFound,
            $"Order {orderId} was not found.");
    }

    private string Currency => _options.Currency.ToUpperInvariant();

    private void EnsureMoney(decimal actualAmount, string actualCurrency, decimal expectedAmount)
    {
        expectedAmount = Decimal.Round(expectedAmount, 2, MidpointRounding.AwayFromZero);
        if (actualAmount != expectedAmount || !actualCurrency.Equals(Currency, StringComparison.Ordinal))
            throw new PaymentApiException(StatusCodes.Status502BadGateway,
                $"PayPal reported {actualAmount:0.00} {actualCurrency}, expected {expectedAmount:0.00} {Currency}.");
    }

    private static void ValidateCard(CardRequest card)
    {
        var number = card.Number?.Replace(" ", string.Empty, StringComparison.Ordinal) ?? string.Empty;
        var securityCode = card.SecurityCode ?? string.Empty;
        if (string.IsNullOrWhiteSpace(card.Name) || number.Length is < 13 or > 19 ||
            number.Any(x => !char.IsDigit(x)) ||
            !DateTime.TryParseExact(card.Expiry, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry < DateTime.UtcNow.Date.AddDays(-DateTime.UtcNow.Day + 1) ||
            securityCode.Length is < 3 or > 4 || securityCode.Any(x => !char.IsDigit(x)) ||
            card.BillingAddress is null || card.BillingAddress.CountryCode?.Length != 2)
            throw BadRequest("The card number, future yyyy-MM expiry, security code, name, and billing address are required.");
    }

    private static AuthorizationResponse AuthorizationDto(Order order) => new(
        order.Id, order.Status.ToString(), order.PayPalOrderId!, order.AuthorizationId!,
        order.AuthorizationStatus!, order.AuthorizedAmount!.Value, order.PaymentCurrency!,
        order.AuthorizationExpiresAt);

    private static CaptureResponse CaptureDto(Order order) => new(
        order.Id, order.Status.ToString(), order.CaptureId!, order.CaptureStatus!,
        order.CapturedAmount!.Value, order.PayPalFee, order.NetProceeds, order.PaymentCurrency!);

    private static RefundResponse RefundDto(Order order, PaymentRefund refund)
    {
        var total = order.Refunds.Sum(x => x.Amount);
        return new RefundResponse(refund.PayPalRefundId, order.Id, order.Status.ToString(),
            refund.Status, refund.Amount, total, order.CapturedAmount!.Value - total,
            order.PaymentCurrency!);
    }

    private static PaymentMethodResponse PaymentMethodDto(SavedPaymentMethod method) =>
        new(method.Id, method.Brand, method.LastFour, method.Expiry, method.CreatedAt);

    private static OrderSummaryResponse OrderSummary(Order order) => new(
        order.Id, order.OrderDate, order.Status.ToString(), order.Total(), order.PaymentCurrency,
        order.AuthorizationId, order.AuthorizationStatus, order.AuthorizedAmount,
        order.AuthorizationExpiresAt, order.CaptureId, order.CaptureStatus, order.CapturedAmount,
        order.PayPalFee, order.NetProceeds, order.Refunds.Sum(x => x.Amount),
        order.Refunds.Select(x => new RefundSummary(x.PayPalRefundId, x.Status, x.Amount, x.CreatedAt)).ToList());

    private static List<LocalPayment> BuildLocalPaymentIndex(IEnumerable<Order> orders,
        DateTimeOffset from, DateTimeOffset to)
    {
        var result = new List<LocalPayment>();
        foreach (var order in orders)
        {
            if (order.AuthorizationId is not null && order.AuthorizationCreatedAt >= from && order.AuthorizationCreatedAt <= to)
                result.Add(new LocalPayment(order.AuthorizationId, order.Id, order.PaymentReference, "Authorization",
                    order.AuthorizationStatus, order.AuthorizationCreatedAt, order.AuthorizedAmount, order.PaymentCurrency));
            if (order.CaptureId is not null && order.FulfilledAt >= from && order.FulfilledAt <= to)
                result.Add(new LocalPayment(order.CaptureId, order.Id, order.PaymentReference, "Capture", order.CaptureStatus,
                    order.FulfilledAt, order.CapturedAmount, order.PaymentCurrency));
            result.AddRange(order.Refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
                .Select(x => new LocalPayment(x.PayPalRefundId, order.Id, order.PaymentReference, "Refund", x.Status,
                    x.CreatedAt, x.Amount, order.PaymentCurrency)));
        }
        return result;
    }

    private static LocalPayment? MatchLocal(PayPalTransaction transaction, List<LocalPayment> local)
    {
        var exact = local.FirstOrDefault(x => x.Id == transaction.Id || x.Id == transaction.ReferenceId);
        if (exact is not null) return exact;
        if (transaction.InvoiceId?.StartsWith("eshop-", StringComparison.Ordinal) == true)
            return local.FirstOrDefault(x => x.PaymentReference == transaction.InvoiceId[6..]);
        if (!string.IsNullOrWhiteSpace(transaction.CustomField))
            return local.FirstOrDefault(x => x.PaymentReference == transaction.CustomField);
        return null;
    }

    private sealed record LocalPayment(string Id, int OrderId, string PaymentReference, string Type, string? Status,
        DateTimeOffset? Date, decimal? Amount, string? Currency);

    private static PaymentApiException BadRequest(string message) =>
        new(StatusCodes.Status400BadRequest, message);
    private static PaymentApiException Conflict(string message) =>
        new(StatusCodes.Status409Conflict, message);

    private static string ProcessorRefundKey(string paymentReference, string callerKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(callerKey)))
            .ToLowerInvariant()[..32];
        return $"eshop-refund-{paymentReference}-{hash}";
    }
}
