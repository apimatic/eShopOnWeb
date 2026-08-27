using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the money movement for orders: authorize at checkout, capture at
/// fulfilment, release on cancel, refund on return. All payment operations are
/// idempotent in effect.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Entities.BuyerAggregate.SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Entities.BuyerAggregate.SavedCard> savedCardRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, Address shipToAddress,
        IReadOnlyList<OrderItemRequest> items, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.", nameof(items));
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new ArgumentException("Item quantities must be positive.", nameof(items));
        }

        var catalogItemsSpecification = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).Distinct().ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification, cancellationToken);

        var missingIds = items.Select(i => i.CatalogItemId).Distinct().Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new ResourceNotFoundException($"Unknown catalog item id(s): {string.Join(", ", missingIds)}.");
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderPayment> PayOrderAsync(string buyerId, int orderId, CardDetails? card,
        int? savedCardId, CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var existingPayment = await GetPaymentAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.PaymentAuthorized && existingPayment != null)
        {
            // Idempotent retry: the hold already exists, return it instead of authorizing again.
            return existingPayment;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new ConflictException($"Order {orderId} is {order.Status} and cannot be paid.");
        }
        if ((card == null) == (savedCardId == null))
        {
            throw new ArgumentException("Provide exactly one of card details or a saved paymentMethodId.");
        }

        var amount = order.Total();
        var currency = _paymentGateway.Currency;
        var customId = order.Id.ToString();
        var invoiceId = InvoiceIdFor(order.Id);

        AuthorizationResult authorization;
        if (savedCardId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId.Value, cancellationToken);
            if (savedCard == null || savedCard.BuyerId != buyerId)
            {
                throw new ResourceNotFoundException($"Payment method {savedCardId.Value} was not found.");
            }

            authorization = await _paymentGateway.AuthorizeWithVaultedCardAsync(
                amount, currency, savedCard.VaultTokenId,
                IdempotencyKey(order.Id, "authorize"), customId, invoiceId, cancellationToken);
        }
        else
        {
            authorization = await _paymentGateway.AuthorizeWithCardAsync(
                amount, currency, card!,
                IdempotencyKey(order.Id, "authorize"), customId, invoiceId, cancellationToken);
        }

        var payment = new OrderPayment(order.Id, authorization.ProviderOrderId, authorization.AuthorizationId,
            authorization.Status, authorization.Amount, authorization.Currency,
            authorization.ExpirationTime, savedCardId);

        order.MarkPaymentAuthorized();
        await _paymentRepository.AddAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id} authorized for {authorization.Amount} {authorization.Currency} (authorization {authorization.AuthorizationId}).");
        return payment;
    }

    public async Task<OrderPayment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled && payment?.CaptureId != null)
        {
            // Idempotent retry: the funds were already captured.
            return payment;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || payment == null)
        {
            throw new ConflictException($"Order {orderId} is {order.Status} and cannot be fulfilled.");
        }

        var authorization = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);

        if (authorization.Status == "CAPTURED" && payment.CaptureId == null)
        {
            throw new ConflictException(
                $"PayPal reports authorization {payment.AuthorizationId} for order {orderId} as already captured, " +
                "but no capture is recorded locally. Run reconciliation and correct the records before fulfilling again.");
        }

        if (authorization.Status == "VOIDED" || authorization.Status == "DENIED")
        {
            throw new AuthorizationNotRenewableException(
                $"The authorization for order {orderId} is {authorization.Status} at PayPal and can no longer be captured. " +
                "Cancel this order and ask the shopper to place it again.");
        }

        if (authorization.Status != "CREATED" && authorization.Status != "PENDING")
        {
            throw new ConflictException(
                $"The authorization for order {orderId} is in unexpected status {authorization.Status}; fulfilment cannot proceed.");
        }

        if (IsStale(authorization))
        {
            payment = await RenewAuthorizationAsync(orderId, payment, authorization, cancellationToken);
        }

        var capture = await _paymentGateway.CaptureAsync(payment.AuthorizationId, payment.AuthorizedAmount,
            payment.Currency, IdempotencyKey(orderId, "capture"), InvoiceIdFor(orderId), cancellationToken);

        if (capture.Status == "DECLINED" || capture.Status == "FAILED")
        {
            throw new PaymentException(
                $"PayPal could not capture the funds for order {orderId} (capture {capture.CaptureId}, status {capture.Status}). " +
                "The shopper's hold is still in place; retry fulfilment or cancel the order.");
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.ProviderFee, capture.NetAmount);
        order.MarkFulfilled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} fulfilled; captured {capture.GrossAmount} {capture.Currency} (capture {capture.CaptureId}).");
        return payment;
    }

    public async Task<OrderPayment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return payment;
        }
        if (order.Status != OrderStatus.AwaitingPayment && order.Status != OrderStatus.PaymentAuthorized)
        {
            throw new ConflictException(
                $"Order {orderId} is {order.Status} and cannot be cancelled; issue a refund instead.");
        }

        if (payment != null && order.Status == OrderStatus.PaymentAuthorized)
        {
            var authorization = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            if (authorization.Status != "VOIDED")
            {
                await _paymentGateway.VoidAuthorizationAsync(payment.AuthorizationId,
                    IdempotencyKey(orderId, "void"), cancellationToken);
            }
            payment.MarkVoided("VOIDED");
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} cancelled; any held funds were released.");
        return payment;
    }

    public async Task<PaymentRefund> RefundOrderAsync(int orderId, string? buyerId, string idempotencyKey,
        decimal? amount, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null || (buyerId != null && order.BuyerId != buyerId))
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await GetPaymentAsync(orderId, cancellationToken);
        if (payment?.CaptureId == null)
        {
            throw new ConflictException($"Order {orderId} has no captured payment to refund.");
        }

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            // Same idempotency key: return the original refund instead of refunding twice.
            return existing;
        }

        var refundable = payment.RefundableAmount;
        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0)
        {
            throw new ArgumentException("The refund amount must be positive.", nameof(amount));
        }
        if (refundAmount > refundable)
        {
            throw new ConflictException(
                $"Order {orderId} has {refundable:0.00} {payment.Currency} refundable; cannot refund {refundAmount:0.00}.");
        }

        var result = await _paymentGateway.RefundAsync(payment.CaptureId, refundAmount, payment.Currency,
            idempotencyKey, noteToPayer, cancellationToken);

        var refund = payment.AddRefund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        payment.SetCaptureStatus(payment.RefundableAmount <= 0 ? "REFUNDED" : "PARTIALLY_REFUNDED");
        order.MarkRefunded(fullyRefunded: payment.RefundableAmount <= 0);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} refunded {result.Amount} {result.Currency} (refund {result.RefundId}).");
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<IReadOnlyList<OrderPayment>> GetPaymentsForOrdersAsync(IReadOnlyCollection<int> orderIds,
        CancellationToken cancellationToken = default)
    {
        if (orderIds.Count == 0)
        {
            return Array.Empty<OrderPayment>();
        }
        return await _paymentRepository.ListAsync(new OrderPaymentsByOrderIdsSpec(orderIds), cancellationToken);
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var transactions = await _paymentGateway.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(cancellationToken);

        var orderIdByPayPalId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            orderIdByPayPalId[payment.AuthorizationId] = payment.OrderId;
            if (payment.CaptureId != null) orderIdByPayPalId[payment.CaptureId] = payment.OrderId;
            foreach (var refund in payment.Refunds)
            {
                orderIdByPayPalId[refund.PayPalRefundId] = payment.OrderId;
            }
        }

        var rows = new List<ReconciliationRow>();
        var seenTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var transaction in transactions)
        {
            seenTransactionIds.Add(transaction.TransactionId);

            int? matchedOrderId = null;
            if (transaction.TransactionId != null && orderIdByPayPalId.TryGetValue(transaction.TransactionId, out var byId))
            {
                matchedOrderId = byId;
            }
            else if (TryParseOrderId(transaction.InvoiceId, out var byInvoice) && payments.Any(p => p.OrderId == byInvoice))
            {
                matchedOrderId = byInvoice;
            }
            else if (int.TryParse(transaction.CustomField, out var byCustom) && payments.Any(p => p.OrderId == byCustom))
            {
                matchedOrderId = byCustom;
            }

            rows.Add(new ReconciliationRow(
                transaction.TransactionId!,
                transaction.EventCode,
                transaction.Status,
                transaction.Amount,
                transaction.Currency,
                transaction.InitiationDate,
                transaction.InvoiceId,
                transaction.CustomField,
                matchedOrderId,
                matchedOrderId.HasValue ? "Matched" : "Unmatched"));
        }

        var missing = payments
            .Where(p =>
                (p.CaptureId != null && !seenTransactionIds.Contains(p.CaptureId)) ||
                (p.CaptureId == null && !seenTransactionIds.Contains(p.AuthorizationId)) ||
                p.Refunds.Any(r => !seenTransactionIds.Contains(r.PayPalRefundId)))
            .Select(p => new UnmatchedPayment(
                p.OrderId,
                p.AuthorizationId,
                p.CaptureId,
                p.Refunds.Select(r => r.PayPalRefundId).ToList(),
                p.CapturedAmount ?? p.AuthorizedAmount,
                p.Currency,
                "Recorded in eShop but absent from PayPal's transaction report for this range. " +
                "Note: PayPal's transaction reporting lags live activity, so very recent payments may legitimately be absent."))
            .ToList();

        return new ReconciliationReport(
            from, to,
            rows.Count,
            rows.Count(r => r.MatchedOrderId.HasValue),
            rows.Count(r => !r.MatchedOrderId.HasValue),
            rows,
            missing);
    }

    private static bool TryParseOrderId(string? invoiceId, out int orderId)
    {
        orderId = 0;
        const string prefix = "eshop-order-";
        return invoiceId != null
            && invoiceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(invoiceId.Substring(prefix.Length), out orderId);
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
    }

    private async Task<OrderPayment> RenewAuthorizationAsync(int orderId, OrderPayment payment,
        AuthorizationState authorization, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Authorization {payment.AuthorizationId} for order {orderId} expired at {authorization.ExpirationTime}; reauthorizing.");
        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(payment.AuthorizationId,
                payment.AuthorizedAmount, payment.Currency,
                IdempotencyKey(orderId, "reauthorize"), cancellationToken);

            payment.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Amount, renewed.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return payment;
        }
        catch (PaymentException ex)
        {
            throw new AuthorizationNotRenewableException(
                $"The authorization for order {orderId} expired and PayPal could not renew it ({ex.Message}). " +
                "Cancel this order and ask the shopper to pay again.");
        }
    }

    private static bool IsStale(AuthorizationState authorization)
    {
        return authorization.ExpirationTime.HasValue && authorization.ExpirationTime.Value <= DateTimeOffset.UtcNow;
    }

    private static string IdempotencyKey(int orderId, string operation) => $"eshop-order-{orderId}-{operation}";

    private static string InvoiceIdFor(int orderId) => $"eshop-order-{orderId}";
}
