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
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderCheckoutService : IOrderCheckoutService
{
    private static readonly Address DefaultShipTo = new("123 Main St", "Seattle", "WA", "US", "98101");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<OrderCheckoutService> _logger;
    private readonly PayPalOptions _payPalOptions;

    public OrderCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPal,
        IAppLogger<OrderCheckoutService> logger,
        IOptions<PayPalOptions> payPalOptions)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _logger = logger;
        _payPalOptions = payPalOptions.Value;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        if (items == null || items.Count == 0)
            throw new PaymentException("At least one catalog item is required.");

        var quantities = new Dictionary<int, int>();
        foreach (var line in items)
        {
            if (line.CatalogItemId <= 0)
                throw new PaymentException("Catalog item id must be positive.");
            if (line.Quantity <= 0)
                throw new PaymentException("Quantity must be greater than zero.");

            if (quantities.ContainsKey(line.CatalogItemId))
                quantities[line.CatalogItemId] += line.Quantity;
            else
                quantities[line.CatalogItemId] = line.Quantity;
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);

        if (catalogItems.Count != quantities.Count)
        {
            var found = catalogItems.Select(c => c.Id).ToHashSet();
            var missing = quantities.Keys.First(id => !found.Contains(id));
            throw new PaymentException($"Catalog item {missing} was not found.", 404);
        }

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
            throw new PaymentException("This order has been cancelled.", 409);
        if (order.IsCaptured() || order.Status == OrderStatus.Authorized)
        {
            _logger.LogInformation("Pay is idempotent for order {OrderId} in status {Status}.", order.Id, order.Status);
            return order;
        }

        if (card == null && paymentMethodId == null)
            throw new PaymentException("Provide card details or a saved paymentMethodId.");
        if (card != null && paymentMethodId != null)
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.");

        string? vaultId = null;
        string? paypalCustomerId = null;
        CardPaymentSource? cardToCharge = null;

        if (paymentMethodId != null)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId.Value, buyerId), cancellationToken);
            if (method == null)
                throw new PaymentException("Saved payment method was not found.", 404);

            vaultId = method.PayPalVaultId;
            paypalCustomerId = method.PayPalCustomerId;
        }
        else
        {
            cardToCharge = NormalizeCard(card!);
        }

        var currency = RequireCurrency();
        var amount = PayPalMoney.Round(order.Total(), currency);
        if (amount <= 0)
            throw new PaymentException("Order total must be greater than zero.");

        var invoiceId = InvoiceIdFor(order.Id);
        var requestId = $"eshop-pay-{order.Id}-{Guid.NewGuid():N}";

        _logger.LogInformation("Authorizing order {OrderId} for {Amount} {Currency}.", order.Id, PayPalMoney.Format(amount, currency), currency);

        var result = await _payPal.AuthorizeAsync(
            invoiceId,
            amount,
            currency,
            cardToCharge,
            vaultId,
            paypalCustomerId,
            requestId,
            cancellationToken);

        order.RecordAuthorization(
            result.PayPalOrderId,
            result.PayPalOrderStatus,
            result.AuthorizationId,
            result.AuthorizationStatus,
            result.ExpirationTime,
            result.CreateTime,
            result.Currency,
            invoiceId);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.IsCaptured())
        {
            _logger.LogInformation("Fulfil is idempotent for order {OrderId}.", order.Id);
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
            throw new PaymentException("A cancelled order cannot be fulfilled.", 409);
        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
            throw new PaymentException("The order must be authorized before it can be fulfilled.", 409);

        var currency = order.PaymentCurrency ?? RequireCurrency();
        var amount = PayPalMoney.Round(order.Total(), currency);
        var authorizationId = order.PayPalAuthorizationId;

        authorizationId = await EnsureFreshAuthorizationAsync(order, authorizationId, amount, currency, cancellationToken);

        var invoiceId = order.PaymentInvoiceId ?? $"ESHOP-{order.Id}";
        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(
                authorizationId,
                amount,
                currency,
                invoiceId,
                $"eshop-fulfil-{order.Id}",
                cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.HasIssueContaining("EXPIRED", "AUTHORIZATION_EXPIRED", "CANNOT_BE_CAPTURED", "AUTH_EXPIRED"))
        {
            _logger.LogWarning("Capture failed because authorization {AuthorizationId} is stale; renewing.", authorizationId);
            authorizationId = await RenewAuthorizationAsync(order, authorizationId, amount, currency, cancellationToken);
            capture = await _payPal.CaptureAsync(
                authorizationId,
                amount,
                currency,
                invoiceId,
                $"eshop-fulfil-{order.Id}-retry",
                cancellationToken);
        }

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PayPalFee,
            capture.NetAmount,
            capture.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
            return order;
        if (order.IsCaptured())
            throw new PaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);

        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            try
            {
                await _payPal.VoidAsync(order.PayPalAuthorizationId, $"eshop-cancel-{order.Id}", cancellationToken);
            }
            catch (PayPalApiException ex) when (ex.StatusCode == 422 && ex.HasIssueContaining("VOIDED", "ALREADY_VOIDED", "AUTHORIZATION_VOIDED"))
            {
                _logger.LogInformation("Authorization {AuthorizationId} was already voided.", order.PayPalAuthorizationId);
            }
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> RefundAsync(
        string buyerId,
        int orderId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentException("A refund idempotency key is required.");

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        if (!order.IsCaptured() || string.IsNullOrEmpty(order.PayPalCaptureId))
            throw new PaymentException("Refunds are only allowed after the order has been fulfilled.", 409);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
            return order;

        var currency = order.PaymentCurrency ?? RequireCurrency();
        var remaining = order.RemainingRefundable();
        var refundAmount = amount.HasValue
            ? PayPalMoney.Round(amount.Value, currency)
            : remaining;

        if (refundAmount <= 0)
            throw new PaymentException("There is no remaining captured amount to refund.");
        if (refundAmount > remaining)
            throw new PaymentException($"Refund of {PayPalMoney.Format(refundAmount, currency)} exceeds the remaining refundable amount of {PayPalMoney.Format(remaining, currency)}.");

        decimal? paypalAmount = refundAmount == remaining && remaining == order.CapturedAmount
            ? null
            : refundAmount;

        var result = await _payPal.RefundAsync(
            order.PayPalCaptureId,
            paypalAmount,
            currency,
            idempotencyKey,
            cancellationToken);

        order.RecordRefund(result.RefundId, result.Status, result.Amount == 0 ? refundAmount : result.Amount, result.Currency, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentSource card,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        var normalized = NormalizeCard(card);
        var paypalCustomerId = PayPalCustomerId.ForBuyer(buyerId);
        var requestId = $"eshop-vault-{Guid.NewGuid():N}";

        var vaulted = await _payPal.VaultCardAsync(paypalCustomerId, normalized, requestId, cancellationToken);

        var method = new SavedPaymentMethod(
            buyerId,
            vaulted.PayPalCustomerId ?? paypalCustomerId,
            vaulted.VaultId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.Name);

        return await _paymentMethodRepository.AddAsync(method, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        var method = await _paymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), cancellationToken);
        if (method == null)
            throw new PaymentException("Saved payment method was not found.", 404);

        try
        {
            await _payPal.DeleteVaultedCardAsync(method.PayPalVaultId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("PayPal vault token {VaultId} was already absent.", method.PayPalVaultId);
        }

        await _paymentMethodRepository.DeleteAsync(method, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new PaymentException("`to` must be on or after `from`.");

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        var matched = new List<ReconciliationMatch>();
        var matchedTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var order = FindMatchingOrder(orders, txn);
            if (order == null)
                continue;

            matchedOrderIds.Add(order.Id);
            matchedTransactionIds.Add(txn.TransactionId);
            matched.Add(new ReconciliationMatch(
                order.Id,
                txn.TransactionId,
                txn.InvoiceId,
                DescribeMatch(order, txn)));
        }

        var paypalOnly = paypalTransactions
            .Where(t => !matchedTransactionIds.Contains(t.TransactionId))
            .ToList();

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && OrderTouchesRange(o, from, to))
            .Select(o => new ReconciliationEshopEntry(
                o.Id,
                o.Status.ToString(),
                o.PayPalOrderId,
                o.PayPalAuthorizationId,
                o.PayPalCaptureId))
            .ToList();

        return new ReconciliationReport(from, to, matched, paypalOnly, eshopOnly);
    }

    private async Task<string> EnsureFreshAuthorizationAsync(
        Order order,
        string authorizationId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        PayPalAuthorizationSnapshot snapshot;
        try
        {
            snapshot = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Could not load authorization {AuthorizationId}: {Message}", authorizationId, ex.Message);
            return authorizationId;
        }

        order.RefreshAuthorization(snapshot.Id, snapshot.Status, snapshot.ExpirationTime, snapshot.CreateTime);

        if (string.Equals(snapshot.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase))
            return snapshot.Id;
        if (string.Equals(snapshot.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(snapshot.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"The PayPal authorization is {snapshot.Status} and cannot be captured. Ask the shopper to pay again.",
                409);
        }

        if (IsAuthorizationStale(snapshot, DateTimeOffset.UtcNow))
            return await RenewAuthorizationAsync(order, snapshot.Id, amount, currency, cancellationToken);

        return snapshot.Id;
    }

    private async Task<string> RenewAuthorizationAsync(
        Order order,
        string authorizationId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                amount,
                currency,
                $"eshop-reauth-{order.Id}",
                cancellationToken);

            order.RefreshAuthorization(renewed.Id, renewed.Status, renewed.ExpirationTime, renewed.CreateTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Renewed authorization for order {OrderId} to {AuthorizationId}.", order.Id, renewed.Id);
            return renewed.Id;
        }
        catch (PayPalApiException ex) when (ex.HasIssueContaining(
            "EXPIRED",
            "AUTHORIZATION_EXPIRED",
            "MAX_NUMBER_OF_REAUTHORIZATION",
            "REAUTHORIZATION_NOT_ALLOWED",
            "CANNOT_BE_REAUTHORIZED",
            "AUTHORIZATION_VOIDED",
            "INVALID_RESOURCE_ID"))
        {
            throw new PaymentException(
                "The PayPal authorization has expired and cannot be renewed. Ask the shopper to place and pay for a new order, then fulfil that order instead.",
                409);
        }
        catch (PayPalApiException ex) when (ex.HasIssueContaining("TOO_SOON", "HONOR_PERIOD", "REAUTHORIZATION_WINDOW"))
        {
            _logger.LogInformation("Reauthorization was not needed yet for {AuthorizationId}; capturing original hold.", authorizationId);
            return authorizationId;
        }
    }

    private static bool IsAuthorizationStale(PayPalAuthorizationSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.ExpirationTime is DateTimeOffset expiration && now >= expiration)
            return true;
        if (snapshot.CreateTime is DateTimeOffset created && now >= created.AddDays(3))
            return true;
        return false;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
            throw new PaymentException($"Order {orderId} was not found.", 404);
        return order;
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!order.BelongsTo(buyerId))
            throw new PaymentException("Order was not found.", 404);
        return order;
    }

    private static void EnsureBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new PaymentException("The caller identity is missing.", 401);
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_payPalOptions.Currency))
            throw new PaymentException("PayPal:Currency is not configured.");
        return _payPalOptions.Currency.ToUpperInvariant();
    }

    private static string InvoiceIdFor(int orderId) => $"ESHOP-{orderId}-{Guid.NewGuid():N}";

    private static CardPaymentSource NormalizeCard(CardPaymentSource card)
    {
        var number = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (number.Length is < 13 or > 19)
            throw new PaymentException("Card number must be 13 to 19 digits.");
        if (string.IsNullOrWhiteSpace(card.Expiry))
            throw new PaymentException("Card expiry (YYYY-MM) is required.");

        var billing = card.BillingAddress ?? new CardBillingAddress(
            "123 Main St",
            null,
            "San Jose",
            "CA",
            "95131",
            "US");

        if (string.IsNullOrWhiteSpace(billing.CountryCode))
            throw new PaymentException("Billing address countryCode is required.");

        return card with
        {
            Number = number,
            Name = string.IsNullOrWhiteSpace(card.Name) ? "John Doe" : card.Name,
            BillingAddress = billing with { CountryCode = billing.CountryCode.ToUpperInvariant() }
        };
    }

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            if (IdsMatch(order, txn.TransactionId) || IdsMatch(order, txn.ReferenceId))
                return order;

            var invoice = order.PaymentInvoiceId ?? $"ESHOP-{order.Id}";
            if (!string.IsNullOrEmpty(txn.InvoiceId) &&
                (string.Equals(txn.InvoiceId, invoice, StringComparison.OrdinalIgnoreCase)
                 || txn.InvoiceId.StartsWith($"ESHOP-{order.Id}", StringComparison.OrdinalIgnoreCase)))
                return order;
            if (!string.IsNullOrEmpty(txn.CustomField) &&
                (string.Equals(txn.CustomField, invoice, StringComparison.OrdinalIgnoreCase)
                 || txn.CustomField.StartsWith($"ESHOP-{order.Id}", StringComparison.OrdinalIgnoreCase)))
                return order;
        }

        return null;
    }

    private static bool IdsMatch(Order order, string? paypalId)
    {
        if (string.IsNullOrEmpty(paypalId))
            return false;
        if (string.Equals(order.PayPalOrderId, paypalId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(order.PayPalAuthorizationId, paypalId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(order.PayPalCaptureId, paypalId, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var refund in order.Refunds)
        {
            if (string.Equals(refund.PayPalRefundId, paypalId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string DescribeMatch(Order order, PayPalReportedTransaction txn)
    {
        if (string.Equals(order.PayPalCaptureId, txn.TransactionId, StringComparison.OrdinalIgnoreCase))
            return "captureId";
        if (string.Equals(order.PayPalAuthorizationId, txn.TransactionId, StringComparison.OrdinalIgnoreCase))
            return "authorizationId";
        if (string.Equals(order.PayPalOrderId, txn.TransactionId, StringComparison.OrdinalIgnoreCase))
            return "paypalOrderId";
        if (order.Refunds.Any(r => string.Equals(r.PayPalRefundId, txn.TransactionId, StringComparison.OrdinalIgnoreCase)))
            return "refundId";
        if (!string.IsNullOrEmpty(txn.InvoiceId))
            return "invoiceId";
        if (!string.IsNullOrEmpty(txn.CustomField))
            return "customField";
        return "referenceId";
    }

    private static bool OrderTouchesRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        if (order.OrderDate >= from && order.OrderDate <= to)
            return true;
        if (order.PayPalAuthorizationCreated is DateTimeOffset created && created >= from && created <= to)
            return true;
        return order.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to);
    }
}
