using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CommerceService
{
    private readonly CatalogContext _context;
    private readonly IPayPalGateway _payPal;
    private readonly PayPalOptions _options;
    private readonly IUriComposer _uriComposer;
    private readonly OrderOperationLock _locks;

    public CommerceService(CatalogContext context, IPayPalGateway payPal, IOptions<PayPalOptions> options,
        IUriComposer uriComposer, OrderOperationLock locks)
    {
        _context = context;
        _payPal = payPal;
        _options = options.Value;
        _uriComposer = uriComposer;
        _locks = locks;
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0 || request.ShippingAddress is null)
            throw Invalid("invalid_order", "At least one item and a shipping address are required.");
        if (request.Items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0)
            || request.Items.Select(i => i.CatalogItemId).Distinct().Count() != request.Items.Count)
            throw Invalid("invalid_order_items", "Catalog item IDs must be unique and quantities must be positive.");

        var ids = request.Items.Select(i => i.CatalogItemId).ToArray();
        var catalog = await _context.CatalogItems.Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);
        if (catalog.Count != ids.Length)
            throw new PaymentApiException(404, "catalog_item_not_found", "One or more catalog items do not exist.");

        var items = request.Items.Select(line =>
        {
            var item = catalog[line.CatalogItemId];
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name,
                _uriComposer.ComposePicUri(item.PictureUri)), item.Price, line.Quantity);
        }).ToList();
        var address = request.ShippingAddress;
        var order = new Order(buyerId, new Address(address.Street, address.City, address.State,
            address.Country, address.ZipCode), items);
        var currency = _options.Currency.ToUpperInvariant();
        order.RequirePayment(currency);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
        return new PlaceOrderResponse(order.Id, order.Total(), currency, PaymentState.AwaitingPayment.ToString());
    }

    public async Task<PaymentResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var claim = await _locks.AcquireAsync(orderId, cancellationToken);
        var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = RequirePayment(order);
        if (payment.State is PaymentState.Authorized or PaymentState.Captured
            or PaymentState.PartiallyRefunded or PaymentState.Refunded)
            return MapPayment(order);
        if (payment.State == PaymentState.Voided || order.FulfilmentState == FulfilmentState.Cancelled)
            throw Conflict("order_cancelled", "A cancelled order cannot be paid.");
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw Invalid("payment_source_required", "Provide either card details or one saved paymentMethodId.");

        string? vaultId = null;
        if (request.PaymentMethodId is not null)
        {
            var saved = await _context.SavedPaymentMethods.SingleOrDefaultAsync(p =>
                p.Id == request.PaymentMethodId.Value && p.BuyerId == buyerId && !p.IsDeleted, cancellationToken);
            if (saved is null)
                throw new PaymentApiException(404, "payment_method_not_found", "The saved payment method does not exist.");
            vaultId = saved.PayPalTokenId;
        }

        var result = await _payPal.AuthorizeAsync(payment.ExternalReference, order.Id,
            payment.OrderAmount, payment.Currency,
            request.Card is null ? null : ToProviderCard(request.Card), vaultId, cancellationToken);
        payment.RecordPayPalOrder(result.PayPalOrderId, result.PayPalOrderStatus);
        payment.RecordAuthorization(result.AuthorizationId, result.AuthorizationStatus, result.Amount,
            result.CreatedAt, result.ExpiresAt, result.UpdatedAt,
            result.ResponseCode, result.AvsCode, result.CvvCode);
        await _context.SaveChangesAsync(cancellationToken);
        return MapPayment(order);
    }

    public async Task<PaymentResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var claim = await _locks.AcquireAsync(orderId, cancellationToken);
        var order = await OrderAsync(orderId, cancellationToken);
        var payment = RequirePayment(order);
        if (order.FulfilmentState == FulfilmentState.Fulfilled && payment.CaptureId is not null)
            return MapPayment(order);
        if (order.FulfilmentState == FulfilmentState.Cancelled || payment.State == PaymentState.Voided)
            throw Conflict("order_cancelled", "A cancelled order cannot be fulfilled.");
        if (payment.State != PaymentState.Authorized || payment.AuthorizationId is null)
            throw Conflict("payment_not_authorized", "The order must have an active authorization before fulfilment.");

        var result = await _payPal.CaptureAsync(payment.ExternalReference, order.Id,
            payment.AuthorizationId, payment.OrderAmount,
            payment.Currency, payment.AuthorizationCreatedAt, cancellationToken);
        payment.RecordAuthorization(result.AuthorizationId, result.AuthorizationStatus, payment.OrderAmount,
            payment.AuthorizationCreatedAt, payment.AuthorizationExpiresAt, DateTimeOffset.UtcNow,
            result.ResponseCode, result.AvsCode, result.CvvCode);
        payment.RecordCapture(result.CaptureId, result.CaptureStatus, result.Amount, result.Fee,
            result.Net, result.CapturedAt, result.ResponseCode, result.AvsCode, result.CvvCode);
        if (result.CaptureStatus != "COMPLETED")
        {
            await _context.SaveChangesAsync(cancellationToken);
            throw Conflict("capture_not_completed", "PayPal has not completed the capture; retry fulfilment with the same order.");
        }
        order.MarkFulfilled();
        await _context.SaveChangesAsync(cancellationToken);
        return MapPayment(order);
    }

    public async Task<PaymentResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var claim = await _locks.AcquireAsync(orderId, cancellationToken);
        var order = await OrderAsync(orderId, cancellationToken);
        var payment = RequirePayment(order);
        if (order.FulfilmentState == FulfilmentState.Cancelled) return MapPayment(order);
        if (order.FulfilmentState == FulfilmentState.Fulfilled || payment.CaptureId is not null)
            throw Conflict("order_already_fulfilled", "Captured orders must be refunded rather than cancelled.");
        if (payment.State == PaymentState.AwaitingPayment)
        {
            order.MarkCancelled();
            await _context.SaveChangesAsync(cancellationToken);
            return MapPayment(order);
        }
        if (payment.AuthorizationId is null)
            throw Conflict("authorization_missing", "The authorization ID is missing; reconcile the order before cancelling it.");

        var result = await _payPal.VoidAsync(payment.ExternalReference, payment.AuthorizationId, cancellationToken);
        payment.RecordAuthorizationStatus(result.Status, result.UpdatedAt);
        if (result.Status != "VOIDED")
        {
            await _context.SaveChangesAsync(cancellationToken);
            throw Conflict("authorization_not_voided", "PayPal has not confirmed release of the held funds.");
        }
        payment.MarkVoided(result.Status);
        order.MarkCancelled();
        await _context.SaveChangesAsync(cancellationToken);
        return MapPayment(order);
    }

    public async Task<RefundResponse> RefundAsync(string buyerId, int orderId, RefundRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            throw Invalid("invalid_idempotency_key", "A non-empty idempotencyKey of at most 128 characters is required.");
        using var claim = await _locks.AcquireAsync(orderId, cancellationToken);
        var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = RequirePayment(order);
        if (order.FulfilmentState != FulfilmentState.Fulfilled || payment.CaptureId is null
            || payment.CapturedAmount is null)
            throw Conflict("payment_not_captured", "Only a fulfilled order with a completed capture can be refunded.");

        var existing = payment.Refunds.SingleOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existing?.PayPalRefundId is not null) return MapRefund(payment, existing);
        if (existing is not null && request.Amount is not null && request.Amount.Value != existing.Amount)
            throw Conflict("idempotency_key_reused", "This idempotencyKey was already used with a different amount.");

        var remaining = payment.CapturedAmount.Value - payment.ReservedRefundAmount;
        var amount = existing?.Amount ?? request.Amount ?? remaining;
        if (amount <= 0 || amount > remaining + (existing?.Amount ?? 0))
            throw Invalid("invalid_refund_amount", "The refund amount exceeds the remaining captured amount.");
        var refund = existing ?? payment.ReserveRefund(request.IdempotencyKey, amount);
        if (existing is null) await _context.SaveChangesAsync(cancellationToken);

        try
        {
            var result = await _payPal.RefundAsync(payment.ExternalReference, order.Id,
                payment.CaptureId, amount,
                payment.Currency, request.IdempotencyKey, cancellationToken);
            refund.RecordProviderResult(result.RefundId, result.Status, result.Amount, result.UpdatedAt);
            payment.RefreshRefundState();
            await _context.SaveChangesAsync(cancellationToken);
            return MapRefund(payment, refund);
        }
        catch (PaymentApiException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            refund.MarkFailed();
            await _context.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<MyOrderResponse>> MyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _context.Orders.AsNoTracking().Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems).ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Payment!).ThenInclude(p => p.Refunds)
            .OrderByDescending(o => o.OrderDate).ToListAsync(cancellationToken);
        return orders.Select(MapOrder).ToList();
    }

    public async Task<SavePaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var result = await _payPal.SavePaymentMethodAsync(buyerId, ToProviderCard(request.Card), cancellationToken);
        var saved = new SavedPaymentMethod(buyerId, result.TokenId, result.CustomerId,
            result.Brand, result.LastDigits, result.Expiry, result.CardType);
        _context.SavedPaymentMethods.Add(saved);
        await _context.SaveChangesAsync(cancellationToken);
        return new SavePaymentMethodResponse(saved.Id, saved.Brand, saved.LastDigits, saved.Expiry, saved.CardType);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> PaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) => await _context.SavedPaymentMethods.AsNoTracking()
        .Where(p => p.BuyerId == buyerId && !p.IsDeleted).OrderByDescending(p => p.CreatedAt)
        .Select(p => new PaymentMethodResponse(p.Id, p.Brand, p.LastDigits, p.Expiry, p.CardType))
        .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await _context.SavedPaymentMethods.SingleOrDefaultAsync(p =>
            p.Id == paymentMethodId && p.BuyerId == buyerId, cancellationToken);
        if (saved is null)
            throw new PaymentApiException(404, "payment_method_not_found", "The saved payment method does not exist.");
        if (saved.IsDeleted) return;
        await _payPal.DeletePaymentMethodAsync(saved.PayPalTokenId, cancellationToken);
        saved.Delete();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (from >= to)
            throw Invalid("invalid_date_range", "from must be earlier than to.");
        if (to > now)
            throw Invalid("invalid_date_range", "to must not be in the future.");
        if (to - from > TimeSpan.FromDays(31))
            throw Invalid("invalid_date_range", "The reconciliation range must not exceed 31 days.");
        if (from < now.AddYears(-3))
            throw Invalid("invalid_date_range", "from must be within the previous three years.");
        var report = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var providerInvoiceIds = report.Transactions.Where(t => !string.IsNullOrWhiteSpace(t.InvoiceId))
            .Select(t => t.InvoiceId!).Distinct().ToList();
        var orders = await _context.Orders.AsNoTracking().Include(o => o.Payment!).ThenInclude(p => p.Refunds)
            .Where(o => o.Payment != null && ((o.Payment.CapturedAt >= from && o.Payment.CapturedAt <= to)
                || o.Payment.Refunds.Any(r => r.UpdatedAt >= from && r.UpdatedAt <= to)
                || providerInvoiceIds.Contains(o.Payment.ExternalReference)))
            .ToListAsync(cancellationToken);

        var local = new Dictionary<string, (int OrderId, string Kind, string Status, decimal Amount,
            string Currency, decimal? Fee, DateTimeOffset? Time)>();
        foreach (var order in orders)
        {
            var p = order.Payment!;
            if (p.CaptureId is not null && p.CapturedAmount is not null)
                local[p.CaptureId] = (order.Id, "capture", p.CaptureStatus ?? "UNKNOWN",
                    p.CapturedAmount.Value, p.Currency, p.PayPalFee, p.CapturedAt);
            foreach (var refund in p.Refunds.Where(r => r.PayPalRefundId is not null))
                local[refund.PayPalRefundId!] = (order.Id, "refund", refund.Status,
                    -refund.Amount, refund.Currency, null, refund.UpdatedAt);
        }

        var entries = new List<ReconciliationEntry>();
        var matched = new HashSet<string>();
        var orderByExternalReference = orders.ToDictionary(o => o.Payment!.ExternalReference, o => o.Id);
        foreach (var provider in report.Transactions)
        {
            var key = new[] { provider.TransactionId, provider.ReferenceId }
                .FirstOrDefault(id => id is not null && local.ContainsKey(id));
            if (key is not null)
            {
                matched.Add(key);
                var item = local[key];
                entries.Add(new ReconciliationEntry("PayPal+eShop", provider.TransactionId, item.OrderId,
                    item.Kind, provider.Status, provider.Amount, provider.Fee, provider.Currency,
                    provider.InitiatedAt, "Matched"));
            }
            else
            {
                var invoiceOrderId = 0;
                var invoiceMatched = provider.InvoiceId is not null
                    && orderByExternalReference.TryGetValue(provider.InvoiceId, out invoiceOrderId);
                int? orderId = invoiceMatched
                    ? invoiceOrderId
                    : int.TryParse(provider.CustomField, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
                entries.Add(new ReconciliationEntry("PayPal", provider.TransactionId, orderId,
                    provider.EventCode ?? "transaction", provider.Status, provider.Amount,
                    provider.Fee, provider.Currency, provider.InitiatedAt,
                    invoiceMatched ? "InvoiceMatched" : "PayPalOnly"));
            }
        }

        var lag = report.Transactions.Count == 0 && to >= DateTimeOffset.UtcNow.AddDays(-2);
        foreach (var pair in local.Where(p => !matched.Contains(p.Key)))
        {
            var item = pair.Value;
            entries.Add(new ReconciliationEntry("eShop", pair.Key, item.OrderId, item.Kind,
                item.Status, item.Amount, item.Fee, item.Currency, item.Time,
                lag ? "PayPalDataPending" : "EShopOnly"));
        }
        return new ReconciliationResponse(from, to, report.LastRefreshedAt, entries);
    }

    private async Task<Order> OwnedOrderAsync(string buyerId, int orderId, CancellationToken ct) =>
        await QueryOrders().SingleOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, ct)
        ?? throw new PaymentApiException(404, "order_not_found", "The order does not exist.");
    private async Task<Order> OrderAsync(int orderId, CancellationToken ct) =>
        await QueryOrders().SingleOrDefaultAsync(o => o.Id == orderId, ct)
        ?? throw new PaymentApiException(404, "order_not_found", "The order does not exist.");
    private IQueryable<Order> QueryOrders() => _context.Orders
        .Include(o => o.OrderItems).ThenInclude(i => i.ItemOrdered)
        .Include(o => o.Payment!).ThenInclude(p => p.Refunds);
    private static PaymentRecord RequirePayment(Order order) => order.Payment
        ?? throw Conflict("payment_not_enabled", "This legacy order was not created through the payment API.");

    private static ProviderCard ToProviderCard(CardRequestDto card)
    {
        if (string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.Number)
            || string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode)
            || card.BillingAddress is null || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
            throw Invalid("invalid_card", "Complete card and billing-address details are required.");
        return new ProviderCard(card.Name, card.Number, card.Expiry, card.SecurityCode, card.BillingAddress);
    }

    private static PaymentResponse MapPayment(Order order)
    {
        var p = RequirePayment(order);
        return new PaymentResponse(order.Id, p.State.ToString(), p.PayPalOrderId, p.AuthorizationId,
            p.AuthorizationStatus, p.AuthorizedAmount, p.Currency, p.CaptureId, p.CaptureStatus,
            p.CapturedAmount, p.PayPalFee, p.NetAmount,
            p.Refunds.Where(r => r.Status == "COMPLETED").Sum(r => r.Amount));
    }
    private static RefundResponse MapRefund(PaymentRecord payment, PaymentRefund refund) => new(
        refund.Id, refund.PayPalRefundId, refund.Status, refund.Amount, refund.Currency,
        Math.Max(0, (payment.CapturedAmount ?? 0) - payment.ReservedRefundAmount));
    private static MyOrderResponse MapOrder(Order order) => new(order.Id, order.OrderDate, order.Total(),
        order.Payment?.Currency, order.Payment?.State.ToString() ?? "NotRequired",
        order.FulfilmentState.ToString(), order.OrderItems.Select(i => new OrderItemResponse(
            i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.Units, i.UnitPrice)).ToList(),
        order.Payment is null ? null : MapPayment(order));
    private static PaymentApiException Invalid(string code, string message) => new(422, code, message);
    private static PaymentApiException Conflict(string code, string message) => new(409, code, message);
}
