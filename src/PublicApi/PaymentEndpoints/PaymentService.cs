using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed partial class PaymentService
{
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;
    private readonly PaymentOperationLock _operationLock;

    public PaymentService(CatalogContext db, IPayPalClient payPal, IOptions<PayPalOptions> options,
        PaymentOperationLock operationLock)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
        _operationLock = operationLock;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw BadRequest("EMPTY_ORDER", "At least one catalog item is required.");
        if (request.Items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
            throw BadRequest("INVALID_QUANTITY", "Catalog item ids and quantities must be positive.");

        var requestedItems = request.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new CreateOrderItemRequest(g.Key, g.Sum(i => i.Quantity)))
            .ToList();
        var ids = requestedItems.Select(i => i.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, cancellationToken);
        var missing = ids.Where(id => !catalogItems.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw BadRequest("CATALOG_ITEM_NOT_FOUND", $"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var orderItems = requestedItems.Select(requested =>
        {
            var item = catalogItems[requested.CatalogItemId];
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
                item.Price, requested.Quantity);
        }).ToList();
        var address = request.ShippingAddress is null
            ? new Address("Not supplied", "Not supplied", string.Empty, "Not supplied", "Not supplied")
            : new Address(request.ShippingAddress.Street, request.ShippingAddress.City,
                request.ShippingAddress.State, request.ShippingAddress.Country, request.ShippingAddress.ZipCode);
        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        return new CreateOrderResponse(order.Id, order.PaymentStatus.ToString(), order.Total(), Currency);
    }

    public async Task<PayOrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"pay:{orderId}", cancellationToken);
        var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled)
            throw Conflict("ORDER_CANCELLED", "A cancelled order cannot be paid.");
        if (order.PaymentStatus != PaymentStatus.AwaitingPayment)
        {
            if (order.PayPalAuthorizationId is not null)
                return PayResponse(order);
            throw Conflict("ORDER_NOT_PAYABLE", $"Order {orderId} is in payment state {order.PaymentStatus}.");
        }
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw BadRequest("PAYMENT_SOURCE_REQUIRED",
                "Provide exactly one payment source: card or paymentMethodId.");

        PayPalPaymentSource source;
        if (request.PaymentMethodId.HasValue)
        {
            var method = await _db.PaymentMethods.SingleOrDefaultAsync(
                p => p.Id == request.PaymentMethodId.Value && p.BuyerId == buyerId, cancellationToken);
            if (method is null)
                throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved payment method was not found.");
            source = new PayPalPaymentSource.VaultedCard(method.PayPalVaultId);
        }
        else
        {
            source = new PayPalPaymentSource.OneOffCard(MapAndValidateCard(request.Card!));
        }

        order.EnsurePaymentReference();
        var total = Decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        var authorization = await _payPal.AuthorizeAsync(total, Currency, order.PaymentReference,
            source, cancellationToken);
        if (authorization.Amount != total)
        {
            try
            {
                await _payPal.VoidAsync(authorization.AuthorizationId, order.PaymentReference, cancellationToken);
            }
            catch
            {
                // The mismatch is the primary failure. PayPal request ids still make a later void retry safe.
            }
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "AMOUNT_MISMATCH",
                "PayPal authorized an amount different from the immutable order total; the hold was voided.");
        }
        if (authorization.Status is not ("CREATED" or "PENDING"))
            throw Conflict("AUTHORIZATION_REJECTED", $"PayPal authorization status is {authorization.Status}.");

        order.RecordAuthorization(Currency, authorization.PayPalOrderId, authorization.OrderStatus,
            authorization.AuthorizationId, authorization.Status, authorization.CreatedAt,
            authorization.ExpiresAt);
        await _db.SaveChangesAsync(cancellationToken);
        return PayResponse(order);
    }

    public async Task<FulfilOrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"fulfil:{orderId}", cancellationToken);
        var order = await AnyOrderAsync(orderId, cancellationToken);
        if (order.FulfillmentStatus == FulfillmentStatus.Fulfilled)
            return FulfilResponse(order);
        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled)
            throw Conflict("ORDER_CANCELLED", "A cancelled order cannot be fulfilled.");
        if (order.PayPalOrderId is null || order.PayPalAuthorizationId is null)
            throw Conflict("ORDER_NOT_AUTHORIZED", "The order must be authorized before fulfilment.");

        var state = await _payPal.GetOrderStateAsync(order.PayPalOrderId, cancellationToken);
        if (state.Capture is not null)
        {
            EnsureCaptureAmount(order, state.Capture);
            order.RecordCapture(state.Capture.Id, state.Capture.Status, state.Capture.Amount,
                state.Capture.PayPalFee, state.Capture.NetAmount, state.Capture.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return FulfilResponse(order);
        }

        var authorization = state.Authorization;
        if (authorization is null)
            throw Conflict("AUTHORIZATION_NOT_FOUND",
                "PayPal no longer reports an authorization for this order; ask the shopper to authorize it again.");
        if (authorization.Status == "PENDING")
            throw Conflict("AUTHORIZATION_PENDING", "PayPal is still reviewing the authorization; retry fulfilment later.");
        if (authorization.Status != "CREATED")
            throw Conflict("AUTHORIZATION_UNAVAILABLE",
                $"PayPal authorization is {authorization.Status}; ask the shopper to authorize the order again.");

        var total = Decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        if (authorization.Amount != total)
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "AMOUNT_MISMATCH",
                "PayPal's authorization amount no longer equals the order total.");

        // The OpenAPI contract defines a three-day honor period and reauthorization from day 4.
        if (authorization.CreatedAt <= DateTimeOffset.UtcNow.AddDays(-3))
        {
            authorization = await _payPal.ReauthorizeAsync(authorization.AuthorizationId, total,
                Currency, order.PaymentReference, cancellationToken);
            if (authorization.Amount != total)
                throw new PaymentApiException((int)HttpStatusCode.BadGateway, "AMOUNT_MISMATCH",
                    "PayPal renewed the authorization for an unexpected amount.");
            order.RecordReauthorization(authorization.AuthorizationId, authorization.Status,
                authorization.CreatedAt, authorization.ExpiresAt);
            if (authorization.Status != "CREATED")
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw Conflict("REAUTHORIZATION_PENDING",
                    $"PayPal renewed the authorization with status {authorization.Status}; retry fulfilment when it becomes CREATED.");
            }
        }

        var capture = await _payPal.CaptureAsync(authorization.AuthorizationId, total, Currency,
            order.PaymentReference, cancellationToken);
        EnsureCaptureAmount(order, capture);
        order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee,
            capture.NetAmount, capture.CreatedAt);
        await _db.SaveChangesAsync(cancellationToken);
        return FulfilResponse(order);
    }

    public async Task<CancelOrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"cancel:{orderId}", cancellationToken);
        var order = await AnyOrderAsync(orderId, cancellationToken);
        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled)
            return new(order.Id, order.PaymentStatus.ToString(), order.FulfillmentStatus.ToString());
        if (order.FulfillmentStatus == FulfillmentStatus.Fulfilled || order.PayPalCaptureId is not null)
            throw Conflict("ORDER_ALREADY_CAPTURED", "A captured order cannot be cancelled; issue a refund instead.");

        if (order.PaymentStatus == PaymentStatus.AwaitingPayment)
        {
            order.CancelUnpaid();
        }
        else if (order.PaymentStatus is PaymentStatus.Authorized or PaymentStatus.AuthorizationPending)
        {
            if (order.PayPalAuthorizationId is null)
                throw Conflict("AUTHORIZATION_NOT_FOUND", "The order has no PayPal authorization to void.");
            var status = await _payPal.VoidAsync(order.PayPalAuthorizationId, order.PaymentReference,
                cancellationToken);
            order.RecordVoid(status);
        }
        else
        {
            throw Conflict("ORDER_NOT_CANCELLABLE", $"Order {orderId} is in payment state {order.PaymentStatus}.");
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new(order.Id, order.PaymentStatus.ToString(), order.FulfillmentStatus.ToString());
    }

    public async Task<RefundOrderResponse> RefundAsync(string buyerId, int orderId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 80)
            throw BadRequest("INVALID_IDEMPOTENCY_KEY", "idempotencyKey must contain 1 to 80 characters.");
        using var operation = await _operationLock.AcquireAsync($"refund:{orderId}", cancellationToken);
        var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);

        var existing = order.Refunds.SingleOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null)
        {
            if (existing.Status == "PENDING")
            {
                var current = await _payPal.GetRefundAsync(existing.PayPalRefundId, cancellationToken);
                order.UpdateRefundStatus(existing.PayPalRefundId, current.Status);
                await _db.SaveChangesAsync(cancellationToken);
            }
            return RefundResponseFor(order, existing);
        }
        if (order.FulfillmentStatus != FulfillmentStatus.Fulfilled || order.PayPalCaptureId is null ||
            order.CapturedAmount is null)
            throw Conflict("ORDER_NOT_CAPTURED", "Only a fulfilled, captured order can be refunded.");

        foreach (var pending in order.Refunds.Where(r => r.Status == "PENDING").ToList())
        {
            var current = await _payPal.GetRefundAsync(pending.PayPalRefundId, cancellationToken);
            order.UpdateRefundStatus(pending.PayPalRefundId, current.Status);
        }

        var remaining = order.CapturedAmount.Value - order.RefundedAmount;
        var amount = request.Amount ?? remaining;
        amount = Decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0 || amount > remaining)
            throw BadRequest("INVALID_REFUND_AMOUNT",
                $"Refund amount must be positive and cannot exceed the remaining {remaining:0.00} {Currency}.");

        var result = await _payPal.RefundAsync(order.PayPalCaptureId, amount, Currency,
            request.IdempotencyKey, cancellationToken);
        if (result.Amount != amount)
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "AMOUNT_MISMATCH",
                "PayPal refunded an amount different from the requested amount.");
        if (result.Status is "FAILED" or "CANCELLED")
            throw Conflict("REFUND_REJECTED", $"PayPal refund status is {result.Status}.");

        var refund = new PaymentRefund(result.Id, request.IdempotencyKey, result.Amount,
            Currency, result.Status, result.CreatedAt);
        order.RecordRefund(refund);
        await _db.SaveChangesAsync(cancellationToken);
        return RefundResponseFor(order, refund);
    }

    public async Task<IReadOnlyList<OrderResponse>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(o => o.OrderItems).ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(MapOrder).ToList();
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var card = MapAndValidateCard(request.Card);
        var saved = await _payPal.SaveCardAsync(buyerId, card, cancellationToken);
        var paymentMethod = new PaymentMethod(buyerId, saved.VaultId, saved.CustomerId,
            saved.Brand, saved.LastDigits, saved.Expiry);
        _db.PaymentMethods.Add(paymentMethod);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _payPal.DeletePaymentTokenAsync(saved.VaultId, CancellationToken.None);
            throw;
        }
        return MapPaymentMethod(paymentMethod);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) => await _db.PaymentMethods.AsNoTracking()
            .Where(p => p.BuyerId == buyerId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PaymentMethodResponse(p.Id, p.Brand, p.LastDigits, p.Expiry, p.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"method:{paymentMethodId}", cancellationToken);
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(
            p => p.Id == paymentMethodId && p.BuyerId == buyerId, cancellationToken);
        if (method is null)
            throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved payment method was not found.");
        await _payPal.DeletePaymentTokenAsync(method.PayPalVaultId, cancellationToken);
        _db.PaymentMethods.Remove(method);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw BadRequest("INVALID_DATE_RANGE", "from must be earlier than to.");
        var transactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _db.Orders.AsNoTracking().Include(o => o.Refunds)
            .Where(o => o.PayPalOrderId != null).ToListAsync(cancellationToken);
        var records = orders.SelectMany(LocalRecords).ToList();
        var byProcessorId = records.Where(r => r.ProcessorId is not null)
            .GroupBy(r => r.ProcessorId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var byInvoice = orders.ToDictionary(o => $"ESHOP-{o.PaymentReference:N}", o => o,
            StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();

        foreach (var transaction in transactions)
        {
            LocalRecord? local = null;
            if (byProcessorId.TryGetValue(transaction.TransactionId, out var byId)) local = byId;
            else if (transaction.ReferenceId is not null &&
                     byProcessorId.TryGetValue(transaction.ReferenceId, out var byReference)) local = byReference;
            else if (transaction.InvoiceId is not null && byInvoice.TryGetValue(transaction.InvoiceId, out var byOrder))
                local = LocalRecords(byOrder).FirstOrDefault();

            if (local is not null) matched.Add(local.Key);
            entries.Add(new ReconciliationEntry(local is null ? "PayPalOnly" : "Matched",
                local?.Order.Id, local?.Order.PaymentStatus.ToString(), transaction.TransactionId,
                transaction.ReferenceId, transaction.EventCode, transaction.Amount, transaction.Fee,
                transaction.Currency, transaction.InitiatedAt, transaction.InvoiceId));
        }

        foreach (var local in records.Where(r => r.At >= from && r.At <= to && !matched.Contains(r.Key)))
        {
            entries.Add(new ReconciliationEntry("EShopOnly", local.Order.Id,
                local.Order.PaymentStatus.ToString(), null, local.ProcessorId, local.Kind,
                local.Amount, null, local.Order.PaymentCurrency, local.At,
                $"ESHOP-{local.Order.PaymentReference:N}"));
        }

        return new ReconciliationResponse(from, to, entries.OrderBy(e => e.TransactionDate).ToList());
    }

    private async Task<Order> OwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(o => o.OrderItems).ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds)
            .SingleOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, cancellationToken);
        return order ?? throw NotFound("ORDER_NOT_FOUND", "The order was not found.");
    }

    private async Task<Order> AnyOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(o => o.OrderItems).ThenInclude(i => i.ItemOrdered)
            .Include(o => o.Refunds).SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        return order ?? throw NotFound("ORDER_NOT_FOUND", "The order was not found.");
    }

    private string Currency => _options.Currency.ToUpperInvariant();

    private static PaymentCard MapAndValidateCard(CardRequest request)
    {
        var number = request.Number?.Replace(" ", string.Empty, StringComparison.Ordinal) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(request.Name) || !CardNumberRegex().IsMatch(number) ||
            !ExpiryRegex().IsMatch(request.Expiry ?? string.Empty) ||
            !SecurityCodeRegex().IsMatch(request.SecurityCode ?? string.Empty))
            throw BadRequest("INVALID_CARD", "Card name, 13-19 digit number, YYYY-MM expiry and 3-4 digit securityCode are required.");
        if (!DateOnly.TryParseExact(request.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry.AddMonths(1) <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw BadRequest("INVALID_CARD_EXPIRY", "The card expiry must be a future month in YYYY-MM format.");

        CardBillingAddress? billing = request.BillingAddress is null ? null : new(
            request.BillingAddress.AddressLine1, request.BillingAddress.AddressLine2,
            request.BillingAddress.City, request.BillingAddress.State, request.BillingAddress.PostalCode,
            request.BillingAddress.CountryCode.ToUpperInvariant());
        if (billing is not null && billing.CountryCode.Length != 2)
            throw BadRequest("INVALID_BILLING_ADDRESS", "Billing address countryCode must contain two letters.");
        return new PaymentCard(request.Name, number, request.Expiry!, request.SecurityCode!, billing);
    }

    private static PayOrderResponse PayResponse(Order order) => new(order.Id,
        order.PaymentStatus.ToString(), order.PayPalAuthorizationId, order.Total(),
        order.PaymentCurrency ?? string.Empty);

    private FulfilOrderResponse FulfilResponse(Order order) => new(order.Id,
        order.PaymentStatus.ToString(), order.FulfillmentStatus.ToString(), order.PayPalCaptureId,
        order.CapturedAmount, order.PayPalFee, order.NetProceeds, order.PaymentCurrency ?? Currency);

    private static RefundOrderResponse RefundResponseFor(Order order, PaymentRefund refund)
    {
        var remaining = (order.CapturedAmount ?? 0) - order.RefundedAmount;
        return new RefundOrderResponse(refund.PayPalRefundId, order.Id, refund.Status, refund.Amount,
            order.RefundedAmount, remaining, refund.Currency);
    }

    private OrderResponse MapOrder(Order order) => new(order.Id, order.OrderDate, order.Total(),
        order.PaymentCurrency ?? Currency, order.PaymentStatus.ToString(),
        order.FulfillmentStatus.ToString(), order.PayPalAuthorizationId, order.PayPalCaptureId,
        order.CapturedAmount, order.PayPalFee, order.NetProceeds, order.RefundedAmount,
        (order.CapturedAmount ?? 0) - order.RefundedAmount,
        order.OrderItems.Select(i => new OrderItemResponse(i.ItemOrdered.CatalogItemId,
            i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
        order.Refunds.Select(r => new RefundResponse(r.PayPalRefundId, r.Status, r.Amount,
            r.Currency, r.CreatedAt)).ToList());

    private static PaymentMethodResponse MapPaymentMethod(PaymentMethod method) => new(method.Id,
        method.Brand, method.LastDigits, method.Expiry, method.CreatedAt);

    private static void EnsureCaptureAmount(Order order, PayPalCapture capture)
    {
        if (capture.Amount != Decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero))
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "AMOUNT_MISMATCH",
                "PayPal's captured amount does not equal the order total.");
    }

    private static IEnumerable<LocalRecord> LocalRecords(Order order)
    {
        if (order.PayPalAuthorizationId is not null && order.AuthorizedAt.HasValue)
            yield return new LocalRecord($"authorization:{order.PayPalAuthorizationId}", "AUTHORIZATION",
                order.PayPalAuthorizationId, order.AuthorizedAt.Value, order.Total(), order);
        if (order.PayPalCaptureId is not null && order.CapturedAt.HasValue)
            yield return new LocalRecord($"capture:{order.PayPalCaptureId}", "CAPTURE",
                order.PayPalCaptureId, order.CapturedAt.Value, order.CapturedAmount, order);
        foreach (var refund in order.Refunds)
            yield return new LocalRecord($"refund:{refund.PayPalRefundId}", "REFUND",
                refund.PayPalRefundId, refund.CreatedAt, refund.Amount, order);
    }

    private sealed record LocalRecord(string Key, string Kind, string? ProcessorId,
        DateTimeOffset At, decimal? Amount, Order Order);

    private static PaymentApiException BadRequest(string code, string message) =>
        new((int)HttpStatusCode.BadRequest, code, message);
    private static PaymentApiException NotFound(string code, string message) =>
        new((int)HttpStatusCode.NotFound, code, message);
    private static PaymentApiException Conflict(string code, string message) =>
        new((int)HttpStatusCode.Conflict, code, message);

    [GeneratedRegex("^[0-9]{13,19}$")]
    private static partial Regex CardNumberRegex();
    [GeneratedRegex("^[0-9]{4}-(0[1-9]|1[0-2])$")]
    private static partial Regex ExpiryRegex();
    [GeneratedRegex("^[0-9]{3,4}$")]
    private static partial Regex SecurityCodeRegex();
}
