using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentOrderService : IPaymentOrderService
{
    // A shipping address is required by the order model; when the API caller omits one we place the
    // order with this neutral placeholder rather than inventing shopper data.
    private static readonly Address UnspecifiedAddress =
        new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentOrderService> _logger;

    public PaymentOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IUriComposer uriComposer,
        IPayPalPaymentGateway payPal,
        PayPalSettings settings,
        IAppLogger<PaymentOrderService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _settings = settings;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        }

        // Amounts always come from catalog prices on the server, never from the caller.
        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new NotFoundException($"Catalog item {line.CatalogItemId} was not found.");

            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri)
                ? "eCatalog-item-default.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? UnspecifiedAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation("Order {0} placed for buyer, awaiting payment.", order.Id);
        return order.Id;
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent in effect: if the hold already exists (or money already moved), do nothing more.
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            if (order.Payment is not null &&
                (order.Payment.Status == PaymentStatus.Authorized || order.Payment.Status == PaymentStatus.Captured))
            {
                _logger.LogInformation("Order {0} is already paid ({1}); returning existing payment.", orderId, order.Status);
                return order;
            }

            throw new InvalidOperationException($"Order {orderId} cannot be paid from status {order.Status}.");
        }

        string? vaultId = null;
        CardDetails? cardToUse = card;

        if (savedPaymentMethodId.HasValue)
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
            var paymentMethod = buyer?.FindPaymentMethod(savedPaymentMethodId.Value)
                ?? throw new NotFoundException($"Saved card {savedPaymentMethodId.Value} was not found.");

            vaultId = paymentMethod.CardId
                ?? throw new InvalidOperationException($"Saved card {savedPaymentMethodId.Value} has no vault reference.");
            cardToUse = null;
        }
        else if (card is null)
        {
            throw new ArgumentException("A payment requires either card details or a saved card id.");
        }

        var amount = order.Total();
        var currency = _settings.Currency;

        // Stable per-order idempotency key so a double-click reuses the same PayPal authorization.
        // The order's creation timestamp keeps the key unique across runs even though in-memory order
        // ids restart at 1 while PayPal still remembers a recent PayPal-Request-Id.
        var requestId = OrderIdempotencyKey(order, "pay");
        var authorization = await _payPal.AuthorizeAsync(amount, currency, cardToUse, vaultId, requestId, cancellationToken);

        var payment = new Payment(currency, amount);
        payment.SetAuthorization(authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
        order.MarkAuthorized(payment);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} authorized (hold {1}).", orderId, authorization.AuthorizationId);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order; // idempotent
        }

        if (order.Status != OrderStatus.Authorized || order.Payment is null || order.Payment.AuthorizationId is null)
        {
            throw new InvalidOperationException($"Order {orderId} cannot be fulfilled from status {order.Status}.");
        }

        var payment = order.Payment;
        var authorizationId = payment.AuthorizationId!;

        // A hold that has gone stale must be renewed rather than failing the fulfilment outright.
        var current = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        if (IsStale(current))
        {
            _logger.LogWarning("Authorization {0} for order {1} is stale ({2}); attempting to renew.", authorizationId, orderId, current.Status);
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(authorizationId, payment.Amount, payment.Currency, OrderIdempotencyKey(order, "reauth"), cancellationToken);
                payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
                authorizationId = renewed.AuthorizationId;
            }
            catch (PayPalGatewayException ex)
            {
                throw new AuthorizationNotRenewableException(
                    $"The payment hold for order {orderId} has expired and can no longer be renewed ({ex.Message}). " +
                    "The order cannot be fulfilled until the shopper pays for it again.", ex);
            }
        }

        var capture = await _payPal.CaptureAsync(authorizationId, payment.Amount, payment.Currency, $"auth-{authorizationId}-capture", cancellationToken);
        payment.SetCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} fulfilled; captured {1} (capture {2}).", orderId, capture.GrossAmount, capture.CaptureId);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }

        if (order.Status == OrderStatus.AwaitingPayment)
        {
            // Never paid — nothing to release.
            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }

        if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId is null)
        {
            throw new InvalidOperationException($"Order {orderId} cannot be cancelled from status {order.Status}.");
        }

        await _payPal.VoidAsync(order.Payment.AuthorizationId!, cancellationToken);
        order.Payment.SetVoided();
        order.MarkCancelled();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} cancelled; hold released.", orderId);
        return order;
    }

    public async Task<Refund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = order.Payment
            ?? throw new InvalidOperationException($"Order {orderId} has no captured payment to refund.");

        if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Order {orderId} cannot be refunded from payment status {payment.Status}.");
        }

        // Idempotent by caller key: repeating the same key returns the existing refund, never a second one.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation("Refund for order {0} under key already exists; returning it.", orderId);
            return existing;
        }

        if (amount.HasValue)
        {
            if (amount.Value <= 0)
            {
                throw new ArgumentException("Refund amount must be greater than zero.", nameof(amount));
            }
            if (amount.Value > payment.RefundableRemaining)
            {
                throw new InvalidOperationException(
                    $"Refund of {amount.Value} exceeds the refundable remaining {payment.RefundableRemaining} on order {orderId}.");
            }
        }

        var captureId = payment.CaptureId
            ?? throw new InvalidOperationException($"Order {orderId} has no capture to refund.");

        var result = await _payPal.RefundAsync(captureId, amount, payment.Currency, idempotencyKey, cancellationToken);

        var refundedAmount = result.Amount > 0 ? result.Amount : (amount ?? payment.RefundableRemaining);
        var refund = payment.AddRefund(idempotencyKey, refundedAmount, result.RefundId, result.Status);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} refunded {1} (refund {2}).", orderId, refundedAmount, result.RefundId);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var transactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        // Index eShop money-moving references (captures and refunds) by the PayPal id, so a PayPal
        // transaction can be matched to the eShop order that produced it.
        var byPayPalId = new Dictionary<string, (Order order, decimal amount, string status)>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            if (!string.IsNullOrEmpty(payment.CaptureId))
            {
                byPayPalId[payment.CaptureId!] = (order, payment.CapturedAmount ?? payment.Amount, payment.CaptureStatus ?? payment.Status.ToString());
            }
            foreach (var refund in payment.Refunds)
            {
                if (!string.IsNullOrEmpty(refund.PayPalRefundId))
                {
                    byPayPalId[refund.PayPalRefundId!] = (order, refund.Amount, refund.Status);
                }
            }
        }

        var entries = new List<ReconciliationEntry>();
        var matchedPayPalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in transactions)
        {
            if (!string.IsNullOrEmpty(txn.TransactionId) && byPayPalId.TryGetValue(txn.TransactionId, out var match))
            {
                matchedPayPalIds.Add(txn.TransactionId);
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatch.Matched,
                    txn.TransactionId,
                    txn.Status,
                    txn.Amount,
                    match.order.Id,
                    txn.TransactionId,
                    match.amount,
                    match.status));
            }
            else
            {
                entries.Add(new ReconciliationEntry(
                    ReconciliationMatch.PayPalOnly,
                    txn.TransactionId,
                    txn.Status,
                    txn.Amount,
                    null, null, null, null));
            }
        }

        foreach (var kvp in byPayPalId)
        {
            if (matchedPayPalIds.Contains(kvp.Key))
            {
                continue;
            }
            var (order, amount, status) = kvp.Value;
            entries.Add(new ReconciliationEntry(
                ReconciliationMatch.EShopOnly,
                null, null, null,
                order.Id,
                kvp.Key,
                amount,
                status));
        }

        var matched = entries.Count(e => e.Match == ReconciliationMatch.Matched);
        var payPalOnly = entries.Count(e => e.Match == ReconciliationMatch.PayPalOnly);
        var eShopOnly = entries.Count(e => e.Match == ReconciliationMatch.EShopOnly);

        return new ReconciliationReport(from, to, transactions.Count, matched, payPalOnly, eShopOnly, entries);
    }

    // A per-order idempotency seed that is stable within an order's lifetime but unique across runs.
    private static string OrderIdempotencyKey(Order order, string action) =>
        $"order-{order.Id}-{order.OrderDate.UtcTicks}-{action}";

    private static bool IsStale(AuthorizationResult authorization)
    {
        if (string.Equals(authorization.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        // A shopper must never see or act on another's order — report not-found rather than leak it.
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }
}
