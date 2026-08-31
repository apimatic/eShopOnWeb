using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // Keys must be unique across process runs: with the in-memory store, order ids
    // restart at 1 after a restart and would otherwise collide with PayPal-Request-Ids
    // already seen by PayPal (which replays the original response, failures included).
    private static readonly string KeyPrefix = $"eshop-{Guid.NewGuid():N}".Substring(0, 14);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentSettings _paymentSettings;

    public PaymentService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        IOptions<PaymentSettings> paymentSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _paymentSettings = paymentSettings.Value;
    }

    public string Currency => _paymentSettings.Currency;

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items, Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0 || items.Any(i => i.Quantity <= 0))
        {
            throw new InvalidOrderStateException("An order must contain at least one item with a positive quantity.");
        }

        var catalogItemsSpecification = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification, ct);

        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
            if (catalogItem is null)
            {
                throw new InvalidOrderStateException($"Catalog item {item.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        return await _orderRepository.AddAsync(order, ct);
    }

    public async Task<Payment> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByOrderIdSpecification(orderId), ct);

        // Idempotent in effect: a double-click after a successful authorization returns the existing hold.
        var activePayment = payments.LastOrDefault(p => p.Status == PaymentStatus.Authorized);
        if (activePayment is not null)
        {
            return activePayment;
        }
        if (payments.Any(p => p.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
        {
            throw new InvalidOrderStateException($"Order {orderId} has already been captured and cannot be paid again.");
        }

        string? vaultTokenId = null;
        if (savedPaymentMethodId.HasValue)
        {
            var savedCard = await _paymentMethodRepository.GetByIdAsync(savedPaymentMethodId.Value, ct);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new SavedPaymentMethodNotFoundException(savedPaymentMethodId.Value);
            }
            vaultTokenId = savedCard.VaultTokenId;
            card = null;
        }
        else if (card is null)
        {
            throw new InvalidOrderStateException("Payment requires either card details or a saved payment method id.");
        }

        var amount = order.Total();
        var attempt = payments.Count + 1;
        var idempotencyKey = $"{KeyPrefix}-order-{orderId}-authorize-{attempt}";

        var authorization = await _paymentGateway.AuthorizeAsync(vaultTokenId, card, amount, Currency,
            referenceId: $"eshop-order-{orderId}", idempotencyKey: idempotencyKey, ct: ct);

        var payment = new Payment(order.Id, buyerId, authorization.PayPalOrderId, authorization.AuthorizationId,
            authorization.Status, authorization.ExpiresAt, amount, Currency);
        payment = await _paymentRepository.AddAsync(payment, ct);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, ct);

        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsSpecification(orderId), ct);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payments = await _paymentRepository.ListAsync(new PaymentsByOrderIdSpecification(orderId), ct);
        var payment = payments.LastOrDefault(p => p.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded);

        // Idempotent: fulfilling an already-captured order returns the recorded capture.
        if (payment is not null && payment.Status != PaymentStatus.Authorized)
        {
            return payment;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || payment is null)
        {
            throw new InvalidOrderStateException($"Order {orderId} is in status '{order.Status}' and cannot be fulfilled. Payment must be authorized first.");
        }

        if (payment.AuthorizationIsStale ||
            (payment.AuthorizationStatus != "CREATED" && payment.AuthorizationStatus != "PENDING"))
        {
            var reauthorizeKey = $"{KeyPrefix}-payment-{payment.Id}-reauthorize-{payment.ReauthorizationCount + 1}";
            var renewed = await _paymentGateway.ReauthorizeAsync(payment.AuthorizationId, payment.Amount, payment.Currency,
                idempotencyKey: reauthorizeKey, ct: ct);
            payment.MarkReauthorized(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        }

        var capture = await _paymentGateway.CaptureAsync(payment.AuthorizationId, payment.Amount, payment.Currency,
            idempotencyKey: $"{KeyPrefix}-payment-{payment.Id}-capture", ct: ct);

        payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.Fee, capture.NetAmount, DateTimeOffset.UtcNow);
        await _paymentRepository.UpdateAsync(payment, ct);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);

        return payment;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsSpecification(orderId), ct);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment && order.Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOrderStateException($"Order {orderId} is in status '{order.Status}' and can no longer be cancelled.");
        }

        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            var payments = await _paymentRepository.ListAsync(new PaymentsByOrderIdSpecification(orderId), ct);
            var payment = payments.LastOrDefault(p => p.Status == PaymentStatus.Authorized);
            if (payment is not null)
            {
                await _paymentGateway.VoidAuthorizationAsync(payment.AuthorizationId,
                    idempotencyKey: $"{KeyPrefix}-payment-{payment.Id}-void", ct: ct);
                payment.MarkVoided("VOIDED");
                await _paymentRepository.UpdateAsync(payment, ct);
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnedOrderAsync(buyerId, orderId, ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByOrderIdSpecification(orderId), ct);
        var payment = payments.LastOrDefault(p => p.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded);
        if (payment is null)
        {
            throw new InvalidOrderStateException($"Order {orderId} has no captured payment to refund.");
        }

        // Caller-supplied idempotency key: a repeat under the same key returns the original refund.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refundAmount = amount ?? payment.RemainingRefundable;
        if (refundAmount <= 0m || refundAmount > payment.RemainingRefundable)
        {
            throw new RefundExceedsCapturedAmountException(refundAmount, payment.RemainingRefundable);
        }

        // Scope caller keys before they reach PayPal: the sandbox merchant account may be
        // shared, and PayPal rejects a PayPal-Request-Id it has seen before (DUPLICATE_REQUEST_ID).
        var result = await _paymentGateway.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.Currency,
            idempotencyKey: $"{KeyPrefix}-refund-{idempotencyKey}", ct: ct);

        var refund = payment.AddRefund(result.RefundId, refundAmount, result.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, ct);

        order.MarkRefunded(inFull: payment.RemainingRefundable <= 0m);
        await _orderRepository.UpdateAsync(order, ct);

        return refund;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), ct);

        return orders
            .Select(o => new OrderWithPayment(o, payments.LastOrDefault(p => p.OrderId == o.Id)))
            .ToList();
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _paymentGateway.VaultCardAsync(buyerId, card,
            idempotencyKey: $"{KeyPrefix}-vault-{Guid.NewGuid():N}", ct: ct);

        var savedCard = new SavedPaymentMethod(buyerId, vaulted.VaultTokenId, vaulted.Brand, vaulted.LastDigits,
            vaulted.Expiry, vaulted.CardholderName);
        return await _paymentMethodRepository.AddAsync(savedCard, ct);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken ct = default)
    {
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var savedCard = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new SavedPaymentMethodNotFoundException(paymentMethodId);
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(savedCard.VaultTokenId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.ProviderStatusCode == 404)
        {
            // Already gone at PayPal — removing the local reference still satisfies the delete.
        }

        await _paymentMethodRepository.DeleteAsync(savedCard, ct);
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(new AllPaymentsWithRefundsSpecification(), ct);

        var entries = transactions.Select(t =>
        {
            var match = MatchPayment(t.TransactionId, payments);
            return new ReconciliationEntry(t.TransactionId, t.Type, t.Status, t.Amount, t.Currency, t.Fee,
                t.InitiatedAt, match?.Payment.OrderId, match?.MatchedAs);
        }).ToList();

        var knownTransactionIds = transactions.Select(t => t.TransactionId).ToHashSet();
        var missing = payments
            .Where(p => p.CapturedAt.HasValue && p.CapturedAt.Value >= from && p.CapturedAt.Value <= to
                && p.CaptureId is not null && !knownTransactionIds.Contains(p.CaptureId))
            .Select(p => new CaptureMissingFromPayPal(p.OrderId, p.CaptureId!, p.CapturedAmount ?? p.Amount, p.Currency, p.CapturedAt!.Value))
            .ToList();

        return new ReconciliationReport(from, to, entries, missing);
    }

    private static (Payment Payment, string MatchedAs)? MatchPayment(string transactionId, IReadOnlyList<Payment> payments)
    {
        foreach (var payment in payments)
        {
            if (payment.CaptureId == transactionId) return (payment, "capture");
            if (payment.AuthorizationId == transactionId) return (payment, "authorization");
            if (payment.Refunds.Any(r => r.PayPalRefundId == transactionId)) return (payment, "refund");
        }
        return null;
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsSpecification(orderId), ct);
        // Ownership is enforced by reporting not-found: one shopper never learns another's order exists.
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }
}
