using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentService : IPaymentService
{
    private static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentOperationLock _operationLock;

    public PaymentService(CatalogContext db, IPayPalClient payPal, IUriComposer uriComposer,
        PaymentOperationLock operationLock)
    {
        _db = db;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _operationLock = operationLock;
    }

    public async Task<OrderView> PlaceOrderAsync(string buyerId,
        IReadOnlyList<OrderLineInput> items, ShippingAddressInput shippingAddress,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        ValidateShipping(shippingAddress);
        if (items.Count == 0)
        {
            throw new PaymentValidationException("At least one catalog item is required.");
        }

        if (items.Any(x => x.CatalogItemId <= 0 || x.Quantity is <= 0 or > 100))
        {
            throw new PaymentValidationException(
                "Each order item needs a valid catalogItemId and a quantity from 1 to 100.");
        }

        var quantities = items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        if (quantities.Values.Any(x => x > 100))
        {
            throw new PaymentValidationException("A catalog item quantity cannot exceed 100.");
        }

        var catalogIds = quantities.Keys.ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(x => catalogIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = catalogIds.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentValidationException(
                $"Catalog item {string.Join(", ", missing)} does not exist.");
        }

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, _uriComposer.ComposePicUri(item.PictureUri)),
            item.Price,
            quantities[item.Id])).ToList();
        var order = new Order(buyerId,
            new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State,
                shippingAddress.Country, shippingAddress.ZipCode), orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<OrderView> PayAsync(string buyerId, int orderId, PayOrderInput input,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        if ((input.Card is null) == (input.PaymentMethodId is null))
        {
            throw new PaymentValidationException(
                "Specify either card or paymentMethodId, but not both.");
        }
        if (input.Card is not null)
        {
            ValidateCard(input.Card);
        }

        using var lease = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureOwned(order, buyerId);

        if (order.Status == OrderStatus.Authorized)
        {
            return Map(order);
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be paid while it is {order.Status}.");
        }

        string? vaultId = null;
        if (input.PaymentMethodId is int paymentMethodId)
        {
            var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
                x => x.Id == paymentMethodId && x.BuyerId == buyerId && x.DeletedAt == null,
                cancellationToken);
            if (method is null)
            {
                throw new PaymentResourceNotFoundException("Saved payment method not found.");
            }
            vaultId = method.PayPalVaultId;
        }

        var total = EnsureCentAmount(order.Total());
        if (order.Payment?.PayPalOrderId is null)
        {
            var payPalOrderId = await _payPal.CreateOrderAsync(total, order.PaymentReference,
                $"order-{order.PaymentReference}", cancellationToken);
            order.SetPayPalOrder(payPalOrderId);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var authorization = await _payPal.AuthorizeOrderAsync(order.Payment!.PayPalOrderId!,
            input.Card, vaultId, $"authorize-{order.PaymentReference}", cancellationToken);
        EnsureMoney(authorization.Amount, authorization.Currency, total, "authorization");
        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentProcessorException(
                $"PayPal did not create the authorization (status: {authorization.Status}).");
        }

        order.RecordAuthorization(authorization.AuthorizationId, authorization.Status,
            authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var lease = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return Map(order);
        }
        if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId is null)
        {
            throw new PaymentConflictException(
                $"Order {orderId} must have an active authorization before fulfilment.");
        }

        var total = EnsureCentAmount(order.Total());
        if (order.Payment.CaptureId is not null)
        {
            var currentCapture = await _payPal.GetCaptureAsync(order.Payment.CaptureId,
                cancellationToken);
            EnsureMoney(currentCapture.Amount, currentCapture.Currency, total, "capture");
            order.RecordCapture(currentCapture.CaptureId, currentCapture.Status,
                currentCapture.Amount, currentCapture.Fee, currentCapture.Net,
                currentCapture.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            EnsureCaptureCompleted(currentCapture.Status);
            return Map(order);
        }

        var authorization = await _payPal.GetAuthorizationAsync(order.Payment.AuthorizationId,
            order.Payment.PayPalOrderId!, cancellationToken);
        EnsureMoney(authorization.Amount, authorization.Currency, total, "authorization");
        if (string.Equals(authorization.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.Status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            // Recover the response from a capture that PayPal completed before a local save failed.
            // The stable request ID makes this replay return the original capture in PayPal.
            var recoveredCapture = await _payPal.CaptureAsync(authorization.AuthorizationId, total,
                order.PaymentReference, $"capture-{order.PaymentReference}", cancellationToken);
            EnsureMoney(recoveredCapture.Amount, recoveredCapture.Currency, total, "capture");
            order.RecordCapture(recoveredCapture.CaptureId, recoveredCapture.Status,
                recoveredCapture.Amount, recoveredCapture.Fee, recoveredCapture.Net,
                recoveredCapture.CreatedAt);
            await _db.SaveChangesAsync(cancellationToken);
            EnsureCaptureCompleted(recoveredCapture.Status);
            return Map(order);
        }
        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentConflictException(
                $"PayPal authorization {authorization.AuthorizationId} is {authorization.Status}; " +
                "the operator must ask the shopper to authorize the order again.");
        }

        if (AuthorizationIsStale(authorization))
        {
            if (authorization.ExpiresAt is not null &&
                authorization.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                throw CannotRenew(authorization.AuthorizationId,
                    "its authorization period has expired");
            }

            try
            {
                authorization = await _payPal.ReauthorizeAsync(authorization.AuthorizationId,
                    authorization.PayPalOrderId, total,
                    $"reauthorize-{order.PaymentReference}", cancellationToken);
            }
            catch (PayPalApiException ex) when ((int)ex.StatusCode is >= 400 and < 500)
            {
                throw CannotRenew(authorization.AuthorizationId, ex.Message);
            }
            EnsureMoney(authorization.Amount, authorization.Currency, total, "reauthorization");
            if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
            {
                throw CannotRenew(authorization.AuthorizationId,
                    $"PayPal returned status {authorization.Status}");
            }
            order.RecordAuthorization(authorization.AuthorizationId, authorization.Status,
                authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var capture = await _payPal.CaptureAsync(authorization.AuthorizationId, total,
            order.PaymentReference, $"capture-{order.PaymentReference}", cancellationToken);
        EnsureMoney(capture.Amount, capture.Currency, total, "capture");
        order.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.Fee,
            capture.Net, capture.CreatedAt);
        await _db.SaveChangesAsync(cancellationToken);
        EnsureCaptureCompleted(capture.Status);
        return Map(order);
    }

    public async Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var lease = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            return Map(order);
        }
        if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId is null)
        {
            throw new PaymentConflictException(
                $"Order {orderId} can only be cancelled after authorization and before fulfilment.");
        }

        await _payPal.VoidAsync(order.Payment.AuthorizationId,
            $"void-{order.PaymentReference}", cancellationToken);
        order.MarkCancelled("VOIDED");
        await _db.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<RefundResult> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        ValidateIdempotencyKey(idempotencyKey);
        using var lease = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureOwned(order, buyerId);
        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded) ||
            order.Payment?.CaptureId is null || order.Payment.CapturedAmount is null)
        {
            throw new PaymentConflictException(
                $"Order {orderId} must be fulfilled before it can be refunded.");
        }

        var existing = order.Payment.Refunds.SingleOrDefault(x =>
            x.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            var current = await _payPal.GetRefundAsync(existing.PayPalRefundId, cancellationToken);
            EnsureMoney(current.Amount, current.Currency, existing.Amount, "refund");
            order.UpdateRefund(idempotencyKey, current.Status, current.Amount);
            await _db.SaveChangesAsync(cancellationToken);
            return RefundResultFor(order, existing.PayPalRefundId, current.Status, current.Amount);
        }

        var captured = order.Payment.CapturedAmount.Value;
        var remaining = captured - order.Payment.RefundedAmount();
        var refundAmount = amount ?? remaining;
        refundAmount = EnsureCentAmount(refundAmount);
        if (refundAmount <= 0 || refundAmount > remaining)
        {
            throw new PaymentValidationException(
                $"Refund amount must be greater than zero and no more than {remaining:F2} {_payPal.Currency}.");
        }

        var refund = await _payPal.RefundAsync(order.Payment.CaptureId, refundAmount,
            order.PaymentReference, $"refund-{order.PaymentReference}-{idempotencyKey}",
            cancellationToken);
        EnsureMoney(refund.Amount, refund.Currency, refundAmount, "refund");
        order.AddRefund(idempotencyKey, refund.RefundId, refund.Status, refund.Amount,
            refund.CreatedAt);
        await _db.SaveChangesAsync(cancellationToken);
        return RefundResultFor(order, refund.RefundId, refund.Status, refund.Amount);
    }

    public async Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        var orders = await OrderQuery()
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(Map).ToList();
    }

    public async Task<PaymentMethodView> SavePaymentMethodAsync(string buyerId, CardInput card,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        ValidateCard(card);
        var external = await _payPal.SaveCardAsync(MerchantCustomerId(buyerId), card,
            Guid.NewGuid().ToString("N"), cancellationToken);
        var method = new SavedPaymentMethod(buyerId, external.VaultId, external.CustomerId,
            external.Brand, external.LastDigits, external.Expiry);
        _db.SavedPaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return Map(method);
    }

    public async Task<IReadOnlyList<PaymentMethodView>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        return await _db.SavedPaymentMethods
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PaymentMethodView(x.Id, x.Brand, x.LastDigits, x.Expiry, x.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        RequireBuyer(buyerId);
        using var lease = await _operationLock.AcquireAsync($"method:{paymentMethodId}",
            cancellationToken);
        var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
            x => x.Id == paymentMethodId && x.BuyerId == buyerId, cancellationToken);
        if (method is null || method.IsDeleted)
        {
            throw new PaymentResourceNotFoundException("Saved payment method not found.");
        }

        await _payPal.DeletePaymentTokenAsync(method.PayPalVaultId, cancellationToken);
        method.MarkDeleted();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw new PaymentValidationException("The reconciliation 'from' value must be before 'to'.");
        }

        var payPalTransactions = new List<PayPalTransaction>();
        var segmentStart = from;
        while (segmentStart < to)
        {
            var segmentEnd = segmentStart.AddDays(31);
            if (segmentEnd > to)
            {
                segmentEnd = to;
            }

            var page = 1;
            while (true)
            {
                var result = await _payPal.SearchTransactionsAsync(segmentStart, segmentEnd,
                    page, cancellationToken);
                payPalTransactions.AddRange(result.Transactions);
                if (page >= result.TotalPages)
                {
                    break;
                }
                page++;
            }
            segmentStart = segmentEnd;
        }

        payPalTransactions = payPalTransactions
            .GroupBy(TransactionIdentity)
            .Select(x => x.First())
            .ToList();

        var orders = await OrderQuery()
            .Where(x => x.Payment != null)
            .ToListAsync(cancellationToken);
        var localTransactions = BuildLocalTransactions(orders)
            .Where(x => x.TransactionTime >= from && x.TransactionTime <= to)
            .ToList();
        var localByPayPalId = localTransactions.ToDictionary(x => x.PayPalId,
            StringComparer.OrdinalIgnoreCase);
        var orderByInvoice = orders.ToDictionary(x => $"eshop-{x.PaymentReference}",
            StringComparer.OrdinalIgnoreCase);
        var matchedLocalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<PayPalTransactionView>();

        foreach (var transaction in payPalTransactions)
        {
            LocalTransactionView? local = null;
            Order? invoiceOrder = null;
            if (localByPayPalId.TryGetValue(transaction.TransactionId, out var exact))
            {
                local = exact;
                matchedLocalIds.Add(exact.PayPalId);
            }
            else if (transaction.InvoiceId is not null)
            {
                orderByInvoice.TryGetValue(transaction.InvoiceId, out invoiceOrder);
            }

            rows.Add(new PayPalTransactionView(
                transaction.TransactionId,
                transaction.EventCode,
                transaction.Status,
                transaction.TransactionTime,
                transaction.Amount,
                transaction.Currency,
                transaction.Fee,
                transaction.InvoiceId,
                local?.OrderId ?? invoiceOrder?.Id,
                local is not null ? "Matched" : invoiceOrder is not null ? "OrderMatched" : "PayPalOnly"));
        }

        var localOnly = localTransactions
            .Where(x => !matchedLocalIds.Contains(x.PayPalId))
            .ToList();
        return new ReconciliationView(from, to, rows, localOnly);
    }

    private IQueryable<Order> OrderQuery() => _db.Orders
        .Include(x => x.OrderItems)
        .ThenInclude(x => x.ItemOrdered)
        .Include(x => x.Payment)
        .ThenInclude(x => x!.Refunds);

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await OrderQuery().SingleOrDefaultAsync(x => x.Id == orderId,
            cancellationToken);
        return order ?? throw new PaymentResourceNotFoundException("Order not found.");
    }

    private OrderView Map(Order order)
    {
        PaymentView? payment = null;
        if (order.Payment is not null)
        {
            payment = new PaymentView(
                order.Payment.PayPalOrderId,
                order.Payment.AuthorizationId,
                order.Payment.AuthorizationStatus,
                order.Payment.AuthorizedAmount,
                order.Payment.AuthorizationExpiresAt,
                order.Payment.CaptureId,
                order.Payment.CaptureStatus,
                order.Payment.CapturedAmount,
                order.Payment.PayPalFee,
                order.Payment.NetAmount,
                order.Payment.RefundedAmount(),
                order.Payment.Refunds.OrderBy(x => x.CreatedAt).Select(x => new RefundView(
                    x.PayPalRefundId, x.Status, x.Amount, x.CreatedAt, x.IdempotencyKey)).ToList());
        }

        return new OrderView(
            order.Id,
            order.OrderDate,
            order.Status.ToString(),
            order.Total(),
            _payPal.Currency,
            order.OrderItems.Select(x => new OrderItemView(x.ItemOrdered.CatalogItemId,
                x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
            payment);
    }

    private static PaymentMethodView Map(SavedPaymentMethod method) => new(
        method.Id, method.Brand, method.LastDigits, method.Expiry, method.CreatedAt);

    private RefundResult RefundResultFor(Order order, string refundId, string status, decimal amount)
    {
        var refunded = order.Payment!.RefundedAmount();
        var refundable = Math.Max(0, order.Payment.CapturedAmount!.Value - refunded);
        return new RefundResult(refundId, status, amount, refunded, refundable);
    }

    private IEnumerable<LocalTransactionView> BuildLocalTransactions(IEnumerable<Order> orders)
    {
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            if (payment.AuthorizationId is not null)
            {
                yield return new LocalTransactionView(order.Id, "Authorization",
                    payment.AuthorizationId, payment.AuthorizationStatus, payment.AuthorizedAt,
                    payment.AuthorizedAmount, _payPal.Currency);
            }
            if (payment.CaptureId is not null)
            {
                yield return new LocalTransactionView(order.Id, "Capture", payment.CaptureId,
                    payment.CaptureStatus, payment.CapturedAt, payment.CapturedAmount,
                    _payPal.Currency);
            }
            foreach (var refund in payment.Refunds)
            {
                yield return new LocalTransactionView(order.Id, "Refund", refund.PayPalRefundId,
                    refund.Status, refund.CreatedAt, refund.Amount, _payPal.Currency);
            }
        }
    }

    private static string TransactionIdentity(PayPalTransaction transaction) => string.Join("|",
        transaction.TransactionId,
        transaction.EventCode,
        transaction.TransactionTime?.ToString("O", CultureInfo.InvariantCulture),
        transaction.Amount?.ToString(CultureInfo.InvariantCulture));

    private static bool AuthorizationIsStale(PayPalAuthorization authorization) =>
        authorization.CreatedAt is not null &&
        authorization.CreatedAt.Value.Add(AuthorizationHonorPeriod) <= DateTimeOffset.UtcNow;

    private static PaymentConflictException CannotRenew(string authorizationId, string reason) =>
        new($"PayPal authorization {authorizationId} can no longer be renewed: {reason}. " +
            "Ask the shopper to place and authorize a new order before fulfilment.");

    private static void EnsureCaptureCompleted(string status)
    {
        if (!string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentConflictException(
                $"PayPal capture is {status}; the order has not been marked fulfilled. Retry fulfilment after resolving the capture status in PayPal.");
        }
    }

    private void EnsureMoney(decimal actual, string currency, decimal expected, string operation)
    {
        if (actual != expected || !string.Equals(currency, _payPal.Currency,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentProcessorException(
                $"PayPal {operation} amount {actual:F2} {currency} does not match order total {expected:F2} {_payPal.Currency}.");
        }
    }

    private static decimal EnsureCentAmount(decimal amount)
    {
        if (amount != decimal.Round(amount, 2, MidpointRounding.AwayFromZero))
        {
            throw new PaymentValidationException("Amounts must have no more than two decimal places.");
        }
        return amount;
    }

    private static void EnsureOwned(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentResourceNotFoundException("Order not found.");
        }
    }

    private static void RequireBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("The authenticated token has no user identity.");
        }
    }

    private static void ValidateShipping(ShippingAddressInput address)
    {
        if (string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw new PaymentValidationException(
                "Shipping street, city, country, and zipCode are required.");
        }
    }

    private static void ValidateCard(CardInput card)
    {
        var number = PayPalClient.NormalizeCardNumber(card.Number);
        if (string.IsNullOrWhiteSpace(card.Name) || number.Length is < 13 or > 19 ||
            !number.All(char.IsDigit) || !PassesLuhn(number) ||
            !Regex.IsMatch(card.Expiry ?? string.Empty, @"^[0-9]{4}-(0[1-9]|1[0-2])$") ||
            !Regex.IsMatch(card.SecurityCode ?? string.Empty, @"^[0-9]{3,4}$") ||
            card.BillingAddress is null ||
            string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.City) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.PostalCode) ||
            card.BillingAddress.CountryCode?.Length != 2)
        {
            throw new PaymentValidationException("The card details are invalid.");
        }

        if (DateOnly.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry))
        {
            var currentMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            if (expiry < currentMonth)
            {
                throw new PaymentValidationException("The card is expired.");
            }
        }
    }

    private static bool PassesLuhn(string value)
    {
        var sum = 0;
        var alternate = false;
        for (var i = value.Length - 1; i >= 0; i--)
        {
            var digit = value[i] - '0';
            if (alternate)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    private static void ValidateIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64 ||
            key.Any(x => x < 0x21 || x > 0x7e))
        {
            throw new PaymentValidationException(
                "idempotencyKey is required and must contain 1 to 64 printable ASCII characters without spaces.");
        }
    }

    private static string MerchantCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return "eshop_" + Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }
}
