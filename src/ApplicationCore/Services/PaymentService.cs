using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the money movement and operator flows on top of the existing order model.
/// Idempotency is enforced in effect: an already-authorized order is not authorized again,
/// an already-captured order is not captured again, and a refund repeated under the same
/// idempotency key returns the original refund without refunding twice.
/// </summary>
public class PaymentService : IPaymentService
{
    // Unique per application run. Combined with the order id it yields an invoice id that is
    // deterministic within a run (so a double-click reuses it and PayPal de-duplicates) yet
    // never collides with a previous run's invoice ids at PayPal.
    private static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 8);

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IRepository<Payment> paymentRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalClient payPal,
        IUriComposer uriComposer,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> items,
        Address? shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }

        // Aggregate quantities by catalog item so a double-listed item is summed, not duplicated.
        var requested = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (requested.Any(r => r.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least one.");
        }

        var catalogItems = await _catalogRepository.ListAsync(
            new CatalogItemsSpecification(requested.Select(r => r.CatalogItemId).ToArray()), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in requested)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new NotFoundException($"Catalog item {line.CatalogItemId} does not exist.");

            // Amounts come from catalog prices, never from the caller.
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation("Placed order {0} for {1} with total {2}", order.Id, buyerId, order.Total());
        return order.Id;
    }

    public async Task AuthorizeOrderAsync(int orderId, string buyerId, PaymentInstruction instruction,
        CancellationToken ct = default)
    {
        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);

        var existing = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        if (existing is not null && existing.Status != PaymentStatus.Failed)
        {
            // Idempotent: the order is already (being) paid — a double-click must not authorize twice.
            _logger.LogInformation("Order {0} already has payment {1} in state {2}; skipping re-authorization.",
                orderId, existing.Id, existing.Status);
            return;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException(order.Id, order.Status, nameof(AuthorizeOrderAsync));
        }

        var amount = order.Total();
        if (amount <= 0)
        {
            throw new PaymentException($"Order {orderId} has a non-positive total and cannot be paid.");
        }

        var currency = _payPal.Currency;
        var invoiceId = InvoiceIdFor(orderId);
        var idempotencyKey = $"authorize-{RunId}-order-{orderId}";

        AuthorizationResult result;
        int? paymentMethodId = null;

        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var method = await _paymentMethodRepository.GetByIdAsync(savedId, ct)
                ?? throw new NotFoundException($"Saved card {savedId} was not found.");
            if (method.BuyerId != buyerId)
            {
                throw new ForbiddenException("The specified saved card does not belong to the caller.");
            }
            paymentMethodId = method.Id;
            result = await _payPal.AuthorizeOrderWithVaultAsync(amount, currency, invoiceId, method.VaultId, idempotencyKey, ct);
        }
        else if (instruction.Card is not null)
        {
            result = await _payPal.AuthorizeOrderWithCardAsync(amount, currency, invoiceId, instruction.Card, idempotencyKey, ct);
        }
        else
        {
            throw new PaymentException("A payment must supply either card details or a saved card id.");
        }

        var payment = new Payment(orderId, invoiceId, currency, amount, result.PayPalOrderId, result.AuthorizationId, paymentMethodId);
        await _paymentRepository.AddAsync(payment, ct);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Authorized {0} {1} for order {2} (paypalOrder {3}, auth {4}).",
            amount, currency, orderId, result.PayPalOrderId, result.AuthorizationId);
    }

    public async Task FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct)
            ?? throw new NotFoundException($"Order {orderId} was not found.");
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct)
            ?? throw new PaymentException($"Order {orderId} has no payment to fulfil; it must be paid first.");

        if (payment.Status == PaymentStatus.Captured || order.Status == OrderStatus.Fulfilled)
        {
            _logger.LogInformation("Order {0} already fulfilled; skipping capture.", orderId);
            return;
        }

        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {orderId} payment is '{payment.Status}' and cannot be fulfilled.");
        }

        var idempotencyKey = $"capture-{RunId}-order-{orderId}";
        var capture = await CaptureRenewingIfStaleAsync(payment, idempotencyKey, ct);

        payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Fulfilled order {0}: captured {1}, fee {2}, net {3}.",
            orderId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
    }

    /// <summary>
    /// Captures the authorization, renewing it first if it has gone stale. If the
    /// authorization can no longer be renewed, surfaces an operator-actionable error.
    /// </summary>
    private async Task<CaptureResult> CaptureRenewingIfStaleAsync(Payment payment, string idempotencyKey, CancellationToken ct)
    {
        // Proactively check the authorization; renew if PayPal reports it is no longer capturable.
        string status;
        try
        {
            status = await _payPal.GetAuthorizationStatusAsync(payment.AuthorizationId, ct);
        }
        catch (PayPalException ex)
        {
            _logger.LogWarning("Could not read authorization {0} status ({1}); will attempt capture.", payment.AuthorizationId, ex.Message);
            status = "CREATED";
        }

        if (status is "VOIDED" or "DENIED")
        {
            throw new PaymentException($"Order {payment.OrderId} cannot be fulfilled: its authorization is '{status}'. Ask the shopper to pay for the order again.");
        }

        if (status is "EXPIRED" or "PENDING")
        {
            await RenewAuthorizationAsync(payment, ct);
        }

        try
        {
            return await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId, payment.Amount, payment.Currency, idempotencyKey, ct);
        }
        catch (PayPalException ex) when (IsAuthorizationStale(ex))
        {
            _logger.LogWarning("Capture of authorization {0} failed as stale ({1}); reauthorizing.", payment.AuthorizationId, ex.Issue ?? ex.Name);
            await RenewAuthorizationAsync(payment, ct);
            return await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId, payment.Amount, payment.Currency, idempotencyKey, ct);
        }
    }

    private async Task RenewAuthorizationAsync(Payment payment, CancellationToken ct)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId, payment.Amount, payment.Currency, ct);
            payment.ApplyReauthorization(reauth.AuthorizationId);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Reauthorized order {0}: new authorization {1}.", payment.OrderId, reauth.AuthorizationId);
        }
        catch (PayPalException ex)
        {
            throw new PaymentException(
                $"Order {payment.OrderId} cannot be fulfilled: its payment authorization has expired and can no longer be renewed ({ex.Issue ?? ex.Name ?? ex.Message}). Ask the shopper to pay for the order again.", ex);
        }
    }

    private static bool IsAuthorizationStale(PayPalException ex)
    {
        var issue = ex.Issue ?? string.Empty;
        return issue is "AUTHORIZATION_EXPIRED" or "AUTH_CAPTURE_CURRENCY_MISMATCH"
            or "MAX_CAPTURE_COUNT_EXCEEDED" or "AUTHORIZATION_VOIDED"
            || issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct)
            ?? throw new NotFoundException($"Order {orderId} was not found.");

        if (order.Status == OrderStatus.Cancelled)
        {
            return; // idempotent
        }

        if (order.Status == OrderStatus.Fulfilled || order.Status == OrderStatus.Refunded || order.Status == OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException($"Order {orderId} is '{order.Status}' and can no longer be cancelled; refund it instead.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        if (payment is not null && payment.Status == PaymentStatus.Authorized)
        {
            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, ct);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Voided authorization {0} for cancelled order {1}.", payment.AuthorizationId, orderId);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
    }

    public async Task<int> RefundOrderAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        await LoadOwnedOrderAsync(orderId, buyerId, ct);

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct)
            ?? throw new PaymentException($"Order {orderId} has no payment to refund.");

        if (payment.CaptureId is null ||
            (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException($"Order {orderId} has not been fulfilled/captured, so there is nothing to refund.");
        }

        // Idempotency: a repeat under the same key returns the original refund, no second call.
        var priorRefund = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (priorRefund is not null)
        {
            _logger.LogInformation("Refund idempotency key {0} already applied to order {1}; returning refund {2}.",
                idempotencyKey, orderId, priorRefund.Id);
            return priorRefund.Id;
        }

        var refundAmount = amount ?? payment.RefundableRemaining;
        if (refundAmount <= 0)
        {
            throw new PaymentException($"Order {orderId} has nothing left to refund.");
        }
        if (refundAmount > payment.RefundableRemaining)
        {
            throw new PaymentException(
                $"Refund of {refundAmount:0.00} {payment.Currency} exceeds the {payment.RefundableRemaining:0.00} {payment.Currency} remaining on order {orderId}.");
        }

        // The app de-duplicates on the caller's raw key (above). The value sent to PayPal as
        // its idempotency header is derived so it stays deterministic for this logical refund
        // yet never collides with the same caller key used in a previous run.
        var payPalRequestId = $"refund-{RunId}-{payment.CaptureId}-{idempotencyKey}";
        var result = await _payPal.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency, payPalRequestId, ct);

        var refundStatus = result.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase)
            ? RefundStatus.Completed
            : RefundStatus.Pending;
        var refund = payment.AddRefund(result.RefundId, refundAmount, idempotencyKey, refundStatus);
        await _paymentRepository.UpdateAsync(payment, ct);

        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        order!.MarkRefunded(partial: payment.Status == PaymentStatus.PartiallyRefunded);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Refunded {0} {1} on order {2} (refund {3}).", refundAmount, payment.Currency, orderId, refund.Id);
        return refund.Id;
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var views = new List<OrderPaymentView>(orders.Count);
        foreach (var order in orders)
        {
            var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id), ct);
            views.Add(new OrderPaymentView(order, payment));
        }
        return views;
    }

    public async Task<OrderPaymentView> GetOrderViewAsync(int orderId, string? buyerId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new NotFoundException($"Order {orderId} was not found.");
        if (buyerId is not null && order.BuyerId != buyerId)
        {
            throw new ForbiddenException("This order does not belong to the caller.");
        }
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        return new OrderPaymentView(order, payment);
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _payPal.VaultCardAsync(card, ct);
        var method = new PaymentMethod(buyerId, vaulted.VaultId, vaulted.CustomerId, vaulted.Brand,
            vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        await _paymentMethodRepository.AddAsync(method, ct);

        _logger.LogInformation("Saved card {0} ({1} ****{2}) for {3}.", method.Id, method.Brand, method.LastFourDigits, buyerId);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct = default)
    {
        return await _paymentMethodRepository.ListAsync(new PaymentMethodsByBuyerSpec(buyerId), ct);
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var method = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, ct);
        if (method is null || method.BuyerId != buyerId)
        {
            // Do not disclose existence of another shopper's card.
            throw new NotFoundException($"Saved card {paymentMethodId} was not found.");
        }

        // Best-effort removal at PayPal; local removal is what makes it unusable to pay.
        try
        {
            await _payPal.DeleteVaultedCardAsync(method.VaultId, ct);
        }
        catch (PayPalException ex)
        {
            _logger.LogWarning("Could not delete vaulted card {0} at PayPal ({1}); removing locally anyway.", method.VaultId, ex.Message);
        }

        await _paymentMethodRepository.DeleteAsync(method, ct);
        _logger.LogInformation("Deleted saved card {0} for {1}.", paymentMethodId, buyerId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
        {
            throw new PaymentException("Reconciliation 'to' must not be before 'from'.");
        }

        var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsWithRefundsSpec(), ct);

        var paymentsByInvoice = payments
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First());

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactionCount = transactions.Count
        };

        var matchedInvoices = new HashSet<string>();
        foreach (var txn in transactions)
        {
            var line = new ReconciliationLine
            {
                PayPalTransactionId = txn.TransactionId,
                PayPalStatus = txn.Status,
                PayPalAmount = txn.Amount,
                PayPalFee = txn.FeeAmount,
                InvoiceId = txn.InvoiceId
            };

            if (!string.IsNullOrEmpty(txn.InvoiceId) && paymentsByInvoice.TryGetValue(txn.InvoiceId!, out var payment))
            {
                line.OrderId = payment.OrderId;
                line.EShopPaymentStatus = payment.Status.ToString();
                line.EShopAmount = payment.CapturedAmount ?? payment.Amount;
                report.Matched.Add(line);
                matchedInvoices.Add(txn.InvoiceId!);
            }
            else
            {
                report.InPayPalNotInEShop.Add(line);
            }
        }

        // eShop payments in the window that PayPal's report does not (yet) show.
        foreach (var payment in payments.Where(p => p.CreatedAt >= from && p.CreatedAt <= to))
        {
            if (matchedInvoices.Contains(payment.InvoiceId))
            {
                continue;
            }
            report.InEShopNotInPayPal.Add(new ReconciliationLine
            {
                InvoiceId = payment.InvoiceId,
                OrderId = payment.OrderId,
                EShopPaymentStatus = payment.Status.ToString(),
                EShopAmount = payment.CapturedAmount ?? payment.Amount
            });
        }

        return report;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        // Load with items so Order.Total() is accurate (GetByIdAsync would not include them).
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new NotFoundException($"Order {orderId} was not found.");
        if (order.BuyerId != buyerId)
        {
            throw new ForbiddenException("This order does not belong to the caller.");
        }
        return order;
    }

    /// <summary>The invoice id used on the PayPal side to tie a transaction back to an order.</summary>
    private static string InvoiceIdFor(int orderId) => $"eshop-{RunId}-order-{orderId}";
}
