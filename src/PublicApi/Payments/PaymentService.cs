using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService
{
    private readonly CatalogContext _db;
    private readonly IPayPalGateway _payPal;
    private readonly OrderOperationLock _operationLock;
    private readonly PayPalOptions _options;

    public PaymentService(CatalogContext db, IPayPalGateway payPal,
        OrderOperationLock operationLock, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _operationLock = operationLock;
        _options = options.Value;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(string shopperId, CreateOrderRequest request,
        CancellationToken ct)
    {
        RequireShopper(shopperId);
        if (request.Items is null || request.Items.Count == 0)
            throw new PaymentApiException(400, "At least one catalog item is required.");
        if (request.ShippingAddress is null)
            throw new PaymentApiException(400, "A shipping address is required.");
        if (request.Items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
            throw new PaymentApiException(400, "Catalog item ids and quantities must be positive.");

        var requested = request.Items.GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => checked(g.Sum(x => x.Quantity)));
        var catalogItems = await _db.CatalogItems
            .Where(i => requested.Keys.Contains(i.Id)).ToListAsync(ct);
        if (catalogItems.Count != requested.Count)
            throw new PaymentApiException(400, "One or more catalog items do not exist.");

        var lines = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            RoundMoney(item.Price), requested[item.Id])).ToList();
        var address = request.ShippingAddress;
        var order = new Order(shopperId,
            new Address(address.Street, address.City, address.State, address.Country, address.PostalCode), lines);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        order.InitializePayment(_options.Currency, StableRequestId($"order:{Guid.NewGuid():N}"));
        await _db.SaveChangesAsync(ct);
        return new CreateOrderResponse(order.Id, order.PaymentStatus.ToString(), order.Total(), order.Currency);
    }

    public async Task<OrderPaymentResponse> PayAsync(string shopperId, int orderId,
        PayOrderRequest request, CancellationToken ct)
    {
        RequireShopper(shopperId);
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw new PaymentApiException(400, "Provide either card details or one saved paymentMethodId.");

        var gate = _operationLock.For(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await OwnedOrderAsync(shopperId, orderId, ct);
            if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
                return ToPaymentResponse(order);

            ProviderCardSource source;
            if (request.PaymentMethodId is int paymentMethodId)
            {
                var method = await _db.PaymentMethods.SingleOrDefaultAsync(p =>
                    p.Id == paymentMethodId && p.BuyerId == shopperId && p.IsActive, ct);
                if (method is null)
                    throw new PaymentApiException(404, "The saved payment method was not found or is no longer active.");
                source = new ProviderCardSource(method.PayPalTokenId, null);
            }
            else
            {
                ValidateCard(request.Card!);
                source = new ProviderCardSource(null, request.Card);
            }

            var authorization = await _payPal.AuthorizeAsync(order.Id, order.Total(), order.Currency,
                source, order.CreatePaymentRequestId, order.AuthorizeRequestId, ct);
            EnsureProviderAmount(order.Total(), order.Currency, authorization.Amount, authorization.Currency,
                "authorization");
            order.RecordAuthorization(authorization.PayPalOrderId, authorization.PayPalOrderStatus,
                authorization.AuthorizationId, authorization.AuthorizationStatus,
                authorization.Amount, authorization.ExpiresAt);
            await _db.SaveChangesAsync(ct);
            return ToPaymentResponse(order);
        }
        finally { gate.Release(); }
    }

    public async Task<OrderPaymentResponse> FulfilAsync(int orderId, CancellationToken ct)
    {
        var gate = _operationLock.For(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await OrderAsync(orderId, ct);
            if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
                return ToPaymentResponse(order);
            if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
                throw new PaymentApiException(409, "A cancelled order cannot be fulfilled.");
            if (order.AuthorizationId is null)
                throw new PaymentApiException(409, "The order has not been authorized for payment.");

            if (order.CaptureId is not null)
            {
                var refreshedCapture = await _payPal.GetCaptureAsync(order.CaptureId, ct);
                RecordCapture(order, refreshedCapture);
                await _db.SaveChangesAsync(ct);
                return ToPaymentResponse(order);
            }

            var current = await _payPal.GetAuthorizationAsync(order.AuthorizationId, ct);
            EnsureProviderAmount(order.Total(), order.Currency, current.Amount, current.Currency, "authorization");
            if (current.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            {
                if (DateTimeOffset.UtcNow - order.OrderDate >= TimeSpan.FromDays(30))
                    throw new PaymentApiException(409,
                        "The PayPal authorization is at least 30 days old and can no longer be renewed. Ask the shopper to authorize payment again on a new order.");
                current = await _payPal.ReauthorizeAsync(current.AuthorizationId, order.Total(), order.Currency,
                    order.ReauthorizeRequestId, ct);
                EnsureProviderAmount(order.Total(), order.Currency, current.Amount, current.Currency, "reauthorization");
                order.RecordReauthorization(current.AuthorizationId, current.Status, current.Amount, current.ExpiresAt);
                await _db.SaveChangesAsync(ct);
            }

            var capture = await _payPal.CaptureAsync(current.AuthorizationId, order.Total(), order.Currency,
                order.CaptureRequestId, ct);
            EnsureProviderAmount(order.Total(), order.Currency, capture.Amount, capture.Currency, "capture");
            RecordCapture(order, capture);
            await _db.SaveChangesAsync(ct);
            return ToPaymentResponse(order);
        }
        finally { gate.Release(); }
    }

    public async Task<OrderPaymentResponse> CancelAsync(int orderId, CancellationToken ct)
    {
        var gate = _operationLock.For(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await OrderAsync(orderId, ct);
            if (order.PaymentStatus == OrderPaymentStatus.Cancelled) return ToPaymentResponse(order);
            if (order.CaptureId is not null || order.PaymentStatus is OrderPaymentStatus.Fulfilled
                or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
                throw new PaymentApiException(409, "Captured funds cannot be cancelled; use the refund endpoint.");
            if (order.AuthorizationId is null)
                throw new PaymentApiException(409, "The order has no authorization to void.");
            var result = await _payPal.VoidAsync(order.AuthorizationId, order.VoidRequestId, ct);
            order.MarkCancelled(result.Status);
            await _db.SaveChangesAsync(ct);
            return ToPaymentResponse(order);
        }
        finally { gate.Release(); }
    }

    public async Task<PaymentRefundResponse> RefundAsync(string shopperId, int orderId,
        RefundOrderRequest request, CancellationToken ct)
    {
        RequireShopper(shopperId);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            throw new PaymentApiException(400, "A non-empty idempotencyKey of at most 128 characters is required.");

        var gate = _operationLock.For(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await OwnedOrderAsync(shopperId, orderId, ct);
            var existing = order.Refunds.SingleOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
            if (existing?.PayPalRefundId is not null) return ToRefundResponse(existing);
            if (order.CaptureId is null || order.CapturedAmount is null ||
                order.PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
                throw new PaymentApiException(409, "Only a captured, un-cancelled payment can be refunded.");

            var remaining = order.CapturedAmount.Value - order.ReservedRefundAmount;
            var amount = request.Amount is null ? remaining : RoundMoney(request.Amount.Value);
            if (amount <= 0 || amount > remaining)
                throw new PaymentApiException(409,
                    $"Refund amount must be positive and no greater than the remaining refundable amount {remaining.ToString("F2", CultureInfo.InvariantCulture)} {order.Currency}.");

            var refund = existing ?? order.StartRefund(request.IdempotencyKey,
                StableRequestId($"refund:{orderId}:{request.IdempotencyKey}"), amount);
            if (existing is null) await _db.SaveChangesAsync(ct);
            var providerRefund = await _payPal.RefundAsync(order.CaptureId, amount, order.Currency,
                request.Amount is null, refund.PayPalRequestId, ct);
            EnsureProviderAmount(amount, order.Currency, providerRefund.Amount, providerRefund.Currency, "refund");
            refund.Complete(providerRefund.RefundId, providerRefund.Status, providerRefund.Amount);
            order.UpdateRefundState();
            await _db.SaveChangesAsync(ct);
            return ToRefundResponse(refund);
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<MyOrderResponse>> MyOrdersAsync(string shopperId, CancellationToken ct)
    {
        RequireShopper(shopperId);
        var orders = await _db.Orders.AsNoTracking()
            .Include(o => o.OrderItems).ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds)
            .Where(o => o.BuyerId == shopperId)
            .OrderByDescending(o => o.OrderDate).ToListAsync(ct);
        return orders.Select(ToMyOrderResponse).ToList();
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string shopperId,
        SavePaymentMethodRequest request, CancellationToken ct)
    {
        RequireShopper(shopperId);
        ValidateCard(request.Card);
        var saved = await _payPal.SaveCardAsync(shopperId, request.Card,
            StableRequestId($"vault:{shopperId}:{Guid.NewGuid():N}"), ct);
        var method = new PaymentMethod(shopperId, saved.CustomerId, saved.TokenId,
            saved.CardholderName, saved.Brand, saved.LastDigits, saved.Expiry);
        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(ct);
        return ToPaymentMethodResponse(method);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> PaymentMethodsAsync(string shopperId,
        CancellationToken ct)
    {
        RequireShopper(shopperId);
        var local = await _db.PaymentMethods.AsNoTracking()
            .Where(p => p.BuyerId == shopperId && p.IsActive).OrderBy(p => p.Id).ToListAsync(ct);
        if (local.Count == 0) return Array.Empty<PaymentMethodResponse>();
        var remoteIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var customerId in local.Select(p => p.PayPalCustomerId).Distinct())
            foreach (var remote in await _payPal.ListCardsAsync(customerId, ct)) remoteIds.Add(remote.TokenId);
        return local.Where(p => remoteIds.Contains(p.PayPalTokenId)).Select(ToPaymentMethodResponse).ToList();
    }

    public async Task DeletePaymentMethodAsync(string shopperId, int paymentMethodId, CancellationToken ct)
    {
        RequireShopper(shopperId);
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(p =>
            p.Id == paymentMethodId && p.BuyerId == shopperId && p.IsActive, ct);
        if (method is null) throw new PaymentApiException(404, "The saved payment method was not found.");
        await _payPal.DeleteCardAsync(method.PayPalTokenId, ct);
        method.Delete();
        await _db.SaveChangesAsync(ct);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        if (from >= to) throw new PaymentApiException(400, "from must be earlier than to.");
        var provider = await _payPal.SearchTransactionsAsync(from, to, ct);
        if (provider.Count == 0) return new ReconciliationResponse(from, to, Array.Empty<ReconciliationEntry>());

        var orders = await _db.Orders.AsNoTracking().Include(o => o.Refunds)
            .Where(o => o.OrderDate <= to &&
                (o.FulfilledAt == null || o.FulfilledAt >= from || o.OrderDate >= from))
            .ToListAsync(ct);
        var entries = new List<ReconciliationEntry>();
        var matchedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transaction in provider)
        {
            var order = orders.FirstOrDefault(o => Matches(o, transaction));
            if (order is not null && transaction.TransactionId is not null) matchedIds.Add(transaction.TransactionId);
            entries.Add(new ReconciliationEntry(order is null ? "PayPalOnly" : "Matched", order?.Id,
                transaction.TransactionId, transaction.ReferenceId, transaction.Status, transaction.EventCode,
                transaction.UpdatedAt ?? transaction.InitiatedAt, transaction.Amount, transaction.Fee,
                transaction.Currency, null));
        }

        foreach (var order in orders)
        {
            var localIds = new[] { order.AuthorizationId, order.CaptureId }
                .Concat(order.Refunds.Select(r => r.PayPalRefundId)).Where(id => id is not null).Cast<string>();
            foreach (var localId in localIds.Where(id => !matchedIds.Contains(id)))
                entries.Add(new ReconciliationEntry("EShopOnly", order.Id, localId, null, null, null,
                    order.FulfilledAt ?? order.OrderDate, null, null, order.Currency,
                    "No matching PayPal transaction was present in the returned report range."));
        }
        return new ReconciliationResponse(from, to, entries);
    }

    private async Task<Order> OwnedOrderAsync(string shopperId, int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.Include(o => o.OrderItems).Include(o => o.Refunds)
            .SingleOrDefaultAsync(o => o.Id == orderId && o.BuyerId == shopperId, ct);
        return order ?? throw new PaymentApiException(404, "The order was not found.");
    }

    private async Task<Order> OrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.Include(o => o.OrderItems).Include(o => o.Refunds)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);
        return order ?? throw new PaymentApiException(404, "The order was not found.");
    }

    private static void RecordCapture(Order order, ProviderCapture capture) =>
        order.RecordCapture(capture.CaptureId, capture.Status, capture.Amount,
            capture.PayPalFee, capture.NetAmount);

    private static bool Matches(Order order, ProviderTransaction tx)
    {
        var ids = new[] { order.PayPalOrderId, order.AuthorizationId, order.CaptureId }
            .Concat(order.Refunds.Select(r => r.PayPalRefundId));
        return ids.Any(id => id is not null && (id == tx.TransactionId || id == tx.ReferenceId)) ||
            tx.InvoiceId == order.CreatePaymentRequestId ||
            tx.CustomField == order.Id.ToString(CultureInfo.InvariantCulture);
    }

    private static MyOrderResponse ToMyOrderResponse(Order order) => new(order.Id, order.OrderDate,
        order.Total(), order.Currency, order.PaymentStatus.ToString(),
        order.OrderItems.Select(i => new MyOrderLineResponse(i.ItemOrdered.CatalogItemId,
            i.ItemOrdered.ProductName, i.Units, i.UnitPrice)).ToList(), ToPaymentResponse(order));

    private static OrderPaymentResponse ToPaymentResponse(Order order) => new(order.Id,
        order.PaymentStatus.ToString(), order.Total(), order.Currency, order.PayPalOrderId,
        order.AuthorizationId, order.AuthorizationStatus, order.AuthorizationExpiresAt,
        order.CaptureId, order.CaptureStatus, order.CapturedAmount, order.PayPalFee,
        order.NetProceeds, order.RefundedAmount, order.Refunds.Select(ToRefundResponse).ToList());

    private static PaymentRefundResponse ToRefundResponse(PaymentRefund refund) => new(
        refund.PayPalRefundId ?? string.Empty, refund.Status, refund.Amount, refund.Currency, refund.IdempotencyKey);

    private static PaymentMethodResponse ToPaymentMethodResponse(PaymentMethod method) => new(
        method.Id, method.Brand, method.LastDigits, method.Expiry, method.CardholderName);

    private static void RequireShopper(string shopperId)
    {
        if (string.IsNullOrWhiteSpace(shopperId)) throw new PaymentApiException(401, "Authentication is required.");
    }

    private static void ValidateCard(CardInput card)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode) ||
            card.BillingAddress is null || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
            throw new PaymentApiException(400, "Complete card and billing-address details are required.");
    }

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static void EnsureProviderAmount(decimal expectedAmount, string expectedCurrency,
        decimal actualAmount, string actualCurrency, string operation)
    {
        if (actualAmount != expectedAmount || !string.Equals(actualCurrency, expectedCurrency, StringComparison.OrdinalIgnoreCase))
            throw new PaymentApiException(502,
                $"PayPal returned a {operation} amount or currency that did not match the order. No local success state was recorded.");
    }

    private static string StableRequestId(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return "eshop-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }
}
