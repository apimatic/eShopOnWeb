using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentWorkflowService
{
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;
    private readonly PaymentOperationLock _operationLock;

    public PaymentWorkflowService(CatalogContext db, IPayPalClient payPal, IOptions<PayPalOptions> options,
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
        var requestedItems = request.Items
            .GroupBy(item => item.CatalogItemId)
            .Select(group => new { CatalogItemId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToList();
        if (requestedItems.Count == 0)
            throw BadRequest("EMPTY_ORDER", "An order must contain at least one catalog item.");
        if (requestedItems.Any(item => item.Quantity is <= 0 or > 1000))
            throw BadRequest("INVALID_QUANTITY", "Each combined catalog item quantity must be between 1 and 1000.");

        var ids = requestedItems.Select(item => item.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.Where(item => ids.Contains(item.Id)).ToListAsync(cancellationToken);
        var missingIds = ids.Except(catalogItems.Select(item => item.Id)).ToArray();
        if (missingIds.Length > 0)
            throw new PaymentWorkflowException(StatusCodes.Status404NotFound, "CATALOG_ITEMS_NOT_FOUND",
                $"Catalog item(s) {string.Join(", ", missingIds)} do not exist.");

        var itemLookup = catalogItems.ToDictionary(item => item.Id);
        var orderItems = requestedItems.Select(item =>
        {
            var catalogItem = itemLookup[item.CatalogItemId];
            return new OrderItem(new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                decimal.Round(catalogItem.Price, 2, MidpointRounding.AwayFromZero), item.Quantity);
        }).ToList();
        var address = new Address(request.ShippingAddress.Street, request.ShippingAddress.City,
            request.ShippingAddress.State, request.ShippingAddress.Country, request.ShippingAddress.PostalCode);
        var order = new Order(buyerId, address, orderItems, true, Currency);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        return new CreateOrderResponse(order.Id, Money(order.Total()), order.Currency!, order.PaymentStatus.ToString());
    }

    public async Task<OrderPaymentResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await FindOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);

        if (order.PaymentStatus is PaymentStatus.Authorized or PaymentStatus.Captured or
            PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            return ToPaymentResponse(order);
        if (order.PaymentStatus == PaymentStatus.AuthorizationPending && order.AuthorizationId is not null)
        {
            var current = await _payPal.GetAuthorizationAsync(order.AuthorizationId, cancellationToken);
            EnsureProcessorMoney(current.Amount, current.Currency, order);
            order.RecordAuthorization(current.Id, current.Status, current.Amount, current.CreatedAt, current.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
            return ToPaymentResponse(order);
        }
        if (order.PaymentStatus is PaymentStatus.Voided or PaymentStatus.Cancelled ||
            order.FulfillmentStatus == FulfillmentStatus.Cancelled)
            throw Conflict("ORDER_CANCELLED", "A cancelled order cannot be paid.");
        if (order.PaymentStatus == PaymentStatus.NotRequired)
            throw Conflict("PAYMENT_NOT_REQUIRED", "This legacy order was not created as a payable API order.");

        PayPalCard? card = null;
        string? vaultId = null;
        if (request.Card is not null)
        {
            card = MapCard(request.Card);
        }
        else
        {
            var paymentMethod = await _db.PaymentMethods.SingleOrDefaultAsync(method =>
                method.Id == request.PaymentMethodId && method.BuyerId == buyerId && method.IsActive, cancellationToken);
            if (paymentMethod is null)
                throw new PaymentWorkflowException(StatusCodes.Status404NotFound, "PAYMENT_METHOD_NOT_FOUND",
                    "The saved payment method does not exist, has been removed, or belongs to another shopper.");
            vaultId = paymentMethod.PayPalPaymentTokenId;
        }

        if (order.PayPalOrderId is null)
        {
            var payPalOrder = await _payPal.CreateOrderAsync(Money(order.Total()), order.Currency!, order.PaymentReference!,
                StableRequestId($"order:{order.PaymentReference}:create"), cancellationToken);
            order.RecordPayPalOrder(payPalOrder.Id, payPalOrder.Status);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var authorization = await _payPal.AuthorizeOrderAsync(order.PayPalOrderId!, card, vaultId,
            StableRequestId($"order:{order.PaymentReference}:authorize"), cancellationToken);
        EnsureProcessorMoney(authorization.Amount, authorization.Currency, order);
        order.RecordAuthorization(authorization.Id, authorization.Status, authorization.Amount,
            authorization.CreatedAt, authorization.ExpiresAt);
        await _db.SaveChangesAsync(cancellationToken);
        return ToPaymentResponse(order);
    }

    public async Task<OrderPaymentResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await FindOrderAsync(orderId, cancellationToken);
        if (order.FulfillmentStatus == FulfillmentStatus.Fulfilled) return ToPaymentResponse(order);
        if (order.PaymentStatus == PaymentStatus.CapturePending && order.CaptureId is not null)
        {
            var currentCapture = await _payPal.GetCaptureAsync(order.CaptureId, cancellationToken);
            EnsureProcessorMoney(currentCapture.Amount, currentCapture.Currency, order);
            order.RecordCapture(currentCapture.Id, currentCapture.Status, currentCapture.Amount,
                currentCapture.Fee, currentCapture.NetAmount, currentCapture.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            return ToPaymentResponse(order);
        }
        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled)
            throw Conflict("ORDER_CANCELLED", "A cancelled order cannot be fulfilled.");
        if (order.PaymentStatus != PaymentStatus.Authorized || order.AuthorizationId is null)
            throw Conflict("ORDER_NOT_AUTHORIZED", "Authorize the shopper's payment before fulfilling this order.");

        var authorization = await _payPal.GetAuthorizationAsync(order.AuthorizationId, cancellationToken);
        EnsureProcessorMoney(authorization.Amount, authorization.Currency, order);
        if (authorization.Status == "PENDING")
            throw Conflict("AUTHORIZATION_PENDING", "PayPal is still reviewing the authorization; retry fulfillment after it reaches CREATED.");
        if (authorization.Status is "DENIED" or "VOIDED")
            throw Conflict("AUTHORIZATION_UNAVAILABLE",
                $"PayPal reports the authorization as {authorization.Status}. Ask the shopper to authorize a new payment.");

        var now = DateTimeOffset.UtcNow;
        var initialCreatedAt = order.InitialAuthorizationCreatedAt ?? authorization.CreatedAt ?? order.AuthorizationCreatedAt;
        var currentCreatedAt = authorization.CreatedAt ?? order.AuthorizationCreatedAt;
        if (currentCreatedAt.HasValue && now >= currentCreatedAt.Value.AddDays(3))
        {
            if (!initialCreatedAt.HasValue || now >= initialCreatedAt.Value.AddDays(29))
                throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                    "The original PayPal authorization is at least 29 days old and can no longer be renewed. Ask the shopper to pay again before fulfillment.");

            authorization = await _payPal.ReauthorizeAsync(order.AuthorizationId, Money(order.Total()), order.Currency!,
                StableRequestId($"order:{order.PaymentReference}:reauthorize:{order.ReauthorizationCount + 1}"), cancellationToken);
            EnsureProcessorMoney(authorization.Amount, authorization.Currency, order);
            if (authorization.Status != "CREATED")
                throw Conflict("REAUTHORIZATION_NOT_READY",
                    $"PayPal returned reauthorization status {authorization.Status}; do not ship until it reaches CREATED.");
            order.RecordReauthorization(authorization.Id, authorization.Status, authorization.Amount,
                authorization.CreatedAt, authorization.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var capture = await _payPal.CaptureAsync(order.AuthorizationId!, Money(order.Total()), order.Currency!,
            order.PaymentReference!, StableRequestId($"order:{order.PaymentReference}:capture"), cancellationToken);
        EnsureProcessorMoney(capture.Amount, capture.Currency, order);
        order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.NetAmount, capture.CreatedAt);
        await _db.SaveChangesAsync(cancellationToken);
        return ToPaymentResponse(order);
    }

    public async Task<OrderPaymentResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await FindOrderAsync(orderId, cancellationToken);
        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled) return ToPaymentResponse(order);
        if (order.FulfillmentStatus == FulfillmentStatus.Fulfilled || order.CaptureId is not null)
            throw Conflict("ORDER_ALREADY_FULFILLED", "A captured order cannot be cancelled; issue a refund instead.");

        if (order.AuthorizationId is not null)
            await _payPal.VoidAsync(order.AuthorizationId, StableRequestId($"order:{order.PaymentReference}:void"), cancellationToken);

        order.RecordCancellation(order.AuthorizationId is null ? "NOT_AUTHORIZED" : "VOIDED");
        await _db.SaveChangesAsync(cancellationToken);
        return ToPaymentResponse(order);
    }

    public async Task<RefundResponse> RefundAsync(string buyerId, int orderId, CreateRefundRequest request,
        CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await FindOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);

        var existing = order.Refunds.SingleOrDefault(refund => refund.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null)
        {
            if (request.Amount.HasValue && Money(request.Amount.Value) != existing.Amount)
                throw Conflict("IDEMPOTENCY_KEY_REUSED",
                    "This idempotency key was already used with a different refund amount.");
            if (existing.PayPalRefundId is not null)
            {
                if (existing.Status == "PENDING")
                {
                    var currentRefund = await _payPal.GetRefundAsync(existing.PayPalRefundId, cancellationToken);
                    order.CompleteRefund(existing, currentRefund.Id, currentRefund.Status, currentRefund.Amount,
                        currentRefund.CreatedAt);
                    await _db.SaveChangesAsync(cancellationToken);
                }
                return ToRefundResponse(existing);
            }
        }

        if (order.CaptureId is null || order.CapturedAmount is null)
            throw Conflict("ORDER_NOT_CAPTURED", "Only a fulfilled order with a captured payment can be refunded.");

        var amount = existing?.Amount ?? Money(request.Amount ?? (order.CapturedAmount.Value - order.RefundedAmount));
        if (amount <= 0)
            throw Conflict("NOTHING_TO_REFUND", "The captured payment has already been refunded in full.");

        var refund = existing;
        if (refund is null)
        {
            var requestId = StableRequestId($"order:{order.PaymentReference}:refund:{request.IdempotencyKey}");
            try
            {
                refund = order.StartRefund(amount, request.IdempotencyKey, requestId);
            }
            catch (InvalidOperationException exception)
            {
                throw Conflict("REFUND_NOT_ALLOWED", exception.Message);
            }
            await _db.SaveChangesAsync(cancellationToken);
        }

        var payPalRefund = await _payPal.RefundAsync(order.CaptureId, refund.Amount, order.Currency!, order.PaymentReference!,
            refund.PayPalRequestId, cancellationToken);
        if (!payPalRefund.Currency.Equals(order.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PayPal returned a refund in a different currency than the capture.");
        order.CompleteRefund(refund, payPalRefund.Id, payPalRefund.Status, payPalRefund.Amount, payPalRefund.CreatedAt);
        await _db.SaveChangesAsync(cancellationToken);
        return ToRefundResponse(refund);
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId, SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        ValidateExpiry(request.Card.Expiry);
        var result = await _payPal.CreatePaymentTokenAsync(MapCard(request.Card), MerchantCustomerId(buyerId),
            StableRequestId($"payment-method:{buyerId}:{Guid.NewGuid():N}"), cancellationToken);
        var method = new PaymentMethod(buyerId, result.Id, result.CustomerId, result.Brand, result.Last4, result.Expiry);
        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return ToPaymentMethodResponse(method);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) =>
        await _db.PaymentMethods.AsNoTracking()
            .Where(method => method.BuyerId == buyerId && method.IsActive)
            .OrderBy(method => method.Id)
            .Select(method => new PaymentMethodResponse(method.Id, method.Brand, method.Last4, method.Expiry))
            .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"payment-method:{paymentMethodId}", cancellationToken);
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(candidate =>
            candidate.Id == paymentMethodId && candidate.BuyerId == buyerId && candidate.IsActive, cancellationToken);
        if (method is null)
            throw new PaymentWorkflowException(StatusCodes.Status404NotFound, "PAYMENT_METHOD_NOT_FOUND",
                "The saved payment method does not exist, has been removed, or belongs to another shopper.");

        try
        {
            await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PayPalApiException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // The desired processor state is already true; remove the stale local reference as well.
        }
        method.Deactivate();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MyOrderResponse>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Where(order => order.BuyerId == buyerId)
            .Include(order => order.OrderItems)
            .Include(order => order.Refunds)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(ToMyOrderResponse).ToList();
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw BadRequest("INVALID_RANGE", "The 'from' timestamp must precede 'to'.");
        var transactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _db.Orders.AsNoTracking().Include(order => order.Refunds)
            .Where(order => order.PayPalOrderId != null).ToListAsync(cancellationToken);

        var byResourceId = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            AddResource(byResourceId, order.PayPalOrderId, order);
            AddResource(byResourceId, order.AuthorizationId, order);
            AddResource(byResourceId, order.CaptureId, order);
            foreach (var refund in order.Refunds) AddResource(byResourceId, refund.PayPalRefundId, order);
        }

        var entries = transactions.Select(transaction =>
        {
            var order = MatchOrder(transaction, orders, byResourceId);
            return new ReconciliationEntryResponse(transaction.TransactionId, transaction.ReferenceId,
                transaction.EventCode, transaction.Status, transaction.InitiatedAt, transaction.Amount,
                transaction.Currency, transaction.Fee, order?.Id, order is null ? "PayPalOnly" : "Matched");
        }).ToList();

        var payPalIds = transactions.SelectMany(transaction => new[] { transaction.TransactionId, transaction.ReferenceId })
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localOnly = new List<LocalOnlyPaymentResponse>();
        foreach (var order in orders)
        {
            if (order.AuthorizationId is not null && order.AuthorizationCreatedAt is { } authorizedAt &&
                InRange(authorizedAt, from, to) && !payPalIds.Contains(order.AuthorizationId))
                localOnly.Add(new LocalOnlyPaymentResponse(order.Id, "Authorization", order.AuthorizationId,
                    authorizedAt, order.AuthorizedAmount ?? Money(order.Total()), order.Currency!));
            if (order.CaptureId is not null && order.CapturedAt is { } capturedAt &&
                InRange(capturedAt, from, to) && !payPalIds.Contains(order.CaptureId))
                localOnly.Add(new LocalOnlyPaymentResponse(order.Id, "Capture", order.CaptureId,
                    capturedAt, order.CapturedAmount ?? Money(order.Total()), order.Currency!));
            foreach (var refund in order.Refunds.Where(refund => refund.PayPalRefundId is not null &&
                         InRange(refund.CompletedAt ?? refund.CreatedAt, from, to) && !payPalIds.Contains(refund.PayPalRefundId)))
                localOnly.Add(new LocalOnlyPaymentResponse(order.Id, "Refund", refund.PayPalRefundId!,
                    refund.CompletedAt ?? refund.CreatedAt, refund.Amount, refund.Currency));
        }

        return new ReconciliationResponse(from, to, entries, localOnly);
    }

    private async Task<Order> FindOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Include(candidate => candidate.OrderItems).Include(candidate => candidate.Refunds)
            .SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
        return order ?? throw new PaymentWorkflowException(StatusCodes.Status404NotFound, "ORDER_NOT_FOUND",
            "The order does not exist.");
    }

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentWorkflowException(StatusCodes.Status404NotFound, "ORDER_NOT_FOUND",
                "The order does not exist.");
    }

    private static void EnsureProcessorMoney(decimal amount, string currency, Order order)
    {
        if (amount != Money(order.Total()) || !currency.Equals(order.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PayPal returned an amount or currency that does not match the order total.");
    }

    private string Currency
    {
        get
        {
            if (_options.Currency.Length != 3)
                throw new InvalidOperationException("PayPal:Currency must be configured as a three-character ISO-4217 code.");
            return _options.Currency.ToUpperInvariant();
        }
    }

    private static PayPalCard MapCard(CardRequest card)
    {
        ValidateExpiry(card.Expiry);
        return new PayPalCard(card.Name, card.Number.Replace(" ", string.Empty, StringComparison.Ordinal), card.Expiry,
            card.SecurityCode, new PayPalAddress(card.BillingAddress.AddressLine1,
                card.BillingAddress.AddressLine2, card.BillingAddress.City, card.BillingAddress.State,
                card.BillingAddress.PostalCode, card.BillingAddress.CountryCode));
    }

    private static void ValidateExpiry(string expiry)
    {
        if (!DateTime.TryParseExact(expiry, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            date.AddMonths(1) <= DateTime.UtcNow)
            throw BadRequest("INVALID_CARD_EXPIRY", "Card expiry must be a future month in yyyy-MM format.");
    }

    private static decimal Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string StableRequestId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"eshop-{Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant()}";
    }

    private static string MerchantCustomerId(string buyerId) => StableRequestId($"customer:{buyerId}");

    private static OrderPaymentResponse ToPaymentResponse(Order order) => new(order.Id,
        order.PaymentStatus.ToString(), order.FulfillmentStatus.ToString(), Money(order.Total()), order.Currency,
        order.PayPalOrderId, order.AuthorizationId, order.AuthorizationStatus, order.AuthorizationExpiresAt,
        order.CaptureId, order.CaptureStatus, order.CapturedAmount, order.PayPalFee, order.NetProceeds,
        order.RefundedAmount, order.Refunds.OrderBy(refund => refund.Id)
            .Select(refund => new OrderRefundResponse(refund.Id, refund.PayPalRefundId, refund.Status,
                refund.Amount, refund.Currency, refund.CreatedAt)).ToList());

    private static RefundResponse ToRefundResponse(PaymentRefund refund) => new(refund.Id,
        refund.PayPalRefundId!, refund.Status, refund.Amount, refund.Currency);

    private static PaymentMethodResponse ToPaymentMethodResponse(PaymentMethod method) =>
        new(method.Id, method.Brand, method.Last4, method.Expiry);

    private static MyOrderResponse ToMyOrderResponse(Order order) => new(order.Id, order.OrderDate,
        Money(order.Total()), order.Currency, order.PaymentStatus.ToString(), order.FulfillmentStatus.ToString(),
        order.OrderItems.Select(item => new MyOrderItemResponse(item.ItemOrdered.CatalogItemId,
            item.ItemOrdered.ProductName, item.Units, item.UnitPrice)).ToList(), ToPaymentResponse(order));

    private static void AddResource(Dictionary<string, Order> resources, string? id, Order order)
    {
        if (!string.IsNullOrWhiteSpace(id)) resources[id] = order;
    }

    private static Order? MatchOrder(PayPalTransaction transaction, IReadOnlyList<Order> orders,
        IReadOnlyDictionary<string, Order> resources)
    {
        if (resources.TryGetValue(transaction.TransactionId, out var order)) return order;
        if (transaction.ReferenceId is not null && resources.TryGetValue(transaction.ReferenceId, out order)) return order;
        var reference = transaction.InvoiceId ?? transaction.CustomField;
        return reference is null ? null : orders.SingleOrDefault(candidate =>
            string.Equals(candidate.PaymentReference, reference, StringComparison.OrdinalIgnoreCase));
    }

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) => value >= from && value <= to;
    private static PaymentWorkflowException BadRequest(string code, string message) =>
        new(StatusCodes.Status400BadRequest, code, message);
    private static PaymentWorkflowException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);
}
