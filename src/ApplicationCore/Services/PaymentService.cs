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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;

    // A hold whose expiry is within this window is proactively renewed before capture.
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(5);

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipTo, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw PaymentException.Invalid("An order must contain at least one line item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw PaymentException.Invalid("Every line item must have a quantity of at least 1.");

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), ct);

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw PaymentException.Invalid($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        // Order requires a shipping address; supply a placeholder when the caller omits one.
        var address = shipTo ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, address, orderItems);
        return await _orderRepository.AddAsync(order, ct);
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken ct)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, ct);

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);

        // Idempotency / state guards: never place a second hold or re-charge a settled order.
        if (payment is not null)
        {
            switch (payment.Status)
            {
                case PaymentStatus.Authorized:
                    return payment; // hold already in place — treat repeat as a no-op
                case PaymentStatus.Captured:
                case PaymentStatus.PartiallyRefunded:
                case PaymentStatus.Refunded:
                    throw PaymentException.Conflict("This order has already been paid and captured.");
                case PaymentStatus.Voided:
                    throw PaymentException.Conflict("This order was cancelled and can no longer be paid.");
            }
        }

        var (card, vaultId) = await ResolveInstrumentAsync(buyerId, instruction, ct);

        var amount = order.Total();
        if (amount <= 0)
            throw PaymentException.Invalid("The order total must be greater than zero to authorize a payment.");

        if (payment is null)
        {
            // Unique per payment (PayPal requires invoice_id uniqueness) and used to reconcile.
            var invoiceId = $"ESHOP-{orderId}-{Guid.NewGuid():N}";
            payment = new Payment(orderId, buyerId, amount, _payPal.CurrencyCode, invoiceId);
            payment = await _paymentRepository.AddAsync(payment, ct);
        }

        var input = new AuthorizeOrderInput(
            Amount: amount,
            CurrencyCode: _payPal.CurrencyCode,
            InvoiceId: payment.InvoiceId,
            ReferenceId: orderId.ToString(),
            CustomId: orderId.ToString(),
            Card: card,
            VaultId: vaultId);

        try
        {
            // Stable PayPal-Request-Id keyed to this payment makes the authorize call idempotent.
            var auth = await _payPal.AuthorizeOrderAsync(input, $"auth-{payment.InvoiceId}", ct);
            payment.MarkAuthorized(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation($"Authorized order {orderId}: authorization {auth.AuthorizationId} ({auth.Status}).");
            return payment;
        }
        catch (PayPalApiException ex)
        {
            payment.MarkAuthorizationFailed(ex.Message);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogWarning($"Authorization failed for order {orderId}: {ex.Name} - {ex.Message} (debug_id={ex.DebugId}).");
            throw PaymentException.Declined($"The card payment was declined: {ex.Message}", ex.Issues);
        }
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
            throw PaymentException.Conflict("This order has no authorized payment to capture.");

        if (payment.Status == PaymentStatus.Captured || payment.Status == PaymentStatus.PartiallyRefunded || payment.Status == PaymentStatus.Refunded)
            return payment; // already captured — idempotent

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw PaymentException.Conflict($"This order cannot be fulfilled from its current state ({payment.Status}).");

        var authorizationId = payment.AuthorizationId;

        // Proactively renew a hold that has expired (or is about to) before attempting capture.
        if (payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value - ExpirySkew <= DateTimeOffset.UtcNow)
        {
            authorizationId = await RenewAuthorizationAsync(payment, ct);
        }

        PayPalCapture capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(authorizationId, $"capture-{payment.InvoiceId}", ct);
        }
        catch (PayPalApiException ex) when (ex.IsAuthorizationExpired)
        {
            // The hold went stale between our check and the capture — renew and try once more.
            _logger.LogWarning($"Authorization {authorizationId} for order {orderId} was stale; renewing before capture.");
            authorizationId = await RenewAuthorizationAsync(payment, ct);
            capture = await _payPal.CaptureAuthorizationAsync(authorizationId, $"capture-{payment.InvoiceId}", ct);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Captured order {orderId}: capture {capture.CaptureId}, gross {capture.GrossAmount}, fee {capture.PayPalFee}, net {capture.NetAmount}.");
        return payment;
    }

    private async Task<string> RenewAuthorizationAsync(Payment payment, CancellationToken ct)
    {
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, ct);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            return renewed.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw PaymentException.Conflict(
                "The payment hold for this order has expired and can no longer be renewed. " +
                "Ask the shopper to pay for the order again before fulfilling it.",
                ex.Issues);
        }
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
            throw PaymentException.Conflict("This order has no authorized payment to cancel.");

        if (payment.Status == PaymentStatus.Voided)
            return payment; // already released — idempotent

        if (payment.Status == PaymentStatus.Captured || payment.Status == PaymentStatus.PartiallyRefunded || payment.Status == PaymentStatus.Refunded)
            throw PaymentException.Conflict("This order has already been fulfilled; issue a refund instead of cancelling.");

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw PaymentException.Conflict($"This order cannot be cancelled from its current state ({payment.Status}).");

        await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, $"void-{payment.InvoiceId}", ct);
        payment.MarkVoided();
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Cancelled order {orderId}: authorization {payment.AuthorizationId} voided, funds released.");
        return payment;
    }

    public async Task<(Payment Payment, PaymentRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw PaymentException.Invalid("A refund idempotency key is required.");

        await LoadOwnedOrderAsync(buyerId, orderId, ct);

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
            throw PaymentException.Conflict("This order has no captured payment to refund.");

        if (payment.CaptureId is null ||
            (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded))
            throw PaymentException.Conflict("Only a captured payment can be refunded.");

        var refundAmount = amount ?? payment.RefundableRemaining();

        PaymentRefund refund;
        try
        {
            refund = payment.AddRefund(idempotencyKey, refundAmount);
        }
        catch (InvalidOperationException ex)
        {
            throw PaymentException.Conflict(ex.Message);
        }

        // Idempotent replay: the same key already produced a refund — return it without re-refunding.
        if (refund.PayPalRefundId is not null)
            return (payment, refund);

        try
        {
            var result = await _payPal.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.CurrencyCode, idempotencyKey, ct);
            payment.ConfirmRefund(refund, result.RefundId, result.Status);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation($"Refunded order {orderId}: refund {result.RefundId} of {refundAmount} ({result.Status}).");
            return (payment, refund);
        }
        catch (PayPalApiException ex)
        {
            payment.DiscardRefund(refund);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw PaymentException.Conflict($"The refund could not be processed: {ex.Message}", ex.Issues);
        }
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), ct);
        var byOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderWithPayment(o, byOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            throw PaymentException.Invalid("'to' must be on or after 'from'.");

        var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);
        var allPayments = await _paymentRepository.ListAsync(ct);

        // eShop payments that moved money and are anchored in this window (by creation time).
        var eShopByInvoice = allPayments
            .Where(p => p.CaptureId is not null || p.Status == PaymentStatus.Authorized)
            .ToDictionary(p => p.InvoiceId, StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationMatch>();
        var payPalOnly = new List<ReconciliationPayPalOnly>();
        var matchedInvoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in transactions)
        {
            var invoice = tx.InvoiceId ?? tx.CustomField;
            if (invoice is not null && eShopByInvoice.TryGetValue(invoice, out var p))
            {
                matchedInvoices.Add(p.InvoiceId);
                matched.Add(new ReconciliationMatch(
                    tx.TransactionId, invoice, p.OrderId, tx.Amount, p.Amount,
                    tx.Status, p.Status.ToString(),
                    Math.Abs(Math.Abs(tx.Amount) - p.Amount) < 0.005m));
            }
            else
            {
                payPalOnly.Add(new ReconciliationPayPalOnly(
                    tx.TransactionId, tx.InvoiceId, tx.Amount, tx.CurrencyCode, tx.Status, tx.EventCode, tx.InitiationDate));
            }
        }

        var eShopOnly = allPayments
            .Where(p => (p.CaptureId is not null || p.Status == PaymentStatus.Authorized)
                        && p.CreatedAt >= from && p.CreatedAt <= to
                        && !matchedInvoices.Contains(p.InvoiceId))
            .Select(p => new ReconciliationEShopOnly(p.OrderId, p.InvoiceId, p.Amount, p.CurrencyCode, p.Status.ToString()))
            .ToList();

        return new ReconciliationReport(from, to, matched, payPalOnly, eShopOnly);
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        // Return 404 (not 403) for another shopper's order so we don't reveal it exists.
        if (order is null || order.BuyerId != buyerId)
            throw PaymentException.NotFound($"Order {orderId} was not found.");
        return order;
    }

    private async Task<(CardDetails? Card, string? VaultId)> ResolveInstrumentAsync(string buyerId, PaymentInstruction instruction, CancellationToken ct)
    {
        var hasCard = instruction.Card is not null;
        var hasSaved = instruction.SavedPaymentMethodId.HasValue;

        if (hasCard == hasSaved)
            throw PaymentException.Invalid("Provide either card details or a saved payment method id, but not both.");

        if (hasCard)
            return (instruction.Card, null);

        var saved = await _savedCardRepository.GetByIdAsync(instruction.SavedPaymentMethodId!.Value, ct);
        if (saved is null || saved.BuyerId != buyerId)
            throw PaymentException.NotFound("The specified saved payment method was not found.");

        return (null, saved.PayPalVaultId);
    }
}
