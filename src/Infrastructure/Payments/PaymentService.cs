using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentService : IPaymentService
{
    private static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);
    private readonly CatalogContext _context;
    private readonly IPaymentGateway _gateway;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(CatalogContext context, IPaymentGateway gateway, ILogger<PaymentService> logger)
    {
        _context = context;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<OrderView> PlaceOrderAsync(string buyerId, IReadOnlyList<CatalogItemQuantity> items,
        ShippingAddressInput shippingAddress, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (items.Count == 0 || items.Count > 100 || items.Any(item => item.CatalogItemId <= 0 || item.Quantity <= 0))
        {
            throw Validation("An order must contain between 1 and 100 valid catalog item quantities.");
        }

        var requested = items.GroupBy(item => item.CatalogItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        if (requested.Values.Any(quantity => quantity > 1000))
        {
            throw Validation("An item quantity cannot exceed 1000.");
        }

        var catalogItems = await _context.CatalogItems.AsNoTracking()
            .Where(item => requested.Keys.Contains(item.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != requested.Count)
        {
            throw new PaymentException(PaymentFailureKind.NotFound, "One or more catalog items do not exist.");
        }

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price,
            requested[item.Id])).ToList();
        var order = new Order(buyerId,
            new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State,
                shippingAddress.Country, shippingAddress.PostalCode),
            orderItems,
            _gateway.Currency);

        EnsureCentAmount(order.Total());
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Order {OrderId} placed for buyer {BuyerId}; awaiting payment.", order.Id, buyerId);
        return Map(order);
    }

    public async Task<OrderView> PayAsync(int orderId, string buyerId, PayOrderInput input,
        CancellationToken cancellationToken)
    {
        var order = await OwnedOrderAsync(orderId, buyerId, cancellationToken);
        if (order.PaymentStatus == PaymentStatus.AuthorizationPending && order.AuthorizationId is not null &&
            order.Currency is not null)
        {
            var current = await _gateway.GetAuthorizationAsync(order.AuthorizationId, cancellationToken);
            EnsureProviderAmount(order.Total(), order.Currency, current.Amount, current.Currency, "authorization");
            order.RefreshAuthorization(current.Status, current.CreatedAt, current.ExpiresAt);
            await SaveOrderAsync(order, buyerId, cancellationToken);
            if (current.Status == "PENDING")
            {
                throw Conflict("PayPal is still reviewing the authorization. Retry payment status later.");
            }
            return Map(order);
        }

        if (order.PaymentStatus != PaymentStatus.AwaitingPayment)
        {
            return Map(order);
        }

        var usesCard = input.Card is not null;
        var usesSavedMethod = input.PaymentMethodId.HasValue;
        if (usesCard == usesSavedMethod)
        {
            throw Validation("Specify exactly one of card or paymentMethodId.");
        }

        string? vaultId = null;
        if (input.PaymentMethodId is { } paymentMethodId)
        {
            var method = await _context.PaymentMethods.SingleOrDefaultAsync(
                candidate => candidate.Id == paymentMethodId && candidate.OwnerId == buyerId,
                cancellationToken);
            if (method is null)
            {
                throw new PaymentException(PaymentFailureKind.NotFound, "The saved payment method was not found.");
            }
            vaultId = method.ProviderTokenId;
        }

        var total = order.Total();
        EnsureCentAmount(total);
        var authorization = await _gateway.AuthorizeAsync(order.Id, order.PaymentReference, total, input.Card, vaultId,
            OrderRequestId(order.PaymentReference, "pay"), cancellationToken);
        EnsureProviderAmount(total, _gateway.Currency, authorization.Amount, authorization.Currency,
            "authorization");

        if (authorization.Status is not ("CREATED" or "PENDING"))
        {
            throw new PaymentException(PaymentFailureKind.ProviderRejected,
                $"PayPal returned authorization status {authorization.Status}; the order was not authorized.");
        }

        order.RecordAuthorization(_gateway.Currency, authorization.OrderId, authorization.AuthorizationId,
            authorization.Status, authorization.CreatedAt, authorization.ExpiresAt, input.PaymentMethodId);
        await SaveOrderAsync(order, buyerId, cancellationToken);
        if (authorization.Status == "PENDING")
        {
            throw Conflict("PayPal is still reviewing the authorization. Retry payment status later.");
        }
        _logger.LogInformation("Order {OrderId} authorized as {AuthorizationId}.", order.Id,
            authorization.AuthorizationId);
        return Map(order);
    }

    public async Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await OrderAsync(orderId, cancellationToken);
        if (order.FulfillmentStatus == FulfillmentStatus.Fulfilled)
        {
            return Map(order);
        }
        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled)
        {
            throw Conflict("A cancelled order cannot be fulfilled.");
        }

        if (order.PaymentStatus == PaymentStatus.CapturePending && order.CaptureId is not null)
        {
            var currentCapture = await _gateway.GetCaptureAsync(order.CaptureId, cancellationToken);
            return await CompleteOrReportCaptureAsync(order, currentCapture, cancellationToken);
        }

        if (order.PaymentStatus is not (PaymentStatus.Authorized or PaymentStatus.AuthorizationPending) ||
            order.AuthorizationId is null || order.Currency is null)
        {
            throw Conflict("The order must have an active authorization before fulfilment.");
        }

        var current = await _gateway.GetAuthorizationAsync(order.AuthorizationId, cancellationToken);
        EnsureProviderAmount(order.Total(), order.Currency, current.Amount, current.Currency, "authorization");
        if (order.PaymentStatus == PaymentStatus.AuthorizationPending || current.Status != order.AuthorizationStatus)
        {
            order.RefreshAuthorization(current.Status, current.CreatedAt, current.ExpiresAt);
            await _context.SaveChangesAsync(cancellationToken);
        }
        if (current.Status == "PENDING")
        {
            throw Conflict("The PayPal authorization is still pending. Retry fulfilment after PayPal completes its review.");
        }
        if (current.Status is "VOIDED" or "DENIED" or "CAPTURED")
        {
            throw Conflict($"The PayPal authorization is {current.Status}. A fresh customer payment is required.");
        }
        if (current.Status != "CREATED")
        {
            throw Conflict($"The PayPal authorization is {current.Status} and cannot currently be captured.");
        }

        var createdAt = current.CreatedAt ?? order.AuthorizationCreatedAt;
        if (createdAt.HasValue && DateTimeOffset.UtcNow - createdAt.Value >= AuthorizationHonorPeriod)
        {
            try
            {
                current = await _gateway.ReauthorizeAsync(order.AuthorizationId, order.Total(), order.Currency,
                    OrderRequestId(order.PaymentReference, "reauthorize"), cancellationToken);
            }
            catch (PaymentException exception) when (exception.Kind is PaymentFailureKind.ProviderRejected or PaymentFailureKind.Conflict)
            {
                throw new PaymentException(PaymentFailureKind.Conflict,
                    "The PayPal authorization is outside its renewable window. Ask the shopper to authorize payment again.",
                    exception.ProviderDebugId, exception);
            }

            EnsureProviderAmount(order.Total(), order.Currency, current.Amount, current.Currency, "reauthorization");
            order.RecordReauthorization(current.AuthorizationId, current.Status, current.CreatedAt, current.ExpiresAt);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var capture = await _gateway.CaptureAsync(current.AuthorizationId, order.PaymentReference, order.Total(), order.Currency,
            OrderRequestId(order.PaymentReference, "capture"), cancellationToken);
        EnsureProviderAmount(order.Total(), order.Currency, capture.Amount, capture.Currency, "capture");
        return await CompleteOrReportCaptureAsync(order, capture, cancellationToken);
    }

    private async Task<OrderView> CompleteOrReportCaptureAsync(Order order, ProviderCapture capture,
        CancellationToken cancellationToken)
    {
        if (capture.Status == "COMPLETED")
        {
            order.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.Fee, capture.Net,
                DateTimeOffset.UtcNow);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                return Map(await OrderAsync(order.Id, cancellationToken));
            }
            _logger.LogInformation("Order {OrderId} fulfilled with capture {CaptureId}.", order.Id, capture.CaptureId);
            return Map(order);
        }

        if (order.PaymentStatus == PaymentStatus.Authorized)
        {
            order.RecordPendingCapture(capture.CaptureId, capture.Status, capture.Amount);
            await _context.SaveChangesAsync(cancellationToken);
        }

        throw Conflict(capture.Status == "PENDING"
            ? "PayPal capture is pending. Retry fulfilment to refresh its status; do not capture again."
            : $"PayPal capture status is {capture.Status}. Review the payment in PayPal before fulfilment.");
    }

    public async Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await OrderAsync(orderId, cancellationToken);
        if (order.FulfillmentStatus == FulfillmentStatus.Cancelled)
        {
            return Map(order);
        }
        if (order.FulfillmentStatus == FulfillmentStatus.Fulfilled ||
            order.PaymentStatus is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded or PaymentStatus.CapturePending)
        {
            throw Conflict("The order has entered capture or fulfilment and can no longer be cancelled; use a refund.");
        }

        var authorizationStatus = "VOIDED";
        if (order.PaymentStatus is (PaymentStatus.Authorized or PaymentStatus.AuthorizationPending) &&
            order.AuthorizationId is not null)
        {
            authorizationStatus = await _gateway.VoidAsync(order.AuthorizationId,
                OrderRequestId(order.PaymentReference, "void"), cancellationToken);
        }

        order.Cancel(authorizationStatus, DateTimeOffset.UtcNow);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            return Map(await OrderAsync(order.Id, cancellationToken));
        }
        _logger.LogInformation("Order {OrderId} cancelled; authorization status {Status}.", order.Id,
            authorizationStatus);
        return Map(order);
    }

    public async Task<RefundCreated> RefundAsync(int orderId, string buyerId, RefundInput input,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.IdempotencyKey) || input.IdempotencyKey.Length > 64)
        {
            throw Validation("A refund idempotencyKey between 1 and 64 characters is required.");
        }

        var order = await OwnedOrderAsync(orderId, buyerId, cancellationToken);
        var existing = order.Refunds.SingleOrDefault(refund => refund.IdempotencyKey == input.IdempotencyKey);
        if (existing is not null)
        {
            if (existing.Status == "PENDING" && order.Currency is not null)
            {
                var currentRefund = await _gateway.GetRefundAsync(existing.ProviderRefundId, cancellationToken);
                EnsureProviderAmount(existing.Amount, order.Currency, currentRefund.Amount,
                    currentRefund.Currency, "refund");
                order.UpdateRefundStatus(existing.ProviderRefundId, currentRefund.Status);
                await _context.SaveChangesAsync(cancellationToken);
            }
            return new RefundCreated(existing.ProviderRefundId, Map(order));
        }

        if (order.FulfillmentStatus != FulfillmentStatus.Fulfilled || order.CaptureId is null || order.Currency is null)
        {
            throw Conflict("Only a fulfilled order with a captured payment can be refunded.");
        }

        var amount = input.Amount ?? order.RefundableAmount;
        EnsureCentAmount(amount);
        if (amount <= 0m || amount > order.RefundableAmount)
        {
            throw Conflict($"The refund amount must be positive and no greater than {order.RefundableAmount:0.00}.");
        }

        var providerRefund = await _gateway.RefundAsync(order.CaptureId, order.PaymentReference, order.Id,
            input.Amount.HasValue ? amount : null, order.Currency,
            RefundRequestId(order.PaymentReference, input.IdempotencyKey), cancellationToken);
        EnsureProviderAmount(amount, order.Currency, providerRefund.Amount, providerRefund.Currency, "refund");

        order.AddRefund(input.IdempotencyKey, providerRefund.RefundId, amount, providerRefund.Status,
            providerRefund.CreatedAt);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            order = await OwnedOrderAsync(orderId, buyerId, cancellationToken);
            existing = order.Refunds.SingleOrDefault(refund => refund.IdempotencyKey == input.IdempotencyKey);
            if (existing is null)
            {
                throw;
            }
            return new RefundCreated(existing.ProviderRefundId, Map(order));
        }

        _logger.LogInformation("Order {OrderId} refunded {Amount} as {RefundId}.", order.Id, amount,
            providerRefund.RefundId);
        return new RefundCreated(providerRefund.RefundId, Map(order));
    }

    public async Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var orders = await OrdersQuery()
            .Where(order => order.BuyerId == buyerId)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(Map).ToList();
    }

    public async Task<PaymentMethodView> SavePaymentMethodAsync(string buyerId, CardInput card,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var providerMethod = await _gateway.SavePaymentMethodAsync(buyerId, card,
            CardRequestId(), cancellationToken);

        var existing = await _context.PaymentMethods.SingleOrDefaultAsync(
            method => method.ProviderTokenId == providerMethod.TokenId, cancellationToken);
        if (existing is not null)
        {
            if (existing.OwnerId != buyerId)
            {
                throw new PaymentException(PaymentFailureKind.Conflict,
                    "PayPal returned a payment token already assigned to another shopper.");
            }
            return Map(existing);
        }

        var method = new PaymentMethod(buyerId, providerMethod.TokenId, providerMethod.Brand,
            providerMethod.Last4, providerMethod.Expiry, DateTimeOffset.UtcNow);
        _context.PaymentMethods.Add(method);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _context.ChangeTracker.Clear();
            existing = await _context.PaymentMethods.SingleOrDefaultAsync(
                candidate => candidate.ProviderTokenId == providerMethod.TokenId && candidate.OwnerId == buyerId,
                cancellationToken);
            if (existing is null)
            {
                throw;
            }
            return Map(existing);
        }
        _logger.LogInformation("Saved payment method {PaymentMethodId} for buyer {BuyerId}.", method.Id, buyerId);
        return Map(method);
    }

    public async Task<IReadOnlyList<PaymentMethodView>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var methods = await _context.PaymentMethods.AsNoTracking()
            .Where(method => method.OwnerId == buyerId)
            .OrderByDescending(method => method.CreatedAt)
            .ToListAsync(cancellationToken);
        return methods.Select(Map).ToList();
    }

    public async Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var method = await _context.PaymentMethods.SingleOrDefaultAsync(
            candidate => candidate.Id == paymentMethodId && candidate.OwnerId == buyerId,
            cancellationToken);
        if (method is null)
        {
            throw new PaymentException(PaymentFailureKind.NotFound, "The saved payment method was not found.");
        }

        try
        {
            await _gateway.DeletePaymentMethodAsync(method.ProviderTokenId, cancellationToken);
        }
        catch (PaymentException exception) when (exception.Kind == PaymentFailureKind.NotFound)
        {
            _logger.LogWarning("PayPal token for payment method {PaymentMethodId} was already absent.", paymentMethodId);
        }

        _context.PaymentMethods.Remove(method);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from)
        {
            throw Validation("The reconciliation end must be after its start.");
        }

        var providerTransactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var providerReferences = providerTransactions
            .Where(transaction => !string.IsNullOrWhiteSpace(transaction.InvoiceId))
            .Select(transaction => transaction.InvoiceId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var providerIds = providerTransactions
            .SelectMany(transaction => new[] { transaction.TransactionId, transaction.ReferenceId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var orders = await OrdersQuery().AsNoTracking()
            .Where(order =>
                providerReferences.Contains(order.PaymentReference) ||
                order.PayPalOrderId != null && providerIds.Contains(order.PayPalOrderId) ||
                order.AuthorizationId != null && providerIds.Contains(order.AuthorizationId) ||
                order.CaptureId != null && providerIds.Contains(order.CaptureId) ||
                order.Refunds.Any(refund => providerIds.Contains(refund.ProviderRefundId)) ||
                order.OrderDate >= from && order.OrderDate <= to ||
                order.AuthorizationCreatedAt >= from && order.AuthorizationCreatedAt <= to ||
                order.FulfilledAt >= from && order.FulfilledAt <= to ||
                order.CancelledAt >= from && order.CancelledAt <= to ||
                order.Refunds.Any(refund => refund.CreatedAt >= from && refund.CreatedAt <= to))
            .ToListAsync(cancellationToken);
        var ordersByReference = orders.ToDictionary(order => order.PaymentReference, StringComparer.Ordinal);
        var ordersByProviderId = new Dictionary<string, Order>(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            AddProviderId(order.PayPalOrderId, order);
            AddProviderId(order.AuthorizationId, order);
            AddProviderId(order.CaptureId, order);
            foreach (var refund in order.Refunds)
            {
                AddProviderId(refund.ProviderRefundId, order);
            }
        }
        var matchedOrderIds = new HashSet<int>();
        var lines = new List<ReconciliationLine>();

        foreach (var transaction in providerTransactions)
        {
            Order? matchedOrder = null;
            var hasOrder = transaction.InvoiceId is not null &&
                           ordersByReference.TryGetValue(transaction.InvoiceId, out matchedOrder);
            hasOrder = hasOrder || ordersByProviderId.TryGetValue(transaction.TransactionId, out matchedOrder);
            hasOrder = hasOrder || transaction.ReferenceId is not null &&
                ordersByProviderId.TryGetValue(transaction.ReferenceId, out matchedOrder);
            if (hasOrder)
            {
                matchedOrderIds.Add(matchedOrder!.Id);
            }

            lines.Add(new ReconciliationLine(
                hasOrder ? matchedOrder!.Id : null,
                transaction.TransactionId,
                transaction.ReferenceId,
                transaction.EventCode,
                transaction.Status,
                transaction.InitiatedAt,
                transaction.Amount,
                transaction.Fee,
                hasOrder ? matchedOrder!.Total() : null,
                transaction.Currency,
                hasOrder ? "Matched" : "PayPalOnly"));
        }

        var emptyProviderRange = providerTransactions.Count == 0;
        foreach (var order in orders.Where(order => !matchedOrderIds.Contains(order.Id)))
        {
            lines.Add(new ReconciliationLine(order.Id, null, null, null, null, null, null, null, order.Total(),
                order.Currency ?? _gateway.Currency,
                emptyProviderRange ? "PendingReporting" : "LocalOnly"));
        }

        return new ReconciliationReport(from, to, lines);

        void AddProviderId(string? providerId, Order order)
        {
            if (!string.IsNullOrWhiteSpace(providerId))
            {
                ordersByProviderId.TryAdd(providerId, order);
            }
        }
    }

    private IQueryable<Order> OrdersQuery() => _context.Orders
        .Include(order => order.OrderItems)
            .ThenInclude(item => item.ItemOrdered)
        .Include(order => order.Refunds);

    private async Task<Order> OrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await OrdersQuery().SingleOrDefaultAsync(candidate => candidate.Id == orderId,
            cancellationToken);
        return order ?? throw new PaymentException(PaymentFailureKind.NotFound, "The order was not found.");
    }

    private async Task<Order> OwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var order = await OrdersQuery().SingleOrDefaultAsync(
            candidate => candidate.Id == orderId && candidate.BuyerId == buyerId,
            cancellationToken);
        return order ?? throw new PaymentException(PaymentFailureKind.NotFound, "The order was not found.");
    }

    private async Task SaveOrderAsync(Order order, string buyerId, CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.ChangeTracker.Clear();
            var current = await OwnedOrderAsync(order.Id, buyerId, cancellationToken);
            if (current.PaymentStatus == PaymentStatus.AwaitingPayment)
            {
                throw;
            }
        }
    }

    private static OrderView Map(Order order) => new(
        order.Id,
        order.OrderDate,
        order.Total(),
        order.Currency,
        order.PaymentStatus,
        order.FulfillmentStatus,
        order.PayPalOrderId,
        order.AuthorizationId,
        order.AuthorizationStatus,
        order.AuthorizationExpiresAt,
        order.CaptureId,
        order.CaptureStatus,
        order.CapturedAmount,
        order.PayPalFee,
        order.NetProceeds,
        order.RefundedAmount,
        order.RefundableAmount,
        order.OrderItems.Select(item => new OrderItemView(item.ItemOrdered.CatalogItemId,
            item.ItemOrdered.ProductName, item.UnitPrice, item.Units)).ToList(),
        order.Refunds.Select(refund => new RefundView(refund.ProviderRefundId, refund.Amount,
            refund.Status, refund.CreatedAt)).ToList());

    private static PaymentMethodView Map(PaymentMethod method) => new(method.Id, method.Brand,
        method.Last4, method.Expiry, method.CreatedAt);

    private static string OrderRequestId(string paymentReference, string phase) => $"{paymentReference}-{phase}";

    private static string RefundRequestId(string paymentReference, string callerKey) =>
        "eshop-refund-" + StableHash($"{paymentReference}:{callerKey}")[..32];

    private static string CardRequestId() => "eshop-card-" + Guid.NewGuid().ToString("N");

    private static string StableHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void RequireBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(PaymentFailureKind.Validation, "An authenticated shopper is required.");
        }
    }

    private static void EnsureCentAmount(decimal amount)
    {
        if (amount <= 0m || decimal.Round(amount, 2, MidpointRounding.ToEven) != amount)
        {
            throw Validation("The amount must be positive and representable to the cent.");
        }
    }

    private static void EnsureProviderAmount(decimal expectedAmount, string expectedCurrency,
        decimal actualAmount, string actualCurrency, string operation)
    {
        if (actualAmount != expectedAmount || !string.Equals(expectedCurrency, actualCurrency,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(PaymentFailureKind.ProviderUnavailable,
                $"PayPal returned a {operation} amount that does not match the order total.");
        }
    }

    private static PaymentException Validation(string message) =>
        new(PaymentFailureKind.Validation, message);

    private static PaymentException Conflict(string message) =>
        new(PaymentFailureKind.Conflict, message);
}
