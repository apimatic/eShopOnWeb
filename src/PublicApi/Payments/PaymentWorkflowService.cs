using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentWorkflowService
{
    private static readonly Regex ExpiryPattern = new("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly IOperationLock _operationLock;
    private readonly PayPalOptions _options;

    public PaymentWorkflowService(CatalogContext db, IPayPalClient payPal,
        IOperationLock operationLock, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _operationLock = operationLock;
        _options = options.Value;
    }

    public async Task<OrderResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (request.Items is null || request.Items.Count == 0)
        {
            BadRequest("ORDER_ITEMS_REQUIRED", "At least one catalog item is required.");
        }
        ValidateAddress(request.ShippingAddress);
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
        {
            BadRequest("INVALID_ORDER_ITEM", "Catalog item IDs and quantities must be positive.");
        }

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(line => line.Quantity));
        var catalogItems = await _db.CatalogItems
            .Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = requested.Keys.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentApiException((int)HttpStatusCode.BadRequest, "CATALOG_ITEMS_NOT_FOUND",
                "One or more catalog items do not exist.", new Dictionary<string, object?> { ["catalogItemIds"] = missing });
        }

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, requested[item.Id])).ToList();
        var shipping = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(shipping.Street, shipping.City, shipping.State, shipping.Country, shipping.ZipCode),
            orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return await MapOrderAsync(order, cancellationToken);
    }

    public async Task<OrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (order.Status == OrderStatus.Authorized)
        {
            return await MapOrderAsync(order, cancellationToken);
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            Conflict("ORDER_NOT_PAYABLE", $"Order {orderId} is {order.Status} and cannot be authorized.");
        }
        if ((request.Card is null) == (request.PaymentMethodId is null))
        {
            BadRequest("PAYMENT_SOURCE_REQUIRED",
                "Provide either card details or paymentMethodId, but not both.");
        }

        string? vaultId = null;
        if (request.PaymentMethodId is int paymentMethodId)
        {
            var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId &&
                x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
            if (method is null)
            {
                NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved payment method does not exist.");
            }
            vaultId = method!.PayPalVaultId;
        }
        else
        {
            ValidateCard(request.Card!);
        }

        var total = DecimalToCents(order.Total());
        var authorization = await _payPal.AuthorizeAsync(order.Id, order.PaymentRequestId, total,
            Currency(), request.Card, vaultId, cancellationToken);
        if (authorization.Amount != total || !authorization.Currency.Equals(Currency(), StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "PAYPAL_AMOUNT_MISMATCH",
                "PayPal's authorization amount or currency did not match the order total.");
        }
        if (!authorization.Status.Equals("CREATED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "PAYPAL_AUTHORIZATION_NOT_CREATED",
                $"PayPal returned authorization status {authorization.Status}.");
        }

        order.RecordAuthorization(authorization.OrderId, authorization.AuthorizationId,
            authorization.Status, authorization.Currency, authorization.CreateTime,
            authorization.ExpirationTime);
        await _db.SaveChangesAsync(cancellationToken);
        return await MapOrderAsync(order, cancellationToken);
    }

    public async Task<OrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OrderAsync(orderId, cancellationToken);
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return await MapOrderAsync(order, cancellationToken);
        }
        if (order.Status != OrderStatus.Authorized || string.IsNullOrWhiteSpace(order.AuthorizationId))
        {
            Conflict("ORDER_NOT_FULFILLABLE", $"Order {orderId} is {order.Status}; it must be authorized first.");
        }

        var now = DateTimeOffset.UtcNow;
        var authorizationId = order.AuthorizationId!;
        if (order.AuthorizationExpiresAt.HasValue && order.AuthorizationExpiresAt <= now)
        {
            Conflict("AUTHORIZATION_EXPIRED",
                "The PayPal authorization is outside its authorization period. Ask the shopper to authorize the order again.");
        }
        if (order.AuthorizedAt.HasValue && order.AuthorizedAt <= now.AddDays(-3))
        {
            if (order.ReauthorizationCount > 0)
            {
                Conflict("REAUTHORIZATION_EXHAUSTED",
                    "The renewed PayPal authorization is stale and cannot be renewed again. Ask the shopper to authorize the order again.");
            }
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(authorizationId, DecimalToCents(order.Total()),
                    Currency(), $"reauth-{order.PaymentRequestId}", cancellationToken);
                if (!renewed.Status.Equals("CREATED", StringComparison.OrdinalIgnoreCase))
                {
                    Conflict("REAUTHORIZATION_NOT_ACTIVE",
                        $"PayPal returned reauthorization status {renewed.Status}. Ask the shopper to authorize the order again.");
                }
                order.RecordReauthorization(renewed.AuthorizationId, renewed.Status,
                    renewed.CreateTime, renewed.ExpirationTime);
                authorizationId = renewed.AuthorizationId;
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (PayPalApiException exception) when (exception.StatusCode is 404 or 422)
            {
                throw new PaymentApiException((int)HttpStatusCode.Conflict, "REAUTHORIZATION_FAILED",
                    "PayPal can no longer renew this authorization. Ask the shopper to authorize the order again.",
                    new Dictionary<string, object?> { ["paypalDebugId"] = exception.DebugId });
            }
        }

        var capture = await _payPal.CaptureAsync(authorizationId, DecimalToCents(order.Total()),
            Currency(), $"capture-{order.PaymentRequestId}", cancellationToken);
        if (!capture.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApiException((int)HttpStatusCode.Conflict, "CAPTURE_NOT_COMPLETED",
                $"PayPal returned capture status {capture.Status}. Retry fulfilment after the payment status changes.");
        }
        if (capture.Amount != DecimalToCents(order.Total()))
        {
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "PAYPAL_CAPTURE_AMOUNT_MISMATCH",
                "PayPal's captured amount did not match the order total.");
        }
        order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee,
            capture.NetAmount, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return await MapOrderAsync(order, cancellationToken);
    }

    public async Task<OrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            return await MapOrderAsync(order, cancellationToken);
        }
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            Conflict("CAPTURED_ORDER_CANNOT_BE_CANCELLED", "Captured orders must be refunded, not cancelled.");
        }
        if (order.Status == OrderStatus.Authorized && !string.IsNullOrWhiteSpace(order.AuthorizationId))
        {
            await _payPal.VoidAsync(order.AuthorizationId!, $"void-{order.PaymentRequestId}", cancellationToken);
            order.Cancel("VOIDED", DateTimeOffset.UtcNow);
        }
        else if (order.Status == OrderStatus.AwaitingPayment)
        {
            order.Cancel("NOT_AUTHORIZED", DateTimeOffset.UtcNow);
        }
        else
        {
            Conflict("ORDER_NOT_CANCELLABLE", $"Order {orderId} is {order.Status} and cannot be cancelled.");
        }
        await _db.SaveChangesAsync(cancellationToken);
        return await MapOrderAsync(order, cancellationToken);
    }

    public async Task<(PaymentRefund Refund, OrderResponse Order)> RefundAsync(string buyerId, int orderId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 108)
        {
            BadRequest("INVALID_IDEMPOTENCY_KEY", "IdempotencyKey must contain 1 to 108 characters.");
        }
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var existing = await _db.PaymentRefunds.SingleOrDefaultAsync(x => x.OrderId == orderId &&
            x.BuyerId == buyerId && x.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (existing is not null)
        {
            var existingOrder = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
            return (existing, await MapOrderAsync(existingOrder, cancellationToken));
        }

        var order = await OwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded) ||
            string.IsNullOrWhiteSpace(order.CaptureId) || !order.CapturedAmount.HasValue)
        {
            Conflict("ORDER_NOT_REFUNDABLE", "Only a fulfilled order with a captured payment can be refunded.");
        }
        var remaining = order.CapturedAmount!.Value - order.RefundedAmount;
        var amount = request.Amount.HasValue ? DecimalToCents(request.Amount.Value) : remaining;
        if (amount <= 0 || amount > remaining)
        {
            throw new PaymentApiException((int)HttpStatusCode.Conflict, "REFUND_EXCEEDS_REMAINING_AMOUNT",
                $"Refund amount must be positive and no more than {remaining:0.00} {order.Currency}.");
        }

        var requestId = StableRequestId($"refund:{order.CaptureId}:{request.IdempotencyKey}");
        var result = await _payPal.RefundAsync(order.CaptureId!, amount, order.Currency ?? Currency(),
            requestId, cancellationToken);
        if (result.Amount != amount)
        {
            throw new PaymentApiException((int)HttpStatusCode.BadGateway, "PAYPAL_REFUND_AMOUNT_MISMATCH",
                "PayPal's refund amount did not match the requested amount.");
        }
        if (result.Status is not ("COMPLETED" or "PENDING"))
        {
            throw new PaymentApiException((int)HttpStatusCode.Conflict, "REFUND_NOT_ACCEPTED",
                $"PayPal returned refund status {result.Status}.");
        }

        var refund = new PaymentRefund(order.Id, buyerId, request.IdempotencyKey, result.Amount,
            result.Currency, result.Id, result.Status);
        _db.PaymentRefunds.Add(refund);
        order.RecordRefund(result.Amount);
        await _db.SaveChangesAsync(cancellationToken);
        return (refund, await MapOrderAsync(order, cancellationToken));
    }

    public async Task<IReadOnlyList<OrderResponse>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var orders = await _db.Orders.Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        var result = new List<OrderResponse>(orders.Count);
        foreach (var order in orders)
        {
            result.Add(await MapOrderAsync(order, cancellationToken));
        }
        return result;
    }

    public async Task<PaymentMethod> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        ValidateCard(request.Card);
        var requestId = Guid.NewGuid().ToString("N");
        var token = await _payPal.CreatePaymentTokenAsync(buyerId, request.Card, requestId, cancellationToken);
        var method = new PaymentMethod(buyerId, token.Id, token.Brand, token.Last4, token.Expiry);
        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        return await _db.PaymentMethods.Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        using var operation = await _operationLock.AcquireAsync($"payment-method:{paymentMethodId}", cancellationToken);
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId &&
            x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
        if (method is null)
        {
            NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved payment method does not exist.");
        }
        await _payPal.DeletePaymentTokenAsync(method!.PayPalVaultId, cancellationToken);
        method.MarkDeleted();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from)
        {
            BadRequest("INVALID_DATE_RANGE", "The 'to' date must be later than 'from'.");
        }
        if (to > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            BadRequest("INVALID_DATE_RANGE", "The reconciliation range cannot end in the future.");
        }

        var transactions = new List<PayPalTransaction>();
        var cursor = from;
        while (cursor < to)
        {
            var windowEnd = cursor.AddDays(30) < to ? cursor.AddDays(30) : to;
            transactions.AddRange(await _payPal.SearchTransactionsAsync(cursor, windowEnd, cancellationToken));
            cursor = windowEnd;
        }
        transactions = transactions.GroupBy(x => new { x.Id, x.EventCode, x.InitiatedAt })
            .Select(x => x.First()).ToList();

        var orders = await _db.Orders.AsNoTracking().ToListAsync(cancellationToken);
        var refunds = await _db.PaymentRefunds.AsNoTracking().ToListAsync(cancellationToken);
        var orderByRemoteId = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            AddRemoteId(orderByRemoteId, order.PayPalOrderId, order);
            AddRemoteId(orderByRemoteId, order.AuthorizationId, order);
            AddRemoteId(orderByRemoteId, order.CaptureId, order);
            AddRemoteId(orderByRemoteId, $"eshop-{order.PaymentRequestId}", order);
        }
        foreach (var refund in refunds)
        {
            var refundOrder = orders.SingleOrDefault(x => x.Id == refund.OrderId);
            if (refundOrder is not null) AddRemoteId(orderByRemoteId, refund.PayPalRefundId, refundOrder);
        }

        var items = new List<ReconciliationItem>();
        var matchedOrderIds = new HashSet<int>();
        foreach (var transaction in transactions)
        {
            var order = FindOrder(orderByRemoteId, transaction);
            if (order is not null) matchedOrderIds.Add(order.Id);
            items.Add(new ReconciliationItem("PayPal", order is null ? "PayPalOnly" : "Matched",
                order?.Id, transaction.Id, transaction.ReferenceId, transaction.EventCode,
                transaction.Status, transaction.Amount, transaction.Fee, transaction.Currency,
                transaction.InitiatedAt, transaction.InvoiceId));
        }

        foreach (var order in orders.Where(x => x.PayPalOrderId != null && !matchedOrderIds.Contains(x.Id) &&
            IsPaymentRelevantInRange(x, refunds, from, to)))
        {
            items.Add(new ReconciliationItem("eShop", "EShopOnly", order.Id, order.CaptureId,
                order.AuthorizationId, null, order.CaptureStatus ?? order.AuthorizationStatus,
                order.CapturedAmount ?? order.Total(), order.PayPalFee, order.Currency,
                order.FulfilledAt ?? order.AuthorizedAt, $"eshop-{order.PaymentRequestId}"));
        }

        return new ReconciliationResponse(from, to, transactions.Count,
            items.Count(x => x.MatchStatus == "Matched"), items.Count(x => x.MatchStatus == "PayPalOnly"),
            items.Count(x => x.MatchStatus == "EShopOnly"), items.OrderBy(x => x.InitiatedAt).ToList());
    }

    private async Task<Order> OwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken);
        if (order is null) NotFound("ORDER_NOT_FOUND", "The order does not exist.");
        return order!;
    }

    private async Task<Order> OrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(x => x.OrderItems)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        if (order is null) NotFound("ORDER_NOT_FOUND", "The order does not exist.");
        return order!;
    }

    private async Task<OrderResponse> MapOrderAsync(Order order, CancellationToken cancellationToken)
    {
        if (!_db.Entry(order).Collection(x => x.OrderItems).IsLoaded)
        {
            await _db.Entry(order).Collection(x => x.OrderItems).LoadAsync(cancellationToken);
        }
        var refunds = await _db.PaymentRefunds.Where(x => x.OrderId == order.Id)
            .OrderBy(x => x.CreatedAt).ToListAsync(cancellationToken);
        var refundResponses = refunds.Select(MapRefund).ToList();
        var captured = order.CapturedAmount ?? 0m;
        return new OrderResponse(order.Id, order.OrderDate, DecimalToCents(order.Total()),
            new ShippingAddressRequest(order.ShipToAddress.Street, order.ShipToAddress.City,
                order.ShipToAddress.State, order.ShipToAddress.Country, order.ShipToAddress.ZipCode),
            order.OrderItems.Select(x => new OrderLineResponse(x.ItemOrdered.CatalogItemId,
                x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
            new PaymentStateResponse(order.Status.ToString(), order.PayPalOrderId, order.AuthorizationId,
                order.AuthorizationStatus, order.AuthorizationExpiresAt, order.CaptureId, order.CaptureStatus,
                order.CapturedAmount, order.PayPalFee, order.NetProceeds, order.RefundedAmount,
                Math.Max(0m, captured - order.RefundedAmount), order.Currency, refundResponses));
    }

    public static PaymentMethodResponse MapPaymentMethod(PaymentMethod method) =>
        new(method.Id, method.Brand, method.Last4, method.Expiry, method.CreatedAt);

    public static RefundResponse MapRefund(PaymentRefund refund) =>
        new(refund.Id, refund.PayPalRefundId, refund.PayPalStatus, refund.Amount,
            refund.Currency, refund.IdempotencyKey, refund.CreatedAt);

    private string Currency() => _options.Currency.ToUpperInvariant();
    private static decimal DecimalToCents(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string StableRequestId(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void ValidateCard(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Name) ||
            card.Number.Replace(" ", string.Empty, StringComparison.Ordinal) is not { Length: >= 13 and <= 19 } number ||
            number.Any(x => !char.IsDigit(x)) || !ExpiryPattern.IsMatch(card.Expiry) ||
            card.SecurityCode.Length is < 3 or > 4 || card.SecurityCode.Any(x => !char.IsDigit(x)))
        {
            BadRequest("INVALID_CARD", "Card name, number, expiry (YYYY-MM), and security code are required and invalid values are rejected.");
        }
        ValidateBillingAddress(card.BillingAddress);
    }

    private static void ValidateBillingAddress(BillingAddress address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.AddressLine1) ||
            string.IsNullOrWhiteSpace(address.City) || string.IsNullOrWhiteSpace(address.State) ||
            string.IsNullOrWhiteSpace(address.PostalCode) || address.CountryCode?.Length != 2)
        {
            BadRequest("INVALID_BILLING_ADDRESS", "A complete billing address with a two-letter country code is required.");
        }
    }

    private static void ValidateAddress(ShippingAddressRequest address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
        {
            BadRequest("INVALID_SHIPPING_ADDRESS", "Street, city, country, and zipCode are required.");
        }
    }

    private static void RequireBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new PaymentApiException((int)HttpStatusCode.Unauthorized, "AUTHENTICATION_REQUIRED", "A signed-in user is required.");
    }

    private static void AddRemoteId(IDictionary<string, Order> index, string? key, Order order)
    {
        if (!string.IsNullOrWhiteSpace(key)) index[key] = order;
    }

    private static Order? FindOrder(IReadOnlyDictionary<string, Order> index, PayPalTransaction transaction)
    {
        foreach (var key in new[] { transaction.Id, transaction.ReferenceId, transaction.InvoiceId, transaction.CustomId })
        {
            if (key is not null && index.TryGetValue(key, out var order)) return order;
        }
        return null;
    }

    private static bool IsPaymentRelevantInRange(Order order, IReadOnlyList<PaymentRefund> refunds,
        DateTimeOffset from, DateTimeOffset to) =>
        InRange(order.AuthorizedAt, from, to) || InRange(order.FulfilledAt, from, to) ||
        InRange(order.CancelledAt, from, to) || refunds.Any(x => x.OrderId == order.Id && InRange(x.CreatedAt, from, to));

    private static bool InRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to) =>
        value.HasValue && value >= from && value <= to;

    [DoesNotReturn]
    private static void BadRequest(string code, string message) =>
        throw new PaymentApiException((int)HttpStatusCode.BadRequest, code, message);
    [DoesNotReturn]
    private static void NotFound(string code, string message) =>
        throw new PaymentApiException((int)HttpStatusCode.NotFound, code, message);
    [DoesNotReturn]
    private static void Conflict(string code, string message) =>
        throw new PaymentApiException((int)HttpStatusCode.Conflict, code, message);
}
