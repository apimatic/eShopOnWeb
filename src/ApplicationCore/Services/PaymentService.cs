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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private static readonly Address DefaultShipTo = new("N/A", "N/A", "N/A", "N/A", "N/A");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPaymentGateway _gateway;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IRepository<CatalogItem> itemRepository,
        IPaymentGateway gateway,
        IOptions<PayPalSettings> settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _itemRepository = itemRepository;
        _gateway = gateway;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemInput> items, Address? shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items.Count == 0)
        {
            throw new PaymentStateException("An order must contain at least one item.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray()), ct);

        var orderItems = new List<OrderItem>();
        foreach (var input in items)
        {
            Guard.Against.NegativeOrZero(input.Quantity, nameof(input.Quantity));
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == input.CatalogItemId)
                ?? throw new PaymentStateException($"Catalog item {input.CatalogItemId} does not exist.");
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? "eCatalog-item-default.png" : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, input.Quantity));
        }

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, orderItems);
        return await _orderRepository.AddAsync(order, ct);
    }

    public async Task<Payment?> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order == null || order.BuyerId != buyerId)
        {
            return null;
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);

        // Idempotent: a repeated pay call for an already-authorized order replays the current state.
        if (order.Status == OrderStatus.Authorized || order.Status == OrderStatus.Fulfilled)
        {
            return payment;
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentStateException($"Order {orderId} is cancelled and cannot be paid.");
        }
        if (payment?.AuthorizationId != null)
        {
            return payment;
        }

        string? vaultTokenId = null;
        string? cardBrand = null;
        string? cardLastDigits = null;

        if (savedPaymentMethodId.HasValue)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(savedPaymentMethodId.Value), ct);
            if (savedCard == null || savedCard.BuyerId != buyerId)
            {
                throw new PaymentStateException($"Saved payment method {savedPaymentMethodId} was not found.");
            }
            vaultTokenId = savedCard.VaultTokenId;
            cardBrand = savedCard.Brand;
            cardLastDigits = savedCard.LastDigits;
        }
        else if (card != null)
        {
            cardLastDigits = card.Number.Length >= 4 ? card.Number[^4..] : null;
            cardBrand = GuessCardBrand(card.Number);
        }
        else
        {
            throw new PaymentStateException("Payment requires either card details or a saved payment method id.");
        }

        var total = order.Total();

        // Persist the payment first so its OperationKey exists before any provider call:
        // the key makes a retried or double-clicked authorize collapse at PayPal.
        if (payment == null)
        {
            payment = new Payment(orderId, buyerId, total, _settings.Currency);
            await _paymentRepository.AddAsync(payment, ct);
        }

        var authorization = await _gateway.AuthorizeAsync(
            orderId, total, _settings.Currency, card, vaultTokenId,
            idempotencyKey: $"eshop-authorize-{payment.OperationKey}", ct);

        payment.RecordAuthorization(
            authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status,
            authorization.ExpiresAt, cardBrand, cardLastDigits);

        order.MarkAuthorized();

        await _paymentRepository.UpdateAsync(payment, ct);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Order {orderId} authorized: {total} {_settings.Currency}, authorization {authorization.AuthorizationId} ({authorization.Status}).");
        return payment;
    }

    public async Task<Payment?> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order == null)
        {
            return null;
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);

        // Idempotent: fulfilling an already-fulfilled order replays the capture state.
        if (order.Status == OrderStatus.Fulfilled)
        {
            return payment;
        }
        if (order.Status != OrderStatus.Authorized || payment?.AuthorizationId == null)
        {
            throw new PaymentStateException($"Order {orderId} is in state {order.Status} and has no authorization to capture.");
        }

        var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, ct);
        var capturable = authorization.Status is "CREATED" or "PENDING"
            && (authorization.ExpiresAt == null || authorization.ExpiresAt > DateTimeOffset.UtcNow);

        if (!capturable)
        {
            _logger.LogInformation($"Authorization {payment.AuthorizationId} for order {orderId} is {authorization.Status}; attempting renewal before capture.");
            try
            {
                var renewed = await _gateway.ReauthorizeAsync(
                    payment.AuthorizationId, payment.Amount, payment.Currency,
                    idempotencyKey: $"eshop-reauthorize-{payment.OperationKey}", ct);
                payment.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            }
            catch (PaymentGatewayException ex) when (ex.ProviderStatusCode == 422)
            {
                throw new PaymentStateException(
                    $"The PayPal authorization for order {orderId} can no longer be renewed " +
                    "(authorizations can only be renewed once, within 29 days of the hold). " +
                    "Cancel this order and ask the shopper to place and pay a new one.");
            }
        }

        var capture = await _gateway.CaptureAsync(
            payment.AuthorizationId, idempotencyKey: $"eshop-capture-{payment.OperationKey}", ct);

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.Fee, capture.Net);
        order.MarkFulfilled();

        await _paymentRepository.UpdateAsync(payment, ct);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Order {orderId} fulfilled: captured {capture.Amount} {payment.Currency} (fee {capture.Fee}, net {capture.Net}), capture {capture.CaptureId}.");
        return payment;
    }

    public async Task<bool> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order == null)
        {
            return false;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return true;
        }

        order.MarkCancelled();

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment?.AuthorizationId != null && !payment.IsCaptured && payment.AuthorizationStatus != "VOIDED")
        {
            await _gateway.VoidAsync(payment.AuthorizationId, idempotencyKey: $"eshop-void-{payment.OperationKey}", ct);
            payment.MarkAuthorizationVoided("VOIDED");
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation($"Order {orderId} cancelled: authorization {payment.AuthorizationId} voided, held funds released.");
        }

        await _orderRepository.UpdateAsync(order, ct);
        return true;
    }

    public async Task<PaymentRefund?> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order == null || order.BuyerId != buyerId)
        {
            return null;
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment?.CaptureId == null)
        {
            throw new PaymentStateException($"Order {orderId} has no captured payment to refund. Cancel it instead if it has not been fulfilled.");
        }

        // Idempotency: a repeated key replays the original refund without touching PayPal again.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableBalance;
        if (refundAmount <= 0 || refundAmount > payment.RefundableBalance)
        {
            throw new PaymentStateException(
                $"Refund of {refundAmount} {payment.Currency} exceeds the refundable balance {payment.RefundableBalance} {payment.Currency} for order {orderId}.");
        }

        var refund = await _gateway.RefundAsync(payment.CaptureId, refundAmount, payment.Currency, idempotencyKey, ct);

        var entity = payment.AddRefund(refund.RefundId, refund.Status, refund.Amount, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Order {orderId} refunded: {refund.Amount} {payment.Currency}, refund {refund.RefundId} ({refund.Status}).");
        return entity;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsInRangeSpecification(from, to), ct);

        var knownIds = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            if (payment.AuthorizationId != null) knownIds[payment.AuthorizationId] = payment;
            if (payment.CaptureId != null) knownIds[payment.CaptureId] = payment;
            foreach (var refund in payment.Refunds)
            {
                knownIds[refund.RefundId] = payment;
            }
        }

        var entries = transactions
            .Select(t => new ReconciliationEntry(
                t.TransactionId, t.EventCode, t.Status, t.Amount, t.Currency, t.Fee,
                t.InitiatedAt ?? t.UpdatedAt,
                knownIds.TryGetValue(t.TransactionId, out var match) ? match.OrderId : null,
                match?.Id))
            .ToList();

        var reportedIds = new HashSet<string>(transactions.Select(t => t.TransactionId), StringComparer.OrdinalIgnoreCase);
        var unmatched = payments
            .Where(p => new[] { p.AuthorizationId, p.CaptureId }
                .Concat(p.Refunds.Select(r => r.RefundId))
                .Where(id => id != null)
                .Any(id => reportedIds.Contains(id!)) == false)
            .Select(p => new UnmatchedPayment(
                p.Id, p.OrderId, p.Amount, p.Currency,
                new[] { p.AuthorizationId, p.CaptureId }
                    .Concat(p.Refunds.Select(r => r.RefundId))
                    .Where(id => id != null)
                    .Select(id => id!)
                    .ToList()))
            .ToList();

        return new ReconciliationReport(from, to, entries, unmatched);
    }

    private static string? GuessCardBrand(string number)
    {
        if (string.IsNullOrEmpty(number)) return null;
        return number[0] switch
        {
            '4' => "VISA",
            '5' => "MASTERCARD",
            '3' => "AMEX",
            '6' => "DISCOVER",
            _ => null
        };
    }
}
