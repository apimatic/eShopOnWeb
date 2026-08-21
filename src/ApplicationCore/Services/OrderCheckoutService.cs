using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderCheckoutService : IOrderCheckoutService
{
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan MaxReauthorizeAge = TimeSpan.FromDays(29);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentsClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderCheckoutService> _logger;

    public OrderCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalPaymentsClient payPal,
        IUriComposer uriComposer,
        IAppLogger<OrderCheckoutService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        PlaceOrderShipping? shipping,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new CheckoutException(401, "The caller is not authenticated.");
        }

        if (items is null || items.Count == 0)
        {
            throw new CheckoutException(400, "At least one catalog item is required.");
        }

        foreach (var item in items)
        {
            if (item.CatalogItemId <= 0 || item.Quantity <= 0)
            {
                throw new CheckoutException(400, "Each item must include a catalogItemId and a quantity greater than zero.");
            }
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var requested in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == requested.CatalogItemId)
                ?? throw new CheckoutException(400, $"Catalog item {requested.CatalogItemId} was not found.");

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrWhiteSpace(pictureUri))
            {
                pictureUri = "placeholder";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity));
        }

        var address = ToAddress(shipping);
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

        if (order.PaymentStatus is OrderPaymentStatus.Authorized
            or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new CheckoutException(409, $"Order {order.Id} is cancelled and cannot be paid.");
        }

        var usingSavedCard = paymentMethodId.HasValue;
        var usingRawCard = card is not null;
        if (usingSavedCard == usingRawCard)
        {
            throw new CheckoutException(400, "Provide either card details or a paymentMethodId, not both or neither.");
        }

        string? vaultId = null;
        CardPaymentSource? cardToCharge = null;
        if (usingSavedCard)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdForBuyerSpec(paymentMethodId!.Value, buyerId), cancellationToken);
            if (saved is null)
            {
                throw new CheckoutException(404, "Saved payment method was not found.");
            }

            vaultId = saved.PayPalPaymentTokenId;
        }
        else
        {
            cardToCharge = NormalizeCard(card!);
        }

        var currency = _payPal.Currency;
        var amount = Money.Round(order.Total());
        var requestId = order.EnsurePayRequestId();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        try
        {
            var result = await _payPal.AuthorizeCardPaymentAsync(
                amount,
                currency,
                customId: $"eshop-{order.Id.ToString(CultureInfo.InvariantCulture)}",
                invoiceId: InvoiceIdFor(order.Id, requestId),
                requestId,
                cardToCharge,
                vaultId,
                cancellationToken);

            order.MarkAuthorized(
                result.OrderId,
                result.OrderStatus,
                result.AuthorizationId,
                result.AuthorizationStatus,
                result.Amount,
                result.Currency,
                result.CreatedAt,
                result.ExpirationTime);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Authorized PayPal order {result.OrderId} for eShop order {order.Id}.");
            return order;
        }
        catch (PayerActionRequiredException)
        {
            throw;
        }
        catch
        {
            order.RotatePayRequestId();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw;
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new CheckoutException(409, $"Order {order.Id} is cancelled and cannot be fulfilled.");
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized || string.IsNullOrWhiteSpace(order.PayPalAuthorizationId))
        {
            throw new CheckoutException(409, $"Order {order.Id} has not been authorized. Capture is only possible after payment.");
        }

        var currency = order.Currency ?? _payPal.Currency;
        var amount = Money.Round(order.AuthorizedAmount ?? order.Total());

        var authorization = await _payPal.GetAuthorizationAsync(order.PayPalAuthorizationId, cancellationToken);
        order.SyncAuthorizationStatus(authorization.AuthorizationStatus);

        if (string.Equals(authorization.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.AuthorizationStatus, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(409,
                $"PayPal authorization {order.PayPalAuthorizationId} is {authorization.AuthorizationStatus}. The hold cannot be captured. Ask the shopper to pay again.");
        }

        if (string.Equals(authorization.AuthorizationStatus, "CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(409,
                $"PayPal authorization {order.PayPalAuthorizationId} is already captured. Refresh payment state before retrying fulfilment.");
        }

        if (RequiresReauthorization(order, authorization))
        {
            if (!CanReauthorize(order, authorization))
            {
                throw new CheckoutException(409,
                    "The payment authorization has expired and can no longer be renewed (more than 29 days since the original hold). Ask the shopper to pay again, then fulfil the new authorization.");
            }

            _logger.LogInformation($"Renewing stale PayPal authorization {order.PayPalAuthorizationId} for order {order.Id}.");
            var renewed = await _payPal.ReauthorizeAsync(
                order.PayPalAuthorizationId,
                amount,
                currency,
                $"{order.EnsureFulfilRequestId()}-reauth",
                cancellationToken);
            order.ReplaceAuthorization(
                renewed.AuthorizationId,
                renewed.AuthorizationStatus,
                renewed.CreatedAt,
                renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                order.PayPalAuthorizationId!,
                amount,
                currency,
                order.EnsureFulfilRequestId(),
                cancellationToken);
        }
        catch (CheckoutException ex) when (ex.StatusCode is 422 or 409)
        {
            if (!CanReauthorize(order, authorization))
            {
                throw new CheckoutException(409,
                    $"PayPal could not capture authorization {order.PayPalAuthorizationId}: {ex.Message}. The hold can no longer be renewed. Ask the shopper to pay again.");
            }

            _logger.LogInformation($"Capture failed for authorization {order.PayPalAuthorizationId}; attempting renewal. {ex.Message}");
            var renewed = await _payPal.ReauthorizeAsync(
                order.PayPalAuthorizationId!,
                amount,
                currency,
                $"{order.EnsureFulfilRequestId()}-reauth-retry",
                cancellationToken);
            order.ReplaceAuthorization(
                renewed.AuthorizationId,
                renewed.AuthorizationStatus,
                renewed.CreatedAt,
                renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            capture = await _payPal.CaptureAuthorizationAsync(
                order.PayPalAuthorizationId!,
                amount,
                currency,
                $"{order.EnsureFulfilRequestId()}-after-reauth",
                cancellationToken);
        }

        order.MarkFulfilled(
            capture.CaptureId,
            capture.Status,
            capture.Amount,
            capture.PaypalFee,
            capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return order;
        }

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new CheckoutException(409, $"Order {order.Id} has already been fulfilled. Cancel is only allowed before fulfilment; issue a refund instead.");
        }

        if (order.PaymentStatus == OrderPaymentStatus.Authorized && !string.IsNullOrWhiteSpace(order.PayPalAuthorizationId))
        {
            await _payPal.VoidAuthorizationAsync(order.PayPalAuthorizationId, order.EnsureCancelRequestId(), cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<RefundOrderResult> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CheckoutException(400, "An idempotencyKey is required so a repeated refund request cannot be charged twice.");
        }

        var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return new RefundOrderResult(order, existing, true);
        }

        if (order.PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new CheckoutException(409, $"Order {order.Id} is {order.PaymentStatus} and cannot be refunded.");
        }

        if (string.IsNullOrWhiteSpace(order.PayPalCaptureId))
        {
            throw new CheckoutException(409, $"Order {order.Id} has no captured PayPal payment to refund.");
        }

        var remaining = order.RemainingRefundable();
        var refundAmount = amount.HasValue ? Money.Round(amount.Value) : remaining;
        if (refundAmount <= 0)
        {
            throw new CheckoutException(409, "There is no remaining captured amount to refund.");
        }

        if (refundAmount > remaining)
        {
            throw new CheckoutException(409,
                $"Refund of {refundAmount:0.00} exceeds the remaining refundable amount {remaining:0.00} of the captured {order.CapturedAmount:0.00}.");
        }

        var currency = order.Currency ?? _payPal.Currency;
        var paypalRefund = await _payPal.RefundCaptureAsync(
            order.PayPalCaptureId,
            refundAmount,
            currency,
            idempotencyKey,
            cancellationToken);

        var refund = order.AddRefund(
            paypalRefund.RefundId,
            paypalRefund.Status,
            paypalRefund.Amount,
            paypalRefund.Currency,
            idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return new RefundOrderResult(order, refund, false);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(
        string buyerId,
        CardPaymentSource card,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeCard(card);
        var paypalCustomerId = ToPayPalCustomerId(buyerId);
        var requestId = $"vault-{buyerId}-{Guid.NewGuid():N}";

        var vaulted = await _payPal.VaultCardAsync(normalized, buyerId, paypalCustomerId, requestId, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName ?? normalized.Name,
            vaulted.PayPalCustomerId ?? paypalCustomerId);

        await _paymentMethodRepository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdForBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            throw new CheckoutException(404, "Saved payment method was not found.");
        }

        await _payPal.DeleteVaultedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        await _paymentMethodRepository.DeleteAsync(saved, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new CheckoutException(400, "`to` must be on or after `from`.");
        }

        var paypalTransactions = await _payPal.ListAllTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInRangeSpec(from, to), cancellationToken);

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<ReconciliationPaypalOnly>();
        var matchedPaypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var order = FindMatchingOrder(orders, txn);
            if (order is null)
            {
                paypalOnly.Add(new ReconciliationPaypalOnly(
                    txn.TransactionId,
                    txn.InvoiceId,
                    txn.CustomField,
                    txn.Status,
                    txn.Amount));
                continue;
            }

            matchedOrderIds.Add(order.Id);
            matchedPaypalIds.Add(txn.TransactionId);
            matched.Add(new ReconciliationMatch(order.Id, txn.TransactionId, txn.Status, txn.Amount));
        }

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasPaypalFootprint(o))
            .Select(o => new ReconciliationEshopOnly(o.Id, o.PaymentStatus.ToString(), o.PayPalCaptureId, o.PayPalAuthorizationId))
            .ToList();

        return new ReconciliationReport(from, to, matched, paypalOnly, eshopOnly);
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        return order ?? throw new CheckoutException(404, $"Order {orderId} was not found.");
    }

    private async Task<Order> GetOwnedOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);
        if (!order.BelongsTo(buyerId))
        {
            throw new CheckoutException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private static Address ToAddress(PlaceOrderShipping? shipping)
    {
        if (shipping is null)
        {
            return new Address("123 Main St", "Seattle", "WA", "US", "98101");
        }

        return new Address(
            shipping.Street,
            shipping.City,
            shipping.State,
            shipping.Country,
            shipping.ZipCode);
    }

    private static CardPaymentSource NormalizeCard(CardPaymentSource card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) ||
            string.IsNullOrWhiteSpace(card.SecurityCode) ||
            string.IsNullOrWhiteSpace(card.Name))
        {
            throw new CheckoutException(400, "Card number, expiry, security code, and name are required.");
        }

        var number = new string(card.Number.Where(char.IsDigit).ToArray());
        var expiry = NormalizeExpiry(card.Expiry);
        var address = card.BillingAddress ?? new CardBillingAddress("US", "123 Main St", null, "San Jose", "CA", "95131");
        if (string.IsNullOrWhiteSpace(address.CountryCode))
        {
            address = address with { CountryCode = "US" };
        }

        return card with { Number = number, Expiry = expiry, BillingAddress = address };
    }

    private static string NormalizeExpiry(string expiry)
    {
        var trimmed = expiry.Trim();
        if (trimmed.Length == 7 && trimmed[4] == '-')
        {
            return trimmed;
        }

        var parts = trimmed.Split('/', '-', ' ');
        if (parts.Length == 2 && parts[0].Length is 1 or 2 && parts[1].Length is 2 or 4)
        {
            var month = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var year = parts[1].Length == 2 ? 2000 + int.Parse(parts[1], CultureInfo.InvariantCulture) : int.Parse(parts[1], CultureInfo.InvariantCulture);
            return $"{year:D4}-{month:D2}";
        }

        throw new CheckoutException(400, "Card expiry must be YYYY-MM.");
    }

    private static string InvoiceIdFor(int orderId, string requestId) => $"EW-{orderId}-{requestId}";

    private static string InvoiceIdFor(int orderId) => $"EW-{orderId}";

    private static string ToPayPalCustomerId(string buyerId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant();
        return hash[..22];
    }

    private static bool RequiresReauthorization(Order order, PayPalAuthorizationResult authorization)
    {
        var now = DateTimeOffset.UtcNow;
        if (authorization.ExpirationTime.HasValue && authorization.ExpirationTime.Value <= now)
        {
            return true;
        }

        var created = authorization.CreatedAt ?? order.AuthorizationCreatedAt;
        return created.HasValue && created.Value + HonorPeriod <= now;
    }

    private static bool CanReauthorize(Order order, PayPalAuthorizationResult authorization)
    {
        var original = order.OriginalAuthorizationCreatedAt ?? authorization.CreatedAt ?? order.AuthorizationCreatedAt;
        if (!original.HasValue)
        {
            return true;
        }

        return original.Value + MaxReauthorizeAge > DateTimeOffset.UtcNow;
    }

    private static bool HasPaypalFootprint(Order order) =>
        !string.IsNullOrWhiteSpace(order.PayPalOrderId) ||
        !string.IsNullOrWhiteSpace(order.PayPalAuthorizationId) ||
        !string.IsNullOrWhiteSpace(order.PayPalCaptureId);

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            if (IdsEqual(txn.InvoiceId, InvoiceIdFor(order.Id)) ||
                (!string.IsNullOrWhiteSpace(order.PayRequestId) && IdsEqual(txn.InvoiceId, InvoiceIdFor(order.Id, order.PayRequestId))))
            {
                return order;
            }

            if (IdsEqual(txn.CustomField, $"eshop-{order.Id.ToString(CultureInfo.InvariantCulture)}") ||
                IdsEqual(txn.CustomField, order.Id.ToString(CultureInfo.InvariantCulture)))
            {
                return order;
            }

            if (IdsEqual(txn.TransactionId, order.PayPalOrderId) ||
                IdsEqual(txn.TransactionId, order.PayPalAuthorizationId) ||
                IdsEqual(txn.TransactionId, order.PayPalCaptureId) ||
                IdsEqual(txn.ReferenceId, order.PayPalOrderId) ||
                IdsEqual(txn.ReferenceId, order.PayPalAuthorizationId) ||
                IdsEqual(txn.ReferenceId, order.PayPalCaptureId) ||
                order.Refunds.Any(r => IdsEqual(txn.TransactionId, r.PayPalRefundId)))
            {
                return order;
            }
        }

        return null;
    }

    private static bool IdsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

}

internal static class Money
{
    public static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
