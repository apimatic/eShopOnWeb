using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentApplicationService : IPaymentApplicationService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly Regex ExpiryPattern = new("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
    private readonly CatalogContext _context;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;

    public PaymentApplicationService(CatalogContext context, IPayPalClient payPal, IUriComposer uriComposer)
    {
        _context = context;
        _payPal = payPal;
        _uriComposer = uriComposer;
    }

    public async Task<int> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> items,
        ShippingAddressInput shippingAddress, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (items is null || items.Count == 0)
            throw Validation("EMPTY_ORDER", "At least one catalog item is required.");
        if (items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw Validation("INVALID_ORDER_ITEM", "Catalog item IDs and quantities must be positive.");
        ValidateShippingAddress(shippingAddress);

        var groupedItems = items.GroupBy(x => x.CatalogItemId)
            .Select(x => new OrderLineInput(x.Key, checked(x.Sum(line => line.Quantity))))
            .ToArray();
        var ids = groupedItems.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _context.CatalogItems.AsNoTracking()
            .Where(x => ids.Contains(x.Id)).ToListAsync(cancellationToken);
        var missingIds = ids.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missingIds.Length > 0)
            throw new PaymentOperationException(PaymentErrorKind.NotFound, "CATALOG_ITEM_NOT_FOUND",
                $"Catalog item {missingIds[0]} was not found.");

        var orderItems = groupedItems.Select(line =>
        {
            var catalogItem = catalogItems.Single(x => x.Id == line.CatalogItemId);
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(ordered, catalogItem.Price, line.Quantity);
        }).ToList();
        var address = new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State,
            shippingAddress.Country, shippingAddress.PostalCode);
        var order = new Order(buyerId, address, orderItems);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
        return order.Id;
    }

    public async Task<PaymentResult> PayAsync(string buyerId, int orderId, CardInput? card,
        int? paymentMethodId, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if ((card is null) == (paymentMethodId is null))
            throw Validation("PAYMENT_SOURCE_REQUIRED",
                "Provide either card details or one saved paymentMethodId, but not both.");
        if (card is not null) ValidateCard(card);

        var gate = await EnterOrderLockAsync(orderId, cancellationToken);
        try
        {
            var order = await FindOwnedOrderAsync(buyerId, orderId, cancellationToken);
            var payment = await LoadPaymentAsync(orderId, cancellationToken);
            if (order.PaymentStatus == OrderPaymentStatus.Authorized && payment?.CurrentAuthorization is not null)
                return MapPayment(order, payment);
            if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
                throw Conflict("ORDER_NOT_PAYABLE", $"An order in payment state {order.PaymentStatus} cannot be paid.");

            SavedPaymentMethod? savedMethod = null;
            if (paymentMethodId.HasValue)
            {
                savedMethod = await _context.SavedPaymentMethods.SingleOrDefaultAsync(x =>
                    x.Id == paymentMethodId.Value && x.BuyerId == buyerId && x.IsActive, cancellationToken);
                if (savedMethod is null)
                    throw new PaymentOperationException(PaymentErrorKind.NotFound, "PAYMENT_METHOD_NOT_FOUND",
                        "The saved payment method was not found or has been removed.");
            }

            var total = ToCent(order.Total());
            if (total <= 0) throw Validation("INVALID_ORDER_TOTAL", "The order total must be positive.");
            payment ??= await CreatePendingPaymentAsync(order, total, paymentMethodId, cancellationToken);
            if (payment.SavedPaymentMethodId != paymentMethodId)
                throw Conflict("PAYMENT_SOURCE_CHANGED",
                    "This payment attempt was already started with a different payment source.");

            var authorization = await CallPayPalAsync(() => _payPal.AuthorizeAsync(total,
                $"eshop-order-{order.Id}", card, savedMethod?.PayPalPaymentTokenId,
                payment.AuthorizationRequestId, cancellationToken), "authorize the order");
            if (authorization.Authorization.Status is not ("CREATED" or "PENDING"))
                throw ProcessorRejected("AUTHORIZATION_NOT_ACTIVE",
                    $"PayPal returned authorization status {authorization.Authorization.Status}.");

            payment.RecordAuthorization(authorization.OrderId, authorization.OrderStatus,
                authorization.Authorization.Id, authorization.Authorization.Status,
                authorization.Authorization.Amount, authorization.Authorization.CreatedAt,
                authorization.Authorization.ExpiresAt, false);
            order.MarkAuthorized();
            await _context.SaveChangesAsync(cancellationToken);
            return MapPayment(order, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentResult> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = await EnterOrderLockAsync(orderId, cancellationToken);
        try
        {
            var order = await FindOrderAsync(orderId, cancellationToken);
            var payment = await LoadPaymentAsync(orderId, cancellationToken)
                ?? throw Conflict("ORDER_NOT_AUTHORIZED", "The order has no payment authorization.");
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled && payment.CaptureId is not null)
                return MapPayment(order, payment);
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Canceled)
                throw Conflict("ORDER_CANCELED", "A canceled order cannot be fulfilled.");
            if (order.PaymentStatus != OrderPaymentStatus.Authorized && payment.CaptureId is null)
                throw Conflict("ORDER_NOT_AUTHORIZED", "The order must have an active authorization before fulfilment.");

            if (payment.CaptureId is not null)
            {
                var refreshedCapture = await CallPayPalAsync(
                    () => _payPal.GetCaptureAsync(payment.CaptureId, cancellationToken), "check the capture");
                payment.RecordCapture(refreshedCapture.Id, refreshedCapture.Status,
                    payment.CaptureRequestId ?? PayPalClient.StableRequestId("capture", $"{orderId}"),
                    refreshedCapture.Amount, refreshedCapture.Fee, refreshedCapture.Net,
                    refreshedCapture.CreatedAt);
                if (refreshedCapture.Status != "COMPLETED")
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    throw Conflict("CAPTURE_PENDING",
                        $"PayPal capture {refreshedCapture.Id} is {refreshedCapture.Status}; retry fulfilment after it completes.");
                }

                order.MarkFulfilled();
                await _context.SaveChangesAsync(cancellationToken);
                return MapPayment(order, payment);
            }

            var current = payment.CurrentAuthorization
                ?? throw Conflict("AUTHORIZATION_MISSING", "The stored payment has no current authorization ID.");
            var remoteAuthorization = await CallPayPalAsync(
                () => _payPal.GetAuthorizationAsync(current.PayPalId, cancellationToken),
                "check the authorization");
            payment.UpdateAuthorizationStatus(remoteAuthorization.Status);
            if (remoteAuthorization.Status is not ("CREATED" or "PENDING"))
                throw Conflict("AUTHORIZATION_NOT_ACTIVE",
                    $"PayPal authorization {current.PayPalId} is {remoteAuthorization.Status}; ask the shopper to authorize a new payment.");

            var currentForCapture = current;
            if (DateTimeOffset.UtcNow >= remoteAuthorization.CreatedAt.AddDays(3))
            {
                if (current.IsReauthorization)
                    throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                        "The authorization was already renewed and is outside its refreshed honor period; ask the shopper to authorize a new payment.");
                if (remoteAuthorization.ExpiresAt.HasValue && remoteAuthorization.ExpiresAt <= DateTimeOffset.UtcNow)
                    throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                        "The PayPal authorization has expired; ask the shopper to authorize a new payment.");

                var reauthorizeRequestId = PayPalClient.StableRequestId("reauthorize", current.PayPalId);
                PayPalAuthorizationState renewed;
                try
                {
                    renewed = await CallPayPalAsync(() => _payPal.ReauthorizeAsync(current.PayPalId,
                        payment.Amount, reauthorizeRequestId, cancellationToken), "renew the authorization");
                }
                catch (PaymentOperationException exception) when (exception.Kind == PaymentErrorKind.ProcessorRejected)
                {
                    throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                        "PayPal could not renew the authorization; ask the shopper to authorize a new payment.");
                }

                payment.RecordAuthorization(payment.PayPalOrderId!, payment.PayPalOrderStatus!, renewed.Id,
                    renewed.Status, renewed.Amount, renewed.CreatedAt, renewed.ExpiresAt, true);
                await _context.SaveChangesAsync(cancellationToken);
                currentForCapture = payment.CurrentAuthorization!;
            }

            var captureRequestId = PayPalClient.StableRequestId("capture",
                $"{order.Id}:{currentForCapture.PayPalId}");
            var capture = await CallPayPalAsync(() => _payPal.CaptureAsync(currentForCapture.PayPalId,
                payment.Amount, captureRequestId, cancellationToken), "capture the authorization");
            payment.RecordCapture(capture.Id, capture.Status, captureRequestId, capture.Amount, capture.Fee,
                capture.Net, capture.CreatedAt);
            if (capture.Status != "COMPLETED")
            {
                await _context.SaveChangesAsync(cancellationToken);
                throw Conflict("CAPTURE_PENDING",
                    $"PayPal capture {capture.Id} is {capture.Status}; retry fulfilment after it completes.");
            }

            order.MarkFulfilled();
            await _context.SaveChangesAsync(cancellationToken);
            return MapPayment(order, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentResult> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = await EnterOrderLockAsync(orderId, cancellationToken);
        try
        {
            var order = await FindOrderAsync(orderId, cancellationToken);
            var payment = await LoadPaymentAsync(orderId, cancellationToken);
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Canceled)
                return payment is null ? MapNoPayment(order) : MapPayment(order, payment);
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled || payment?.CaptureId is not null)
                throw Conflict("ORDER_ALREADY_CAPTURED", "A captured order must be refunded, not canceled.");

            var authorization = payment?.CurrentAuthorization;
            if (authorization is not null)
            {
                var voidRequestId = PayPalClient.StableRequestId("void", authorization.PayPalId);
                await CallPayPalAsync(async () =>
                {
                    await _payPal.VoidAsync(authorization.PayPalId, voidRequestId, cancellationToken);
                    return true;
                }, "void the authorization");
                payment!.UpdateAuthorizationStatus("VOIDED");
            }

            order.MarkCanceled(authorization is not null);
            await _context.SaveChangesAsync(cancellationToken);
            return payment is null ? MapNoPayment(order) : MapPayment(order, payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundResult> RefundAsync(string buyerId, int orderId, string idempotencyKey,
        decimal? amount, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 108)
            throw Validation("INVALID_IDEMPOTENCY_KEY", "idempotencyKey must contain between 1 and 108 characters.");

        var gate = await EnterOrderLockAsync(orderId, cancellationToken);
        try
        {
            var order = await FindOwnedOrderAsync(buyerId, orderId, cancellationToken);
            var payment = await LoadPaymentAsync(orderId, cancellationToken)
                ?? throw Conflict("ORDER_NOT_CAPTURED", "The order has no captured payment to refund.");
            if (payment.CaptureId is null || payment.CapturedAmount is null)
                throw Conflict("ORDER_NOT_CAPTURED", "The order has no captured payment to refund.");

            var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
            PaymentRefund refund;
            if (existing is not null)
            {
                if (amount.HasValue && ToCent(amount.Value) != existing.Amount)
                    throw Conflict("IDEMPOTENCY_KEY_REUSED",
                        "This idempotencyKey was already used with a different refund amount.");
                if (existing.Status == "COMPLETED" && existing.PayPalRefundId is not null)
                    return MapRefund(existing);
                refund = existing;
            }
            else
            {
                var remaining = payment.CapturedAmount.Value - payment.ReservedRefundAmount;
                var requested = amount.HasValue ? ToCent(amount.Value) : remaining;
                if (requested <= 0)
                    throw Validation("INVALID_REFUND_AMOUNT", "The refund amount must be positive.");
                if (requested > remaining)
                    throw Conflict("REFUND_EXCEEDS_CAPTURE",
                        $"Only {remaining.ToString("0.00", CultureInfo.InvariantCulture)} {payment.Currency} remains refundable.");
                var requestId = PayPalClient.StableRequestId("refund", $"{payment.CaptureId}:{idempotencyKey}");
                refund = payment.ReserveRefund(idempotencyKey, requestId, requested);
                await _context.SaveChangesAsync(cancellationToken);
            }

            try
            {
                var result = await CallPayPalAsync(() => _payPal.RefundAsync(payment.CaptureId, refund.Amount,
                    refund.PayPalRequestId, cancellationToken), "refund the capture");
                refund.Complete(result.Id, result.Status);
                if (result.Status == "COMPLETED")
                    order.MarkRefunded(payment.RefundedAmount, payment.CapturedAmount.Value);
                await _context.SaveChangesAsync(cancellationToken);
                return MapRefund(refund);
            }
            catch (PaymentOperationException exception) when (exception.Kind == PaymentErrorKind.ProcessorRejected)
            {
                refund.Fail();
                await _context.SaveChangesAsync(cancellationToken);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyCollection<OrderResult>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var orders = await _context.Orders.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var payments = await _context.OrderPayments.AsNoTracking().Where(x => orderIds.Contains(x.OrderId))
            .Include(x => x.Authorizations).Include(x => x.Refunds).ToListAsync(cancellationToken);
        var byOrder = payments.ToDictionary(x => x.OrderId);
        return orders.Select(order => MapOrder(order, byOrder.GetValueOrDefault(order.Id))).ToArray();
    }

    public async Task<PaymentMethodResult> SavePaymentMethodAsync(string buyerId, CardInput card,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        ValidateCard(card);
        var customerId = await _context.SavedPaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId).Select(x => x.PayPalCustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        var merchantCustomerId = MerchantCustomerId(buyerId);
        var operationId = PayPalClient.StableRequestId("vault", Guid.NewGuid().ToString("N"));
        var vaulted = await CallPayPalAsync(() => _payPal.SaveCardAsync(merchantCustomerId, customerId,
            card, operationId, cancellationToken), "save the card");
        var method = new SavedPaymentMethod(buyerId, vaulted.PaymentTokenId, vaulted.CustomerId,
            vaulted.Brand, vaulted.Last4, vaulted.Expiry, vaulted.CardholderName);
        _context.SavedPaymentMethods.Add(method);
        await _context.SaveChangesAsync(cancellationToken);
        return MapPaymentMethod(method);
    }

    public async Task<IReadOnlyCollection<PaymentMethodResult>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        return await _context.SavedPaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.IsActive).OrderBy(x => x.Id)
            .Select(x => new PaymentMethodResult(x.Id, x.Brand, x.Last4, x.Expiry, x.CardholderName))
            .ToListAsync(cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var method = await _context.SavedPaymentMethods.SingleOrDefaultAsync(x =>
            x.Id == paymentMethodId && x.BuyerId == buyerId && x.IsActive, cancellationToken);
        if (method is null)
            throw new PaymentOperationException(PaymentErrorKind.NotFound, "PAYMENT_METHOD_NOT_FOUND",
                "The saved payment method was not found.");
        await CallPayPalAsync(async () =>
        {
            await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken);
            return true;
        }, "delete the saved card");
        method.Delete();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw Validation("INVALID_DATE_RANGE", "from must be earlier than to.");
        var transactions = await CallPayPalAsync(() => _payPal.SearchTransactionsAsync(from, to,
            cancellationToken), "retrieve PayPal transactions");
        var payments = await _context.OrderPayments.AsNoTracking()
            .Include(x => x.Authorizations).Include(x => x.Refunds).ToListAsync(cancellationToken);

        var local = new List<ReconciliationLocalEntryResult>();
        var orderByPayPalId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var payment in payments)
        {
            foreach (var authorization in payment.Authorizations)
            {
                orderByPayPalId[authorization.PayPalId] = payment.OrderId;
                if (InRange(authorization.CreatedAt, from, to))
                    local.Add(new ReconciliationLocalEntryResult(payment.OrderId, "Authorization",
                        authorization.PayPalId, authorization.Status, authorization.Amount,
                        authorization.Currency, authorization.CreatedAt));
            }
            if (payment.CaptureId is not null)
            {
                orderByPayPalId[payment.CaptureId] = payment.OrderId;
                if (payment.CapturedAt.HasValue && InRange(payment.CapturedAt.Value, from, to))
                    local.Add(new ReconciliationLocalEntryResult(payment.OrderId, "Capture", payment.CaptureId,
                        payment.CaptureStatus ?? "UNKNOWN", payment.CapturedAmount ?? 0m, payment.Currency,
                        payment.CapturedAt.Value));
            }
            foreach (var refund in payment.Refunds.Where(x => x.PayPalRefundId is not null))
            {
                orderByPayPalId[refund.PayPalRefundId!] = payment.OrderId;
                if (InRange(refund.UpdatedAt, from, to))
                    local.Add(new ReconciliationLocalEntryResult(payment.OrderId, "Refund",
                        refund.PayPalRefundId!, refund.Status, refund.Amount, refund.Currency, refund.UpdatedAt));
            }
        }

        var payPalIds = transactions.SelectMany(x => new[] { x.TransactionId, x.ReferenceId })
            .Where(x => x is not null).ToHashSet(StringComparer.Ordinal);
        var paypalResults = transactions.Select(transaction =>
        {
            int? orderId = null;
            if (orderByPayPalId.TryGetValue(transaction.TransactionId, out var directOrder)) orderId = directOrder;
            else if (transaction.ReferenceId is not null &&
                     orderByPayPalId.TryGetValue(transaction.ReferenceId, out var referenceOrder))
                orderId = referenceOrder;
            return new ReconciliationTransactionResult(transaction.TransactionId, transaction.ReferenceId,
                transaction.EventCode, transaction.Status, transaction.Amount, transaction.Fee,
                transaction.Currency, transaction.InitiatedAt, orderId);
        }).ToArray();
        var localOnly = local.Where(x => !payPalIds.Contains(x.PayPalId)).ToArray();
        return new ReconciliationResult(from, to, paypalResults, localOnly);
    }

    private async Task<OrderPayment> CreatePendingPaymentAsync(Order order, decimal total,
        int? paymentMethodId, CancellationToken cancellationToken)
    {
        var requestId = PayPalClient.StableRequestId("authorize",
            $"{order.Id}:{Guid.NewGuid():N}");
        var payment = new OrderPayment(order.Id, total, _payPal.Currency, requestId, paymentMethodId);
        _context.OrderPayments.Add(payment);
        await _context.SaveChangesAsync(cancellationToken);
        return payment;
    }

    private Task<Order?> QueryOrderAsync(int orderId, CancellationToken cancellationToken) =>
        _context.Orders.Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.Id == orderId,
            cancellationToken);

    private async Task<Order> FindOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await QueryOrderAsync(orderId, cancellationToken) ??
        throw new PaymentOperationException(PaymentErrorKind.NotFound, "ORDER_NOT_FOUND", "The order was not found.");

    private async Task<Order> FindOwnedOrderAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        var order = await QueryOrderAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            throw new PaymentOperationException(PaymentErrorKind.NotFound, "ORDER_NOT_FOUND",
                "The order was not found.");
        return order;
    }

    private Task<OrderPayment?> LoadPaymentAsync(int orderId, CancellationToken cancellationToken) =>
        _context.OrderPayments.Include(x => x.Authorizations).Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);

    private OrderResult MapOrder(Order order, OrderPayment? payment) => new(order.Id, order.OrderDate,
        ToCent(order.Total()), payment?.Currency ?? _payPal.Currency, order.PaymentStatus.ToString(),
        order.FulfillmentStatus.ToString(), new ShippingAddressInput(order.ShipToAddress.Street,
            order.ShipToAddress.City, order.ShipToAddress.State, order.ShipToAddress.Country,
            order.ShipToAddress.ZipCode), order.OrderItems.Select(x => new OrderItemResult(
            x.ItemOrdered.CatalogItemId, x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToArray(),
        payment is null ? null : MapPayment(order, payment));

    private static PaymentResult MapPayment(Order order, OrderPayment payment) => new(
        order.PaymentStatus.ToString(), payment.Amount, payment.Currency, payment.PayPalOrderId,
        payment.Authorizations.OrderBy(x => x.CreatedAt).Select(x => new PaymentAuthorizationResult(
            x.PayPalId, x.Status, x.Amount, x.CreatedAt, x.ExpiresAt, x.IsReauthorization, x.IsCurrent)).ToArray(),
        payment.CaptureId, payment.CaptureStatus, payment.CapturedAmount, payment.PayPalFee,
        payment.NetAmount, payment.RefundedAmount, payment.Refunds.Where(x => x.PayPalRefundId is not null)
            .Select(MapRefund).ToArray());

    private PaymentResult MapNoPayment(Order order) => new(order.PaymentStatus.ToString(), ToCent(order.Total()),
        _payPal.Currency, null, Array.Empty<PaymentAuthorizationResult>(), null, null, null, null, null, 0m,
        Array.Empty<RefundResult>());

    private static RefundResult MapRefund(PaymentRefund refund) => new(refund.PayPalRefundId ?? string.Empty,
        refund.Status, refund.Amount, refund.Currency, refund.IdempotencyKey);

    private static PaymentMethodResult MapPaymentMethod(SavedPaymentMethod method) => new(method.Id,
        method.Brand, method.Last4, method.Expiry, method.CardholderName);

    private static async Task<SemaphoreSlim> EnterOrderLockAsync(int orderId,
        CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return gate;
    }

    private static async Task<T> CallPayPalAsync<T>(Func<Task<T>> action, string operation)
    {
        try
        {
            return await action();
        }
        catch (PayPalApiException exception) when (exception.RequiresPayerAction)
        {
            throw new PaymentOperationException(PaymentErrorKind.PayerActionRequired,
                "PAYER_ACTION_REQUIRED", exception.Message, exception);
        }
        catch (PayPalApiException exception)
        {
            var detail = exception.Issue ?? exception.Name;
            var debug = string.IsNullOrWhiteSpace(exception.DebugId) ? string.Empty : $" Debug ID: {exception.DebugId}.";
            var kind = exception.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout
                ? PaymentErrorKind.ProcessorUnavailable
                : PaymentErrorKind.ProcessorRejected;
            throw new PaymentOperationException(kind, detail,
                $"PayPal could not {operation}: {detail}.{debug}", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new PaymentOperationException(PaymentErrorKind.ProcessorUnavailable,
                "PAYPAL_UNAVAILABLE", $"PayPal was unavailable while trying to {operation}.", exception);
        }
    }

    private static void ValidateCard(CardInput card)
    {
        if (card.Number is null || card.Number.Length is < 13 or > 19 || card.Number.Any(x => !char.IsDigit(x)))
            throw Validation("INVALID_CARD_NUMBER", "Card number must contain 13 to 19 digits.");
        if (!ExpiryPattern.IsMatch(card.Expiry ?? string.Empty) ||
            !DateOnly.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry.AddMonths(1) <= DateOnly.FromDateTime(DateTime.UtcNow))
            throw Validation("INVALID_CARD_EXPIRY", "Card expiry must be a future date in YYYY-MM format.");
        if (card.SecurityCode is null || card.SecurityCode.Length is < 3 or > 4 ||
            card.SecurityCode.Any(x => !char.IsDigit(x)))
            throw Validation("INVALID_SECURITY_CODE", "securityCode must contain three or four digits.");
        if (string.IsNullOrWhiteSpace(card.Name))
            throw Validation("INVALID_CARDHOLDER_NAME", "Cardholder name is required.");
        if (card.BillingAddress is null || string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.AdminArea2) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.PostalCode) ||
            card.BillingAddress.CountryCode?.Length != 2)
            throw Validation("INVALID_BILLING_ADDRESS",
                "Billing address line 1, city, postal code, and two-letter country code are required.");
    }

    private static void ValidateShippingAddress(ShippingAddressInput address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) || string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.PostalCode))
            throw Validation("INVALID_SHIPPING_ADDRESS", "Street, city, country, and postalCode are required.");
    }

    private static void RequireBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new PaymentOperationException(PaymentErrorKind.Validation, "CALLER_IDENTITY_MISSING",
                "The bearer token does not contain a caller name.");
    }

    private static decimal ToCent(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;

    private static string MerchantCustomerId(string buyerId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant();
        return "eshop_" + hash[..40];
    }

    private static PaymentOperationException Validation(string code, string message) =>
        new(PaymentErrorKind.Validation, code, message);
    private static PaymentOperationException Conflict(string code, string message) =>
        new(PaymentErrorKind.Conflict, code, message);
    private static PaymentOperationException ProcessorRejected(string code, string message) =>
        new(PaymentErrorKind.ProcessorRejected, code, message);
}
