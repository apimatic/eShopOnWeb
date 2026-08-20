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
    public const string InvoicePrefix = "ESHOP-";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IPaymentSettings _paymentSettings;
    private readonly IUriComposer _uriComposer;
    private readonly IOrderOperationLock _operationLock;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPal,
        IPaymentSettings paymentSettings,
        IUriComposer uriComposer,
        IOrderOperationLock operationLock)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _paymentSettings = paymentSettings;
        _uriComposer = uriComposer;
        _operationLock = operationLock;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shipTo,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("A signed-in shopper is required to place an order.");
        }

        if (items is null || items.Count == 0)
        {
            throw new PaymentValidationException("At least one catalog item is required.");
        }

        foreach (var line in items)
        {
            if (line.CatalogItemId <= 0)
            {
                throw new PaymentValidationException("Catalog item id must be a positive integer.");
            }

            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException($"Quantity for catalog item {line.CatalogItemId} must be at least 1.");
            }
        }

        var quantities = items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(quantities.Keys.ToArray()),
            cancellationToken);

        if (catalogItems.Count != quantities.Count)
        {
            var found = catalogItems.Select(c => c.Id).ToHashSet();
            var missing = quantities.Keys.Where(id => !found.Contains(id)).ToArray();
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var address = shipTo ?? new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken)
    {
        if (card is null && paymentMethodId is null)
        {
            throw new PaymentValidationException("Provide card details or a saved paymentMethodId.");
        }

        if (card is not null && paymentMethodId is not null)
        {
            throw new PaymentValidationException("Provide either card details or a saved paymentMethodId, not both.");
        }

        await using var gate = await _operationLock.AcquireAsync(orderId, cancellationToken);
        var order = await GetOrderForBuyerAsync(orderId, buyerId, cancellationToken);

        if (order.Status == OrderStatus.Authorized)
        {
            return order;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentConflictException($"Order {orderId} cannot be paid from status {order.Status}.");
        }

        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
        {
            throw new PaymentValidationException("Order total must be greater than zero.");
        }

        var currency = _paymentSettings.Currency;
        var invoiceId = $"ESHOP-{order.Id}-{Guid.NewGuid():N}";
        var customId = CustomIdFor(order.Id);
        var requestId = $"eshop-pay-{order.Id}-{Guid.NewGuid():N}";

        PayPalAuthorizationResult authorization;
        if (paymentMethodId is int savedId)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(savedId, buyerId),
                cancellationToken);
            if (saved is null)
            {
                throw new PaymentNotFoundException("Saved payment method was not found.");
            }

            authorization = await _payPal.AuthorizeVaultedCardAsync(
                amount, currency, invoiceId, customId, requestId, saved.PayPalPaymentTokenId, cancellationToken);
        }
        else
        {
            authorization = await _payPal.AuthorizeCardAsync(
                amount, currency, invoiceId, customId, requestId, card!, cancellationToken);
        }

        if (authorization.Amount != amount)
        {
            throw new PaymentException(
                $"PayPal authorized {authorization.Amount} {currency} but the order total is {amount} {currency}.");
        }

        order.RecordAuthorization(
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.ExpiresAt,
            authorization.Amount,
            currency,
            invoiceId);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var gate = await _operationLock.AcquireAsync(orderId, cancellationToken);
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled ||
            order.Status == OrderStatus.PartiallyRefunded ||
            order.Status == OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.AuthorizationId))
        {
            throw new PaymentConflictException($"Order {orderId} cannot be fulfilled from status {order.Status}. It must be authorized first.");
        }

        var currency = order.Currency ?? _paymentSettings.Currency;
        var amount = order.AuthorizedAmount ?? decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        var authorizationId = await EnsureCaptureReadyAuthorizationAsync(order, amount, currency, cancellationToken);
        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(
                authorizationId,
                amount,
                currency,
                InvoiceIdFor(order),
                $"eshop-fulfil-{order.Id}",
                cancellationToken);
        }
        catch (PaymentException captureEx) when (captureEx is not AuthorizationCannotBeRenewedException)
        {
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(
                    authorizationId,
                    order.PayPalOrderId ?? string.Empty,
                    amount,
                    currency,
                    $"eshop-reauth-{order.Id}",
                    cancellationToken);
                order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
                await _orderRepository.UpdateAsync(order, cancellationToken);

                capture = await _payPal.CaptureAsync(
                    renewed.AuthorizationId,
                    amount,
                    currency,
                    InvoiceIdFor(order),
                    $"eshop-fulfil-{order.Id}-renewed",
                    cancellationToken);
            }
            catch (PaymentException reauthEx)
            {
                throw new AuthorizationCannotBeRenewedException(
                    $"The hold on order {order.Id} could not be captured and PayPal could not renew it " +
                    $"({reauthEx.Message}). Ask the shopper to pay again so a new authorization can be created. " +
                    $"Original capture error: {captureEx.Message}");
            }
        }

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetAmount);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var gate = await _operationLock.AcquireAsync(orderId, cancellationToken);
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(order.AuthorizationId))
        {
            await _payPal.VoidAuthorizationAsync(order.AuthorizationId, $"eshop-cancel-{order.Id}", cancellationToken);
            order.RecordCancellation("VOIDED");
        }
        else if (order.Status == OrderStatus.AwaitingPayment)
        {
            order.RecordCancellation(null);
        }
        else
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be cancelled from status {order.Status}. After fulfilment, issue a refund instead.");
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("A refund idempotencyKey is required.");
        }

        await using var gate = await _operationLock.AcquireAsync(orderId, cancellationToken);
        var order = await GetOrderForBuyerAsync(orderId, buyerId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException($"Order {orderId} cannot be refunded from status {order.Status}.");
        }

        if (string.IsNullOrEmpty(order.CaptureId))
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
        }

        var remaining = order.RemainingRefundableAmount();
        var refundAmount = amount.HasValue
            ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
            : remaining;

        if (refundAmount <= 0)
        {
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentValidationException(
                $"Refund of {refundAmount} exceeds the remaining refundable amount {remaining}.");
        }

        var currency = order.Currency ?? _paymentSettings.Currency;
        var paypalRefund = await _payPal.RefundAsync(
            order.CaptureId,
            refundAmount == remaining && !amount.HasValue ? null : refundAmount,
            currency,
            $"eshop-refund-{order.Id}-{idempotencyKey}",
            cancellationToken);

        var recorded = order.RecordRefund(
            paypalRefund.RefundId,
            paypalRefund.Amount > 0 ? paypalRefund.Amount : refundAmount,
            currency,
            paypalRefund.Status,
            idempotencyKey);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return recorded;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId),
            cancellationToken);
        return orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new PaymentValidationException("'to' must be greater than or equal to 'from'.");
        }

        var paypalTransactions = await _payPal.ListAllTransactionsAsync(from, to, cancellationToken);
        var eshopOrders = await _orderRepository.ListAsync(
            new OrdersInDateRangeSpecification(from, to),
            cancellationToken);

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var order = MatchOrder(eshopOrders, txn);
            if (order is null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matched.Add(new ReconciliationMatch(order.Id, txn));
            matchedOrderIds.Add(order.Id);
        }

        var eshopOnly = eshopOrders
            .Where(o => !matchedOrderIds.Contains(o.Id) && HasPayPalFootprint(o))
            .Select(o => new ReconciliationEshopEntry(
                o.Id,
                o.Status.ToString(),
                o.PayPalOrderId,
                o.AuthorizationId,
                o.CaptureId,
                o.OrderDate,
                o.Total()))
            .ToList();

        return new ReconciliationReport(from, to, matched, paypalOnly, eshopOnly);
    }

    private async Task<string> EnsureCaptureReadyAuthorizationAsync(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var authorizationId = order.AuthorizationId!;
        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException ex)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The authorization for order {order.Id} could not be loaded from PayPal ({ex.Message}). " +
                "Ask the shopper to place and pay a new order.");
        }

        if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(details.Status, "DENIED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(details.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationCannotBeRenewedException(
                $"Authorization {authorizationId} is {details.Status} and cannot be captured. " +
                "Ask the shopper to pay again.");
        }

        var stale = details.ExpiresAt.HasValue && details.ExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(5);
        if (!stale)
        {
            return authorizationId;
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                order.PayPalOrderId ?? string.Empty,
                amount,
                currency,
                $"eshop-reauth-{order.Id}",
                cancellationToken);

            order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The hold on order {order.Id} has expired and PayPal could not renew it ({ex.Message}). " +
                "Ask the shopper to pay again so a new authorization can be created.");
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId),
            cancellationToken);
        if (order is null)
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Order> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentForbiddenException("You cannot access another shopper's order.");
        }

        return order;
    }

    private static string CustomIdFor(int orderId) => $"{InvoicePrefix}{orderId}";

    private static string InvoiceIdFor(Order order) =>
        string.IsNullOrEmpty(order.PayPalInvoiceId) ? CustomIdFor(order.Id) : order.PayPalInvoiceId;

    private static bool HasPayPalFootprint(Order order) =>
        !string.IsNullOrEmpty(order.PayPalOrderId) ||
        !string.IsNullOrEmpty(order.AuthorizationId) ||
        !string.IsNullOrEmpty(order.CaptureId);

    private static Order? MatchOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            var invoice = CustomIdFor(order.Id);
            if (!string.IsNullOrEmpty(txn.InvoiceId) &&
                (string.Equals(txn.InvoiceId, invoice, StringComparison.OrdinalIgnoreCase) ||
                 (!string.IsNullOrEmpty(order.PayPalInvoiceId) &&
                  string.Equals(txn.InvoiceId, order.PayPalInvoiceId, StringComparison.OrdinalIgnoreCase)) ||
                 txn.InvoiceId.StartsWith(invoice + "-", StringComparison.OrdinalIgnoreCase)))
            {
                return order;
            }

            if (!string.IsNullOrEmpty(txn.CustomField) &&
                string.Equals(txn.CustomField, invoice, StringComparison.OrdinalIgnoreCase))
            {
                return order;
            }

            if (IdsEqual(txn.TransactionId, order) || IdsEqual(txn.PayPalReferenceId, order))
            {
                return order;
            }
        }

        return null;
    }

    private static bool IdsEqual(string? paypalId, Order order)
    {
        if (string.IsNullOrEmpty(paypalId))
        {
            return false;
        }

        return string.Equals(paypalId, order.PayPalOrderId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paypalId, order.AuthorizationId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paypalId, order.CaptureId, StringComparison.OrdinalIgnoreCase) ||
               order.Refunds.Any(r => string.Equals(paypalId, r.PayPalRefundId, StringComparison.OrdinalIgnoreCase));
    }
}
