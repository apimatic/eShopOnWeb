using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the order payment lifecycle against the payment gateway:
/// authorize (hold) at checkout, capture at fulfilment, void on cancel, refund on return.
/// All operations are idempotent in effect: repeating them returns the current state
/// instead of moving money twice.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan AuthorizationExpirySafetyMargin = TimeSpan.FromMinutes(5);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PayPalSettings _payPalSettings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        PayPalSettings payPalSettings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _payPalSettings = payPalSettings;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentOperationException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentOperationException("Item quantities must be positive.");
        }

        var catalogItemsSpecification = new CatalogItemsSpecification(lines.Select(l => l.CatalogItemId).Distinct().ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification, cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new PaymentOperationException($"Catalog item {line.CatalogItemId} does not exist.");
            }
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? new Address(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty), items);
        order.SetCurrency(_payPalSettings.Currency);

        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation($"Created order {order.Id} for buyer {buyerId} with total {order.Total()} {order.Currency}");
        return order;
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null == savedPaymentMethodId is null)
        {
            throw new PaymentOperationException("Provide either card details or a saved payment method id, not both.");
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            // Idempotent retry of a successful pay: report the existing hold.
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentOperationException($"Order {order.Id} cannot be paid while in status {order.Status}.");
        }

        string? vaultTokenId = null;
        if (savedPaymentMethodId is not null)
        {
            var savedMethod = await _paymentMethodRepository.GetByIdAsync(savedPaymentMethodId.Value, cancellationToken);
            if (savedMethod is null || savedMethod.BuyerId != buyerId)
            {
                throw new SavedPaymentMethodNotFoundException(savedPaymentMethodId.Value);
            }
            vaultTokenId = savedMethod.PayPalPaymentTokenId;
        }

        order.BeginPaymentAttempt();
        var amount = order.Total();
        var currency = order.Currency ?? _payPalSettings.Currency;
        var idempotencyKey = $"eshop-pay-{order.PaymentReference}";

        GatewayAuthorization authorization;
        try
        {
            authorization = await _paymentGateway.AuthorizeAsync(
                invoiceId: order.PaymentReference!,
                customId: order.Id.ToString(),
                amount: amount,
                currency: currency,
                card: card,
                vaultPaymentTokenId: vaultTokenId,
                idempotencyKey: idempotencyKey,
                cancellationToken: cancellationToken);
        }
        catch (PayerActionRequiredException)
        {
            throw;
        }
        catch (PaymentGatewayException ex) when (ex.HttpStatusCode >= 400 && ex.HttpStatusCode < 500)
        {
            _logger.LogWarning($"PayPal declined payment for order {order.Id}: {ex.ErrorName} {ex.Message} (debug {ex.DebugId})");
            throw new PaymentDeclinedException($"PayPal declined the payment: {ex.Message}");
        }

        order.MarkPaymentAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId,
            authorization.Status, authorization.ExpiresAt);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id} authorized: PayPal authorization {authorization.AuthorizationId} for {amount} {currency}");
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled)
        {
            // Idempotent retry of a successful fulfilment.
            return order;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || order.PayPalAuthorizationId is null)
        {
            throw new PaymentOperationException(
                $"Order {order.Id} cannot be fulfilled while in status {order.Status} (payment: {order.PaymentStatus}).");
        }

        var amount = order.Total();
        var currency = order.Currency ?? _payPalSettings.Currency;

        var authorization = await _paymentGateway.GetAuthorizationAsync(order.PayPalAuthorizationId, cancellationToken);
        var usable = authorization.Status == "CREATED"
            && (authorization.ExpiresAt is null || authorization.ExpiresAt > DateTimeOffset.UtcNow + AuthorizationExpirySafetyMargin);

        if (!usable)
        {
            authorization = await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
        }

        GatewayCapture capture;
        try
        {
            capture = await _paymentGateway.CaptureAsync(authorization.AuthorizationId, amount, currency,
                invoiceId: order.PaymentReference!,
                idempotencyKey: $"eshop-capture-{order.PaymentReference}",
                cancellationToken: cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning($"PayPal capture failed for order {order.Id}: {ex.ErrorName} {ex.Message} (debug {ex.DebugId})");
            throw;
        }

        order.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id} fulfilled: captured {capture.GrossAmount} {capture.Currency} (fee {capture.PayPalFee}, net {capture.NetAmount}), capture {capture.CaptureId}");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentOperationException(
                $"Order {order.Id} has already been fulfilled and its payment captured; issue a refund instead of cancelling.");
        }

        if (order.PaymentStatus == PaymentStatus.Authorized && order.PayPalAuthorizationId is not null)
        {
            var authorization = await _paymentGateway.GetAuthorizationAsync(order.PayPalAuthorizationId, cancellationToken);
            if (authorization.Status != "VOIDED")
            {
                await _paymentGateway.VoidAsync(order.PayPalAuthorizationId,
                    idempotencyKey: $"eshop-void-{order.PaymentReference}",
                    cancellationToken: cancellationToken);
            }
            order.MarkVoided();
            _logger.LogInformation($"Order {order.Id} cancelled: PayPal authorization {order.PayPalAuthorizationId} voided, hold released.");
        }
        else
        {
            order.MarkCancelledWithoutPayment();
            _logger.LogInformation($"Order {order.Id} cancelled before payment; no funds were held.");
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, PaymentRefund Refund, bool AlreadyExisted)> RefundAsync(string buyerId, int orderId,
        decimal? amount, string idempotencyKey, string? note, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        var existing = order.PaymentRefunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return (order, existing, true);
        }

        if (order.PaymentStatus is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded) || order.PayPalCaptureId is null)
        {
            throw new PaymentOperationException(
                $"Order {order.Id} has no captured payment to refund (status: {order.Status}, payment: {order.PaymentStatus}).");
        }

        var currency = order.Currency ?? _payPalSettings.Currency;
        var refundAmount = amount ?? order.RefundableAmount();
        refundAmount = decimal.Round(refundAmount, 2, MidpointRounding.AwayFromZero);
        if (refundAmount <= 0 || refundAmount > order.RefundableAmount())
        {
            throw new PaymentOperationException(
                $"Refund amount {refundAmount} is invalid; the remaining refundable amount on order {order.Id} is {order.RefundableAmount()} {currency}.");
        }

        var gatewayRefund = await _paymentGateway.RefundAsync(order.PayPalCaptureId, refundAmount, currency,
            customId: order.Id.ToString(),
            idempotencyKey: idempotencyKey,
            note: note,
            cancellationToken: cancellationToken);

        var refund = new PaymentRefund(gatewayRefund.RefundId, gatewayRefund.Amount, gatewayRefund.Currency,
            idempotencyKey, gatewayRefund.Status, note);
        order.ApplyRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id} refunded {gatewayRefund.Amount} {gatewayRefund.Currency}: PayPal refund {gatewayRefund.RefundId}");
        return (order, refund, false);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentsSpecification(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new PaymentOperationException("The 'to' timestamp must be after the 'from' timestamp.");
        }

        var transactions = await _paymentGateway.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpecification(), cancellationToken);

        var report = new ReconciliationReport { From = from, To = to };
        var seenTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transaction in transactions)
        {
            seenTransactionIds.Add(transaction.TransactionId);
            var match = FindMatchingOrder(orders, transaction);
            report.Rows.Add(new ReconciliationRow
            {
                MatchStatus = match is null ? "UnknownToEShop" : "Matched",
                OrderId = match?.Id,
                PayPalTransactionId = transaction.TransactionId,
                EventCode = transaction.EventCode,
                TransactionStatus = transaction.Status,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Fee = transaction.Fee,
                TransactionTime = transaction.Time,
                InvoiceId = transaction.InvoiceId,
                CustomField = transaction.CustomField,
                Note = match is null ? "PayPal knows this transaction; no eShop order references it." : null
            });
        }

        foreach (var order in orders)
        {
            var missingIds = new List<string>();
            if (order.PayPalCaptureId is not null && !seenTransactionIds.Contains(order.PayPalCaptureId))
            {
                missingIds.Add(order.PayPalCaptureId);
            }
            missingIds.AddRange(order.PaymentRefunds
                .Where(r => !seenTransactionIds.Contains(r.PayPalRefundId))
                .Select(r => r.PayPalRefundId));

            foreach (var missingId in missingIds)
            {
                report.Rows.Add(new ReconciliationRow
                {
                    MatchStatus = "MissingInPayPal",
                    OrderId = order.Id,
                    PayPalTransactionId = missingId,
                    Note = "eShop recorded this transaction; it is not (yet) in PayPal's transaction report. " +
                           "PayPal reporting lags live activity, so recent transactions may legitimately be absent."
                });
            }
        }

        report.MatchedCount = report.Rows.Count(r => r.MatchStatus == "Matched");
        report.UnknownToEShopCount = report.Rows.Count(r => r.MatchStatus == "UnknownToEShop");
        report.MissingInPayPalCount = report.Rows.Count(r => r.MatchStatus == "MissingInPayPal");
        return report;
    }

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, GatewayTransaction transaction)
    {
        return orders.FirstOrDefault(o =>
            o.PayPalCaptureId == transaction.TransactionId
            || o.PayPalAuthorizationId == transaction.TransactionId
            || o.PaymentRefunds.Any(r => r.PayPalRefundId == transaction.TransactionId))
        ?? orders.FirstOrDefault(o =>
            transaction.CustomField is not null && transaction.CustomField == o.Id.ToString())
        ?? orders.FirstOrDefault(o =>
            transaction.InvoiceId is not null && transaction.InvoiceId == o.PaymentReference);
    }

    private async Task<GatewayAuthorizationInfo> RenewAuthorizationAsync(Order order, decimal amount, string currency,
        CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(order.PayPalAuthorizationId!, amount, currency,
                idempotencyKey: $"eshop-reauthorize-{order.PaymentReference}",
                cancellationToken: cancellationToken);
            order.MarkAuthorizationRenewed(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Order {order.Id}: stale authorization renewed as {renewed.AuthorizationId}.");
            return renewed;
        }
        catch (PaymentGatewayException ex)
        {
            order.MarkAuthorizationUnrecoverable();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogWarning($"Order {order.Id}: authorization {order.PayPalAuthorizationId} could not be renewed: {ex.ErrorName} {ex.Message} (debug {ex.DebugId})");
            throw new AuthorizationUnrecoverableException(
                $"The PayPal authorization for order {order.Id} has expired and could not be renewed ({ex.Message}). " +
                "No funds are held. Ask the shopper to pay again via POST /api/orders/{orderId}/pay, then fulfil the order.");
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentDetailsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.BuyerId != buyerId)
        {
            // Do not reveal that the order exists under another shopper.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }
}
