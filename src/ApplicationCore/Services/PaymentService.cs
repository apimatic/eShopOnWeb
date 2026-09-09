using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // Stable for the lifetime of the process, distinct across runs. Keeps the PayPal-Request-Id and
    // invoice id for a given order id unique across in-memory restarts (which reset order ids to 1),
    // while remaining constant within a run so a same-run retry still de-dupes at PayPal.
    private static readonly string RunToken = Guid.NewGuid().ToString("N").Substring(0, 8);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;
    private readonly string _currency;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedCard> savedCardRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
        _currency = gateway.Currency;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
            throw new InvalidPaymentStateException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new InvalidPaymentStateException("Every order line must have a positive quantity.");

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);

        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new InvalidPaymentStateException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // Shipping address is not part of the payment surface; use the supplied one or a placeholder
        // so the existing (required) ShipToAddress contract is satisfied.
        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");

        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order.Id;
    }

    public async Task<Payment> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        var existing = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (existing is not null)
        {
            // Idempotent in effect: a double-click after a successful hold returns the same payment.
            if (order.Status == OrderStatus.PaymentAuthorized)
                return existing;
            throw new InvalidPaymentStateException($"Order {orderId} has already been paid and is {order.Status}.");
        }

        if (order.Status != OrderStatus.AwaitingPayment)
            throw new InvalidPaymentStateException($"Order {orderId} is {order.Status} and cannot be paid.");

        string? vaultTokenId = null;
        if (savedCardId.HasValue)
        {
            if (card is not null)
                throw new InvalidPaymentStateException("Provide either card details or a saved card, not both.");

            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdSpecification(savedCardId.Value, buyerId), cancellationToken)
                ?? throw new PaymentMethodNotFoundException(savedCardId.Value);
            vaultTokenId = savedCard.VaultTokenId;
        }
        else if (card is null)
        {
            throw new InvalidPaymentStateException("Payment requires either card details or a saved card id.");
        }

        var amount = order.Total();
        // Stable idempotency key per order so a retried authorize never doubles the hold at PayPal.
        var requestId = $"auth-{orderId}-{RunToken}";
        var invoiceId = OrderInvoiceReference.For(orderId, RunToken);

        var result = await _gateway.CreateAuthorizedOrderAsync(
            new CreateAuthorizationRequest(amount, _currency, invoiceId, requestId, card, vaultTokenId,
                $"eShopOnWeb order {orderId}"),
            cancellationToken);

        var payment = new Payment(orderId, buyerId, _currency, amount, result.PayPalOrderId,
            result.AuthorizationId, result.Status, result.ExpiresAt, requestId);

        await _paymentRepository.AddAsync(payment, cancellationToken);
        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Authorized order {orderId}: paypalOrder={result.PayPalOrderId} auth={result.AuthorizationId} status={result.Status}");
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new InvalidPaymentStateException($"Order {orderId} has no payment; it must be paid before fulfilment.");

        // Idempotent: fulfilling an already-fulfilled order returns the existing capture.
        if (order.Status == OrderStatus.Fulfilled && payment.Status == PaymentStatus.Captured)
            return payment;

        if (order.Status != OrderStatus.PaymentAuthorized)
            throw new InvalidPaymentStateException($"Order {orderId} is {order.Status} and cannot be fulfilled.");

        await EnsureHoldIsFreshAsync(payment, cancellationToken);

        var captureRequestId = payment.CaptureRequestId ?? $"capture-{orderId}-{RunToken}";
        var capture = await _gateway.CaptureAsync(payment.AuthorizationId, captureRequestId, cancellationToken);

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount,
            capture.PayPalFee, capture.NetAmount, captureRequestId);
        order.MarkFulfilled();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Fulfilled order {orderId}: capture={capture.CaptureId} gross={capture.GrossAmount} fee={capture.PayPalFee} net={capture.NetAmount}");
        return payment;
    }

    public async Task<Payment> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new InvalidPaymentStateException($"Order {orderId} has no payment to cancel.");

        // Idempotent: cancelling an already-cancelled order returns the voided payment.
        if (order.Status == OrderStatus.Cancelled && payment.Status == PaymentStatus.Voided)
            return payment;

        if (order.Status != OrderStatus.PaymentAuthorized)
            throw new InvalidPaymentStateException($"Order {orderId} is {order.Status}; only an authorized-but-unfulfilled order can be cancelled.");

        await _gateway.VoidAsync(payment.AuthorizationId, cancellationToken);
        payment.MarkVoided();
        order.MarkCancelled();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Cancelled order {orderId}: voided auth={payment.AuthorizationId}");
        return payment;
    }

    public async Task<Refund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new InvalidPaymentStateException("A refund idempotency key is required.");

        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new InvalidPaymentStateException($"Order {orderId} has no captured payment to refund.");

        // Idempotent: a repeat under the same key returns the refund it originally produced.
        var priorRefund = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (priorRefund is not null)
            return priorRefund;

        var refundAmount = amount ?? payment.RemainingRefundable;
        payment.GuardRefundable(refundAmount);

        var result = await _gateway.RefundAsync(payment.CaptureId!, amount, payment.Currency, idempotencyKey, cancellationToken);
        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Refunded order {orderId}: refund={result.RefundId} amount={result.Amount} status={result.Status}");
        return refund;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderWithPayment(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new InvalidPaymentStateException("Reconciliation 'to' must not be earlier than 'from'.");

        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var allPayments = await _paymentRepository.ListAsync(cancellationToken);

        // eShop side: captured payments whose capture happened within the requested window.
        var capturedInRange = allPayments
            .Where(p => p.CaptureId is not null && p.CapturedAt.HasValue
                        && p.CapturedAt.Value >= from && p.CapturedAt.Value <= to)
            .ToList();
        var paymentsByOrder = allPayments
            .Where(p => p.CaptureId is not null)
            .ToDictionary(p => p.OrderId);
        var paymentsByCaptureId = allPayments
            .Where(p => p.CaptureId is not null)
            .ToDictionary(p => p.CaptureId!, StringComparer.OrdinalIgnoreCase);

        var lines = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            Payment? match = null;
            if (OrderInvoiceReference.TryParse(txn.InvoiceId, out var orderId))
                paymentsByOrder.TryGetValue(orderId, out match);
            if (match is null)
                paymentsByCaptureId.TryGetValue(txn.TransactionId, out match);

            if (match is not null)
            {
                matchedOrderIds.Add(match.OrderId);
                lines.Add(new ReconciliationLine(ReconciliationOutcome.Matched, txn.TransactionId,
                    txn.Status, txn.Amount, txn.Currency ?? match.Currency, txn.Date,
                    match.OrderId, match.CapturedGrossAmount, match.CaptureId));
            }
            else
            {
                lines.Add(new ReconciliationLine(ReconciliationOutcome.InPayPalOnly, txn.TransactionId,
                    txn.Status, txn.Amount, txn.Currency, txn.Date, null, null, null));
            }
        }

        foreach (var payment in capturedInRange.Where(p => !matchedOrderIds.Contains(p.OrderId)))
        {
            lines.Add(new ReconciliationLine(ReconciliationOutcome.InEShopOnly, null, null, null,
                payment.Currency, payment.CapturedAt, payment.OrderId, payment.CapturedGrossAmount,
                payment.CaptureId));
        }

        return new ReconciliationReport(from, to, transactions.Count, matchedOrderIds.Count, lines);
    }

    private async Task EnsureHoldIsFreshAsync(Payment payment, CancellationToken cancellationToken)
    {
        AuthorizationDetails details;
        try
        {
            details = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            payment.UpdateAuthorizationStatus(details.Status, details.ExpiresAt);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning($"Could not read authorization {payment.AuthorizationId}: {ex.Message}");
            return; // Fall through to capture; PayPal is the final arbiter.
        }

        var isStale = string.Equals(details.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase)
            || (details.ExpiresAt.HasValue && details.ExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(1));
        if (!isStale)
            return;

        _logger.LogInformation($"Authorization {payment.AuthorizationId} is stale ({details.Status}, expires {details.ExpiresAt:o}); renewing.");
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId, payment.AuthorizedAmount,
                payment.Currency, cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw new AuthorizationNotRenewableException(
                $"The payment hold for this order has expired and could not be renewed ({ex.Message}). " +
                "Ask the shopper to pay the order again to place a fresh hold before fulfilling.");
        }
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        // Load with items (scoped to the owner) so Order.Total() is correct and one shopper can never
        // see or act on another's order.
        var order = await _orderRepository.FirstOrDefaultAsync(
            new CustomerOrderWithItemsByIdSpecification(orderId, buyerId), cancellationToken);
        if (order is null)
            throw new OrderNotFoundException(orderId);
        return order;
    }
}
