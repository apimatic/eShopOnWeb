using System;
using System.Collections.Concurrent;
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

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class CommercePaymentService : ICommercePaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private readonly CatalogContext _dbContext;
    private readonly IPayPalClient _payPal;

    public CommercePaymentService(CatalogContext dbContext, IPayPalClient payPal)
    {
        _dbContext = dbContext;
        _payPal = payPal;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLineData> lines,
        Address shippingAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw new PaymentOperationException("UNAUTHENTICATED", "A signed-in shopper is required.");
        if (lines.Count == 0) throw new PaymentOperationException("EMPTY_ORDER", "At least one catalog item is required.");
        if (lines.Any(line => line.CatalogItemId <= 0 || line.Quantity <= 0))
            throw new PaymentOperationException("INVALID_ORDER_ITEM", "Catalog item IDs and quantities must be positive.");

        var grouped = lines.GroupBy(line => line.CatalogItemId)
            .Select(group => new OrderLineData(group.Key, checked(group.Sum(line => line.Quantity))))
            .ToArray();
        var ids = grouped.Select(line => line.CatalogItemId).ToArray();
        var catalogItems = await _dbContext.CatalogItems.Where(item => ids.Contains(item.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
            throw new PaymentOperationException("CATALOG_ITEM_NOT_FOUND", "One or more catalog items do not exist.");

        var items = grouped.Select(line =>
        {
            var catalogItem = catalogItems.Single(item => item.Id == line.CatalogItemId);
            return new OrderItem(new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price, line.Quantity);
        }).ToList();
        var order = new Order(buyerId, shippingAddress, items);
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(int orderId, string buyerId, CardData? card,
        int? paymentMethodId, CancellationToken cancellationToken)
    {
        if ((card is null) == !paymentMethodId.HasValue)
            throw new PaymentOperationException("INVALID_PAYMENT_SOURCE", "Supply either card details or paymentMethodId, but not both.");

        return await WithOrderLock(orderId, async () =>
        {
            var order = await ShopperOrder(orderId, buyerId, cancellationToken);
            if (order.PaymentStatus != PaymentStatus.AwaitingPayment) return order;

            string? paymentToken = null;
            if (paymentMethodId.HasValue)
            {
                var method = await _dbContext.PaymentMethods.SingleOrDefaultAsync(
                    candidate => candidate.Id == paymentMethodId.Value && candidate.BuyerId == buyerId &&
                                 candidate.DeletedAt == null, cancellationToken);
                if (method is null)
                    throw new PaymentOperationException("PAYMENT_METHOD_NOT_FOUND", "The saved payment method was not found or is no longer available.");
                paymentToken = method.PayPalPaymentTokenId;
            }

            var total = CentAmount(order.Total(), "order total");
            var authorization = await _payPal.AuthorizeAsync(order.Id, order.PaymentReference, total, card, paymentToken,
                $"eshop-{order.PaymentReference}-authorize", cancellationToken);
            if (authorization.Amount != total || !authorization.Currency.Equals(_payPal.Currency, StringComparison.OrdinalIgnoreCase))
                throw new PaymentOperationException("PAYPAL_AMOUNT_MISMATCH", "PayPal authorized an amount or currency different from the order total.");
            if (!authorization.AuthorizationStatus.Equals("CREATED", StringComparison.OrdinalIgnoreCase))
                throw new PaymentOperationException("AUTHORIZATION_NOT_ACTIVE", $"PayPal returned authorization status '{authorization.AuthorizationStatus}'.");

            order.RecordAuthorization(authorization.PayPalOrderId, authorization.PayPalOrderStatus,
                authorization.AuthorizationId, authorization.AuthorizationStatus,
                authorization.Currency, authorization.CreatedAt, authorization.ExpiresAt);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return order;
        });
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await AnyOrder(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled) return order;
            if (order.PaymentStatus != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(order.PayPalAuthorizationId))
                throw new PaymentOperationException("ORDER_NOT_AUTHORIZED", "The order must have an active payment authorization before fulfilment.");

            var authorizationId = order.PayPalAuthorizationId;
            var now = DateTimeOffset.UtcNow;
            var honorPeriodEnded = order.AuthorizationCreatedAt.HasValue && order.AuthorizationCreatedAt.Value.AddDays(3) <= now;
            if (honorPeriodEnded)
            {
                if (order.AuthorizationExpiresAt.HasValue && order.AuthorizationExpiresAt.Value <= now)
                    throw new PaymentOperationException("AUTHORIZATION_EXPIRED",
                        "The PayPal authorization is outside its renewal window. Ask the shopper to pay again before fulfilling this order.");
                try
                {
                    var renewed = await _payPal.ReauthorizeAsync(authorizationId, CentAmount(order.Total(), "order total"),
                        $"eshop-{order.PaymentReference}-reauthorize", cancellationToken);
                    authorizationId = renewed.AuthorizationId;
                    order.RecordReauthorization(renewed.AuthorizationId, renewed.AuthorizationStatus,
                        renewed.CreatedAt, renewed.ExpiresAt);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (PayPalException exception) when (exception.ProviderCode is "AUTHORIZATION_EXPIRED" or "REAUTHORIZE_NOT_ALLOWED" or "AUTHORIZATION_ALREADY_COMPLETED")
                {
                    throw new PaymentOperationException("AUTHORIZATION_CANNOT_BE_RENEWED",
                        $"PayPal cannot renew this authorization ({exception.ProviderCode}). Ask the shopper to pay again before fulfilling the order.");
                }
                catch (PayPalException exception) when ((int)exception.StatusCode is >= 400 and < 500)
                {
                    throw new PaymentOperationException("AUTHORIZATION_CANNOT_BE_RENEWED",
                        $"PayPal cannot renew this authorization ({exception.ProviderCode}). Ask the shopper to provide a new payment authorization before fulfilling the order.");
                }
            }

            var total = CentAmount(order.Total(), "order total");
            var capture = await _payPal.CaptureAsync(authorizationId, order.PaymentReference, total,
                $"eshop-{order.PaymentReference}-capture", cancellationToken);
            if (capture.Amount != total || !capture.Currency.Equals(_payPal.Currency, StringComparison.OrdinalIgnoreCase))
                throw new PaymentOperationException("PAYPAL_CAPTURE_MISMATCH", "PayPal captured an amount or currency different from the order total.");
            if (!capture.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase))
                throw new PaymentOperationException("CAPTURE_NOT_COMPLETED",
                    $"PayPal reported capture status '{capture.Status}'. Resolve that payment before marking the order fulfilled.");

            order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee,
                capture.NetAmount, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return order;
        });
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await AnyOrder(orderId, cancellationToken);
            if (order.FulfilmentStatus == FulfilmentStatus.Cancelled) return order;
            if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled)
                throw new PaymentOperationException("ORDER_ALREADY_FULFILLED", "A fulfilled order must be refunded rather than cancelled.");

            var authorizationStatus = "NOT_AUTHORIZED";
            if (order.PaymentStatus == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(order.PayPalAuthorizationId))
            {
                authorizationStatus = await _payPal.VoidAsync(order.PayPalAuthorizationId,
                    $"eshop-{order.PaymentReference}-void", cancellationToken);
            }
            order.RecordCancellation(authorizationStatus, DateTimeOffset.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return order;
        });
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
            throw new PaymentOperationException("INVALID_IDEMPOTENCY_KEY", "An idempotencyKey of 1 to 200 characters is required.");

        return await WithOrderLock(orderId, async () =>
        {
            var order = await ShopperOrder(orderId, buyerId, cancellationToken);
            var existing = order.Refunds.SingleOrDefault(refund => refund.IdempotencyKey == idempotencyKey);
            if (existing is not null) return existing;
            if (order.PaymentStatus != PaymentStatus.Captured && order.PaymentStatus != PaymentStatus.PartiallyRefunded)
                throw new PaymentOperationException("ORDER_NOT_REFUNDABLE", "Only a captured payment with funds remaining can be refunded.");
            if (string.IsNullOrWhiteSpace(order.PayPalCaptureId) || !order.CapturedAmount.HasValue)
                throw new PaymentOperationException("CAPTURE_NOT_FOUND", "The order does not contain a PayPal capture to refund.");

            var remaining = order.CapturedAmount.Value - order.RefundedAmount;
            var refundAmount = amount.HasValue ? CentAmount(amount.Value, "refund amount") : remaining;
            if (refundAmount <= 0 || refundAmount > remaining)
                throw new PaymentOperationException("REFUND_EXCEEDS_CAPTURE", $"The refund must be greater than zero and no more than {remaining:0.00} {_payPal.Currency}.");

            var providerRefund = await _payPal.RefundAsync(order.PayPalCaptureId, refundAmount,
                RefundRequestId(order.PaymentReference, idempotencyKey), cancellationToken);
            if (providerRefund.Amount != refundAmount || !providerRefund.Currency.Equals(_payPal.Currency, StringComparison.OrdinalIgnoreCase))
                throw new PaymentOperationException("PAYPAL_REFUND_MISMATCH", "PayPal refunded an amount or currency different from the request.");
            if (!providerRefund.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                !providerRefund.Status.Equals("PENDING", StringComparison.OrdinalIgnoreCase))
                throw new PaymentOperationException("REFUND_NOT_ACCEPTED",
                    $"PayPal reported refund status '{providerRefund.Status}'. No refunded amount was recorded locally.");
            var refund = order.RecordRefund(providerRefund.Id, idempotencyKey, providerRefund.Amount,
                providerRefund.Status, providerRefund.CreatedAt);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return refund;
        });
    }

    public async Task<IReadOnlyCollection<Order>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken) => await _dbContext.Orders
        .AsNoTracking()
        .Where(order => order.BuyerId == buyerId)
        .Include(order => order.OrderItems).ThenInclude(item => item.ItemOrdered)
        .Include(order => order.Refunds)
        .OrderByDescending(order => order.OrderDate)
        .ToListAsync(cancellationToken);

    public async Task<PaymentMethod> SavePaymentMethodAsync(string buyerId, CardData card,
        CancellationToken cancellationToken)
    {
        var customerId = await _dbContext.PaymentMethods.Where(method => method.BuyerId == buyerId)
            .OrderByDescending(method => method.Id).Select(method => method.PayPalCustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        var requestId = $"eshop-vault-{Guid.NewGuid():N}";
        var token = await _payPal.SaveCardAsync(card, customerId, requestId, cancellationToken);
        var paymentMethod = new PaymentMethod(buyerId, token.Id, token.CustomerId, token.Brand,
            token.LastDigits, token.Expiry, DateTimeOffset.UtcNow);
        _dbContext.PaymentMethods.Add(paymentMethod);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return paymentMethod;
    }

    public async Task<IReadOnlyCollection<PaymentMethod>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) => await _dbContext.PaymentMethods.AsNoTracking()
        .Where(method => method.BuyerId == buyerId && method.DeletedAt == null)
        .OrderByDescending(method => method.CreatedAt)
        .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken)
    {
        var method = await _dbContext.PaymentMethods.SingleOrDefaultAsync(
            candidate => candidate.Id == paymentMethodId && candidate.BuyerId == buyerId,
            cancellationToken);
        if (method is null) throw new PaymentOperationException("PAYMENT_METHOD_NOT_FOUND", "The saved payment method was not found.");
        if (method.IsDeleted) return;
        try
        {
            await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PayPalException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // A previous attempt may have deleted the provider token before the local save completed.
        }
        method.Delete(DateTimeOffset.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw new PaymentOperationException("INVALID_DATE_RANGE", "'from' must be earlier than 'to'.");
        var providerTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _dbContext.Orders.AsNoTracking().Include(order => order.Refunds)
            .Where(order =>
                (order.AuthorizationCreatedAt >= from && order.AuthorizationCreatedAt <= to) ||
                (order.FulfilledAt >= from && order.FulfilledAt <= to) ||
                order.Refunds.Any(refund => refund.CreatedAt >= from && refund.CreatedAt <= to))
            .ToListAsync(cancellationToken);

        var entries = providerTransactions.Select(transaction =>
        {
            var order = orders.FirstOrDefault(candidate => Matches(candidate, transaction));
            return new ReconciliationEntry("PayPal", transaction.TransactionId, order?.Id,
                order?.PayPalOrderId, order?.PayPalCaptureId, transaction.Status,
                transaction.Amount, transaction.Currency, transaction.InitiatedAt, order is not null);
        }).ToList();
        var providerIds = providerTransactions.SelectMany(transaction => new[]
            { transaction.TransactionId, transaction.ReferenceId }).Where(value => value is not null)
            .Cast<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var order in orders)
        {
            AddLocalIfMissing(order.PayPalAuthorizationId, order.AuthorizationCreatedAt,
                order.PayPalAuthorizationStatus, order.Total(), order);
            AddLocalIfMissing(order.PayPalCaptureId, order.FulfilledAt,
                order.PayPalCaptureStatus, order.CapturedAmount, order);
            foreach (var refund in order.Refunds)
                AddLocalIfMissing(refund.PayPalRefundId, refund.CreatedAt, refund.Status, refund.Amount, order);
        }

        return new ReconciliationResult(from, to, entries.OrderBy(entry => entry.OccurredAt).ToArray());

        void AddLocalIfMissing(string? providerId, DateTimeOffset? occurredAt, string? status,
            decimal? localAmount, Order order)
        {
            if (providerId is null || !occurredAt.HasValue || occurredAt < from || occurredAt > to ||
                providerIds.Contains(providerId)) return;
            entries.Add(new ReconciliationEntry("eShop", providerId, order.Id, order.PayPalOrderId,
                order.PayPalCaptureId, status ?? string.Empty, localAmount ?? 0m,
                order.PaymentCurrency ?? _payPal.Currency, occurredAt.Value, false));
        }
    }

    private async Task<Order> ShopperOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders.Include(candidate => candidate.OrderItems)
            .ThenInclude(item => item.ItemOrdered).Include(candidate => candidate.Refunds)
            .SingleOrDefaultAsync(candidate => candidate.Id == orderId && candidate.BuyerId == buyerId,
                cancellationToken);
        return order ?? throw new PaymentOperationException("ORDER_NOT_FOUND", "The order was not found.");
    }

    private async Task<Order> AnyOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders.Include(candidate => candidate.OrderItems)
            .ThenInclude(item => item.ItemOrdered).Include(candidate => candidate.Refunds)
            .SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
        return order ?? throw new PaymentOperationException("ORDER_NOT_FOUND", "The order was not found.");
    }

    private static async Task<T> WithOrderLock<T>(int orderId, Func<Task<T>> action)
    {
        var semaphore = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync();
        try { return await action(); }
        finally { semaphore.Release(); }
    }

    private static decimal CentAmount(decimal amount, string name)
    {
        if (amount <= 0 || decimal.Round(amount, 2, MidpointRounding.AwayFromZero) != amount)
            throw new PaymentOperationException("INVALID_AMOUNT", $"The {name} must be positive and have at most two decimal places.");
        return amount;
    }

    private static string RefundRequestId(string paymentReference, string key)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return $"eshop-{paymentReference}-refund-{hash[..32]}";
    }

    private static IEnumerable<string> LocalProviderIds(Order order) =>
        new[] { order.PayPalOrderId, order.PayPalAuthorizationId, order.PayPalCaptureId }
            .Concat(order.Refunds.Select(refund => refund.PayPalRefundId))
            .Where(value => value is not null).Cast<string>();

    private static bool Matches(Order order, PayPalTransaction transaction)
    {
        var ids = LocalProviderIds(order).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ids.Contains(transaction.TransactionId) ||
               (transaction.ReferenceId is not null && ids.Contains(transaction.ReferenceId));
    }
}
