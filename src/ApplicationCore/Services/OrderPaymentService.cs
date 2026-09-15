using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan MaxAuthorizationLifetime = TimeSpan.FromDays(29);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            throw new OrderPaymentException("A signed-in shopper is required.", 401, "UNAUTHENTICATED");
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            throw new OrderPaymentException("At least one catalog item is required.", 400, "EMPTY_ORDER");
        }

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0)
            {
                throw new OrderPaymentException("Item quantity must be greater than zero.", 400, "INVALID_QUANTITY");
            }
        }

        var catalogItemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        if (catalogItems.Count != catalogItemIds.Length)
        {
            var missing = catalogItemIds.Except(catalogItems.Select(c => c.Id));
            throw new OrderPaymentException($"Catalog item(s) not found: {string.Join(", ", missing)}.", 404, "CATALOG_ITEM_NOT_FOUND");
        }

        var orderItems = request.Items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var shipping = request.ShippingAddress ?? new Address("123 Eshop Street", "San Jose", "CA", "United States", "95131");
        var order = new Order(request.BuyerId, shipping, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(PayOrderRequest request, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(request.OrderId, cancellationToken);
        EnsureBuyer(order, request.BuyerId);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            _logger.LogInformation("Pay is idempotent for order {OrderId} in status {Status}.", order.Id, order.Status);
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new OrderPaymentException($"Order {order.Id} is cancelled and cannot be paid.", 409, "INVALID_ORDER_STATE");
        }

        CardPaymentDetails? card = request.Card;
        string? vaultId = null;

        if (request.PaymentMethodId.HasValue)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpecification(request.PaymentMethodId.Value, request.BuyerId),
                cancellationToken);
            if (saved == null)
            {
                throw new OrderPaymentException("Saved payment method was not found.", 404, "PAYMENT_METHOD_NOT_FOUND");
            }

            vaultId = saved.PayPalPaymentTokenId;
        }
        else if (card == null)
        {
            throw new OrderPaymentException("Provide card details or a saved paymentMethodId.", 400, "PAYMENT_SOURCE_REQUIRED");
        }

        var currency = _payPal.Currency;
        var amount = PayPalMoneyFormatter.Round(order.Total(), currency);
        var invoiceId = InvoiceId(order);
        var result = await _payPal.AuthorizeAsync(new AuthorizePaymentCommand(
            amount,
            currency,
            invoiceId,
            order.Id.ToString(),
            $"pay-{order.PaymentAttemptKey}",
            card,
            vaultId), cancellationToken);

        order.RecordAuthorization(
            result.CheckoutOrderId,
            result.AuthorizationId,
            result.AuthorizationStatus,
            result.Amount,
            result.Currency,
            result.ExpirationTime,
            result.CreateTime,
            invoiceId);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            _logger.LogInformation("Fulfil is idempotent for order {OrderId} in status {Status}.", order.Id, order.Status);
            return order;
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new OrderPaymentException($"Order {order.Id} has no authorization to capture.", 409, "NOT_AUTHORIZED");
        }

        var currency = order.Currency ?? _payPal.Currency;
        var amount = order.AuthorizedAmount ?? PayPalMoneyFormatter.Round(order.Total(), currency);
        var authorizationId = order.PayPalAuthorizationId;

        AuthorizationSnapshot snapshot;
        try
        {
            snapshot = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (OrderPaymentException ex) when (ex.StatusCode == 404)
        {
            throw new OrderPaymentException(
                $"PayPal authorization {authorizationId} is no longer available. Ask the shopper to pay again before fulfilment.",
                409,
                "AUTHORIZATION_MISSING");
        }

        order.RefreshAuthorization(snapshot.AuthorizationId, snapshot.Status, snapshot.ExpirationTime, snapshot.CreateTime);

        if (string.Equals(snapshot.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(snapshot.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new OrderPaymentException(
                $"PayPal authorization {authorizationId} is {snapshot.Status} and cannot be captured. Ask the shopper to pay again.",
                409,
                "AUTHORIZATION_UNUSABLE");
        }

        if (string.Equals(snapshot.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            var existing = await _payPal.GetCaptureAsync(order.PayPalCaptureId, cancellationToken);
            order.RecordCapture(existing.CaptureId, existing.CaptureStatus, existing.CapturedAmount, existing.PaypalFee, existing.NetProceeds, existing.Currency);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }

        if (NeedsReauthorization(snapshot))
        {
            authorizationId = await RenewAuthorizationAsync(order, snapshot, amount, currency, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(authorizationId, amount, currency, InvoiceId(order), $"fulfil-{order.PaymentAttemptKey}", cancellationToken);
        }
        catch (OrderPaymentException ex) when (IsExpiredAuthorization(ex))
        {
            authorizationId = await RenewAuthorizationAsync(order, snapshot, amount, currency, cancellationToken);
            capture = await _payPal.CaptureAsync(authorizationId, amount, currency, InvoiceId(order), $"fulfil-{order.PaymentAttemptKey}-retry", cancellationToken);
        }

        if ((capture.PaypalFee == null || capture.NetProceeds == null) && !string.IsNullOrEmpty(capture.CaptureId))
        {
            try
            {
                var detailed = await _payPal.GetCaptureAsync(capture.CaptureId, cancellationToken);
                capture = detailed;
            }
            catch (Exception)
            {
                _logger.LogWarning("Could not refresh capture {CaptureId} for fee details.", capture.CaptureId);
            }
        }

        order.RecordCapture(capture.CaptureId, capture.CaptureStatus, capture.CapturedAmount, capture.PaypalFee, capture.NetProceeds, capture.Currency);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new OrderPaymentException("A fulfilled order cannot be cancelled; issue a refund instead.", 409, "ALREADY_FULFILLED");
        }

        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId) && order.Status == OrderStatus.Authorized)
        {
            try
            {
                await _payPal.VoidAuthorizationAsync(order.PayPalAuthorizationId, $"cancel-{order.PaymentAttemptKey}", cancellationToken);
                order.RecordCancellation("VOIDED");
            }
            catch (OrderPaymentException ex) when (ex.ErrorCode == "ALREADY_VOIDED" || ex.StatusCode == 422 || ex.StatusCode == 409)
            {
                _logger.LogInformation("Authorization {AuthorizationId} already released for order {OrderId}.", order.PayPalAuthorizationId, order.Id);
                order.RecordCancellation("VOIDED");
            }
        }
        else
        {
            order.RecordCancellation(order.PayPalAuthorizationStatus);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(RefundOrderRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new OrderPaymentException("An idempotencyKey is required for refunds.", 400, "IDEMPOTENCY_KEY_REQUIRED");
        }

        var order = await GetRequiredOrderAsync(request.OrderId, cancellationToken);
        EnsureBuyer(order, request.BuyerId);

        var existing = order.FindRefundByIdempotencyKey(request.IdempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId) || order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new OrderPaymentException("Only a fulfilled capture can be refunded.", 409, "NOT_REFUNDABLE");
        }

        var currency = order.Currency ?? _payPal.Currency;
        var remaining = order.RemainingRefundable(currency);
        var amount = request.Amount.HasValue
            ? PayPalMoneyFormatter.Round(request.Amount.Value, currency)
            : remaining;

        if (amount <= 0m)
        {
            throw new OrderPaymentException("There is no remaining captured amount to refund.", 400, "NOTHING_TO_REFUND");
        }

        if (amount > remaining)
        {
            throw new OrderPaymentException(
                $"Refund amount {PayPalMoneyFormatter.Format(amount, currency)} exceeds remaining refundable {PayPalMoneyFormatter.Format(remaining, currency)}.",
                400,
                "REFUND_EXCEEDS_CAPTURE");
        }

        var result = await _payPal.RefundAsync(
            order.PayPalCaptureId,
            amount,
            currency,
            PaypalIdempotencyKey(order.PaymentAttemptKey, request.IdempotencyKey),
            cancellationToken);
        var refund = order.RecordRefund(result.PayPalRefundId, result.Status, result.Amount, result.Currency, request.IdempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<Order?> GetBuyerOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            return null;
        }

        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderPaymentException("Order was not found.", 404, "ORDER_NOT_FOUND");
        }

        return order;
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardPaymentDetails card, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new OrderPaymentException("A signed-in shopper is required.", 401, "UNAUTHENTICATED");
        }

        var vaulted = await _payPal.VaultCardAsync(card, SanitiseCustomerId(buyerId), $"vault-{Guid.NewGuid():N}", cancellationToken);
        var last4 = vaulted.LastDigits;
        if (string.IsNullOrEmpty(last4))
        {
            var digits = new string(card.Number.Where(char.IsDigit).ToArray());
            last4 = digits.Length >= 4 ? digits[^4..] : digits;
        }

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.PayPalCustomerId,
            vaulted.Brand,
            last4,
            vaulted.Expiry,
            vaulted.CardholderName ?? card.Name);

        await _paymentMethodRepository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId),
            cancellationToken);
        if (saved == null)
        {
            throw new OrderPaymentException("Saved payment method was not found.", 404, "PAYMENT_METHOD_NOT_FOUND");
        }

        try
        {
            await _payPal.DeletePaymentTokenAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (OrderPaymentException ex) when (ex.StatusCode == 404)
        {
            _logger.LogInformation("PayPal payment token {TokenId} was already deleted.", saved.PayPalPaymentTokenId);
        }

        await _paymentMethodRepository.DeleteAsync(saved, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new OrderPaymentException("`to` must be on or after `from`.", 400, "INVALID_DATE_RANGE");
        }

        var paypalTransactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersInDateRangeSpecification(from, to), cancellationToken);
        var matchedOrderIds = new HashSet<int>();
        var matches = new List<ReconciliationMatch>();

        foreach (var txn in paypalTransactions)
        {
            var orderId = MatchOrderId(txn, orders);
            if (orderId.HasValue)
            {
                matchedOrderIds.Add(orderId.Value);
                matches.Add(new ReconciliationMatch(txn, orderId, "matched"));
            }
            else
            {
                matches.Add(new ReconciliationMatch(txn, null, "paypal_only"));
            }
        }

        var eshopOnly = orders
            .Where(o => HasPayPalFootprint(o) && !matchedOrderIds.Contains(o.Id))
            .Select(o => o.Id)
            .ToList();

        return new ReconciliationReport(from, to, matches, eshopOnly);
    }

    private async Task<string> RenewAuthorizationAsync(Order order, AuthorizationSnapshot snapshot, decimal amount, string currency, CancellationToken cancellationToken)
    {
        if (!CanReauthorize(snapshot))
        {
            var expiry = snapshot.ExpirationTime?.ToString("u") ?? "unknown";
            throw new OrderPaymentException(
                $"PayPal authorization {snapshot.AuthorizationId} is stale (expires {expiry}) and can no longer be renewed. PayPal allows a single reauthorization between day 4 and day 29 after the original hold. Ask the shopper to pay again, then fulfil the new authorization.",
                409,
                "AUTHORIZATION_NOT_RENEWABLE");
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(snapshot.AuthorizationId, amount, currency, $"reauth-{order.PaymentAttemptKey}", cancellationToken);
            order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime, renewed.CreateTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Reauthorized order {OrderId}: {OldId} -> {NewId}.", order.Id, snapshot.AuthorizationId, renewed.AuthorizationId);
            return renewed.AuthorizationId;
        }
        catch (OrderPaymentException ex)
        {
            throw new OrderPaymentException(
                $"PayPal authorization {snapshot.AuthorizationId} could not be renewed ({ex.ErrorCode ?? ex.Message}). Ask the shopper to pay again; do not capture this hold.",
                409,
                "AUTHORIZATION_NOT_RENEWABLE");
        }
    }

    private static bool NeedsReauthorization(AuthorizationSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        if (snapshot.ExpirationTime.HasValue && snapshot.ExpirationTime.Value <= now.AddHours(1))
        {
            return true;
        }

        if (snapshot.CreateTime.HasValue && now - snapshot.CreateTime.Value >= AuthorizationHonorPeriod)
        {
            return true;
        }

        return false;
    }

    private static bool CanReauthorize(AuthorizationSnapshot snapshot)
    {
        var now = DateTimeOffset.UtcNow;
        if (snapshot.CreateTime.HasValue)
        {
            var age = now - snapshot.CreateTime.Value;
            if (age > MaxAuthorizationLifetime)
            {
                return false;
            }
        }

        if (snapshot.ExpirationTime.HasValue && snapshot.ExpirationTime.Value.AddDays(1) < now)
        {
            return false;
        }

        return true;
    }

    private static bool IsExpiredAuthorization(OrderPaymentException ex)
    {
        var code = (ex.ErrorCode ?? string.Empty).ToUpperInvariant();
        return code.Contains("EXPIRED") || code.Contains("AUTHORIZATION_VOIDED") || code == "INVALID_RESOURCE_ID";
    }

    private async Task<Order> GetRequiredOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new OrderPaymentException("Order was not found.", 404, "ORDER_NOT_FOUND");
        }

        return order;
    }

    private static void EnsureBuyer(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderPaymentException("Order was not found.", 404, "ORDER_NOT_FOUND");
        }
    }

    private static string InvoiceId(Order order) =>
        order.PayPalInvoiceId ?? $"ESHOP-{order.PaymentAttemptKey}";

    private static string PaypalIdempotencyKey(string attemptKey, string callerKey)
    {
        var combined = $"{attemptKey}:{callerKey}";
        if (combined.Length <= 108)
        {
            return combined;
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(combined)));
        var prefix = $"{attemptKey}:";
        return prefix + hash[..Math.Min(hash.Length, 108 - prefix.Length)];
    }

    private static string SanitiseCustomerId(string buyerId)
    {
        var cleaned = new string(buyerId.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or '@').ToArray());
        if (cleaned.Length > 64)
        {
            cleaned = cleaned[..64];
        }

        return string.IsNullOrEmpty(cleaned) ? "eshop-buyer" : cleaned;
    }

    private static bool HasPayPalFootprint(Order order) =>
        !string.IsNullOrEmpty(order.PayPalCheckoutOrderId) ||
        !string.IsNullOrEmpty(order.PayPalAuthorizationId) ||
        !string.IsNullOrEmpty(order.PayPalCaptureId);

    private static int? MatchOrderId(PayPalReportedTransaction txn, IReadOnlyList<Order> orders)
    {
        foreach (var order in orders)
        {
            if (!string.IsNullOrEmpty(txn.InvoiceId) &&
                (string.Equals(txn.InvoiceId, order.PayPalInvoiceId, StringComparison.OrdinalIgnoreCase) ||
                 txn.InvoiceId.StartsWith($"ESHOP-{order.PaymentAttemptKey}", StringComparison.OrdinalIgnoreCase)))
            {
                return order.Id;
            }

            if (IdsEqual(txn.TransactionId, order.PayPalCaptureId) ||
                IdsEqual(txn.TransactionId, order.PayPalAuthorizationId) ||
                IdsEqual(txn.ReferenceId, order.PayPalCaptureId) ||
                IdsEqual(txn.ReferenceId, order.PayPalAuthorizationId) ||
                order.Refunds.Any(r => IdsEqual(txn.TransactionId, r.PayPalRefundId)))
            {
                return order.Id;
            }
        }

        return null;
    }

    private static bool IdsEqual(string? left, string? right) =>
        !string.IsNullOrEmpty(left) &&
        !string.IsNullOrEmpty(right) &&
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
