using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CommercePaymentService
{
    private readonly CatalogContext _db;
    private readonly IPayPalGateway _paypal;
    public CommercePaymentService(CatalogContext db, IPayPalGateway paypal) { _db = db; _paypal = paypal; }

    public async Task<object> PlaceOrderAsync(string shopper, PlaceOrderRequest request, CancellationToken ct)
    {
        if (request.Items.Count == 0 || request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0)) throw Bad("At least one catalog item with a positive quantity is required.");
        if (request.Items.GroupBy(x => x.CatalogItemId).Any(x => x.Count() > 1)) throw Bad("Each catalog item may appear only once.");
        var ids = request.Items.Select(x => x.CatalogItemId).ToArray();
        var catalog = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        if (catalog.Count != ids.Length) throw new PaymentOperationException(404, "One or more catalog items do not exist.");
        var items = request.Items.Select(x => { var c = catalog[x.CatalogItemId]; return new OrderItem(new CatalogItemOrdered(c.Id, c.Name, c.PictureUri), c.Price, x.Quantity); }).ToList();
        var a = request.ShippingAddress ?? new ShippingAddressRequest();
        var order = new Order(shopper, new Address(a.Street, a.City, a.State, a.Country, a.ZipCode), items);
        _db.Orders.Add(order); await _db.SaveChangesAsync(ct);
        return new { orderId = order.Id, total = order.Total(), currency = _paypal.Currency, paymentStatus = order.PaymentStatus.ToString(), fulfillmentStatus = order.FulfillmentStatus.ToString() };
    }

    public async Task<object> PayAsync(string shopper, int orderId, PayOrderRequest request, CancellationToken ct)
    {
        var order = await OrderAsync(orderId, ct); Own(order, shopper);
        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled) throw Conflict("A cancelled order cannot be paid.");
        if (order.PaymentStatus == PaymentStatus.Authorized) return PaymentView(order);
        if (order.PaymentStatus != PaymentStatus.AwaitingPayment) throw Conflict($"Order payment is already {order.PaymentStatus}.");
        if ((request.Card == null) == (request.PaymentMethodId == null)) throw Bad("Supply either card or paymentMethodId, but not both.");
        string? vaultId = null;
        if (request.PaymentMethodId != null)
        {
            var buyer = await _db.Buyers.Include(x => x.PaymentMethods).SingleOrDefaultAsync(x => x.IdentityGuid == shopper, ct);
            var method = buyer?.PaymentMethods.SingleOrDefault(x => x.Id == request.PaymentMethodId && x.IsActive);
            if (method == null) throw new PaymentOperationException(404, "Payment method not found.");
            vaultId = method.PayPalTokenId;
        }
        var result = await _paypal.AuthorizeAsync(order.PaymentReference, order.Total(), request.Card == null ? null : Card(request.Card), vaultId, ct);
        if (result.Amount != order.Total() || result.Currency != _paypal.Currency) throw new InvalidOperationException("PayPal authorized an amount or currency different from the order total.");
        order.RecordAuthorization(new OrderPayment(result.Currency, result.Amount, result.PayPalOrderId, result.AuthorizationId, result.Status, result.CreatedAt, result.ExpiresAt));
        await _db.SaveChangesAsync(ct); return PaymentView(order);
    }

    public async Task<object> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await OrderAsync(orderId, ct);
        if (order.FulfillmentStatus == FulfillmentStatus.Fulfilled) return PaymentView(order);
        if (order.PaymentStatus != PaymentStatus.Authorized || order.Payment == null) throw Conflict("The order must have an authorized payment before fulfilment.");
        var payment = order.Payment;
        if (DateTimeOffset.UtcNow >= payment.AuthorizationExpiresAt || DateTimeOffset.UtcNow >= payment.OriginalAuthorizedAt.AddDays(29))
            throw Conflict("The PayPal authorization has passed its 29-day validity period. Ask the shopper to pay the order again before fulfilment.");
        if (DateTimeOffset.UtcNow >= payment.AuthorizedAt.AddDays(3))
        {
            PayPalAuthorization renewed;
            try { renewed = await _paypal.ReauthorizeAsync(order.PaymentReference, payment.AuthorizationId, ct); }
            catch (PayPalException ex) { throw Conflict($"PayPal could not renew the stale authorization ({ex.Issue ?? ex.Name}). Ask the shopper to pay again or contact PayPal with debug ID {ex.DebugId ?? "not supplied"}."); }
            payment.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.CreatedAt, renewed.ExpiresAt);
            await _db.SaveChangesAsync(ct);
        }
        var capture = await _paypal.CaptureAsync(order.PaymentReference, payment.AuthorizationId, order.Total(), ct);
        if (capture.Status != "COMPLETED") throw Conflict($"PayPal capture is {capture.Status}; the order was not marked fulfilled. Retry after PayPal completes the capture.");
        if (capture.Amount != order.Total() || capture.Currency != payment.Currency) throw new InvalidOperationException("PayPal captured an amount or currency different from the order authorization.");
        order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.NetAmount, capture.CreatedAt);
        await _db.SaveChangesAsync(ct); return PaymentView(order);
    }

    public async Task<object> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await OrderAsync(orderId, ct);
        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled) return PaymentView(order);
        if (order.FulfillmentStatus == FulfillmentStatus.Fulfilled) throw Conflict("A fulfilled order cannot be cancelled; refund it instead.");
        string? status = null;
        if (order.PaymentStatus == PaymentStatus.Authorized && order.Payment != null) status = await _paypal.VoidAsync(order.PaymentReference, order.Payment.AuthorizationId, ct);
        order.Cancel(status); await _db.SaveChangesAsync(ct); return PaymentView(order);
    }

    public async Task<object> RefundAsync(string shopper, int orderId, RefundOrderRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128) throw Bad("idempotencyKey is required and must be at most 128 characters.");
        var order = await OrderAsync(orderId, ct); Own(order, shopper);
        if (order.Payment == null || order.Payment.CaptureId == null || order.Payment.CapturedAmount == null) throw Conflict("The order has no captured payment to refund.");
        var existing = order.Payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing != null) return RefundView(order, existing);
        var remaining = order.Payment.CapturedAmount.Value - order.Payment.RefundedAmount;
        var amount = request.Amount ?? remaining;
        if (amount <= 0 || amount > remaining) throw Conflict($"Refund amount must be positive and no more than the remaining captured amount {remaining:0.00} {order.Payment.Currency}.");
        var result = await _paypal.RefundAsync(order.PaymentReference, order.Payment.CaptureId, amount, request.IdempotencyKey, ct);
        if (result.Amount != amount || result.Currency != order.Payment.Currency) throw new InvalidOperationException("PayPal refunded an amount or currency different from the request.");
        var refund = order.RecordRefund(request.IdempotencyKey, result.Id, result.Status, result.Amount, result.CreatedAt);
        await _db.SaveChangesAsync(ct); return RefundView(order, refund);
    }

    public async Task<object> SavePaymentMethodAsync(string shopper, SavePaymentMethodRequest request, CancellationToken ct)
    {
        ValidateCard(request.Card);
        var buyer = await _db.Buyers.Include(x => x.PaymentMethods).SingleOrDefaultAsync(x => x.IdentityGuid == shopper, ct);
        if (buyer == null) { buyer = new Buyer(shopper); _db.Buyers.Add(buyer); }
        var result = await _paypal.SaveCardAsync(shopper, buyer.PayPalCustomerId, Card(request.Card), ct);
        buyer.SetPayPalCustomerId(result.CustomerId);
        var method = buyer.AddPaymentMethod(result.TokenId, result.Brand, result.Last4, result.Expiry, result.CardholderName);
        await _db.SaveChangesAsync(ct); return MethodView(method);
    }

    public async Task<object> ListPaymentMethodsAsync(string shopper, CancellationToken ct)
    {
        var buyer = await _db.Buyers.Include(x => x.PaymentMethods).SingleOrDefaultAsync(x => x.IdentityGuid == shopper, ct);
        return new { paymentMethods = buyer?.PaymentMethods.Where(x => x.IsActive).OrderBy(x => x.Id).Select(MethodView).ToList() ?? new List<object>() };
    }

    public async Task DeletePaymentMethodAsync(string shopper, int id, CancellationToken ct)
    {
        var buyer = await _db.Buyers.Include(x => x.PaymentMethods).SingleOrDefaultAsync(x => x.IdentityGuid == shopper, ct);
        var method = buyer?.PaymentMethods.SingleOrDefault(x => x.Id == id && x.IsActive);
        if (buyer == null || method == null) throw new PaymentOperationException(404, "Payment method not found.");
        await _paypal.DeletePaymentTokenAsync(method.PayPalTokenId, ct); buyer.RemovePaymentMethod(method, DateTimeOffset.UtcNow); await _db.SaveChangesAsync(ct);
    }

    public async Task<object> MyOrdersAsync(string shopper, CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking().Include(x => x.OrderItems).Include(x => x.Payment!).ThenInclude(x => x.Refunds)
            .Where(x => x.BuyerId == shopper).OrderByDescending(x => x.OrderDate).ToListAsync(ct);
        return new { orders = orders.Select(OrderView).ToList() };
    }

    public async Task<object> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from >= to) throw Bad("from must be earlier than to.");
        var paypal = await _paypal.ListTransactionsAsync(from, to, ct);
        var orders = await _db.Orders.AsNoTracking().Include(x => x.Payment!).ThenInclude(x => x.Refunds).Where(x => x.Payment != null).ToListAsync(ct);
        var local = new List<(string Id, int OrderId, string Type, DateTimeOffset At, decimal Amount, string Currency)>();
        foreach (var o in orders) { var p = o.Payment!; local.Add((p.AuthorizationId, o.Id, "Authorization", p.AuthorizedAt, p.AuthorizedAmount, p.Currency)); if (p.CaptureId != null && p.CapturedAt != null) local.Add((p.CaptureId, o.Id, "Capture", p.CapturedAt.Value, p.CapturedAmount!.Value, p.Currency)); foreach (var r in p.Refunds) local.Add((r.RefundId, o.Id, "Refund", r.CreatedAt, r.Amount, p.Currency)); }
        local = local.Where(x => x.At >= from && x.At <= to).ToList();
        var byId = local.ToDictionary(x => x.Id, StringComparer.Ordinal); var paypalIds = paypal.Select(x => x.TransactionId).ToHashSet(StringComparer.Ordinal);
        var byInvoice = orders.ToDictionary(x => $"eshop-order-{x.PaymentReference}", x => x.Id, StringComparer.Ordinal);
        var paypalRows = paypal.Select(x => { byId.TryGetValue(x.TransactionId, out var match); int? orderId = match == default && x.InvoiceId != null && byInvoice.TryGetValue(x.InvoiceId, out var invoiceOrderId) ? invoiceOrderId : match == default ? null : match.OrderId; return new { x.TransactionId, x.ReferenceId, x.ReferenceIdType, x.EventCode, x.InitiatedAt, x.UpdatedAt, x.Amount, x.Currency, x.Fee, x.Status, x.InvoiceId, orderId, matchStatus = orderId == null ? "PayPalOnly" : "Matched" }; }).ToList();
        var localOnly = local.Where(x => !paypalIds.Contains(x.Id)).Select(x => new { transactionId = x.Id, x.OrderId, x.Type, occurredAt = x.At, x.Amount, x.Currency, matchStatus = "EShopOnly" }).ToList();
        return new { from, to, paypalTransactions = paypalRows, localOnly };
    }

    private async Task<Order> OrderAsync(int id, CancellationToken ct) => await _db.Orders.Include(x => x.OrderItems).Include(x => x.Payment!).ThenInclude(x => x.Refunds).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new PaymentOperationException(404, "Order not found.");
    private static void Own(Order order, string shopper) { if (!string.Equals(order.BuyerId, shopper, StringComparison.OrdinalIgnoreCase)) throw new PaymentOperationException(404, "Order not found."); }
    private static PaymentOperationException Bad(string m) => new(400, m); private static PaymentOperationException Conflict(string m) => new(409, m);
    private static CardDto Card(CardRequest c) { ValidateCard(c); return new(c.Number.Replace(" ", string.Empty, StringComparison.Ordinal), c.Expiry, c.SecurityCode, c.Name, new(c.BillingAddress.AddressLine1, c.BillingAddress.AddressLine2, c.BillingAddress.AdminArea2, c.BillingAddress.AdminArea1, c.BillingAddress.PostalCode, c.BillingAddress.CountryCode)); }
    private static void ValidateCard(CardRequest c) { if (string.IsNullOrWhiteSpace(c.Number) || string.IsNullOrWhiteSpace(c.Expiry) || string.IsNullOrWhiteSpace(c.SecurityCode) || string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.BillingAddress.AddressLine1) || string.IsNullOrWhiteSpace(c.BillingAddress.AdminArea2) || string.IsNullOrWhiteSpace(c.BillingAddress.PostalCode) || c.BillingAddress.CountryCode.Length != 2) throw Bad("Complete card and billing-address details are required."); }
    private static object MethodView(PaymentMethod x) => new { paymentMethodId = x.Id, x.Brand, x.Last4, x.Expiry, x.CardholderName };
    private static object RefundView(Order o, PaymentRefund r) => new { refundId = r.RefundId, orderId = o.Id, r.Status, r.Amount, currency = o.Payment!.Currency, paymentStatus = o.PaymentStatus.ToString(), refundedAmount = o.Payment.RefundedAmount };
    private static object PaymentView(Order o) => new { orderId = o.Id, total = o.Total(), paymentStatus = o.PaymentStatus.ToString(), fulfillmentStatus = o.FulfillmentStatus.ToString(), payment = o.Payment == null ? null : new { currency = o.Payment.Currency, paypalOrderId = o.Payment.PayPalOrderId, authorizationId = o.Payment.AuthorizationId, authorizationStatus = o.Payment.AuthorizationStatus, authorizedAmount = o.Payment.AuthorizedAmount, authorizationExpiresAt = o.Payment.AuthorizationExpiresAt, captureId = o.Payment.CaptureId, captureStatus = o.Payment.CaptureStatus, capturedAmount = o.Payment.CapturedAmount, paypalFee = o.Payment.PayPalFee, netProceeds = o.Payment.NetAmount, refundedAmount = o.Payment.RefundedAmount } };
    private static object OrderView(Order o) => new { orderId = o.Id, o.OrderDate, total = o.Total(), paymentStatus = o.PaymentStatus.ToString(), fulfillmentStatus = o.FulfillmentStatus.ToString(), items = o.OrderItems.Select(i => new { catalogItemId = i.ItemOrdered.CatalogItemId, name = i.ItemOrdered.ProductName, unitPrice = i.UnitPrice, quantity = i.Units }), payment = o.Payment == null ? null : new { currency = o.Payment.Currency, authorizationId = o.Payment.AuthorizationId, authorizationStatus = o.Payment.AuthorizationStatus, authorizedAmount = o.Payment.AuthorizedAmount, captureId = o.Payment.CaptureId, captureStatus = o.Payment.CaptureStatus, capturedAmount = o.Payment.CapturedAmount, paypalFee = o.Payment.PayPalFee, netProceeds = o.Payment.NetAmount, refundedAmount = o.Payment.RefundedAmount, refunds = o.Payment.Refunds.Select(r => new { refundId = r.RefundId, r.Status, r.Amount, r.CreatedAt }) } };
}
