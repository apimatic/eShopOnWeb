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
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentWorkflowService
{
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PaymentOperationLock _locks;

    public PaymentWorkflowService(CatalogContext db, IPayPalClient payPal, PaymentOperationLock locks)
    {
        _db = db;
        _payPal = payPal;
        _locks = locks;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var requestedItems = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new { CatalogItemId = x.Key, Quantity = x.Sum(y => y.Quantity) })
            .ToList();
        if (requestedItems.Count == 0 || requestedItems.Any(x => x.Quantity <= 0))
            throw BadRequest("At least one catalog item with a positive quantity is required.");

        var ids = requestedItems.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.AsNoTracking()
            .Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var missingIds = ids.Where(x => !catalogItems.ContainsKey(x)).ToArray();
        if (missingIds.Length > 0)
            throw BadRequest($"Catalog item(s) not found: {string.Join(", ", missingIds)}.");

        var address = request.ShippingAddress;
        var orderItems = requestedItems.Select(x =>
        {
            var item = catalogItems[x.CatalogItemId];
            return new OrderItem(new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, x.Quantity);
        }).ToList();
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return new CreateOrderResponse(order.Id, order.PaymentStatus.ToString(), order.Total(), _payPal.Currency);
    }

    public async Task<PayOrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw BadRequest("Provide exactly one of card or paymentMethodId.");

        await using var lease = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await FindOrderAsync(orderId, buyerId, cancellationToken);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled)
            throw Conflict("A cancelled order cannot be paid.");
        if (order.Payment?.AuthorizationId is not null)
        {
            var current = await _payPal.GetAuthorizationAsync(order.Payment.AuthorizationId, cancellationToken);
            ValidateAmount(current.Amount, current.Currency, order.Total());
            order.Payment.RecordAuthorization(current.Id, current.Status, current.Amount, current.CreateTime,
                current.UpdateTime, current.ExpirationTime, order.Payment.PaymentMethodId);
            if (current.Status == "CREATED") order.MarkAuthorized();
            await _db.SaveChangesAsync(cancellationToken);
            if (current.Status != "CREATED")
                throw Conflict($"PayPal authorization is {current.Status}; use another payment method if it does not resolve.");
            return new PayOrderResponse(order.Id, MapPayment(order));
        }

        PaymentMethod? savedMethod = null;
        if (request.PaymentMethodId is not null)
        {
            savedMethod = await _db.PaymentMethods
                .SingleOrDefaultAsync(x => x.Id == request.PaymentMethodId.Value && x.Buyer.IdentityGuid == buyerId,
                    cancellationToken);
            if (savedMethod is null) throw NotFound("Saved payment method was not found.");
        }

        var payment = order.StartPayment(_payPal.Currency);
        if (payment.PayPalOrderId is null)
        {
            var paypalOrder = await _payPal.CreateOrderAsync(order.PaymentReference, order.Total(),
                $"eshop-{order.PaymentReference}-create", cancellationToken);
            payment.RecordPayPalOrder(paypalOrder.Id, paypalOrder.Status);
            await _db.SaveChangesAsync(cancellationToken);
        }

        PayPalAuthorizationResult authorization;
        var authorizationRequestId = $"eshop-{order.PaymentReference}-authorize-{payment.AuthorizationAttempt}";
        try
        {
            authorization = request.Card is not null
                ? await _payPal.AuthorizeOrderAsync(payment.PayPalOrderId!, MapCard(request.Card),
                    authorizationRequestId, cancellationToken)
                : await _payPal.AuthorizeOrderAsync(payment.PayPalOrderId!, savedMethod!.CardId,
                    authorizationRequestId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex is not PayPalChallengeRequiredException &&
                                            (int)ex.StatusCode is >= 400 and < 500)
        {
            payment.AdvanceAuthorizationAttempt();
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
        ValidateAmount(authorization.Amount, authorization.Currency, order.Total());
        payment.RecordAuthorization(authorization.Id, authorization.Status, authorization.Amount,
            authorization.CreateTime, authorization.UpdateTime, authorization.ExpirationTime, savedMethod?.Id);
        if (authorization.Status == "CREATED") order.MarkAuthorized();
        await _db.SaveChangesAsync(cancellationToken);
        if (authorization.Status != "CREATED")
            throw Conflict($"PayPal authorization is {authorization.Status}; the order was not made ready for fulfilment.");
        return new PayOrderResponse(order.Id, MapPayment(order));
    }

    public async Task<PayOrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var lease = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await FindOrderAsync(orderId, null, cancellationToken);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled)
            throw Conflict("A cancelled order cannot be fulfilled.");
        var payment = order.Payment ?? throw Conflict("The order has not been paid.");
        if (payment.AuthorizationId is null) throw Conflict("The order has no PayPal authorization.");

        if (payment.CaptureId is not null)
        {
            var existingCapture = await _payPal.GetCaptureAsync(payment.CaptureId, cancellationToken);
            RecordCapture(order, existingCapture);
            await _db.SaveChangesAsync(cancellationToken);
            EnsureCaptureCompleted(existingCapture);
            return new PayOrderResponse(order.Id, MapPayment(order));
        }

        var authorization = await _payPal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        ValidateAmount(authorization.Amount, authorization.Currency, order.Total());
        payment.RecordAuthorization(authorization.Id, authorization.Status, authorization.Amount,
            authorization.CreateTime, authorization.UpdateTime, authorization.ExpirationTime, payment.PaymentMethodId);
        if (authorization.Status != "CREATED")
            throw Conflict($"PayPal authorization is {authorization.Status}; it cannot be captured.");

        var now = DateTimeOffset.UtcNow;
        var originalCreatedAt = authorization.CreateTime ?? payment.AuthorizationCreatedAt;
        var currentHonorStart = authorization.UpdateTime ?? originalCreatedAt;
        if (originalCreatedAt is not null && now >= originalCreatedAt.Value.AddDays(29))
            throw Conflict("The PayPal authorization is too old to renew. Ask the shopper to pay the order again.");
        if (currentHonorStart is not null && now >= currentHonorStart.Value.AddDays(3))
        {
            authorization = await _payPal.ReauthorizeAsync(payment.AuthorizationId, order.Total(),
                $"eshop-{order.PaymentReference}-reauthorize-{now:yyyyMMdd}", cancellationToken);
            ValidateAmount(authorization.Amount, authorization.Currency, order.Total());
            payment.RecordAuthorization(authorization.Id, authorization.Status, authorization.Amount,
                authorization.CreateTime, authorization.UpdateTime, authorization.ExpirationTime, payment.PaymentMethodId);
            await _db.SaveChangesAsync(cancellationToken);
            if (authorization.Status != "CREATED")
                throw Conflict($"PayPal could not renew the authorization (status {authorization.Status}). Ask the shopper to pay again.");
        }

        var capture = await _payPal.CaptureAsync(payment.AuthorizationId, order.Total(),
            $"eshop-{order.PaymentReference}-capture", cancellationToken);
        RecordCapture(order, capture);
        await _db.SaveChangesAsync(cancellationToken);
        EnsureCaptureCompleted(capture);
        return new PayOrderResponse(order.Id, MapPayment(order));
    }

    public async Task<PayOrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var lease = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await FindOrderAsync(orderId, null, cancellationToken);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled)
            return new PayOrderResponse(order.Id, MapPayment(order));
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Fulfilled || order.Payment?.CaptureId is not null)
            throw Conflict("A captured order cannot be cancelled; refund it instead.");

        if (order.Payment?.AuthorizationId is not null)
        {
            var authorization = await _payPal.GetAuthorizationAsync(order.Payment.AuthorizationId, cancellationToken);
            if (authorization.Status == "CAPTURED")
                throw Conflict("PayPal reports this payment as captured; refund it instead.");
            if (authorization.Status != "VOIDED")
                await _payPal.VoidAsync(order.Payment.AuthorizationId, $"eshop-{order.PaymentReference}-void", cancellationToken);
            order.Payment.RecordVoid("VOIDED");
        }

        order.MarkCancelled(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return new PayOrderResponse(order.Id, MapPayment(order));
    }

    public async Task<RefundOrderResponse> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw BadRequest("idempotencyKey is required.");
        var keyHash = Hash(request.IdempotencyKey.Trim());
        await using var lease = await _locks.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await FindOrderAsync(orderId, buyerId, cancellationToken);
        var payment = order.Payment ?? throw Conflict("The order has no payment to refund.");
        if (order.FulfilmentStatus != OrderFulfilmentStatus.Fulfilled || payment.CaptureId is null ||
            payment.CapturedAmount is null)
            throw Conflict("Only a fulfilled, captured order can be refunded.");

        var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKeyHash == keyHash);
        if (existing is not null)
            return MapRefund(order, existing);

        var remaining = payment.CapturedAmount.Value - payment.RefundedAmount;
        var amount = request.Amount ?? remaining;
        if (amount <= 0) throw BadRequest("Refund amount must be greater than zero.");
        if (amount > remaining)
            throw Conflict($"Only {remaining.ToString("0.00", CultureInfo.InvariantCulture)} {payment.Currency} remains refundable.");

        var paypalRefund = await _payPal.RefundAsync(payment.CaptureId,
            request.Amount is null ? null : amount, $"eshop-refund-{keyHash}", cancellationToken);
        if (paypalRefund.Amount != amount)
            throw new PaymentApiException(502, "PayPal returned a refund amount different from the requested amount.");
        var refund = payment.AddRefund(paypalRefund.Id, keyHash, paypalRefund.Status, paypalRefund.Amount,
            paypalRefund.CreateTime ?? DateTimeOffset.UtcNow);
        if (paypalRefund.Status is "COMPLETED" or "PENDING") order.MarkRefunded(payment.RefundedAmount);
        await _db.SaveChangesAsync(cancellationToken);
        return MapRefund(order, refund);
    }

    public async Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.OrderItems)
            .Include(x => x.Payment)!.ThenInclude(x => x!.Refunds)
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(MapOrder).ToList();
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId, CardRequest request,
        CancellationToken cancellationToken)
    {
        await using var lease = await _locks.AcquireAsync($"buyer:{buyerId}", cancellationToken);
        var customerId = $"eshop-{Hash(buyerId)[..58]}";
        var requestId = $"eshop-vault-{Guid.NewGuid():N}";
        var token = await _payPal.CreatePaymentTokenAsync(customerId, MapCard(request), requestId, cancellationToken);
        var buyer = await _db.Buyers.Include(x => x.PaymentMethods)
            .SingleOrDefaultAsync(x => x.IdentityGuid == buyerId, cancellationToken);
        if (buyer is null)
        {
            buyer = new Buyer(buyerId);
            _db.Buyers.Add(buyer);
        }

        var existing = buyer.PaymentMethods.SingleOrDefault(x => x.CardId == token.Id);
        if (existing is not null) return MapPaymentMethod(existing);

        var paymentMethod = buyer.AddPaymentMethod(token.Id, token.Brand, token.LastDigits, token.Expiry);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            try { await _payPal.DeletePaymentTokenAsync(token.Id, CancellationToken.None); } catch { }
            throw;
        }
        return MapPaymentMethod(paymentMethod);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        return await _db.PaymentMethods.AsNoTracking()
            .Where(x => x.Buyer.IdentityGuid == buyerId)
            .OrderBy(x => x.Id)
            .Select(x => new PaymentMethodResponse(x.Id, x.Brand, x.Last4, x.Expiry))
            .ToListAsync(cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        await using var lease = await _locks.AcquireAsync($"buyer:{buyerId}", cancellationToken);
        var method = await _db.PaymentMethods
            .SingleOrDefaultAsync(x => x.Id == paymentMethodId && x.Buyer.IdentityGuid == buyerId,
                cancellationToken);
        if (method is null) throw NotFound("Saved payment method was not found.");
        try
        {
            await _payPal.DeletePaymentTokenAsync(method.CardId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        _db.PaymentMethods.Remove(method);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from) throw BadRequest("to must be greater than or equal to from.");
        var paypal = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.Payment)!.ThenInclude(x => x!.Refunds)
            .Where(x => x.Payment != null &&
                ((x.Payment.CapturedAt >= from && x.Payment.CapturedAt <= to) ||
                 x.Payment.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to)))
            .ToListAsync(cancellationToken);

        var local = new List<LocalMovement>();
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            if (payment.CaptureId is not null && payment.CapturedAt >= from && payment.CapturedAt <= to)
                local.Add(new LocalMovement(order.Id, order.PaymentReference, "Capture", payment.CaptureId, payment.PayPalOrderId,
                    payment.CapturedAt, payment.CapturedAmount, payment.Currency));
            local.AddRange(payment.Refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
                .Select(x => new LocalMovement(order.Id, order.PaymentReference, "Refund", x.PayPalRefundId, payment.CaptureId,
                    x.CreatedAt, x.Amount, payment.Currency)));
        }

        var entries = new List<ReconciliationEntryResponse>();
        var matchedLocalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transaction in paypal.Transactions)
        {
            var match = local.FirstOrDefault(x => x.PayPalId == transaction.TransactionId ||
                x.PayPalId == transaction.PayPalReferenceId || x.ReferenceId == transaction.TransactionId ||
                x.ReferenceId == transaction.PayPalReferenceId);
            if (match is null && TryPaymentReference(transaction.InvoiceId, out var paymentReference))
                match = local.FirstOrDefault(x => x.PaymentReference == paymentReference);
            if (match is not null) matchedLocalIds.Add(match.PayPalId);
            entries.Add(new ReconciliationEntryResponse(match is null ? "PayPalOnly" : "Matched", "PayPal",
                match?.OrderId, transaction.TransactionId, transaction.PayPalReferenceId, transaction.EventCode,
                transaction.Status, transaction.InitiatedAt, transaction.Amount, transaction.Currency,
                transaction.Fee, match?.Type));
        }

        entries.AddRange(local.Where(x => !matchedLocalIds.Contains(x.PayPalId)).Select(x =>
            new ReconciliationEntryResponse("EShopOnly", "EShop", x.OrderId, x.PayPalId, x.ReferenceId,
                null, null, x.OccurredAt, x.Amount, x.Currency, null, x.Type)));
        return new ReconciliationResponse(from, to, paypal.LastRefreshedAt, entries);
    }

    private async Task<Order> FindOrderAsync(int orderId, string? buyerId, CancellationToken cancellationToken)
    {
        var query = _db.Orders.Include(x => x.OrderItems)
            .Include(x => x.Payment)!.ThenInclude(x => x!.Refunds)
            .Where(x => x.Id == orderId);
        if (buyerId is not null) query = query.Where(x => x.BuyerId == buyerId);
        return await query.SingleOrDefaultAsync(cancellationToken) ?? throw NotFound("Order was not found.");
    }

    private void RecordCapture(Order order, PayPalCaptureResult capture)
    {
        ValidateAmount(capture.Amount, capture.Currency, order.Total());
        order.Payment!.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.Net,
            capture.CreateTime);
        if (capture.Status == "COMPLETED") order.MarkFulfilled(DateTimeOffset.UtcNow);
    }

    private static void EnsureCaptureCompleted(PayPalCaptureResult capture)
    {
        if (capture.Status != "COMPLETED")
            throw Conflict($"PayPal capture is {capture.Status}. Retry fulfilment after the operator resolves that status.");
    }

    private void ValidateAmount(decimal amount, string currency, decimal expected)
    {
        if (amount != expected || !string.Equals(currency, _payPal.Currency, StringComparison.OrdinalIgnoreCase))
            throw new PaymentApiException(502,
                "PayPal returned an amount or currency that does not match the eShop order total.");
    }

    private PaymentStateResponse MapPayment(Order order)
    {
        var payment = order.Payment;
        return new PaymentStateResponse(order.PaymentStatus.ToString(), payment?.Currency ?? _payPal.Currency,
            payment?.PayPalOrderId, payment?.AuthorizationId, payment?.AuthorizationStatus,
            payment?.AuthorizedAmount, payment?.AuthorizationExpiresAt, payment?.CaptureId,
            payment?.CaptureStatus, payment?.CapturedAmount, payment?.PayPalFee, payment?.NetAmount,
            payment?.RefundedAmount ?? 0,
            payment?.Refunds.OrderBy(x => x.CreatedAt)
                .Select(x => new RefundStateResponse(x.PayPalRefundId, x.Status, x.Amount, x.CreatedAt)).ToList()
                ?? new List<RefundStateResponse>());
    }

    private OrderResponse MapOrder(Order order) => new(order.Id, order.OrderDate, order.Total(),
        order.FulfilmentStatus.ToString(), order.OrderItems.Select(x =>
            new OrderItemResponse(x.ItemOrdered.CatalogItemId, x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
        MapPayment(order));

    private static RefundOrderResponse MapRefund(Order order, PaymentRefund refund)
    {
        var payment = order.Payment!;
        return new RefundOrderResponse(refund.PayPalRefundId, order.Id, refund.Status, refund.Amount,
            payment.RefundedAmount, payment.CapturedAmount!.Value - payment.RefundedAmount);
    }

    private static PaymentMethodResponse MapPaymentMethod(PaymentMethod method) =>
        new(method.Id, method.Brand, method.Last4, method.Expiry);

    private static PayPalCard MapCard(CardRequest card) => new(card.Name, card.Number.Replace(" ", string.Empty),
        card.Expiry, card.SecurityCode,
        new PayPalBillingAddress(card.BillingAddress.AddressLine1, card.BillingAddress.AddressLine2,
            card.BillingAddress.City, card.BillingAddress.State, card.BillingAddress.PostalCode,
            card.BillingAddress.CountryCode));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool TryPaymentReference(string? invoiceId, out string paymentReference)
    {
        paymentReference = invoiceId?.StartsWith("ESHOP-", StringComparison.OrdinalIgnoreCase) == true
            ? invoiceId[6..] : string.Empty;
        return paymentReference.Length == 32;
    }

    private static PaymentApiException BadRequest(string message) => new(400, message);
    private static PaymentApiException NotFound(string message) => new(404, message);
    private static PaymentApiException Conflict(string message) => new(409, message);

    private sealed record LocalMovement(int OrderId, string PaymentReference, string Type, string PayPalId, string? ReferenceId,
        DateTimeOffset? OccurredAt, decimal? Amount, string Currency);
}
