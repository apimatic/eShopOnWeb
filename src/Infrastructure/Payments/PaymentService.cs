using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentService : IPaymentService
{
    private readonly CatalogContext _db;
    private readonly IPayPalGateway _payPal;
    private readonly PaymentOperationLock _operationLock;
    private readonly TimeProvider _timeProvider;

    public PaymentService(CatalogContext db, IPayPalGateway payPal, PaymentOperationLock operationLock,
        TimeProvider timeProvider)
    {
        _db = db;
        _payPal = payPal;
        _operationLock = operationLock;
        _timeProvider = timeProvider;
    }

    public async Task<OrderPaymentView> CreateOrderAsync(string buyerId, IReadOnlyList<CreateOrderItem> items,
        ShippingAddress address, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (items.Count == 0) Invalid("An order must contain at least one catalog item.");
        if (items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0 || x.Quantity > 100))
            Invalid("Each catalog item ID must be positive and quantity must be between 1 and 100.");
        if (items.Select(x => x.CatalogItemId).Distinct().Count() != items.Count)
            Invalid("Duplicate catalog item IDs are not allowed; combine their quantities.");
        ValidateAddress(address);

        var ids = items.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var missing = ids.Where(x => !catalogItems.ContainsKey(x)).ToArray();
        if (missing.Length > 0) throw new PaymentOperationException(PaymentErrorKind.NotFound,
            $"Catalog item(s) {string.Join(", ", missing)} were not found.");

        var orderItems = items.Select(item =>
        {
            var catalog = catalogItems[item.CatalogItemId];
            return new OrderItem(new CatalogItemOrdered(catalog.Id, catalog.Name, catalog.PictureUri),
                catalog.Price, item.Quantity);
        }).ToList();
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), orderItems);
        order.SetCurrency(_payPal.Currency);
        EnsureCentAmount(order.Total());
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardDetails? card,
        int? paymentMethodId, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await FindOwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (order.FulfilmentStatus == FulfilmentStatus.Cancelled) Conflict("A cancelled order cannot be paid.");
        if (order.PaymentStatus == PaymentStatus.Authorized || order.PaymentStatus == PaymentStatus.AuthorizationPending)
            return Map(order);
        if (order.PaymentStatus != PaymentStatus.AwaitingPayment)
            Conflict($"Order {orderId} is already in payment state {order.PaymentStatus}.");
        order.EnsurePaymentOperationId();
        if (string.IsNullOrWhiteSpace(order.Currency)) order.SetCurrency(_payPal.Currency);
        EnsureCentAmount(order.Total());
        if ((card is null) == (paymentMethodId is null))
            Invalid("Provide exactly one of card or paymentMethodId.");

        PaymentSource source;
        if (card is not null)
        {
            ValidateCard(card);
            source = PaymentSource.FromCard(card);
        }
        else
        {
            var method = await _db.PaymentMethods.SingleOrDefaultAsync(
                x => x.Id == paymentMethodId && x.BuyerId == buyerId, cancellationToken);
            if (method is null) throw new PaymentOperationException(PaymentErrorKind.NotFound,
                "The saved payment method was not found.");
            source = PaymentSource.FromVault(method.PayPalPaymentTokenId);
        }

        var total = order.Total();
        if (order.PayPalOrderId is null)
        {
            var payPalOrder = await _payPal.CreateOrderAsync(order.Id, total, order.Currency,
                $"eshop-order-{OperationKey(order)}-create-v1", cancellationToken);
            order.RecordPayPalOrder(payPalOrder.Id);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var authorization = await _payPal.AuthorizeAsync(order.PayPalOrderId!, source,
            $"eshop-order-{OperationKey(order)}-authorize-v1", cancellationToken);
        order.RecordAuthorization(authorization.Id, authorization.Status, authorization.CreatedAt,
            authorization.ExpiresAt);
        await _db.SaveChangesAsync(cancellationToken);
        VerifyAmount(total, order.Currency, authorization.Amount, authorization.Currency,
            "authorization");
        if (authorization.Status != "CREATED") Conflict(
            $"PayPal authorization {authorization.Id} is {authorization.Status}; review it before fulfilment.");
        return Map(order);
    }

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await FindOrderAsync(orderId, cancellationToken);
        if (order.FulfilmentStatus == FulfilmentStatus.Cancelled) Conflict("A cancelled order cannot be fulfilled.");
        if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled) return Map(order);

        if (order.PayPalCaptureId is not null)
        {
            var existingCapture = await _payPal.GetCaptureAsync(order.PayPalCaptureId, cancellationToken);
            RecordCapture(order, existingCapture);
            await _db.SaveChangesAsync(cancellationToken);
            VerifyAmount(order.Total(), order.Currency, existingCapture.Amount, existingCapture.Currency, "capture");
            if (existingCapture.Status != "COMPLETED") Conflict(
                $"PayPal capture {existingCapture.Id} is {existingCapture.Status}; retry fulfilment after it settles.");
            return Map(order);
        }
        if (order.PayPalAuthorizationId is null) Conflict("The order has not been authorized for payment.");

        var authorization = await _payPal.GetAuthorizationAsync(order.PayPalAuthorizationId!, cancellationToken);
        order.RecordAuthorizationStatus(authorization.Status);
        if (authorization.CaptureId is not null || authorization.Status == "CAPTURED")
        {
            if (authorization.CaptureId is null) Conflict(
                "PayPal reports this authorization captured but did not return a capture ID; inspect the transaction in PayPal.");
            var recoveredCapture = await _payPal.GetCaptureAsync(authorization.CaptureId!, cancellationToken);
            RecordCapture(order, recoveredCapture);
            await _db.SaveChangesAsync(cancellationToken);
            VerifyAmount(order.Total(), order.Currency, recoveredCapture.Amount, recoveredCapture.Currency, "capture");
            if (recoveredCapture.Status != "COMPLETED") Conflict(
                $"PayPal capture {recoveredCapture.Id} is {recoveredCapture.Status}; retry fulfilment after it settles.");
            return Map(order);
        }
        if (authorization.Status != "CREATED") Conflict(
            $"PayPal authorization {authorization.Id} is {authorization.Status}; it cannot be captured. Ask the shopper to pay again if it was denied or voided.");
        VerifyAmount(order.Total(), order.Currency, authorization.Amount, authorization.Currency,
            "authorization");

        var now = _timeProvider.GetUtcNow();
        var authorizedAt = order.AuthorizedAt ?? authorization.CreatedAt;
        var originalAuthorizedAt = order.OriginalAuthorizedAt ?? authorizedAt;
        if (now >= authorizedAt.AddDays(3))
        {
            var finalRenewalTime = originalAuthorizedAt.AddDays(29);
            if (now >= finalRenewalTime) Conflict(
                $"Authorization {authorization.Id} is outside PayPal's renewal window. Ask the shopper to place and pay for a new order before fulfilment.");
            authorization = await _payPal.ReauthorizeAsync(authorization.Id, order.Total(), order.Currency,
                $"eshop-order-{OperationKey(order)}-reauthorize-{Hash(authorization.Id)}", cancellationToken);
            order.RecordAuthorization(authorization.Id, authorization.Status, authorization.CreatedAt,
                authorization.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
            VerifyAmount(order.Total(), order.Currency, authorization.Amount, authorization.Currency,
                "reauthorization");
            if (authorization.Status != "CREATED") Conflict(
                $"PayPal renewed authorization {authorization.Id} as {authorization.Status}; resolve it in PayPal before retrying fulfilment.");
        }

        var capture = await _payPal.CaptureAsync(authorization.Id, order.Total(), order.Currency,
            $"eshop-order-{OperationKey(order)}-capture-{Hash(authorization.Id)}", cancellationToken);
        RecordCapture(order, capture);
        await _db.SaveChangesAsync(cancellationToken);
        VerifyAmount(order.Total(), order.Currency, capture.Amount, capture.Currency, "capture");
        if (capture.Status != "COMPLETED") Conflict(
            $"PayPal capture {capture.Id} is {capture.Status}; retry fulfilment after it settles.");
        return Map(order);
    }

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await FindOrderAsync(orderId, cancellationToken);
        if (order.FulfilmentStatus == FulfilmentStatus.Cancelled) return Map(order);
        if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled || order.PayPalCaptureId is not null)
            Conflict("A captured or fulfilled order cannot be cancelled; refund it instead.");
        if (order.PayPalAuthorizationId is null)
        {
            order.Cancel();
            await _db.SaveChangesAsync(cancellationToken);
            return Map(order);
        }

        var authorization = await _payPal.GetAuthorizationAsync(order.PayPalAuthorizationId, cancellationToken);
        if (authorization.Status == "CAPTURED") Conflict(
            "PayPal reports that funds were captured; reconcile the capture and refund it instead of cancelling.");
        if (authorization.Status != "VOIDED")
            authorization = await _payPal.VoidAsync(authorization.Id,
                $"eshop-order-{OperationKey(order)}-void-v1", cancellationToken);
        order.RecordAuthorizationStatus(authorization.Status);
        if (authorization.Status != "VOIDED") Conflict(
            $"PayPal authorization {authorization.Id} is {authorization.Status} and was not released; inspect it in PayPal.");
        order.Cancel();
        await _db.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<RefundView> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            Invalid("idempotencyKey is required and must be at most 100 characters.");
        using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await FindOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null) return Map(existing);
        if (order.PayPalCaptureId is null || order.CapturedAmount is null ||
            order.FulfilmentStatus != FulfilmentStatus.Fulfilled)
            Conflict("Only a captured, fulfilled order can be refunded.");
        var remaining = order.CapturedAmount!.Value - order.RefundedAmount;
        var refundAmount = amount ?? remaining;
        EnsureCentAmount(refundAmount);
        if (refundAmount <= 0 || refundAmount > remaining)
            Invalid($"Refund amount must be positive and no more than the remaining {remaining:0.00} {order.Currency}.");
        var refund = await _payPal.RefundAsync(order.PayPalCaptureId!, refundAmount, order.Currency,
            $"eshop-refund-{OperationKey(order)}-{Hash(idempotencyKey)}", cancellationToken);
        var recorded = order.RecordRefund(idempotencyKey, refund.Id, refund.Status, refund.Amount);
        await _db.SaveChangesAsync(cancellationToken);
        VerifyAmount(refundAmount, order.Currency, refund.Amount, refund.Currency, "refund");
        return Map(recorded);
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var orders = await _db.Orders.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered).Include(x => x.Refunds)
            .OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        return orders.Select(Map).ToList();
    }

    public async Task<SavedCardView> SavePaymentMethodAsync(string buyerId, CardDetails card,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        ValidateCard(card);
        using var operation = await _operationLock.AcquireAsync($"vault:{buyerId}", cancellationToken);
        var fingerprint = buyerId + "|" + Hash(card.Number + "|" + card.Expiry);
        var requestId = _operationLock.GetOrCreateVaultRequestId(fingerprint);
        var merchantCustomerId = "eshop-" + Hash(buyerId.ToLowerInvariant());
        var vaulted = await _payPal.SaveCardAsync(merchantCustomerId, card, requestId, cancellationToken);
        _operationLock.RememberVaultToken(vaulted.Id, fingerprint);
        var existing = await _db.PaymentMethods.SingleOrDefaultAsync(
            x => x.BuyerId == buyerId && x.PayPalPaymentTokenId == vaulted.Id, cancellationToken);
        if (existing is not null) return Map(existing);
        var method = new PaymentMethod(buyerId, vaulted.Id, vaulted.CustomerId, vaulted.Brand,
            vaulted.Last4, vaulted.Expiry);
        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(method);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var methods = await _db.PaymentMethods.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        return methods.Select(Map).ToList();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        using var operation = await _operationLock.AcquireAsync($"vault:{buyerId}", cancellationToken);
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(
            x => x.Id == paymentMethodId && x.BuyerId == buyerId, cancellationToken);
        if (method is null) throw new PaymentOperationException(PaymentErrorKind.NotFound,
            "The saved payment method was not found.");
        await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken);
        _operationLock.ForgetVaultToken(method.PayPalPaymentTokenId);
        _db.PaymentMethods.Remove(method);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from) Invalid("to must be later than from.");
        if (to - from > TimeSpan.FromDays(31)) Invalid("PayPal transaction searches support ranges of at most 31 days.");
        var payPalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _db.Orders.AsNoTracking().Include(x => x.Refunds).ToListAsync(cancellationToken);
        var local = new List<(int OrderId, string Type, string Id)>();
        foreach (var order in orders)
        {
            if (order.PayPalCaptureId is not null && order.CapturedAt >= from && order.CapturedAt <= to)
                local.Add((order.Id, "Capture", order.PayPalCaptureId));
            local.AddRange(order.Refunds.Where(x => x.CreatedAt >= from && x.CreatedAt <= to)
                .Select(x => (order.Id, "Refund", x.PayPalRefundId)));
        }

        var lines = new List<ReconciliationLine>();
        var matchedLocal = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transaction in payPalTransactions)
        {
            var match = local.FirstOrDefault(x => x.Id == transaction.TransactionId ||
                x.Id == transaction.ReferenceId);
            var parsedOrderId = ParseOrderId(transaction.CustomId);
            int? orderId = match == default && parsedOrderId.HasValue && orders.Any(x => x.Id == parsedOrderId.Value)
                ? parsedOrderId : match == default ? null : match.OrderId;
            if (match != default) matchedLocal.Add(match.Id);
            lines.Add(new ReconciliationLine(match == default ? "PayPalOnly" : "Matched",
                orderId, match == default ? null : match.Type, match == default ? null : match.Id,
                transaction.TransactionId, transaction.ReferenceId, transaction.InvoiceId,
                transaction.InitiatedAt, transaction.Amount, transaction.Currency, transaction.Fee,
                transaction.Status));
        }
        lines.AddRange(local.Where(x => !matchedLocal.Contains(x.Id)).Select(x =>
            new ReconciliationLine("EShopOnly", x.OrderId, x.Type, x.Id, null, null, null, null,
                null, null, null, null)));
        return new ReconciliationReport(from, to, lines);
    }

    private async Task<Order> FindOwnedOrderAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Where(x => x.Id == orderId && x.BuyerId == buyerId)
            .Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered).Include(x => x.Refunds)
            .SingleOrDefaultAsync(cancellationToken);
        return order ?? throw new PaymentOperationException(PaymentErrorKind.NotFound, "The order was not found.");
    }

    private async Task<Order> FindOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders.Where(x => x.Id == orderId).Include(x => x.OrderItems)
            .ThenInclude(x => x.ItemOrdered).Include(x => x.Refunds).SingleOrDefaultAsync(cancellationToken);
        return order ?? throw new PaymentOperationException(PaymentErrorKind.NotFound, "The order was not found.");
    }

    private void RecordCapture(Order order, CaptureResult capture)
    {
        var amountMatches = order.Total() == capture.Amount && string.Equals(order.Currency,
            capture.Currency, StringComparison.OrdinalIgnoreCase);
        order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee,
            capture.NetAmount, capture.CreatedAt, amountMatches);
    }

    private static OrderPaymentView Map(Order order) => new(order.Id, order.OrderDate, order.BuyerId,
        order.Total(), order.Currency, order.PaymentStatus.ToString(), order.FulfilmentStatus.ToString(),
        order.PayPalOrderId, order.PayPalAuthorizationId, order.PayPalAuthorizationStatus,
        order.AuthorizationExpiresAt, order.PayPalCaptureId, order.PayPalCaptureStatus,
        order.CapturedAmount, order.PayPalFee, order.NetProceeds, order.RefundedAmount,
        order.OrderItems.Select(x => new OrderItemView(x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(), order.Refunds.Select(Map).ToList());

    private static RefundView Map(PaymentRefund refund) => new(refund.Id, refund.PayPalRefundId,
        refund.Status, refund.Amount, refund.CreatedAt);
    private static SavedCardView Map(PaymentMethod method) => new(method.Id, method.Brand, method.Last4,
        method.Expiry, method.CreatedAt);

    private static void ValidateCard(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
            Invalid("Card name, number, expiry, securityCode and billingAddress.countryCode are required.");
    }
    private static void ValidateAddress(ShippingAddress address)
    {
        if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
            Invalid("Shipping street, city, country and zipCode are required.");
    }
    private static void RequireBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new PaymentOperationException(PaymentErrorKind.InvalidRequest, "The authenticated identity is missing.");
    }
    private static void EnsureCentAmount(decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
            Invalid("Amounts must be positive and have no more than two decimal places.");
    }
    private static void VerifyAmount(decimal expectedAmount, string expectedCurrency, decimal actualAmount,
        string actualCurrency, string operation)
    {
        if (expectedAmount != actualAmount || !string.Equals(expectedCurrency, actualCurrency,
                StringComparison.OrdinalIgnoreCase))
            throw new PaymentOperationException(PaymentErrorKind.Conflict,
                $"PayPal's {operation} amount ({actualAmount:0.00} {actualCurrency}) does not match the order ({expectedAmount:0.00} {expectedCurrency}). Inspect the payment before continuing.");
    }
    private static int? ParseOrderId(string? customId) =>
        int.TryParse(customId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
    private static string OperationKey(Order order) => order.PaymentOperationId.ToString("N", CultureInfo.InvariantCulture);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant()[..24];
    private static void Invalid(string message) => throw new PaymentOperationException(PaymentErrorKind.InvalidRequest, message);
    private static void Conflict(string message) => throw new PaymentOperationException(PaymentErrorKind.Conflict, message);
}
