using System;
using System.Collections.Concurrent;
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
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // Serializes payment state transitions per order, so a double-click or concurrent
    // retry can never authorize/capture/void/refund the same order twice.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    // PayPal caches responses (including error responses) by PayPal-Request-Id for a long
    // window. With the in-memory dev database, entity ids restart from 1 on every run, so a
    // bare "eshop-authorize-1" key would replay a previous run's cached response. Salting
    // keys with a per-process run id keeps them unique across runs while staying stable for
    // genuine retries within a run. With a persistent database the entity ids are already
    // globally unique and the salt is simply harmless.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IRepository<CatalogItem> itemRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _itemRepository = itemRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<OrderAndPayment> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items,
        Address shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new PaymentConflictException("An order must contain at least one item.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray()), ct);
        if (catalogItems.Count != items.Select(i => i.CatalogItemId).Distinct().Count())
        {
            throw new NotFoundException("One or more catalog items do not exist.");
        }

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);

        var payment = new Payment(order.Id, buyerId, order.Total(), _gateway.Currency);
        await _paymentRepository.AddAsync(payment, ct);

        return new OrderAndPayment { Order = order, Payment = payment };
    }

    public async Task<Payment> PayOrderAsync(string buyerId, int orderId, CardPaymentDetails? card,
        int? savedPaymentMethodId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        var payment = await GetPaymentAsync(orderId, ct);

        using var _ = await LockAsync(orderId, ct);

        // Idempotent in effect: a repeat pay request for an already-authorized order
        // returns the current state instead of authorizing again.
        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.Pending)
        {
            throw new PaymentConflictException($"Order {orderId} cannot be paid in its current state ({payment.Status}).");
        }

        string? vaultTokenId = null;
        if (savedPaymentMethodId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedPaymentMethodId.Value, ct);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new NotFoundException($"Payment method {savedPaymentMethodId} was not found.");
            }
            vaultTokenId = savedCard.VaultTokenId;
        }
        else if (card is null)
        {
            throw new PaymentConflictException("Either card details or a saved payment method id must be supplied.");
        }

        if (payment.PayPalOrderId is null)
        {
            // The merchant account requires a globally unique invoice id per transaction;
            // the order-{orderId} prefix keeps it usable as a reconciliation join key.
            // It is attached at capture time (sending it on the order makes PayPal count
            // the authorize as a duplicate use of the same invoice id).
            var invoiceId = $"order-{order.Id}-{payment.Id}-{Guid.NewGuid().ToString("N")[..8]}";
            var orderResult = await _gateway.CreateOrderAsync(
                idempotencyKey: $"eshop-order-{payment.Id}-{RunId}",
                amount: payment.Amount,
                currency: payment.Currency,
                customId: payment.Id.ToString(),
                ct);
            payment.RecordPayPalOrder(orderResult.PayPalOrderId, invoiceId);
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        var authorization = vaultTokenId is not null
            ? await _gateway.AuthorizeWithVaultedCardAsync(payment.PayPalOrderId!,
                $"eshop-authorize-{payment.Id}-{RunId}", vaultTokenId, ct)
            : await _gateway.AuthorizeWithCardAsync(payment.PayPalOrderId!,
                $"eshop-authorize-{payment.Id}-{RunId}", card!, ct);

        if (string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentConflictException(
                $"The card was declined by PayPal (reason: {authorization.StatusReason ?? "unknown"}).");
        }

        payment.RecordAuthorization(authorization.AuthorizationId, authorization.Status, authorization.ExpirationTime);
        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, ct);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {OrderId} authorized (authorization id recorded).", orderId);
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        var payment = await GetPaymentAsync(orderId, ct);

        using var _ = await LockAsync(orderId, ct);

        // Idempotent: fulfilling an already-captured order returns current state.
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentConflictException($"Order {orderId} cannot be fulfilled in its current state ({payment.Status}).");
        }

        var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, ct);
        if (string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            payment.UpdateAuthorizationStatus(authorization.Status, authorization.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentConflictException(
                $"The PayPal authorization for order {orderId} is {authorization.Status}; the order cannot be fulfilled. Cancel it or ask the shopper to pay again.");
        }

        var stale = authorization.ExpirationTime.HasValue && authorization.ExpirationTime.Value < DateTimeOffset.UtcNow;
        if (stale)
        {
            await RenewAuthorizationAsync(order, payment, authorization, ct);
        }

        GatewayCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId,
                $"eshop-capture-{payment.Id}-{RunId}", payment.Amount, payment.Currency, payment.InvoiceId!, ct);
        }
        catch (PaymentGatewayException ex) when (!stale && ex.IsProviderRejection)
        {
            // The hold went stale between our check and the capture: renew once, then retry.
            await RenewAuthorizationAsync(order, payment, authorization, ct);
            capture = await _gateway.CaptureAsync(payment.AuthorizationId,
                $"eshop-capture-{payment.Id}-{RunId}", payment.Amount, payment.Currency, payment.InvoiceId!, ct);
        }

        if (string.Equals(capture.Status, "DECLINED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(capture.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentConflictException(
                $"PayPal declined the capture for order {orderId} (reason: {capture.StatusReason ?? "unknown"}).");
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.SellerFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {OrderId} fulfilled; capture id recorded.", orderId);
        return payment;
    }

    public async Task<Payment> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        var payment = await GetPaymentAsync(orderId, ct);

        using var _ = await LockAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled || payment.Status == PaymentStatus.Voided)
        {
            return payment;
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentConflictException(
                $"Order {orderId} has already been fulfilled; issue a refund instead of cancelling.");
        }

        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            var voided = await _gateway.VoidAsync(payment.AuthorizationId, $"eshop-void-{payment.Id}-{RunId}", ct);
            payment.MarkVoided(voided.Status);
        }
        else
        {
            payment.MarkVoided(null);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {OrderId} cancelled; held funds released.", orderId);
        return payment;
    }

    public async Task<PaymentRefund> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var payment = await GetPaymentAsync(orderId, ct);

        using var _ = await LockAsync(orderId, ct);

        // Idempotent: a repeated request under the same key returns the original refund.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded)
            || payment.CaptureId is null)
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
        }

        var refundable = payment.RefundableAmount;
        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0)
        {
            throw new PaymentConflictException($"Order {orderId} has nothing left to refund.");
        }
        if (refundAmount > refundable)
        {
            throw new PaymentConflictException(
                $"Refund amount {refundAmount:0.00} exceeds the refundable balance {refundable:0.00} for order {orderId}.");
        }

        var result = await _gateway.RefundAsync(payment.CaptureId,
            $"eshop-refund-{payment.Id}-{idempotencyKey}-{RunId}", refundAmount, payment.Currency, payment.Id.ToString(), ct);

        var refund = payment.AddRefund(idempotencyKey, result.RefundId, refundAmount, result.Status);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {OrderId} refunded {Amount} {Currency}.", orderId, refundAmount, payment.Currency);
        return refund;
    }

    public async Task<IReadOnlyList<OrderAndPayment>> ListOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerIdSpecification(buyerId), ct);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderAndPayment
            {
                Order = o,
                Payment = payments.FirstOrDefault(p => p.OrderId == o.Id)
            })
            .ToList();
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var vaulted = await _gateway.VaultCardAsync(
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}", buyerId, card, ct);

        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultTokenId, vaulted.PayPalCustomerId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        await _savedCardRepository.AddAsync(saved, ct);

        _logger.LogInformation("Saved a card ending in {LastDigits} for a shopper.", vaulted.LastDigits ?? "????");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken ct)
    {
        return await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(paymentMethodId, ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new NotFoundException($"Payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(savedCard.VaultTokenId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.ProviderStatusCode == 404)
        {
            // Already gone at PayPal; still remove it locally.
        }

        await _savedCardRepository.DeleteAsync(savedCard, ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsWithCaptureSpecification(), ct);

        var report = new ReconciliationReport { From = from, To = to };
        var matchedPaymentIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var entry = new ReconciliationEntry { Transaction = txn };

            // Confirmed matches: the exact invoice id generated for a local payment, or a
            // PayPal id recorded on one (capture id, refund id, or the order id a
            // transaction references).
            var confirmed = txn.InvoiceId is not null
                ? payments.FirstOrDefault(p => p.InvoiceId == txn.InvoiceId)
                : null;
            if (confirmed is null && txn.TransactionId is not null)
            {
                confirmed = payments.FirstOrDefault(p =>
                    p.CaptureId == txn.TransactionId ||
                    p.Refunds.Any(r => r.PayPalRefundId == txn.TransactionId) ||
                    (txn.ReferenceId is not null && p.PayPalOrderId == txn.ReferenceId));
            }

            if (confirmed is not null)
            {
                entry.PaymentId = confirmed.Id;
                entry.OrderId = confirmed.OrderId;
                entry.Matched = true;
                matchedPaymentIds.Add(confirmed.Id);
            }
            else
            {
                // Informational hints only, never counted as a match: the naming convention
                // (invoice "order-{orderId}-...", custom id = payment id) points at a local
                // entity, but on a shared merchant account another system's transaction can
                // carry the same numbers, so only exact id matches above are authoritative.
                if (txn.InvoiceId is not null && txn.InvoiceId.StartsWith("order-", StringComparison.OrdinalIgnoreCase))
                {
                    // Format: order-{orderId}-{paymentId}-{suffix}
                    var remainder = txn.InvoiceId.Substring("order-".Length);
                    var orderIdPart = remainder.Split('-')[0];
                    if (int.TryParse(orderIdPart, out var orderId) && payments.Any(p => p.OrderId == orderId))
                    {
                        entry.OrderId = orderId;
                    }
                }
                if (txn.CustomField is not null && int.TryParse(txn.CustomField, out var paymentId)
                    && payments.Any(p => p.Id == paymentId))
                {
                    entry.PaymentId = paymentId;
                }
            }

            report.Transactions.Add(entry);
        }

        report.PaymentsMissingInPayPal = payments
            .Where(p => !matchedPaymentIds.Contains(p.Id) && p.CapturedOn.HasValue
                && p.CapturedOn.Value >= from && p.CapturedOn.Value <= to)
            .ToList();

        return report;
    }

    private async Task RenewAuthorizationAsync(Order order, Payment payment, GatewayAuthorizationInfo authorization, CancellationToken ct)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!,
                $"eshop-reauthorize-{payment.Id}-{authorization.ExpirationTime:yyyyMMddHHmmss}-{RunId}",
                payment.Amount, payment.Currency, ct);
            payment.UpdateAuthorizationStatus(renewed.Status, renewed.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, ct);
        }
        catch (PaymentGatewayException ex) when (ex.IsProviderRejection || ex.ProviderStatusCode is 400 or 403 or 404 or 422)
        {
            // The authorization can no longer be renewed: move the order back to
            // awaiting-payment so the shopper can pay again, and tell the operator.
            payment.RequireRepayment();
            order.MarkAwaitingPayment();
            await _orderRepository.UpdateAsync(order, ct);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentConflictException(
                $"The PayPal authorization for order {order.Id} can no longer be renewed. " +
                "The order was moved back to 'PendingPayment'; ask the shopper to pay again.");
        }
    }

    private async Task<Payment> GetPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private static async Task<IDisposable> LockAsync(int orderId, CancellationToken ct)
    {
        var semaphore = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => _semaphore.Release();
    }
}
