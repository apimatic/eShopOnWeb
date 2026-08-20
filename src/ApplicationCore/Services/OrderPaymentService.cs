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

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _items;
    private readonly IRepository<SavedPaymentMethod> _savedCards;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orders,
        IRepository<CatalogItem> items,
        IRepository<SavedPaymentMethod> savedCards,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        IAppLogger<OrderPaymentService> logger)
    {
        _orders = orders;
        _items = items;
        _savedCards = savedCards;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shipTo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required.", 401);
        }

        if (items == null || items.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.", 400);
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _items.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException("Quantity must be greater than zero.", 400);
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.", 400);
            }

            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "placeholder.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri),
                catalogItem.Price,
                line.Quantity));
        }

        var address = shipTo ?? new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var order = new Order(buyerId, address, orderItems);
        order.AssignCurrency(_payPal.Currency);
        await _orders.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);
        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", 409);
        }

        string? vaultId = null;
        if (paymentMethodId.HasValue)
        {
            if (card != null)
            {
                throw new PaymentException("Send either card details or a paymentMethodId, not both.", 400);
            }

            var saved = await _savedCards.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId.Value, buyerId), cancellationToken);
            if (saved == null)
            {
                throw new PaymentException("Saved payment method was not found.", 404);
            }

            vaultId = saved.PayPalPaymentTokenId;
        }
        else if (card == null)
        {
            throw new PaymentException("Card details or a saved payment method is required.", 400);
        }

        var result = await _payPal.AuthorizeAsync(
            order.Id,
            order.Total(),
            card,
            vaultId,
            $"eshop-pay-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}",
            cancellationToken);

        if (!AmountEquals(result.Amount, order.Total()))
        {
            throw new PaymentException(
                $"PayPal authorized {result.Amount} {result.Currency} but the order total is {order.Total().ToString("0.00", CultureInfo.InvariantCulture)} {order.Currency}.",
                502);
        }

        order.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.Expiration);
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Authorized order {0} with PayPal authorization {1}.", order.Id, result.AuthorizationId);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new PaymentException("Only an authorized order can be fulfilled.", 409);
        }

        var authorizationId = await EnsureFreshAuthorizationAsync(order, cancellationToken);
        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(authorizationId, order.Total(), $"eshop-fulfil-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}", cancellationToken);
        }
        catch (PaymentException)
        {
            authorizationId = await EnsureFreshAuthorizationAsync(order, cancellationToken, forceRefresh: true);
            try
            {
                capture = await _payPal.CaptureAsync(authorizationId, order.Total(), $"eshop-fulfil-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}", cancellationToken);
            }
            catch (PaymentException)
            {
                throw new PaymentException(
                    "The PayPal authorization is no longer valid and cannot be renewed. Ask the shopper to pay again before fulfilling.",
                    409);
            }
        }

        order.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PaypalFee, capture.NetAmount);
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Fulfilled order {0} with PayPal capture {1}.", order.Id, capture.CaptureId);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled. Create a refund instead.", 409);
        }

        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId) && order.Status == OrderStatus.Authorized)
        {
            await _payPal.VoidAsync(order.PayPalAuthorizationId, $"eshop-cancel-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}", cancellationToken);
        }

        order.RecordCancellation();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {0}.", order.Id);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("An idempotencyKey is required for refunds.", 400);
        }

        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!isAdministrator)
        {
            order.EnsureOwnedBy(buyerId);
        }

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId) || order.CapturedAmount is null)
        {
            throw new PaymentException("Only a fulfilled order can be refunded.", 409);
        }

        var refundAmount = amount ?? order.RemainingRefundable();
        if (refundAmount <= 0)
        {
            throw new PaymentException("There is no remaining captured amount to refund.", 400);
        }

        if (refundAmount > order.RemainingRefundable())
        {
            throw new PaymentException(
                $"Refund amount {refundAmount} exceeds remaining refundable amount {order.RemainingRefundable()}.", 400);
        }

        var currency = order.Currency ?? _payPal.Currency;
        var result = await _payPal.RefundAsync(
            order.PayPalCaptureId,
            amount.HasValue ? refundAmount : null,
            currency,
            idempotencyKey,
            cancellationToken);

        var refund = order.RecordRefund(result.RefundId, result.Amount, result.Currency, result.Status, idempotencyKey);
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Refunded {0} on order {1} as {2}.", result.Amount, order.Id, result.RefundId);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public Task<Order> GetMyOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
        => LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken = default)
    {
        if (card == null)
        {
            throw new PaymentException("Card details are required.", 400);
        }

        var vault = await _payPal.VaultCardAsync(
            ToPayPalCustomerId(buyerId),
            card,
            $"eshop-vault-{Guid.NewGuid():N}",
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vault.PaymentTokenId,
            vault.Brand,
            vault.LastDigits,
            vault.Expiry,
            vault.CardholderName ?? card.Name);
        await _savedCards.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _savedCards.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _savedCards.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), cancellationToken);
        if (saved == null)
        {
            throw new PaymentException("Saved payment method was not found.", 404);
        }

        await _payPal.DeleteVaultedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        await _savedCards.DeleteAsync(saved, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("`to` must be on or after `from`.", 400);
        }

        var paypal = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var local = await _orders.ListAsync(new OrdersInDateRangeSpecification(from, to), cancellationToken);

        var localIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in local)
        {
            AddId(localIds, order.PayPalOrderId);
            AddId(localIds, order.PayPalAuthorizationId);
            AddId(localIds, order.PayPalCaptureId);
            foreach (var refund in order.Refunds)
            {
                AddId(localIds, refund.PayPalRefundId);
            }
        }

        var paypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mismatches = new List<ReconciliationMismatch>();
        foreach (var txn in paypal)
        {
            AddId(paypalIds, txn.TransactionId);
            AddId(paypalIds, txn.PaypalReferenceId);

            var matchesLocal =
                ContainsId(localIds, txn.TransactionId) ||
                ContainsId(localIds, txn.PaypalReferenceId) ||
                MatchesInvoice(local, txn.InvoiceId) ||
                MatchesCustom(local, txn.CustomField);

            if (!matchesLocal)
            {
                mismatches.Add(new ReconciliationMismatch(
                    "paypal_only",
                    txn.TransactionId,
                    $"PayPal transaction {txn.TransactionId} ({txn.EventCode}/{txn.Status}) was not matched to an eShop order."));
            }
        }

        foreach (var order in local)
        {
            if (order.Status == OrderStatus.PendingPayment)
            {
                continue;
            }

            var known = new[] { order.PayPalCaptureId, order.PayPalAuthorizationId, order.PayPalOrderId }
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();
            if (known.Count > 0 && known.All(id => !paypalIds.Contains(id!)))
            {
                mismatches.Add(new ReconciliationMismatch(
                    "eshop_only",
                    order.Id.ToString(CultureInfo.InvariantCulture),
                    $"eShop order {order.Id} has PayPal ids that were not present in PayPal reporting for this range."));
            }
        }

        return new ReconciliationReport(from, to, paypal, local, mismatches);
    }

    private async Task<string> EnsureFreshAuthorizationAsync(
        Order order,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        var details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        if (!forceRefresh && !IsAuthorizationStale(details))
        {
            return authorizationId;
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId, order.Total(), $"eshop-reauth-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}", cancellationToken);
            order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Expiration);
            await _orders.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Reauthorized order {0} as {1}.", order.Id, renewed.AuthorizationId);
            return renewed.AuthorizationId;
        }
        catch (PaymentException)
        {
            throw new PaymentException(
                "The PayPal authorization has expired and could not be renewed. Ask the shopper to pay again before fulfilling.",
                409);
        }
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }

        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        order.EnsureOwnedBy(buyerId);
        return order;
    }

    private static bool IsAuthorizationStale(PayPalAuthorizationDetails details)
    {
        if (string.Equals(details.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(details.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return details.Expiration.HasValue && details.Expiration.Value <= DateTimeOffset.UtcNow.AddMinutes(5);
    }

    private static bool AmountEquals(string paypalAmount, decimal expected)
    {
        return decimal.TryParse(paypalAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
               && parsed == decimal.Round(expected, 2);
    }

    private static string ToPayPalCustomerId(string buyerId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant();
        return hash[..22];
    }

    private static void AddId(HashSet<string> set, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            set.Add(id);
        }
    }

    private static bool ContainsId(HashSet<string> set, string? id)
        => !string.IsNullOrWhiteSpace(id) && set.Contains(id);

    private static bool MatchesInvoice(IEnumerable<Order> orders, string? invoiceId)
    {
        if (string.IsNullOrWhiteSpace(invoiceId))
        {
            return false;
        }

        return orders.Any(o =>
            string.Equals(invoiceId, $"ORDER-{o.Id}", StringComparison.OrdinalIgnoreCase) ||
            invoiceId.StartsWith($"eshop-pay-{o.Id}-", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesCustom(IEnumerable<Order> orders, string? custom)
        => !string.IsNullOrWhiteSpace(custom)
           && orders.Any(o => string.Equals(custom, o.Id.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase));
}
