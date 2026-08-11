using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Application service that layers PayPal money movement over the existing order model. All
/// amounts are derived from catalog prices; the currency comes from configuration. Ownership is
/// enforced on every shopper-scoped operation, and the payment record keeps the PayPal-owned
/// state (hold, capture, refunds) so later requests can act on it.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private const string DefaultPicture = "eCatalog-item-default.png";

    // Stable within a process (so a double-click reuses the same PayPal-Request-Id and is
    // idempotent) but unique across restarts (so deterministic order ids from the in-memory
    // store don't collide with a previous run's cached request id on PayPal's side).
    private static readonly string InstanceNonce = System.Guid.NewGuid().ToString("N").Substring(0, 8);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentSettings _settings;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway gateway,
        IPaymentSettings settings,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _settings = settings;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Payment> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address shipToAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
            throw new PaymentException("An order must contain at least one line item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentException("Every order line must have a quantity of at least 1.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.");

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri)) pictureUri = DefaultPicture;

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, order.Total(), _settings.CurrencyCode);
        payment = await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {order.Id} placed for buyer with total {order.Total()} {_settings.CurrencyCode}.");
        return payment;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null && savedPaymentMethodId is null)
            throw new PaymentException("Provide either card details or a saved card to pay with.");
        if (card is not null && savedPaymentMethodId is not null)
            throw new PaymentException("Provide either card details or a saved card, not both.");

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment.Status == PaymentStatus.Authorized)
        {
            _logger.LogInformation($"Order {orderId} is already authorized ({payment.AuthorizationId}); returning existing hold.");
            return payment;
        }
        if (payment.Status != PaymentStatus.AwaitingPayment)
            throw new PaymentException($"Order {orderId} cannot be paid because its payment is {payment.Status}.");

        var amount = order.Total();
        var idempotencyKey = $"order-{orderId}-{InstanceNonce}-authorize";

        GatewayAuthorization auth;
        int? usedSavedCardId = null;
        if (savedPaymentMethodId is not null)
        {
            var saved = await LoadOwnedSavedCardAsync(buyerId, savedPaymentMethodId.Value, cancellationToken);
            usedSavedCardId = saved.Id;
            auth = await _gateway.AuthorizeWithVaultedCardAsync(amount, _settings.CurrencyCode, saved.VaultId,
                idempotencyKey, cancellationToken);
        }
        else
        {
            auth = await _gateway.AuthorizeWithCardAsync(amount, _settings.CurrencyCode, card!, idempotencyKey,
                cancellationToken);
        }

        payment.MarkAuthorized(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt, usedSavedCardId);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} authorized: hold {auth.AuthorizationId} ({auth.Status}) for {amount} {_settings.CurrencyCode}.");
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // Idempotent: fulfilling an already-captured order does not capture twice.
        if (payment.Status == PaymentStatus.Captured
            || payment.Status == PaymentStatus.PartiallyRefunded
            || payment.Status == PaymentStatus.Refunded)
        {
            _logger.LogInformation($"Order {orderId} already captured ({payment.CaptureId}); returning existing capture.");
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentException($"Order {orderId} cannot be fulfilled because its payment is {payment.Status}.");

        var captureKey = $"order-{orderId}-{InstanceNonce}-capture";
        var amount = payment.Amount;

        GatewayCapture capture;
        try
        {
            capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId, amount, payment.CurrencyCode,
                captureKey, cancellationToken);
        }
        catch (AuthorizationExpiredException ex)
        {
            // Stale hold: renew rather than failing the fulfilment outright.
            _logger.LogWarning($"Order {orderId} authorization {payment.AuthorizationId} is stale; attempting re-authorization. ({ex.Message})");
            GatewayAuthorization renewed;
            try
            {
                renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId, amount, payment.CurrencyCode,
                    cancellationToken);
            }
            catch (PaymentGatewayException reauthEx)
            {
                throw new AuthorizationNotRenewableException(
                    $"Order {orderId} could not be fulfilled: the payment hold has expired and cannot be renewed. " +
                    $"Ask the shopper to place and pay for a new order. PayPal reported: {reauthEx.Message}");
            }

            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            capture = await _gateway.CaptureAuthorizationAsync(renewed.AuthorizationId, amount, payment.CurrencyCode,
                captureKey + "-renewed", cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} fulfilled: captured {capture.GrossAmount} {capture.CurrencyCode} " +
            $"(fee {capture.PayPalFee}, net {capture.NetAmount}), capture {capture.CaptureId}.");
        return payment;
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Voided)
            return payment; // idempotent

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentException(
                $"Order {orderId} cannot be cancelled because its payment is {payment.Status}. " +
                "Only an authorized (not yet captured) order can be cancelled; use a refund after fulfilment.");

        await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        payment.MarkVoided();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} cancelled: hold {payment.AuthorizationId} voided, funds released.");
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken); // ownership check
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // Repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Refund for order {orderId} with key {idempotencyKey} already exists ({existing.PayPalRefundId}); returning it.");
            return existing;
        }

        if (payment.Status != PaymentStatus.Captured
            && payment.Status != PaymentStatus.PartiallyRefunded)
            throw new PaymentException($"Order {orderId} cannot be refunded because its payment is {payment.Status}.");
        if (payment.CaptureId is null)
            throw new PaymentException($"Order {orderId} has no capture to refund.");

        var refundable = payment.RefundableAmount();
        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0m)
            throw new PaymentException("Refund amount must be greater than zero.");
        if (refundAmount > refundable)
            throw new PaymentException(
                $"Refund of {refundAmount} {payment.CurrencyCode} exceeds the refundable amount of {refundable} {payment.CurrencyCode}.");

        var result = await _gateway.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.CurrencyCode,
            idempotencyKey, cancellationToken);

        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} refunded {result.Amount} {result.CurrencyCode} ({result.Status}), refund {result.RefundId}.");
        return refund;
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new CustomerPaymentsSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments
            .GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Id).First());

        var views = new List<OrderPaymentView>();
        foreach (var order in orders.OrderByDescending(o => o.Id))
        {
            if (paymentsByOrder.TryGetValue(order.Id, out var payment))
                views.Add(new OrderPaymentView(order, payment));
        }
        return views;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from) throw new PaymentException("Reconciliation 'to' must not be earlier than 'from'.");

        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsInDateRangeSpecification(from, to), cancellationToken);

        // Map every PayPal id eShop knows about back to its order.
        var eShopIdToOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in payments)
        {
            foreach (var id in new[] { p.CaptureId, p.AuthorizationId, p.PayPalOrderId })
                if (!string.IsNullOrEmpty(id)) eShopIdToOrder[id!] = p.OrderId;
            foreach (var r in p.Refunds)
                if (!string.IsNullOrEmpty(r.PayPalRefundId)) eShopIdToOrder[r.PayPalRefundId] = p.OrderId;
        }

        var entries = new List<ReconciliationEntry>();
        var matchedPayPalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in transactions)
        {
            var matched = eShopIdToOrder.TryGetValue(txn.TransactionId, out var orderId);
            if (matched) matchedPayPalIds.Add(txn.TransactionId);
            entries.Add(new ReconciliationEntry(
                MatchStatus: matched ? "Matched" : "PayPalOnly",
                PayPalTransactionId: txn.TransactionId,
                PayPalStatus: txn.Status,
                PayPalAmount: txn.Amount,
                PayPalCurrency: txn.CurrencyCode,
                PayPalDate: txn.Date,
                OrderId: matched ? orderId : null,
                EShopAmount: null,
                EShopStatus: null));
        }

        // eShop payments (captured) that PayPal's report does not show in this range.
        var payPalTxnIds = new HashSet<string>(transactions.Select(t => t.TransactionId), StringComparer.OrdinalIgnoreCase);
        foreach (var p in payments)
        {
            var known = (p.CaptureId is not null && payPalTxnIds.Contains(p.CaptureId))
                || (p.AuthorizationId is not null && payPalTxnIds.Contains(p.AuthorizationId))
                || (p.PayPalOrderId is not null && payPalTxnIds.Contains(p.PayPalOrderId));
            if (known) continue;
            entries.Add(new ReconciliationEntry(
                MatchStatus: "EShopOnly",
                PayPalTransactionId: null,
                PayPalStatus: null,
                PayPalAmount: null,
                PayPalCurrency: null,
                PayPalDate: null,
                OrderId: p.OrderId,
                EShopAmount: p.CapturedAmount ?? p.Amount,
                EShopStatus: p.Status.ToString()));
        }

        var matchedCount = entries.Count(e => e.MatchStatus == "Matched");
        var payPalOnly = entries.Count(e => e.MatchStatus == "PayPalOnly");
        var eShopOnly = entries.Count(e => e.MatchStatus == "EShopOnly");

        return new ReconciliationReport(from, to, transactions.Count, payments.Count,
            matchedCount, payPalOnly, eShopOnly, entries);
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        return order;
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        return payment ?? throw new PaymentNotFoundException($"Order {orderId} has no payment record.");
    }

    private async Task<SavedPaymentMethod> LoadOwnedSavedCardAsync(string buyerId, int savedPaymentMethodId,
        CancellationToken cancellationToken)
    {
        var saved = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(savedPaymentMethodId), cancellationToken);
        if (saved is null || !string.Equals(saved.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentNotFoundException($"Saved card {savedPaymentMethodId} was not found.");
        return saved;
    }
}
