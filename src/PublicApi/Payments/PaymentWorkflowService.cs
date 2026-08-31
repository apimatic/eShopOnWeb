using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentWorkflowService
{
    private readonly CatalogContext _context;
    private readonly IPayPalPaymentGateway _paypal;
    private readonly PayPalSettings _settings;
    private readonly PaymentOperationLock _operationLock;

    public PaymentWorkflowService(CatalogContext context, IPayPalPaymentGateway paypal,
        IOptions<PayPalSettings> settings, PaymentOperationLock operationLock)
    {
        _context = context;
        _paypal = paypal;
        _settings = settings.Value;
        _operationLock = operationLock;
    }

    public async Task<CreateOrderResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
            throw BadRequest("EMPTY_ORDER", "At least one catalog item is required.");
        var quantities = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(item => item.Quantity));
        if (quantities.Any(x => x.Value is < 1 or > 100))
            throw BadRequest("INVALID_QUANTITY", "Each combined catalog item quantity must be between 1 and 100.");

        var ids = quantities.Keys.ToArray();
        var catalogItems = await _context.CatalogItems.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = ids.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0)
            throw BadRequest("CATALOG_ITEM_NOT_FOUND", $"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, quantities[item.Id])).ToList();
        var address = new Address(request.ShippingAddress.Street, request.ShippingAddress.City,
            request.ShippingAddress.State, request.ShippingAddress.Country, request.ShippingAddress.ZipCode);
        var order = new Order(buyerId, address, orderItems);
        var total = order.Total();
        EnsureCentAmount(total);

        IDbContextTransaction? transaction = null;
        if (_context.Database.IsRelational()) transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _context.Orders.Add(order);
            await _context.SaveChangesAsync(cancellationToken);
            _context.OrderPayments.Add(new OrderPayment(order.Id, buyerId, total, Currency));
            await _context.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
        return new CreateOrderResponse(order.Id, order.Status.ToString(), total, Currency);
    }

    public async Task<OrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var lease = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var (order, payment) = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (order.Status == OrderStatus.Authorized) return MapOrder(order, payment);
        if (order.Status != OrderStatus.AwaitingPayment)
            throw Conflict("ORDER_NOT_PAYABLE", $"Order {orderId} cannot be paid from {order.Status}.");

        var hasCard = request.Card is not null;
        var hasSaved = request.PaymentMethodId.HasValue;
        if (hasCard == hasSaved)
            throw BadRequest("PAYMENT_SOURCE_REQUIRED", "Provide either card details or paymentMethodId, but not both.");

        PaymentMethod? method = null;
        PayPalPaymentSource source;
        if (request.Card is not null)
        {
            source = PayPalPaymentSource.OneOff(MapCard(request.Card));
        }
        else
        {
            method = await _context.PaymentMethods.SingleOrDefaultAsync(x =>
                x.Id == request.PaymentMethodId && x.BuyerId == buyerId && x.DeletedAt == null,
                cancellationToken);
            if (method is null) throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved card was not found.");
            source = PayPalPaymentSource.Saved(method.PayPalTokenId);
        }

        var invoiceId = InvoiceId(order);
        var customId = CustomId(order);
        if (payment.PayPalOrderId is null)
        {
            var paypalOrder = await _paypal.CreateOrderAsync(payment.Amount, payment.Currency, invoiceId,
                customId, RequestId(order, "create"), cancellationToken);
            payment.RecordPayPalOrder(paypalOrder.Id, paypalOrder.Status);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var authorization = await _paypal.AuthorizeOrderAsync(payment.PayPalOrderId!, source,
            RequestId(order, "authorize"), cancellationToken);
        if (authorization.PayerActionRequired)
            throw Conflict("PAYER_ACTION_REQUIRED",
                "The issuer requires browser approval; this API supports headless card flows only.");
        ValidateMoney(authorization.Amount, authorization.Currency, payment.Amount, payment.Currency,
            "authorization");
        if (authorization.Status != "CREATED")
            throw Conflict("AUTHORIZATION_NOT_APPROVED",
                $"PayPal authorization status is {authorization.Status}; funds are not ready for fulfilment.");

        payment.RecordAuthorization(authorization.Id, authorization.Status, authorization.CreateTime,
            authorization.ExpirationTime, authorization.CardBrand ?? method?.Brand,
            authorization.CardLast4 ?? method?.Last4, method?.Id, authorization.OrderStatus);
        order.MarkAuthorized();
        await _context.SaveChangesAsync(cancellationToken);
        return MapOrder(order, payment);
    }

    public async Task<OrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var lease = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var (order, payment) = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            return MapOrder(order, payment);
        if (order.Status != OrderStatus.Authorized || payment.AuthorizationId is null)
            throw Conflict("ORDER_NOT_AUTHORIZED", $"Order {orderId} must be authorized before fulfilment.");

        if (payment.CaptureId is not null)
        {
            var currentCapture = await _paypal.GetCaptureAsync(payment.CaptureId, cancellationToken);
            ValidateMoney(currentCapture.Amount, currentCapture.Currency, payment.Amount, payment.Currency, "capture");
            payment.RecordCapture(currentCapture.Id, currentCapture.Status, currentCapture.Amount,
                currentCapture.PayPalFee, currentCapture.NetAmount, currentCapture.CreateTime);
            if (currentCapture.Status == "COMPLETED") order.MarkFulfilled(DateTimeOffset.UtcNow);
            await _context.SaveChangesAsync(cancellationToken);
            if (currentCapture.Status != "COMPLETED")
                throw Conflict("CAPTURE_NOT_COMPLETED",
                    $"PayPal capture {currentCapture.Id} is {currentCapture.Status}; retry fulfilment after it settles.");
            return MapOrder(order, payment);
        }

        var authorization = await _paypal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        if (authorization.Status is "VOIDED" or "DENIED")
            throw Conflict("AUTHORIZATION_CANNOT_BE_CAPTURED",
                $"PayPal authorization {authorization.Id} is {authorization.Status}. Ask the shopper to authorize the order again.");

        var authorizationTime = authorization.CreateTime ?? payment.AuthorizedAt;
        var expirationTime = authorization.ExpirationTime ?? payment.AuthorizationExpiresAt;
        var stale = authorizationTime.HasValue && authorizationTime.Value <= DateTimeOffset.UtcNow.AddDays(-3);
        if (stale)
        {
            if ((expirationTime.HasValue && expirationTime.Value <= DateTimeOffset.UtcNow) ||
                (authorizationTime.HasValue && authorizationTime.Value <= DateTimeOffset.UtcNow.AddDays(-29)))
                throw Conflict("AUTHORIZATION_EXPIRED",
                    $"PayPal authorization {authorization.Id} is outside its reauthorization window. Ask the shopper to authorize the order again.");
            try
            {
                authorization = await _paypal.ReauthorizeAsync(authorization.Id, payment.Amount, payment.Currency,
                    RequestId(order, "reauthorize"), cancellationToken);
            }
            catch (PayPalApiException ex) when ((int)ex.StatusCode is >= 400 and < 500)
            {
                throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                    $"PayPal could not renew authorization {authorization.Id}: {ex.Message}. Ask the shopper to authorize the order again.");
            }
            ValidateMoney(authorization.Amount, authorization.Currency, payment.Amount, payment.Currency,
                "reauthorization");
            if (authorization.Status != "CREATED")
                throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                    $"PayPal renewed authorization {authorization.Id} with status {authorization.Status}. Ask the shopper to authorize the order again.");
            payment.RefreshAuthorization(authorization.Id, authorization.Status, authorization.CreateTime,
                authorization.ExpirationTime);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var capture = await _paypal.CaptureAsync(payment.AuthorizationId!, payment.Amount, payment.Currency,
            InvoiceId(order), RequestId(order, "capture"), cancellationToken);
        ValidateMoney(capture.Amount, capture.Currency, payment.Amount, payment.Currency, "capture");
        payment.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount,
            capture.CreateTime);
        if (capture.Status == "COMPLETED") order.MarkFulfilled(DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        if (capture.Status != "COMPLETED")
            throw Conflict("CAPTURE_NOT_COMPLETED",
                $"PayPal capture {capture.Id} is {capture.Status}; retry fulfilment after it settles.");
        return MapOrder(order, payment);
    }

    public async Task<OrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var lease = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var (order, payment) = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled) return MapOrder(order, payment);
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            throw Conflict("ORDER_ALREADY_FULFILLED", "A fulfilled order must be refunded, not cancelled.");
        if (payment.AuthorizationId is not null)
        {
            var authorization = await _paypal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            if (authorization.Status != "VOIDED")
                await _paypal.VoidAsync(authorization.Id, RequestId(order, "void"), cancellationToken);
            payment.RecordVoid("VOIDED");
        }
        order.MarkCancelled(DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return MapOrder(order, payment);
    }

    public async Task<RefundResponse> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var lease = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var (order, payment) = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null) return MapRefund(existing, payment);
        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded) ||
            payment.CaptureId is null || payment.CapturedAmount is null)
            throw Conflict("ORDER_NOT_REFUNDABLE", "Only a captured, fulfilled order can be refunded.");

        var remaining = payment.CapturedAmount.Value - payment.RefundedAmount;
        var amount = request.Amount ?? remaining;
        EnsureCentAmount(amount);
        if (amount <= 0 || amount > remaining)
            throw Conflict("REFUND_EXCEEDS_CAPTURE",
                $"Refund amount must be positive and no more than {remaining.ToString("0.00", CultureInfo.InvariantCulture)} {payment.Currency}.");

        var refund = await _paypal.RefundAsync(payment.CaptureId, amount, payment.Currency,
            $"refund-{ShortHash(request.IdempotencyKey)}", RefundRequestId(order, request.IdempotencyKey),
            cancellationToken);
        ValidateMoney(refund.Amount, refund.Currency, amount, payment.Currency, "refund");
        if (refund.Status is not ("COMPLETED" or "PENDING"))
            throw Conflict("REFUND_NOT_ACCEPTED", $"PayPal refund {refund.Id} is {refund.Status}.");
        var entity = payment.AddRefund(request.IdempotencyKey, refund.Id, refund.Status, refund.Amount,
            refund.CreateTime);
        order.MarkRefunded(payment.RefundedAmount >= payment.CapturedAmount.Value);
        payment.SetCaptureStatus(payment.RefundedAmount >= payment.CapturedAmount.Value
            ? "REFUNDED"
            : "PARTIALLY_REFUNDED");
        await _context.SaveChangesAsync(cancellationToken);
        return MapRefund(entity, payment);
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId, CardRequest card,
        CancellationToken cancellationToken)
    {
        using var lease = await _operationLock.AcquireAsync($"vault:{buyerId}", cancellationToken);
        var nonce = Guid.NewGuid().ToString("N");
        var result = await _paypal.VaultCardAsync(MapCard(card), MerchantCustomerId(buyerId),
            $"eshop-setup-{nonce}", $"eshop-token-{nonce}", cancellationToken);
        var method = new PaymentMethod(buyerId, result.Id, result.CustomerId, result.Brand, result.Last4,
            result.Expiry);
        _context.PaymentMethods.Add(method);
        await _context.SaveChangesAsync(cancellationToken);
        return MapMethod(method);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) => await _context.PaymentMethods.AsNoTracking()
        .Where(x => x.BuyerId == buyerId && x.DeletedAt == null).OrderByDescending(x => x.CreatedAt)
        .Select(x => new PaymentMethodResponse(x.Id, x.Brand, x.Last4, x.Expiry, x.CreatedAt))
        .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        using var lease = await _operationLock.AcquireAsync($"vault:{buyerId}", cancellationToken);
        var method = await _context.PaymentMethods.SingleOrDefaultAsync(x =>
            x.Id == paymentMethodId && x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
        if (method is null) throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved card was not found.");
        await _paypal.DeletePaymentTokenAsync(method.PayPalTokenId, cancellationToken);
        method.MarkDeleted();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _context.Orders.AsNoTracking().Include(x => x.OrderItems)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToArray();
        var payments = await _context.OrderPayments.AsNoTracking().Include(x => x.Refunds)
            .Where(x => orderIds.Contains(x.OrderId)).ToDictionaryAsync(x => x.OrderId, cancellationToken);
        return orders.Select(x => MapOrder(x, payments[x.Id])).ToList();
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw BadRequest("INVALID_DATE_RANGE", "from must be earlier than to.");
        var allTransactions = new List<PayPalTransaction>();
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(30) < to ? windowStart.AddDays(30) : to;
            for (var page = 1; ; page++)
            {
                var result = await _paypal.SearchTransactionsAsync(windowStart, windowEnd, page, 500,
                    cancellationToken);
                allTransactions.AddRange(result.Transactions);
                if ((result.TotalPages.HasValue && page >= result.TotalPages.Value) ||
                    (!result.TotalPages.HasValue && result.Transactions.Count < 500)) break;
            }
            windowStart = windowEnd;
        }
        allTransactions = allTransactions.GroupBy(x => new { x.TransactionId, x.EventCode, x.InitiatedAt })
            .Select(x => x.First()).ToList();

        var orders = await _context.Orders.AsNoTracking().Where(x => x.OrderDate >= from && x.OrderDate <= to)
            .ToListAsync(cancellationToken);
        var ids = orders.Select(x => x.Id).ToArray();
        var payments = await _context.OrderPayments.AsNoTracking().Include(x => x.Refunds)
            .Where(x => ids.Contains(x.OrderId)).ToDictionaryAsync(x => x.OrderId, cancellationToken);
        var byInvoice = orders.ToDictionary(InvoiceId, x => x);
        var transactionRows = allTransactions.Select(transaction =>
        {
            Order? order = null;
            if (transaction.InvoiceId is not null) byInvoice.TryGetValue(transaction.InvoiceId, out order);
            order ??= orders.FirstOrDefault(x => payments[x.Id].PayPalOrderId == transaction.ReferenceId ||
                payments[x.Id].AuthorizationId == transaction.TransactionId ||
                payments[x.Id].CaptureId == transaction.TransactionId ||
                payments[x.Id].Refunds.Any(r => r.PayPalRefundId == transaction.TransactionId));
            return new ReconciliationTransactionResponse(transaction.TransactionId, transaction.ReferenceId,
                transaction.EventCode, transaction.InitiatedAt, transaction.Amount, transaction.Fee,
                transaction.Currency, transaction.Status, transaction.InvoiceId, order?.Id);
        }).ToList();
        var orderRows = orders.Select(order =>
        {
            var payment = payments[order.Id];
            var match = transactionRows.Any(x => x.OrderId == order.Id);
            return new ReconciliationOrderResponse(order.Id, InvoiceId(order), order.Status.ToString(),
                payment.Amount, payment.Currency, payment.PayPalOrderId, payment.AuthorizationId,
                payment.CaptureId, payment.Refunds.Select(x => x.PayPalRefundId).ToList(), match);
        }).ToList();
        return new ReconciliationResponse(from, to, transactionRows, orderRows,
            transactionRows.Where(x => x.OrderId is null).Select(x => x.TransactionId).ToList(),
            orderRows.Where(x => !x.HasMatchingPayPalTransaction && x.PayPalOrderId is not null)
                .Select(x => x.OrderId).ToList());
    }

    private async Task<(Order Order, OrderPayment Payment)> GetOwnedOrderAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        var result = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(result.Order.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
            throw NotFound("ORDER_NOT_FOUND", "The order was not found.");
        return result;
    }

    private async Task<(Order Order, OrderPayment Payment)> GetOrderAsync(int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _context.Orders.Include(x => x.OrderItems).SingleOrDefaultAsync(x => x.Id == orderId,
            cancellationToken);
        if (order is null) throw NotFound("ORDER_NOT_FOUND", "The order was not found.");
        var payment = await _context.OrderPayments.Include(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.OrderId == orderId, cancellationToken);
        if (payment is null) throw new PaymentWorkflowException(500, "PAYMENT_STATE_MISSING",
            "The order has no payment state.");
        return (order, payment);
    }

    private string Currency => _settings.Currency.ToUpperInvariant();
    private static string InvoiceId(Order order) => $"ESHOP-{order.PaymentReference:N}";
    private static string CustomId(Order order) => $"eshop-order-{order.PaymentReference:N}";
    private static string RequestId(Order order, string action) => $"eshop-{action}-{order.PaymentReference:N}";
    private static string RefundRequestId(Order order, string key) =>
        $"eshop-refund-{order.PaymentReference:N}-{ShortHash(key)}";
    private static string ShortHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    private static string MerchantCustomerId(string buyerId) => $"eshop_{ShortHash(buyerId)}";

    private static PayPalCard MapCard(CardRequest card) => new(card.Number, card.Expiry, card.SecurityCode,
        card.Name, new PayPalAddress(card.BillingAddress.AddressLine1, card.BillingAddress.AddressLine2,
            card.BillingAddress.City, card.BillingAddress.State, card.BillingAddress.PostalCode,
            card.BillingAddress.CountryCode));

    private static PaymentMethodResponse MapMethod(PaymentMethod method) =>
        new(method.Id, method.Brand, method.Last4, method.Expiry, method.CreatedAt);

    private static OrderResponse MapOrder(Order order, OrderPayment payment)
    {
        var refunds = payment.Refunds.OrderBy(x => x.CreatedAt).Select(x =>
            new RefundStateResponse(x.PayPalRefundId, x.Status, x.Amount, x.IdempotencyKey, x.CreatedAt)).ToList();
        var state = new PaymentStateResponse(payment.PayPalOrderId, payment.PayPalOrderStatus,
            payment.AuthorizationId, payment.AuthorizationStatus, payment.AuthorizationExpiresAt,
            payment.CaptureId, payment.CaptureStatus, payment.CapturedAmount, payment.PayPalFee,
            payment.NetAmount, payment.RefundedAmount, payment.Currency, payment.CardBrand,
            payment.CardLast4, refunds);
        var items = order.OrderItems.Select(x => new OrderLineResponse(x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName, x.Units, x.UnitPrice)).ToList();
        return new OrderResponse(order.Id, order.OrderDate, order.Status.ToString(), order.Total(), items, state);
    }

    private static RefundResponse MapRefund(PaymentRefund refund, OrderPayment payment) =>
        new(refund.PayPalRefundId, refund.Status, refund.Amount, payment.RefundedAmount,
            Math.Max(0, (payment.CapturedAmount ?? 0) - payment.RefundedAmount));

    private static void EnsureCentAmount(decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            throw BadRequest("INVALID_AMOUNT", "Amounts must be positive and have at most two decimal places.");
    }

    private static void ValidateMoney(decimal actualAmount, string actualCurrency, decimal expectedAmount,
        string expectedCurrency, string operation)
    {
        if (actualAmount != expectedAmount || !actualCurrency.Equals(expectedCurrency,
                StringComparison.OrdinalIgnoreCase))
            throw new PaymentWorkflowException(502, "PAYPAL_AMOUNT_MISMATCH",
                $"PayPal reported a different {operation} amount or currency; no local transition was applied.");
    }

    private static PaymentWorkflowException BadRequest(string code, string message) => new(400, code, message);
    private static PaymentWorkflowException NotFound(string code, string message) => new(404, code, message);
    private static PaymentWorkflowException Conflict(string code, string message) => new(409, code, message);
}
