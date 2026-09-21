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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentOptions _options;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalPaymentGateway gateway,
        IUriComposer uriComposer,
        PaymentOptions options)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _options = options;
    }

    public async Task<OrderPayment> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        ShippingAddressRequest? shipTo, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentProcessingException(400, "An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentProcessingException(400, "Every order line must have a quantity greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentProcessingException(400, $"Catalog item {line.CatalogItemId} does not exist.");
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = new Address(
            shipTo?.Street ?? "N/A",
            shipTo?.City ?? "N/A",
            shipTo?.State ?? "N/A",
            shipTo?.Country ?? "N/A",
            shipTo?.ZipCode ?? "00000");

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        // Unique per order and stable for its life: seeds both the PayPal invoice_id (which the account
        // requires to be unique) and the PayPal request ids (so a restart with reset order ids never
        // collides with a cached PayPal response).
        var invoiceReference = $"eshop-{order.Id}-{Guid.NewGuid():N}";
        var payment = new OrderPayment(order.Id, buyerId, _options.Currency, order.Total(), invoiceReference);
        return await _paymentRepository.AddAsync(payment, ct);
    }

    public async Task<OrderPayment> PayOrderAsync(string buyerId, int orderId, PayInstruction instruction,
        CancellationToken ct)
    {
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

        // Idempotent in effect: a repeat once authorized never authorizes the shopper twice.
        if (payment.Status == PaymentStatus.Authorized)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.AwaitingPayment && payment.Status != PaymentStatus.Failed)
        {
            throw new PaymentProcessingException(409,
                $"Order {orderId} cannot be paid because it is {payment.Status}.");
        }

        var instrument = await ResolveInstrumentAsync(buyerId, instruction, ct);

        try
        {
            // The invoice reference (unique, stable per order) seeds the PayPal request id, so a genuine
            // double-click reuses it (PayPal dedupes → one hold) while a different order never collides.
            var outcome = await _gateway.AuthorizeAsync(payment.Amount, payment.Currency,
                payment.InvoiceReference, instrument, payment.InvoiceReference, ct);
            payment.MarkAuthorized(outcome.PayPalOrderId, outcome.AuthorizationId, outcome.Status,
                outcome.ExpiresAt, outcome.PaymentMethodDescription);
            await _paymentRepository.UpdateAsync(payment, ct);
            return payment;
        }
        catch (PaymentProcessingException)
        {
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }
    }

    public async Task<OrderPayment> FulfilOrderAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        // Idempotent: already captured (or beyond) → return current state.
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentProcessingException(409,
                $"Order {orderId} cannot be fulfilled because it is {payment.Status}.");
        }

        var authorizationId = payment.AuthorizationId;

        // Renew a stale hold rather than failing fulfilment outright.
        var current = await _gateway.GetAuthorizationAsync(authorizationId, ct);
        if (IsStale(current))
        {
            try
            {
                var renewed = await _gateway.ReauthorizeAsync(authorizationId, payment.Amount, payment.Currency, ct);
                payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
                await _paymentRepository.UpdateAsync(payment, ct);
                authorizationId = renewed.AuthorizationId;
            }
            catch (PaymentProcessingException ex)
            {
                throw new PaymentProcessingException(409,
                    $"The authorization for order {orderId} has expired and could not be renewed " +
                    $"({ex.Message}). Ask the shopper to pay for the order again.", ex);
            }
        }

        var capture = await _gateway.CaptureAsync(authorizationId, payment.Amount, payment.Currency,
            payment.InvoiceReference + ":cap", ct);
        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.CapturedAmount,
            capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        return payment;
    }

    public async Task<OrderPayment> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Canceled)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentProcessingException(409,
                $"Order {orderId} cannot be cancelled because it is {payment.Status}. " +
                "Only an order awaiting fulfilment can be cancelled; use a refund after fulfilment.");
        }

        await _gateway.VoidAsync(payment.AuthorizationId, ct);
        payment.MarkCanceled();
        await _paymentRepository.UpdateAsync(payment, ct);
        return payment;
    }

    public async Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

        // Idempotent: the same key never refunds twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentProcessingException(409,
                $"Order {orderId} cannot be refunded because it is {payment.Status}; it must be fulfilled first.");
        }
        if (payment.CaptureId is null)
        {
            throw new PaymentProcessingException(409, $"Order {orderId} has no capture to refund.");
        }

        if (amount is <= 0m)
        {
            throw new PaymentProcessingException(400, "A refund amount must be greater than zero.");
        }
        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
        {
            throw new PaymentProcessingException(409, $"Order {orderId} has nothing left to refund.");
        }
        if (amount.HasValue && amount.Value > remaining)
        {
            throw new PaymentProcessingException(422,
                $"Refund of {amount.Value} exceeds the {remaining} still refundable on order {orderId}.");
        }

        // Namespace the PayPal request id with the order's unique reference so the same caller key used
        // against a different order (or a prior run) never replays a cached refund; the caller's raw key
        // still drives our own no-double-refund guard above.
        var outcome = await _gateway.RefundAsync(payment.CaptureId, amount, payment.Currency,
            payment.InvoiceReference + ":refund:" + idempotencyKey, ct);

        var recordedAmount = outcome.Amount > 0m ? outcome.Amount : (amount ?? remaining);
        var refund = new PaymentRefund(idempotencyKey, recordedAmount, outcome.PayPalRefundId, outcome.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, ct);
        return refund;
    }

    public async Task<IReadOnlyList<OrderPayment>> GetMyOrderPaymentsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), ct);
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, CardDetails card,
        CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var outcome = await _gateway.VaultCardAsync(card, Guid.NewGuid().ToString("N"), ct);
        var saved = new SavedPaymentMethod(buyerId, outcome.VaultId, outcome.Brand, outcome.Last4,
            outcome.Expiry, outcome.CardholderName);
        await _savedCardRepository.AddAsync(saved, ct);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetPaymentMethodsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var saved = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdForBuyerSpecification(paymentMethodId, buyerId), ct);
        if (saved is null)
        {
            throw new PaymentProcessingException(404, "Saved card not found.");
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(saved.VaultId, ct);
        }
        catch (PaymentProcessingException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal — treat as deleted and remove our record.
        }

        await _savedCardRepository.DeleteAsync(saved, ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        if (to < from)
        {
            throw new PaymentProcessingException(400, "The reconciliation 'to' date must not precede 'from'.");
        }

        var payPalTransactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var allPayments = await _paymentRepository.ListAsync(ct);
        var captures = allPayments.Where(p => p.CaptureId is { Length: > 0 }).ToList();

        // Index eShop payments by every id PayPal might report for them: the capture id, each refund id,
        // and the unique invoice reference stamped on the order.
        var byTransactionId = new Dictionary<string, OrderPayment>(StringComparer.Ordinal);
        var byInvoice = new Dictionary<string, OrderPayment>(StringComparer.Ordinal);
        foreach (var p in captures)
        {
            byTransactionId[p.CaptureId!] = p;
            if (p.InvoiceReference is { Length: > 0 } invoice)
            {
                byInvoice[invoice] = p;
            }
            foreach (var refund in p.Refunds.Where(r => r.PayPalRefundId is { Length: > 0 }))
            {
                byTransactionId[refund.PayPalRefundId!] = p;
            }
        }

        var payPalTransactionIds = new HashSet<string>(
            payPalTransactions.Where(t => t.TransactionId is { Length: > 0 }).Select(t => t.TransactionId!),
            StringComparer.Ordinal);
        var payPalInvoices = new HashSet<string>(
            payPalTransactions.Where(t => t.InvoiceId is { Length: > 0 }).Select(t => t.InvoiceId!),
            StringComparer.Ordinal);

        var matched = new List<ReconciliationLine>();
        var inPayPalNotInEshop = new List<ReconciliationLine>();

        foreach (var tx in payPalTransactions)
        {
            var payment = MatchTransaction(tx, byTransactionId, byInvoice);
            var line = new ReconciliationLine(
                payment?.OrderId,
                tx.InvoiceId ?? payment?.InvoiceReference,
                tx.TransactionId,
                payment?.CaptureId,
                tx.Amount,
                payment?.CapturedAmount,
                tx.Currency ?? payment?.Currency,
                tx.Status);

            if (payment is not null)
            {
                matched.Add(line);
            }
            else
            {
                inPayPalNotInEshop.Add(line);
            }
        }

        var inEshopNotInPayPal = new List<ReconciliationLine>();
        foreach (var c in captures)
        {
            if (c.UpdatedAt < from || c.UpdatedAt > to)
            {
                continue; // outside the window on our own clock
            }

            var known = (c.CaptureId is { Length: > 0 } id && payPalTransactionIds.Contains(id))
                || (c.InvoiceReference is { Length: > 0 } inv && payPalInvoices.Contains(inv));
            if (!known)
            {
                inEshopNotInPayPal.Add(new ReconciliationLine(
                    c.OrderId, c.InvoiceReference, null, c.CaptureId,
                    null, c.CapturedAmount, c.Currency, c.Status.ToString()));
            }
        }

        return new ReconciliationReport(from, to, payPalTransactions.Count, captures.Count, matched.Count,
            matched, inPayPalNotInEshop, inEshopNotInPayPal);
    }

    private static OrderPayment? MatchTransaction(ReconciliationTransaction tx,
        Dictionary<string, OrderPayment> byTransactionId, Dictionary<string, OrderPayment> byInvoice)
    {
        if (tx.TransactionId is { Length: > 0 } id && byTransactionId.TryGetValue(id, out var byId))
        {
            return byId;
        }
        if (tx.InvoiceId is { Length: > 0 } invoice && byInvoice.TryGetValue(invoice, out var byInv))
        {
            return byInv;
        }
        return null;
    }

    private async Task<PaymentInstrument> ResolveInstrumentAsync(string buyerId, PayInstruction instruction,
        CancellationToken ct)
    {
        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdForBuyerSpecification(savedId, buyerId), ct);
            if (saved is null)
            {
                // Owner-scoped: another shopper's card (or a deleted one) is not usable.
                throw new PaymentProcessingException(404, "Saved card not found.");
            }
            return new PaymentInstrument(null, saved.VaultId);
        }

        if (instruction.Card is not null)
        {
            return new PaymentInstrument(instruction.Card, null);
        }

        throw new PaymentProcessingException(400,
            "Provide either card details or the id of a saved card to pay.");
    }

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), ct);
        return payment ?? throw new PaymentProcessingException(404, $"Order {orderId} not found.");
    }

    private async Task<OrderPayment> LoadOwnedPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // 404 rather than 403 so one shopper cannot probe another's order ids.
            throw new PaymentProcessingException(404, $"Order {orderId} not found.");
        }
        return payment;
    }

    private static bool IsStale(AuthorizationOutcome authorization)
    {
        if (string.Equals(authorization.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow;
    }
}
