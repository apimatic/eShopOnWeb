using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CommercePaymentService
{
    private static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;

    public CommercePaymentService(CatalogContext db, IPayPalClient payPal, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        var requestedItems = request.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(i => (long)i.Quantity) })
            .ToList();
        if (requestedItems.Count == 0 || requestedItems.Any(i => i.Quantity is < 1 or > 1000))
        {
            throw new ArgumentException("At least one catalog item with a quantity from 1 to 1000 is required.");
        }

        var ids = requestedItems.Select(i => i.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Count)
        {
            var missing = ids.Except(catalogItems.Select(i => i.Id));
            throw new KeyNotFoundException($"Catalog item(s) {string.Join(", ", missing)} do not exist.");
        }

        var orderItems = requestedItems.Select(requested =>
        {
            var catalogItem = catalogItems.Single(i => i.Id == requested.CatalogItemId);
            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                checked((int)requested.Quantity));
        }).ToList();
        var address = request.ShippingAddress;
        var order = new Order(
            buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems);
        if (decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero) != order.Total())
        {
            throw new PaymentStateException("The catalog produced an order total with more than two decimal places; correct the catalog price before taking payment.");
        }

        order.InitializePayment(_options.Currency);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);
        if (order.Status == OrderStatus.Authorized || order.Payment?.CaptureId is not null)
        {
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment || order.Payment is null)
        {
            throw new PaymentStateException($"Order {orderId} cannot be paid while it is {order.Status}.");
        }
        if ((request.Card is null) == (request.PaymentMethodId is null))
        {
            throw new ArgumentException("Provide exactly one of card or paymentMethodId.");
        }

        string? vaultId = null;
        if (request.PaymentMethodId is not null)
        {
            var savedMethod = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
                p => p.Id == request.PaymentMethodId && p.BuyerId == buyerId,
                cancellationToken);
            if (savedMethod is null)
            {
                throw new KeyNotFoundException("The saved payment method does not exist for this shopper.");
            }
            vaultId = savedMethod.PayPalVaultId;
        }

        var authorizationAttempt = order.Payment.PrepareAuthorizationAttempt();
        await _db.SaveChangesAsync(cancellationToken);
        var result = await _payPal.AuthorizeAsync(
            order.Payment.ExternalReference,
            authorizationAttempt,
            order.Payment.Amount,
            order.Payment.Currency,
            request.Card is null ? null : ToPayPalCard(request.Card),
            vaultId,
            cancellationToken);
        EnsureMoneyMatches(order.Payment.Amount, order.Payment.Currency, result.Amount, result.Currency, "authorization");
        order.Payment.RecordAuthorization(
            result.PayPalOrderId,
            result.PayPalOrderStatus,
            result.AuthorizationId,
            result.AuthorizationStatus,
            result.Amount,
            result.CreatedAt,
            result.ExpiresAt,
            result.CardBrand,
            result.CardLastFour);
        if (result.AuthorizationStatus == "CREATED")
        {
            order.MarkAuthorized();
        }
        await _db.SaveChangesAsync(cancellationToken);
        if (result.AuthorizationStatus != "CREATED")
        {
            throw new PaymentStateException(
                $"PayPal authorization {result.AuthorizationId} is {result.AuthorizationStatus}; the order remains awaiting a usable authorization.");
        }
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            if (order.Payment?.CaptureId is not null && order.Payment.CaptureStatus != "COMPLETED")
            {
                var refreshed = await _payPal.GetCaptureAsync(order.Payment.CaptureId, cancellationToken);
                RecordCapture(order, refreshed);
                await _db.SaveChangesAsync(cancellationToken);
            }
            return order;
        }
        if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId is null)
        {
            throw new PaymentStateException($"Order {orderId} must have an authorization before it can be fulfilled.");
        }

        var payment = order.Payment;
        var authorization = await _payPal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        EnsureMoneyMatches(payment.Amount, payment.Currency, authorization.Amount, authorization.Currency, "authorization");
        payment.UpdateAuthorization(authorization.Id, authorization.Status, authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt);

        if (authorization.Status is "DENIED" or "VOIDED" ||
            authorization.ExpiresAt is not null && authorization.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            payment.RequireNewAuthorization(authorization.Status is "DENIED" or "VOIDED" ? authorization.Status : "EXPIRED");
            order.MarkAwaitingPayment();
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentStateException(
                $"PayPal authorization {authorization.Id} is {authorization.Status} or expired. Ask the shopper to call the pay endpoint again before fulfilment.");
        }
        if (authorization.Status == "PENDING")
        {
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentStateException($"PayPal authorization {authorization.Id} is still pending; retry fulfilment after PayPal clears the hold.");
        }

        if (authorization.Status is "CAPTURED" or "PARTIALLY_CAPTURED" && payment.CaptureId is null)
        {
            // Recover a capture that PayPal completed after the external call but before
            // the local transaction committed. The same request ID replays that capture.
            var recoveredCapture = await _payPal.CaptureAsync(
                payment.ExternalReference, authorization.Id, payment.Amount, payment.Currency, cancellationToken);
            EnsureMoneyMatches(payment.Amount, payment.Currency, recoveredCapture.Amount, recoveredCapture.Currency, "capture");
            RecordCapture(order, recoveredCapture);
            await _db.SaveChangesAsync(cancellationToken);
            if (recoveredCapture.Status != "COMPLETED")
            {
                throw new PaymentStateException($"PayPal capture {recoveredCapture.Id} is {recoveredCapture.Status}; retry fulfilment after PayPal completes it.");
            }
            return order;
        }

        if (authorization.CreatedAt + AuthorizationHonorPeriod <= DateTimeOffset.UtcNow)
        {
            try
            {
                authorization = await _payPal.ReauthorizeAsync(
                    payment.ExternalReference,
                    authorization.Id,
                    payment.Amount,
                    payment.Currency,
                    cancellationToken);
            }
            catch (PayPalException ex)
            {
                payment.RequireNewAuthorization("REAUTHORIZATION_FAILED");
                order.MarkAwaitingPayment();
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentStateException(
                    $"PayPal could not renew authorization {payment.AuthorizationId} ({ex.Issue ?? ex.ErrorName}, debug ID {ex.DebugId ?? "not supplied"}). Ask the shopper to call the pay endpoint again.");
            }
            EnsureMoneyMatches(payment.Amount, payment.Currency, authorization.Amount, authorization.Currency, "reauthorization");
            payment.UpdateAuthorization(authorization.Id, authorization.Status, authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt);
        }

        var capture = payment.CaptureId is null
            ? await _payPal.CaptureAsync(payment.ExternalReference, authorization.Id, payment.Amount, payment.Currency, cancellationToken)
            : await _payPal.GetCaptureAsync(payment.CaptureId, cancellationToken);
        EnsureMoneyMatches(payment.Amount, payment.Currency, capture.Amount, capture.Currency, "capture");
        RecordCapture(order, capture);
        await _db.SaveChangesAsync(cancellationToken);
        if (capture.Status != "COMPLETED")
        {
            throw new PaymentStateException($"PayPal capture {capture.Id} is {capture.Status}; the order has not been marked fulfilled. Retry after PayPal completes it.");
        }
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded || order.Payment?.CaptureId is not null)
        {
            throw new PaymentStateException($"Order {orderId} has been captured and must be refunded rather than cancelled.");
        }
        if (order.Payment?.AuthorizationId is not null && order.Payment.Status != PaymentStatus.Voided)
        {
            await _payPal.VoidAsync(order.Payment.ExternalReference, order.Payment.AuthorizationId, cancellationToken);
            order.Payment.RecordVoid("VOIDED");
        }
        order.MarkCancelled(DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(
        string buyerId,
        int orderId,
        RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);
        var payment = order.Payment;
        if (payment?.CaptureId is null || payment.CapturedAmount is null ||
            order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw new PaymentStateException($"Order {orderId} has no captured payment to refund.");
        }

        var existing = payment.Refunds.SingleOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null)
        {
            if (request.Amount is not null && request.Amount != existing.Amount)
            {
                throw new PaymentStateException("The idempotency key was already used with a different refund amount.");
            }
            return existing;
        }

        var remaining = payment.CapturedAmount.Value - payment.RefundedAmount;
        var amount = request.Amount ?? remaining;
        if (amount <= 0 || amount > remaining)
        {
            throw new PaymentStateException($"Refund amount must be positive and no more than the remaining captured amount {remaining:0.00} {payment.Currency}.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{payment.ExternalReference}:{request.IdempotencyKey}"))).ToLowerInvariant();
        var result = await _payPal.RefundAsync($"eshop-refund-{hash}", payment.CaptureId, amount, payment.Currency, cancellationToken);
        EnsureMoneyMatches(amount, payment.Currency, result.Amount, result.Currency, "refund");
        var refund = payment.AddRefund(request.IdempotencyKey, result.Id, result.Status, result.Amount, result.CreatedAt);
        order.MarkRefunded(payment.RefundedAmount >= payment.CapturedAmount.Value);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent replay can race after PayPal has already returned the same
            // idempotent refund. The unique key is authoritative for the local result.
            _db.ChangeTracker.Clear();
            var racedRefund = await _db.PaymentRefunds.SingleOrDefaultAsync(
                r => r.OrderPaymentId == payment.Id && r.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);
            if (racedRefund is not null)
            {
                return racedRefund;
            }
            throw;
        }
        return refund;
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardRequest card, CancellationToken cancellationToken)
    {
        var result = await _payPal.SaveCardAsync(buyerId, ToPayPalCard(card), cancellationToken);
        var paymentMethod = new SavedPaymentMethod(
            buyerId,
            result.VaultId,
            result.CustomerId,
            result.Brand,
            result.LastFour,
            result.Expiry);
        _db.SavedPaymentMethods.Add(paymentMethod);
        await _db.SaveChangesAsync(cancellationToken);
        return paymentMethod;
    }

    public Task<List<SavedPaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken)
        => _db.SavedPaymentMethods.Where(p => p.BuyerId == buyerId).OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var paymentMethod = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
            p => p.Id == paymentMethodId && p.BuyerId == buyerId,
            cancellationToken);
        if (paymentMethod is null)
        {
            throw new KeyNotFoundException("The saved payment method does not exist for this shopper.");
        }
        try
        {
            await _payPal.DeletePaymentTokenAsync(paymentMethod.PayPalVaultId, cancellationToken);
        }
        catch (PayPalException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // The desired PayPal state is already true; complete the local half of the delete.
        }
        _db.SavedPaymentMethods.Remove(paymentMethod);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
        => await _db.Orders
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .Include(o => o.Payment)!.ThenInclude(p => p!.Refunds)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw new ArgumentException("The reconciliation 'from' value must be earlier than 'to'.");
        }
        if (to - from > TimeSpan.FromDays(31))
        {
            throw new ArgumentException("PayPal Transaction Search supports a maximum date range of 31 days.");
        }

        var payPalTransactions = new List<PayPalTransaction>();
        DateTimeOffset? lastRefreshed = null;
        var page = 1;
        while (true)
        {
            var result = await _payPal.SearchTransactionsAsync(from, to, page, cancellationToken);
            payPalTransactions.AddRange(result.Transactions);
            lastRefreshed = result.LastRefreshedAt ?? lastRefreshed;
            if (result.TotalPages <= page || result.TotalPages == 0)
            {
                break;
            }
            page++;
        }

        var payments = await _db.OrderPayments
            .Include(p => p.Refunds)
            .Where(p =>
                p.CapturedAt >= from && p.CapturedAt <= to ||
                p.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to))
            .ToListAsync(cancellationToken);
        var orderIds = payments.ToDictionary(p => p.Id, p => p.OrderId);
        var byTransactionId = new Dictionary<string, (OrderPayment Payment, string Type)>(StringComparer.OrdinalIgnoreCase);
        var byExternalReference = payments.ToDictionary(p => p.ExternalReference, StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            if (payment.CaptureId is not null)
            {
                byTransactionId[payment.CaptureId] = (payment, "Capture");
            }
            foreach (var refund in payment.Refunds)
            {
                byTransactionId[refund.PayPalRefundId] = (payment, "Refund");
            }
        }

        var matchedLocalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<ReconciliationEntryResponse>();
        foreach (var transaction in payPalTransactions)
        {
            (OrderPayment Payment, string Type)? local = null;
            if (byTransactionId.TryGetValue(transaction.TransactionId, out var direct))
            {
                local = direct;
                matchedLocalIds.Add(transaction.TransactionId);
            }
            else if (transaction.PayPalReferenceId is not null && byTransactionId.TryGetValue(transaction.PayPalReferenceId, out var referenced))
            {
                local = referenced;
                matchedLocalIds.Add(transaction.PayPalReferenceId);
            }
            else if (transaction.CustomId is not null && byExternalReference.TryGetValue(transaction.CustomId, out var referencedPayment))
            {
                local = (referencedPayment, "Payment");
            }

            entries.Add(new ReconciliationEntryResponse(
                local is null ? "PayPalOnly" : "Matched",
                local?.Payment.OrderId,
                local?.Type,
                transaction.TransactionId,
                transaction.PayPalReferenceId,
                transaction.Status,
                transaction.EventCode,
                transaction.Amount,
                transaction.Currency,
                transaction.Fee,
                transaction.InitiatedAt));
        }

        foreach (var payment in payments)
        {
            if (payment.CaptureId is not null && payment.CapturedAt >= from && payment.CapturedAt <= to && !matchedLocalIds.Contains(payment.CaptureId))
            {
                entries.Add(new ReconciliationEntryResponse(
                    "EShopOnly", payment.OrderId, "Capture", payment.CaptureId, null, payment.CaptureStatus,
                    null, payment.CapturedAmount ?? 0, payment.Currency, payment.PayPalFee, payment.CapturedAt));
            }
            foreach (var refund in payment.Refunds.Where(r => r.CreatedAt >= from && r.CreatedAt <= to && !matchedLocalIds.Contains(r.PayPalRefundId)))
            {
                entries.Add(new ReconciliationEntryResponse(
                    "EShopOnly", payment.OrderId, "Refund", refund.PayPalRefundId, payment.CaptureId, refund.PayPalStatus,
                    null, -refund.Amount, refund.Currency, null, refund.CreatedAt));
            }
        }

        return new ReconciliationResponse(from, to, lastRefreshed, entries.OrderBy(e => e.OccurredAt).ToList());
    }

    public static OrderResponse ToResponse(Order order, string fallbackCurrency)
    {
        var payment = order.Payment;
        var paymentResponse = payment is null
            ? new PaymentResponse(PaymentStatus.AwaitingPayment.ToString(), null, null, null, null, null, null, null, null, null, null, 0, fallbackCurrency, null, null, Array.Empty<RefundDetailResponse>())
            : new PaymentResponse(
                payment.Status.ToString(), payment.PayPalOrderId, payment.AuthorizationId, payment.AuthorizationStatus,
                payment.AuthorizedAmount, payment.AuthorizationExpiresAt, payment.CaptureId, payment.CaptureStatus,
                payment.CapturedAmount, payment.PayPalFee, payment.NetAmount, payment.RefundedAmount, payment.Currency,
                payment.CardBrand, payment.CardLastFour,
                payment.Refunds.Select(r => new RefundDetailResponse(r.Id, r.PayPalRefundId, r.PayPalStatus, r.Amount, r.Currency, r.CreatedAt)).ToList());
        return new OrderResponse(
            order.Id,
            order.OrderDate,
            order.Status.ToString(),
            order.Total(),
            order.OrderItems.Select(i => new OrderItemResponse(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.Units, i.UnitPrice)).ToList(),
            paymentResponse);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Payment)!.ThenInclude(p => p!.Refunds)
            .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        return order ?? throw new KeyNotFoundException($"Order {orderId} does not exist.");
    }

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Deliberately indistinguishable from a missing order to prevent ownership probing.
            throw new KeyNotFoundException($"Order {order.Id} does not exist.");
        }
    }

    private static void RecordCapture(Order order, PayPalCapture capture)
    {
        order.Payment!.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount, capture.CreatedAt);
        if (capture.Status == "COMPLETED")
        {
            order.MarkFulfilled(DateTimeOffset.UtcNow);
        }
    }

    private static void EnsureMoneyMatches(decimal expectedAmount, string expectedCurrency, decimal actualAmount, string actualCurrency, string operation)
    {
        if (expectedAmount != actualAmount || !string.Equals(expectedCurrency, actualCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentStateException(
                $"PayPal {operation} amount {actualAmount:0.00} {actualCurrency} did not match order total {expectedAmount:0.00} {expectedCurrency}.");
        }
    }

    private static PayPalCard ToPayPalCard(CardRequest card) => new(
        card.Name,
        card.Number,
        card.Expiry,
        card.SecurityCode,
        new PayPalAddress(
            card.BillingAddress.AddressLine1,
            card.BillingAddress.AddressLine2,
            card.BillingAddress.City,
            card.BillingAddress.State,
            card.BillingAddress.PostalCode,
            card.BillingAddress.CountryCode));
}
