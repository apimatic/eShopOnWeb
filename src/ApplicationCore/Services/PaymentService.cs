using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

public class PaymentService : IPaymentService
{
    private static readonly TimeSpan AuthorizationExpirySafetyMargin = TimeSpan.FromMinutes(5);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway gateway,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
            throw new PaymentStateException("An order must contain at least one item.");
        if (items.Any(i => i.Quantity <= 0))
            throw new PaymentStateException("Item quantities must be positive.");

        var spec = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(spec, cancellationToken);

        var missing = items.Select(i => i.CatalogItemId).Distinct()
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
            throw new PaymentStateException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress(), orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Payment?> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            return null;

        if (card is null && paymentMethodId is null)
            throw new PaymentStateException("Provide either card details or a saved paymentMethodId.");
        if (card is not null && paymentMethodId is not null)
            throw new PaymentStateException("Provide either card details or a saved paymentMethodId, not both.");

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent in effect: a repeated pay call on an already-held/captured order
        // returns the existing payment state without touching PayPal again.
        if (payment is not null && payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
            throw new PaymentStateException($"Order {orderId} is not awaiting payment (current status: {order.Status}).");

        string? vaultTokenId = null;
        if (paymentMethodId is not null)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(paymentMethodId.Value, cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
                throw new PaymentStateException($"Saved payment method {paymentMethodId} was not found for this shopper.");
            vaultTokenId = savedCard.VaultTokenId;
        }

        payment ??= new Payment(order.Id, buyerId, order.Total(), _settings.Currency);
        if (payment.Id == 0)
            await _paymentRepository.AddAsync(payment, cancellationToken);

        if (payment.PayPalOrderId is null)
        {
            var payPalOrder = await _gateway.CreateOrderAsync(
                payment.Amount, payment.Currency,
                referenceId: order.Id.ToString(),
                invoiceId: payment.InvoiceId,
                idempotencyKey: $"eshop-payment-{payment.Id}-paypal-create-{payment.InvoiceId}",
                cancellationToken);
            payment.SetPayPalOrder(payPalOrder.Id);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        if (payment.PayPalOrderId is null)
            throw new PaymentGatewayException("PayPal did not return an order id; cannot authorize the payment.");

        var authorization = await _gateway.AuthorizeOrderAsync(
            payment.PayPalOrderId, card, vaultTokenId,
            idempotencyKey: $"eshop-payment-{payment.Id}-authorize",
            cancellationToken);

        if (authorization.Status is "DENIED" or "VOIDED")
            throw new PaymentStateException(
                $"PayPal could not authorize the payment (status: {authorization.Status}). The card was declined; no funds were held.");

        payment.MarkAuthorized(authorization.Id, authorization.Status, authorization.ExpirationTime);
        order.MarkPaymentAuthorized();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<Payment?> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return null;

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent: fulfilling an already-fulfilled order replays the captured state.
        if (order.Status == OrderStatus.Fulfilled && payment?.Status is PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment!;
        }

        if (payment is null || payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentStateException(
                $"Order {orderId} cannot be fulfilled: it has no authorized payment to capture (order status: {order.Status}).");

        var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        var stale = authorization.Status != "CREATED"
            || (authorization.ExpirationTime is not null
                && authorization.ExpirationTime <= DateTimeOffset.UtcNow + AuthorizationExpirySafetyMargin);

        if (stale)
        {
            _logger.LogInformation(
                $"Authorization {payment.AuthorizationId} for order {orderId} is stale (status {authorization.Status}); attempting to renew it before capture.");
            try
            {
                var renewed = await _gateway.ReauthorizeAsync(
                    payment.AuthorizationId, payment.Amount, payment.Currency,
                    idempotencyKey: $"eshop-order-{orderId}-reauthorize",
                    cancellationToken);
                payment.MarkAuthorizationRenewed(renewed.Id, renewed.Status, renewed.ExpirationTime);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                throw new PaymentStateException(
                    $"The PayPal authorization for order {orderId} has gone stale and can no longer be renewed " +
                    $"({ex.Message}). Ask the shopper to pay again, or cancel the order.");
            }
        }

        var capture = await _gateway.CaptureAuthorizationAsync(
            payment.AuthorizationId, payment.Amount, payment.Currency,
            invoiceId: payment.InvoiceId,
            idempotencyKey: $"eshop-payment-{payment.Id}-capture-{payment.AuthorizationId}",
            cancellationToken);

        if (capture.Status is "DECLINED" or "FAILED")
            throw new PaymentStateException(
                $"PayPal declined the capture for order {orderId} (status: {capture.Status}). No money was taken; retry fulfilment or cancel the order.");

        payment.MarkCaptured(capture.Id, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<(Order Order, Payment? Payment)?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
            return null;

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent: cancelling twice returns the cancelled state.
        if (order.Status == OrderStatus.Cancelled)
            return (order, payment);

        order.MarkCancelled();

        if (payment?.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            await _gateway.VoidAuthorizationAsync(
                payment.AuthorizationId,
                idempotencyKey: $"eshop-order-{orderId}-void",
                cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, payment);
    }

    public async Task<PaymentRefund?> RefundOrderAsync(string buyerId, bool isOperator, int orderId, decimal? amount,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null || (!isOperator && order.BuyerId != buyerId))
            return null;

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null)
            throw new PaymentStateException($"Order {orderId} has no payment to refund.");

        // Idempotency: a repeated request under the same key replays the original refund.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
            return existing;

        var refundAmount = amount ?? payment.RefundableAmount;
        var refund = payment.AddRefund(idempotencyKey, refundAmount, noteToPayer);

        var result = await _gateway.RefundCaptureAsync(
            payment.CaptureId!, refundAmount, payment.Currency,
            idempotencyKey: idempotencyKey,
            noteToPayer: noteToPayer,
            cancellationToken);

        payment.ApplySettledRefund(refund, result.Id, result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<(Order Order, Payment? Payment)>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(
            new PaymentsByOrderIdsSpecification(orders.Select(o => o.Id).ToArray()), cancellationToken);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => (o, payments.FirstOrDefault(p => p.OrderId == o.Id)))
            .ToList();
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));
        Guard.Against.NullOrEmpty(card.Number, nameof(card.Number));
        Guard.Against.NullOrEmpty(card.Expiry, nameof(card.Expiry));

        // Deterministic idempotency key from a transient hash of the PAN so a
        // double-submit vaults the card once. The hash is never stored or logged.
        var panHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(card.Number)));
        var token = await _gateway.VaultCardAsync(card, buyerId,
            idempotencyKey: $"eshop-vault-{buyerId}-{panHash[..16]}",
            cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, token.Id, token.Brand, token.LastDigits, token.Expiry, token.CardholderName);
        await _savedCardRepository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(
            new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task<bool> DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (savedCard is null || savedCard.BuyerId != buyerId)
            return false;

        try
        {
            await _gateway.DeleteVaultedCardAsync(savedCard.VaultTokenId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            // A token PayPal no longer knows about must still disappear locally.
            _logger.LogWarning($"Deleting vaulted card for buyer failed at PayPal; removing locally anyway. {ex.Message}");
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
        return true;
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
            throw new PaymentStateException("The 'to' timestamp must be after the 'from' timestamp.");

        var transactions = await _gateway.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(
            new PaymentsInDateRangeSpecification(from, to), cancellationToken);

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            GeneratedAt = DateTimeOffset.UtcNow
        };

        var matchedPaymentIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var match = FindMatchingPayment(txn, payments);
            if (match is not null)
                matchedPaymentIds.Add(match.Id);

            report.Transactions.Add(new ReconciliationEntry
            {
                TransactionId = txn.TransactionId,
                EventCode = txn.EventCode,
                Status = txn.Status,
                Amount = txn.Amount,
                Currency = txn.Currency,
                FeeAmount = txn.FeeAmount,
                TransactionDate = txn.InitiationDate,
                InvoiceId = txn.InvoiceId,
                MatchedOrderId = match?.OrderId,
                MatchedPaymentId = match?.Id,
                MatchStatus = match is not null ? "Matched" : "NotKnownToEShop"
            });
        }

        foreach (var payment in payments.Where(p => !matchedPaymentIds.Contains(p.Id)))
        {
            report.UnmatchedEShopPayments.Add(new UnmatchedEShopPayment
            {
                OrderId = payment.OrderId,
                PaymentId = payment.Id,
                PayPalOrderId = payment.PayPalOrderId,
                AuthorizationId = payment.AuthorizationId,
                CaptureId = payment.CaptureId,
                Status = payment.Status.ToString()
            });
        }

        report.TotalPayPalTransactions = report.Transactions.Count;
        report.TotalMatched = report.Transactions.Count(t => t.MatchStatus == "Matched");
        report.TotalUnmatchedPayPal = report.Transactions.Count(t => t.MatchStatus == "NotKnownToEShop");
        report.TotalUnmatchedEShop = report.UnmatchedEShopPayments.Count;
        return report;
    }

    private static Payment? FindMatchingPayment(PayPalTransaction txn, IReadOnlyList<Payment> payments)
    {
        return payments.FirstOrDefault(p =>
            p.CaptureId == txn.TransactionId ||
            p.AuthorizationId == txn.TransactionId ||
            p.PayPalOrderId == txn.TransactionId ||
            p.Refunds.Any(r => r.PayPalRefundId == txn.TransactionId) ||
            (txn.InvoiceId is not null && txn.InvoiceId == p.InvoiceId) ||
            (txn.CustomField is not null && txn.CustomField == p.InvoiceId));
    }

    private static Address DefaultShipToAddress() =>
        new Address("Not provided", "Not provided", "Not provided", "Not provided", "00000");
}
