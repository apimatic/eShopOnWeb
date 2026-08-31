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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class CommercePaymentService : ICommercePaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private readonly CatalogContext _context;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalOptions _options;

    public CommercePaymentService(CatalogContext context, IPayPalGateway payPal, IUriComposer uriComposer,
        IOptions<PayPalOptions> options)
    {
        _context = context;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _options = options.Value;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        Address shippingAddress, CancellationToken cancellationToken)
    {
        if (lines.Count == 0) throw BadRequest("At least one catalog item is required.");
        if (lines.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw BadRequest("Catalog item ids and quantities must be positive.");
        if (lines.Select(x => x.CatalogItemId).Distinct().Count() != lines.Count)
            throw BadRequest("Each catalog item may appear only once; combine duplicate quantities.");

        var ids = lines.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _context.CatalogItems.Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = ids.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0) throw BadRequest($"Catalog items do not exist: {string.Join(", ", missing)}.");

        var orderItems = lines.Select(line =>
        {
            var item = catalogItems.Single(x => x.Id == line.CatalogItemId);
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name,
                _uriComposer.ComposePicUri(item.PictureUri)), item.Price, line.Quantity);
        }).ToList();
        var order = new Order(buyerId, shippingAddress, orderItems, _options.Currency);
        if (decimal.Round(order.Total(), 2) != order.Total())
            throw BadRequest("The catalog total has more precision than PayPal can charge for this currency.");
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, PaymentCardData? card,
        int? paymentMethodId, CancellationToken cancellationToken)
    {
        if ((card is null) == (paymentMethodId is null))
            throw BadRequest("Specify exactly one of card or paymentMethodId.");
        return await Locked(orderId, async () =>
        {
            var order = await OwnedOrder(orderId, buyerId, cancellationToken);
            if (order.PaymentStatus == PaymentStatus.Authorized) return order;
            if (order.PaymentStatus == PaymentStatus.AuthorizationPending && order.AuthorizationId is not null)
            {
                var current = await _payPal.GetAuthorizationAsync(order.AuthorizationId, cancellationToken);
                order.RefreshAuthorization(current.Status);
                await _context.SaveChangesAsync(cancellationToken);
                if (order.PaymentStatus == PaymentStatus.Authorized) return order;
                throw Conflict($"PayPal authorization {current.AuthorizationId} is {current.Status}; retry when it reaches CREATED.");
            }
            if (order.PaymentStatus != PaymentStatus.AwaitingPayment && order.PaymentStatus != PaymentStatus.AuthorizationDenied)
                throw Conflict($"Order {orderId} cannot be paid while its payment state is {order.PaymentStatus}.");
            if (order.FulfilmentStatus != FulfilmentStatus.Unfulfilled)
                throw Conflict($"Order {orderId} is {order.FulfilmentStatus}.");

            PayPalPaymentSource source;
            if (paymentMethodId.HasValue)
            {
                var method = await _context.PaymentMethods.SingleOrDefaultAsync(x =>
                    x.Id == paymentMethodId.Value && x.BuyerId == buyerId && x.IsActive, cancellationToken)
                    ?? throw NotFound("The saved payment method was not found.");
                source = new PayPalPaymentSource(method.PayPalPaymentTokenId, null,
                    $"{method.Brand} ending {method.LastDigits}");
            }
            else
            {
                ValidateCard(card!);
                var normalized = card!.Number.Replace(" ", string.Empty, StringComparison.Ordinal);
                source = new PayPalPaymentSource(null, card, $"Card ending {normalized[^4..]}");
            }

            var total = decimal.Round(order.Total(), 2);
            var result = await _payPal.AuthorizeAsync(order.ExternalReference, total, order.Currency,
                source, cancellationToken);
            EnsureAmount(total, order.Currency, result.Amount, result.Currency, "authorization");
            order.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus,
                result.Amount, result.CreatedAt, result.ExpiresAt, source.Description);
            await _context.SaveChangesAsync(cancellationToken);
            if (order.PaymentStatus != PaymentStatus.Authorized)
                throw Conflict($"PayPal authorization {result.AuthorizationId} is {result.AuthorizationStatus}; no capturable hold exists yet.");
            return order;
        });
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken) =>
        await Locked(orderId, async () =>
        {
            var order = await Order(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled && order.CaptureId is not null) return order;
            if (order.FulfilmentStatus != FulfilmentStatus.Unfulfilled)
                throw Conflict($"Order {orderId} is {order.FulfilmentStatus} and cannot be fulfilled.");
            if (order.PaymentStatus != PaymentStatus.Authorized || order.AuthorizationId is null)
                throw Conflict($"Order {orderId} needs a CREATED PayPal authorization before fulfilment.");

            PayPalAuthorizationDetails current;
            try { current = await _payPal.GetAuthorizationAsync(order.AuthorizationId, cancellationToken); }
            catch (PayPalApiException ex) when (ex.StatusCode is 404 or 422)
            {
                throw Conflict($"Authorization {order.AuthorizationId} can no longer be inspected or renewed. Cancel this order and ask the shopper to place and pay a replacement order. {ex.Message}");
            }
            order.RefreshAuthorization(current.Status);
            if (current.Status is not ("CREATED" or "CAPTURED"))
            {
                await _context.SaveChangesAsync(cancellationToken);
                throw Conflict($"Authorization {current.AuthorizationId} is {current.Status}. Cancel this order and ask the shopper to place and pay a replacement order.");
            }

            var stale = current.Status == "CREATED" && DateTimeOffset.UtcNow >= current.CreatedAt.AddDays(3);
            if (stale)
            {
                if (current.ExpiresAt.HasValue && DateTimeOffset.UtcNow >= current.ExpiresAt.Value)
                    throw Conflict($"Authorization {current.AuthorizationId} expired at {current.ExpiresAt:O} and cannot be renewed. Cancel this order and ask the shopper to place and pay a replacement order.");
                try
                {
                    current = await _payPal.ReauthorizeAsync(order.ExternalReference, current.AuthorizationId,
                        order.Total(), order.Currency, cancellationToken);
                }
                catch (PayPalApiException ex) when (ex.StatusCode is 404 or 422)
                {
                    throw Conflict($"Authorization {order.AuthorizationId} could not be renewed. Cancel this order and ask the shopper to place and pay a replacement order. {ex.Message}");
                }
                if (current.Status != "CREATED")
                    throw Conflict($"Renewed authorization {current.AuthorizationId} is {current.Status}; retry fulfilment after PayPal reports CREATED.");
                order.RecordReauthorization(current.AuthorizationId, current.Status, current.CreatedAt, current.ExpiresAt);
                await _context.SaveChangesAsync(cancellationToken);
            }

            var capture = await _payPal.CaptureAsync(order.ExternalReference, current.AuthorizationId,
                order.Total(), order.Currency, cancellationToken);
            EnsureAmount(order.Total(), order.Currency, capture.Amount, capture.Currency, "capture");
            if (capture.Status != "COMPLETED")
                throw Conflict($"PayPal capture {capture.CaptureId} is {capture.Status}; the order was not marked fulfilled. Retry after PayPal completes it.");
            order.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.Fee, capture.Net,
                capture.CreatedAt);
            await _context.SaveChangesAsync(cancellationToken);
            return order;
        });

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken) =>
        await Locked(orderId, async () =>
        {
            var order = await Order(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Cancelled) return order;
            if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled || order.CaptureId is not null)
                throw Conflict("A fulfilled/captured order cannot be cancelled; refund it instead.");
            var voided = false;
            if (order.AuthorizationId is not null && order.PaymentStatus is PaymentStatus.Authorized or PaymentStatus.AuthorizationPending)
            {
                var authorization = await _payPal.GetAuthorizationAsync(order.AuthorizationId, cancellationToken);
                if (authorization.Status == "CREATED")
                {
                    await _payPal.VoidAsync(order.ExternalReference, order.AuthorizationId, cancellationToken);
                    voided = true;
                }
                else if (authorization.Status == "VOIDED") voided = true;
                else if (authorization.Status == "CAPTURED")
                    throw Conflict("PayPal reports this authorization as captured; refund it instead of cancelling.");
            }
            order.Cancel(voided);
            await _context.SaveChangesAsync(cancellationToken);
            return order;
        });

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 128)
            throw BadRequest("idempotencyKey is required and must be at most 128 characters.");
        return await Locked(orderId, async () =>
        {
            var order = await OwnedOrder(orderId, buyerId, cancellationToken);
            var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
            if (existing is not null) return existing;
            if (order.CaptureId is null || order.CapturedAmount is null)
                throw Conflict("Only a captured order can be refunded.");
            var remaining = order.CapturedAmount.Value - order.RefundedAmount;
            var refundAmount = amount ?? remaining;
            if (refundAmount <= 0 || refundAmount > remaining)
                throw BadRequest($"Refund amount must be greater than zero and no more than the remaining captured amount {remaining:0.00} {order.Currency}.");
            if (decimal.Round(refundAmount, 2) != refundAmount)
                throw BadRequest("Refund amount must have no more than two decimal places.");

            var requestId = "refund-" + order.ExternalReference[..16] + "-" + Hash(idempotencyKey)[..32];
            var result = await _payPal.RefundAsync(requestId, order.CaptureId, refundAmount, order.Currency,
                cancellationToken);
            EnsureAmount(refundAmount, order.Currency, result.Amount, result.Currency, "refund");
            if (result.Status is not ("COMPLETED" or "PENDING"))
                throw Conflict($"PayPal refund {result.RefundId} is {result.Status}.");
            var refund = order.AddRefund(idempotencyKey, result.RefundId, result.Status, result.Amount,
                result.Fee, result.Net, result.CreatedAt);
            await _context.SaveChangesAsync(cancellationToken);
            return refund;
        });
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken) =>
        await _context.Orders.AsNoTracking().Include(x => x.OrderItems).Include(x => x.Refunds)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);

    public async Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, PaymentCardData card,
        CancellationToken cancellationToken)
    {
        ValidateCard(card);
        var result = await _payPal.SaveCardAsync(Guid.NewGuid().ToString("N"), CustomerId(buyerId), card,
            cancellationToken);
        var method = new PaymentMethod(buyerId, result.TokenId, result.Brand, result.LastDigits, result.Expiry);
        _context.PaymentMethods.Add(method);
        await _context.SaveChangesAsync(cancellationToken);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) => await _context.PaymentMethods.AsNoTracking()
        .Where(x => x.BuyerId == buyerId && x.IsActive).OrderByDescending(x => x.CreatedAt)
        .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var method = await _context.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId &&
            x.BuyerId == buyerId, cancellationToken) ?? throw NotFound("The saved payment method was not found.");
        if (!method.IsActive) return;
        try { await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken); }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            // The previous delete may have reached PayPal before a local database failure.
        }
        method.Deactivate();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from) throw BadRequest("to must be later than from.");
        if (to - from > TimeSpan.FromDays(31))
            throw BadRequest("PayPal Transaction Search supports a maximum range of 31 days.");
        var paypal = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _context.Orders.AsNoTracking().Include(x => x.Refunds)
            .Where(x => (x.CapturedAt >= from && x.CapturedAt <= to) ||
                        x.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to))
            .ToListAsync(cancellationToken);
        var local = orders.SelectMany(order => LocalEntries(order, from, to)).ToList();
        var entries = new List<ReconciliationEntry>();
        var matchedLocalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transaction in paypal)
        {
            var exactMatch = local.FirstOrDefault(x => x.PayPalId == transaction.TransactionId ||
                x.PayPalId == transaction.ReferenceId);
            var invoiceOrder = exactMatch is null && transaction.InvoiceId is not null
                ? orders.FirstOrDefault(x => x.ExternalReference == transaction.InvoiceId)
                : null;
            if (exactMatch is not null) matchedLocalIds.Add(exactMatch.PayPalId);
            var matchStatus = exactMatch is not null ? "Matched" : invoiceOrder is not null
                ? "OrderMatchedIdDifferent" : "PayPalOnly";
            entries.Add(new ReconciliationEntry("PayPal", matchStatus,
                exactMatch?.OrderId ?? invoiceOrder?.Id, transaction.TransactionId, transaction.ReferenceId, transaction.InvoiceId,
                transaction.EventCode, transaction.Status, transaction.Amount, transaction.Fee,
                transaction.Currency, transaction.InitiatedAt));
        }
        entries.AddRange(local.Where(x => !matchedLocalIds.Contains(x.PayPalId)).Select(x =>
            new ReconciliationEntry("eShop", "EShopOnly", x.OrderId, x.PayPalId, null, x.InvoiceId,
                x.Type, x.Status, x.Amount, x.Fee, x.Currency, x.Time)));
        return new ReconciliationResult(from, to, paypal.Count, local.Count, entries);
    }

    private async Task<Order> OwnedOrder(int id, string buyerId, CancellationToken cancellationToken) =>
        await _context.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == id && x.BuyerId == buyerId, cancellationToken)
            ?? throw NotFound("The order was not found.");

    private async Task<Order> Order(int id, CancellationToken cancellationToken) =>
        await _context.Orders.Include(x => x.OrderItems).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound("The order was not found.");

    private static async Task<T> Locked<T>(int orderId, Func<Task<T>> action)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { return await action(); }
        finally { gate.Release(); }
    }

    private static void EnsureAmount(decimal expectedAmount, string expectedCurrency, decimal actualAmount,
        string actualCurrency, string operation)
    {
        if (expectedAmount != actualAmount || !expectedCurrency.Equals(actualCurrency, StringComparison.OrdinalIgnoreCase))
            throw new CommercePaymentException(502,
                $"PayPal {operation} amount {actualAmount:0.00} {actualCurrency} did not match order total {expectedAmount:0.00} {expectedCurrency}.");
    }

    private static void ValidateCard(PaymentCardData card)
    {
        var number = card.Number.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (number.Length is < 13 or > 19 || number.Any(c => !char.IsDigit(c)))
            throw BadRequest("card.number must contain 13 to 19 digits.");
        if (card.SecurityCode.Length is < 3 or > 4 || card.SecurityCode.Any(c => !char.IsDigit(c)))
            throw BadRequest("card.securityCode must contain 3 or 4 digits.");
        if (!DateOnly.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry < new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1))
            throw BadRequest("card.expiry must be a current or future year-month in YYYY-MM format.");
        if (string.IsNullOrWhiteSpace(card.Name) || card.BillingAddress.CountryCode.Length != 2)
            throw BadRequest("Cardholder name and a two-letter billing country code are required.");
    }

    private static string CustomerId(string buyerId) => Hash(buyerId.ToLowerInvariant());
    private static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    private static CommercePaymentException BadRequest(string message) => new(400, message);
    private static CommercePaymentException NotFound(string message) => new(404, message);
    private static CommercePaymentException Conflict(string message) => new(409, message);

    private sealed record LocalEntry(int OrderId, string PayPalId, string InvoiceId, string Type,
        string Status, decimal Amount, decimal? Fee, string Currency, DateTimeOffset Time);

    private static IEnumerable<LocalEntry> LocalEntries(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        if (order.CaptureId is not null && order.CapturedAt >= from && order.CapturedAt <= to)
            yield return new LocalEntry(order.Id, order.CaptureId, order.ExternalReference, "Capture",
                order.CaptureStatus ?? "UNKNOWN", order.CapturedAmount ?? 0, order.PayPalFee, order.Currency,
                order.CapturedAt.Value);
        foreach (var refund in order.Refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to))
            yield return new LocalEntry(order.Id, refund.PayPalRefundId, order.ExternalReference, "Refund",
                refund.Status, -refund.Amount, refund.PayPalFee, order.Currency, refund.CreatedAt);
    }
}
