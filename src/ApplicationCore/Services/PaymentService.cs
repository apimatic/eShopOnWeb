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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentOptions
{
    public string Currency { get; set; } = "USD";
}

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentOptions _options;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        PaymentOptions options,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _options = options;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, Address shipToAddress, IReadOnlyList<OrderItemRequest> items, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items == null || items.Count == 0)
        {
            throw new PaymentConflictException("An order must contain at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentConflictException("Item quantities must be greater than zero.");
        }

        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).Distinct().ToArray()), cancellationToken);
        var missing = items.Select(i => i.CatalogItemId).Distinct().Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentConflictException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<OrderPayment> AuthorizePaymentAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        if ((card == null) == (savedPaymentMethodId == null))
        {
            throw new PaymentConflictException("Provide either card details or a saved payment method id, not both.");
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);

        // Idempotent replay: the hold is already in place.
        if (payment != null && payment.Status == PaymentStatus.Authorized)
        {
            return payment;
        }
        if (payment != null && payment.Status == PaymentStatus.Captured)
        {
            throw new PaymentConflictException($"Order {orderId} has already been captured.");
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {orderId} has been cancelled and cannot be paid.");
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentConflictException($"Order {orderId} is not awaiting payment (current state: {order.Status}).");
        }

        string paymentMethodDescription;
        string? vaultTokenId = null;
        if (savedPaymentMethodId.HasValue)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(savedPaymentMethodId.Value), cancellationToken);
            if (method == null || method.BuyerId != buyerId)
            {
                throw new PaymentMethodNotFoundException(savedPaymentMethodId.Value);
            }
            vaultTokenId = method.VaultTokenId;
            paymentMethodDescription = method.Describe();
        }
        else
        {
            paymentMethodDescription = $"Card ending in {card!.Number[^4..]}";
        }

        var amount = Math.Round(order.Total(), 2, MidpointRounding.AwayFromZero);

        if (payment == null)
        {
            payment = new OrderPayment(order.Id, buyerId, amount, _options.Currency, paymentMethodDescription);
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }

        // Stable per payment record so a retried request replays at PayPal instead of double-authorizing.
        var idempotencyKey = $"authorize-{payment.Id}";
        try
        {
            var result = vaultTokenId != null
                ? await _paymentGateway.AuthorizeWithVaultedCardAsync(amount, _options.Currency, vaultTokenId, idempotencyKey, cancellationToken)
                : await _paymentGateway.AuthorizeWithCardAsync(amount, _options.Currency, card!, idempotencyKey, cancellationToken);

            payment.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
            order.MarkPaymentAuthorized();
        }
        catch (PaymentGatewayException ex)
        {
            payment.MarkAuthorizationFailed(null);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogWarning($"Authorization for order {orderId} failed: {ex.Message}");
            throw new PaymentException($"PayPal could not authorize the payment for order {orderId}: {ex.Message}", ex);
        }

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<OrderPayment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);

        if (payment != null && payment.Status == PaymentStatus.Captured)
        {
            return payment; // idempotent replay
        }
        if (order.Status != OrderStatus.PaymentAuthorized || payment == null || payment.AuthorizationId == null)
        {
            throw new PaymentConflictException($"Order {orderId} cannot be fulfilled from state {order.Status}; it must be paid (authorized) first.");
        }

        var idempotencyKey = $"capture-{payment.Id}";
        GatewayCaptureResult capture;
        try
        {
            capture = await CaptureWithRenewalAsync(payment, idempotencyKey, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            payment.MarkAuthorizationFailed(null);
            order.ResetToAwaitingPayment();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw new PaymentConflictException(
                $"The PayPal authorization for order {orderId} could not be captured or renewed ({ex.Message}). " +
                "The order was moved back to 'awaiting payment'; ask the shopper to pay again, then fulfil.");
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.Fee, capture.NetAmount);
        order.MarkFulfilled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    private async Task<GatewayCaptureResult> CaptureWithRenewalAsync(OrderPayment payment, string idempotencyKey, CancellationToken cancellationToken)
    {
        var stale = payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow;
        if (!stale)
        {
            try
            {
                return await _paymentGateway.CaptureAuthorizationAsync(payment.AuthorizationId!, payment.Amount, payment.Currency, idempotencyKey, cancellationToken);
            }
            catch (PaymentGatewayException ex) when (ex.IsAuthorizationUnusable)
            {
                _logger.LogWarning($"Authorization {payment.AuthorizationId} for order {payment.OrderId} is stale; attempting reauthorization.");
            }
        }

        // Renew the stale hold, then capture the renewed authorization.
        var renewed = await _paymentGateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.Currency, cancellationToken);
        payment.RecordAuthorization(
            string.IsNullOrEmpty(renewed.PayPalOrderId) ? payment.PayPalOrderId ?? string.Empty : renewed.PayPalOrderId,
            renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return await _paymentGateway.CaptureAuthorizationAsync(renewed.AuthorizationId, payment.Amount, payment.Currency, idempotencyKey, cancellationToken);
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent replay
        }

        var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);
        if (payment != null && payment.Status == PaymentStatus.Authorized && payment.AuthorizationId != null)
        {
            await _paymentGateway.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);

        var existing = payment?.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return existing; // idempotent replay under the same key
        }

        if (payment == null || payment.Status != PaymentStatus.Captured || payment.CaptureId == null)
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
        }

        var refundable = Math.Round(payment.RefundableAmount, 2, MidpointRounding.AwayFromZero);
        var refundAmount = amount.HasValue
            ? Math.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
            : refundable;
        if (refundAmount <= 0 || refundAmount > refundable)
        {
            throw new PaymentConflictException($"Refund amount must be between 0.01 and the remaining refundable amount {refundable:0.00} {payment.Currency}.");
        }

        var result = await _paymentGateway.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency, idempotencyKey, cancellationToken);
        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        order.MarkRefunded(Math.Round(payment.RefundableAmount, 2, MidpointRounding.AwayFromZero) == 0m);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<OrderPayment?> GetPaymentForOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var existing = await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var customerId = existing.FirstOrDefault(m => m.PayPalCustomerId != null)?.PayPalCustomerId;

        var vault = await _paymentGateway.VaultCardAsync(card, customerId, $"vault-{buyerId}-{Guid.NewGuid():N}", cancellationToken);

        var method = new SavedPaymentMethod(buyerId, vault.VaultTokenId, vault.PayPalCustomerId ?? customerId,
            vault.Brand, vault.LastDigits, vault.ExpiryMonth, vault.ExpiryYear);
        return await _paymentMethodRepository.AddAsync(method, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var method = await _paymentMethodRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(paymentMethodId), cancellationToken);
        if (method == null || method.BuyerId != buyerId)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(method.VaultTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.IsNotFound)
        {
            // Already gone at PayPal; removing the local reference is still correct.
        }

        await _paymentMethodRepository.DeleteAsync(method, cancellationToken);
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new PaymentConflictException("'to' must be after 'from'.");
        }

        var transactions = await _paymentGateway.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsInRangeSpec(from, to), cancellationToken);

        var report = new ReconciliationReport { From = from, To = to };
        var matchedTransactionIds = new HashSet<string>();

        foreach (var txn in transactions)
        {
            var entry = new ReconciliationEntry
            {
                PayPalTransactionId = txn.TransactionId,
                PayPalReferenceId = txn.ReferenceId,
                Status = txn.Status,
                Amount = txn.Amount,
                Fee = txn.Fee,
                Currency = txn.Currency,
                TransactionTime = txn.InitiatedAt
            };

            var match = payments.FirstOrDefault(p =>
                p.CaptureId == txn.TransactionId ||
                p.AuthorizationId == txn.TransactionId ||
                p.Refunds.Any(r => r.PayPalRefundId == txn.TransactionId) ||
                (txn.ReferenceId != null && (p.CaptureId == txn.ReferenceId || p.AuthorizationId == txn.ReferenceId)));

            if (match != null)
            {
                entry.MatchedOrderId = match.OrderId;
                matchedTransactionIds.Add(txn.TransactionId);
                report.Transactions.Add(entry);
            }
            else
            {
                report.UnmatchedPayPalTransactions.Add(entry);
            }
        }

        var knownIds = transactions.Select(t => t.TransactionId).Concat(transactions.Where(t => t.ReferenceId != null).Select(t => t.ReferenceId!)).ToHashSet();
        report.OrdersWithoutPayPalTransaction = payments
            .Where(p => p.CaptureId == null || !knownIds.Contains(p.CaptureId))
            .Where(p => p.AuthorizationId == null || !knownIds.Contains(p.AuthorizationId))
            .Select(p => p.OrderId)
            .Distinct()
            .ToList();

        return report;
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }
}
