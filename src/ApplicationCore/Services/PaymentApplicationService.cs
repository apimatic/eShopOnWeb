using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the additive payment capability. Each action is separately invocable and idempotent in
/// effect; ownership is enforced per shopper. All PayPal interaction is delegated to
/// <see cref="IPaymentProcessor"/> — this service holds no PayPal specifics.
/// </summary>
public class PaymentApplicationService : IPaymentApplicationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentProcessor _processor;
    private readonly IPaymentSettings _settings;
    private readonly IAppLogger<PaymentApplicationService> _logger;

    public PaymentApplicationService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IUriComposer uriComposer,
        IPaymentProcessor processor,
        IPaymentSettings settings,
        IAppLogger<PaymentApplicationService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _catalogItemRepository = catalogItemRepository;
        _uriComposer = uriComposer;
        _processor = processor;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    // ---- Flow 1: place ----

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput? shipTo, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentValidationException("Every order line must have a quantity of at least 1.");

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(itemIds), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentValidationException($"Catalog item {line.CatalogItemId} was not found.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "N/A")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {order.Id} placed by shopper (awaiting payment), total {order.Total()} {Currency}.");
        return order.Id;
    }

    // ---- Flow 1: pay (authorize) ----

    public async Task<OrderPayment> PayAsync(string buyerId, int orderId, PaymentInstrumentInput instrument,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, cancellationToken);

        // Idempotent: a double-click never authorizes twice.
        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            _logger.LogInformation($"Order {orderId} is already authorized ({payment.AuthorizationId}); returning existing hold.");
            return payment;
        }
        if (payment.Status != PaymentStatus.AwaitingPayment)
            throw new PaymentStateException($"Order {orderId} cannot be paid because it is {payment.Status}.");

        var paymentInstrument = await ResolveInstrumentAsync(buyerId, instrument, cancellationToken);

        var request = new AuthorizationRequest(orderId, payment.Amount, Currency,
            payment.ReconciliationReference, payment.PaymentReference, paymentInstrument);

        var result = await _processor.AuthorizeAsync(request, cancellationToken);

        switch (result.Outcome)
        {
            case AuthorizationOutcome.Declined:
                _logger.LogWarning($"Order {orderId} authorization declined: {result.DeclineReason}");
                throw new PaymentDeclinedException(result.DeclineReason
                    ?? "The card was declined. Please try a different card.");
            case AuthorizationOutcome.ChallengeRequired:
                _logger.LogWarning($"Order {orderId} authorization requires a browser challenge (3DS); not supported.");
                throw new PaymentChallengeRequiredException(
                    "This card requires additional browser-based verification, which this API does not support. Please use a different card.");
            case AuthorizationOutcome.Authorized:
            case AuthorizationOutcome.Pending:
                payment.MarkAuthorized(result.ProcessorOrderId!, result.AuthorizationId!, result.RawStatus,
                    result.ExpiresAt, result.PaymentMethodDescription);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                _logger.LogInformation($"Order {orderId} authorized: hold {result.AuthorizationId} for {payment.Amount} {payment.CurrencyCode} (status {result.RawStatus}).");
                return payment;
            default:
                throw new PaymentGatewayException("Unexpected authorization outcome.",
                    PaymentGatewayFailureKind.Unreadable);
        }
    }

    // ---- Flow 1: fulfil (capture) ----

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Fulfilled)
        {
            _logger.LogInformation($"Order {orderId} is already fulfilled ({payment.CaptureId}); returning existing capture.");
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentStateException($"Order {orderId} cannot be fulfilled because it is {payment.Status}.");

        // Renew a stale authorization before capturing rather than failing fulfilment outright.
        if (payment.AuthorizationExpiresAt is DateTimeOffset expiry && expiry <= DateTimeOffset.UtcNow)
        {
            await RenewAuthorizationOrThrowAsync(payment, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _processor.CaptureAsync(payment.PaymentReference, payment.AuthorizationId!,
                payment.Amount, payment.CurrencyCode, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.Kind == PaymentGatewayFailureKind.RequestRejected)
        {
            // The hold may have gone stale between our check and the capture — try to renew once, then recapture.
            _logger.LogWarning($"Order {orderId} capture rejected ({ex.Message}); attempting to renew the authorization.");
            await RenewAuthorizationOrThrowAsync(payment, cancellationToken);
            capture = await _processor.CaptureAsync(payment.PaymentReference, payment.AuthorizationId!,
                payment.Amount, payment.CurrencyCode, cancellationToken);
        }

        payment.MarkFulfilled(capture.CaptureId, capture.RawStatus, capture.GrossAmount,
            capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation($"Order {orderId} fulfilled: capture {capture.CaptureId} gross {capture.GrossAmount} fee {capture.PayPalFee} net {capture.NetAmount} {capture.CurrencyCode}.");
        return payment;
    }

    private async Task RenewAuthorizationOrThrowAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        var reauth = await _processor.ReauthorizeAsync(payment.PaymentReference, payment.AuthorizationId!,
            payment.Amount, payment.CurrencyCode, cancellationToken);
        if (!reauth.Renewed || reauth.AuthorizationId is null)
        {
            throw new ReauthorizationFailedException(
                $"The authorization for order {payment.OrderId} has expired and can no longer be renewed" +
                (reauth.Reason is null ? "" : $" ({reauth.Reason})") +
                ". Ask the shopper to place and pay for a new order.");
        }
        payment.RenewAuthorization(reauth.AuthorizationId, reauth.RawStatus, reauth.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation($"Order {payment.OrderId} authorization renewed: {reauth.AuthorizationId}.");
    }

    // ---- Flow 1: cancel (void) — operator ----

    public async Task<OrderPayment> CancelAsync(string? buyerId, int orderId, CancellationToken cancellationToken)
    {
        var payment = buyerId is null
            ? await LoadPaymentAsync(orderId, cancellationToken)
            : await LoadOwnedPaymentAsync(buyerId, orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
            return payment;
        if (payment.Status == PaymentStatus.AwaitingPayment)
            throw new PaymentStateException($"Order {orderId} has no held funds to release.");
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentStateException($"Order {orderId} cannot be cancelled because it is {payment.Status}. A fulfilled order must be refunded instead.");

        await _processor.VoidAsync(payment.PaymentReference, payment.AuthorizationId!, cancellationToken);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation($"Order {orderId} cancelled: hold {payment.AuthorizationId} voided, funds released.");
        return payment;
    }

    // ---- Flow 1: refund ----

    public async Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, cancellationToken);

        if (payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded)
            || payment.CaptureId is null)
            throw new PaymentStateException($"Order {orderId} cannot be refunded because it is {payment.Status}.");

        // Idempotent: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Order {orderId} refund for key '{idempotencyKey}' already processed ({existing.RefundId}); returning it.");
            return new RefundOutcome(payment, existing);
        }

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
            throw new RefundExceedsCaptureException($"Order {orderId} has already been fully refunded.");

        decimal refundAmount;
        if (amount is decimal requested)
        {
            if (requested <= 0m)
                throw new PaymentValidationException("A refund amount must be greater than zero.");
            refundAmount = decimal.Round(requested, 2, MidpointRounding.AwayFromZero);
            if (refundAmount > remaining)
                throw new RefundExceedsCaptureException(
                    $"Refund of {refundAmount} {payment.CurrencyCode} exceeds the {remaining} {payment.CurrencyCode} still refundable on order {orderId}.");
        }
        else
        {
            refundAmount = remaining; // full refund of what remains
        }

        var result = await _processor.RefundAsync(payment.CaptureId!, refundAmount, payment.CurrencyCode,
            idempotencyKey, cancellationToken);

        var refund = payment.AddRefund(idempotencyKey, result.RefundId, refundAmount, result.RawStatus);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation($"Order {orderId} refunded {refundAmount} {payment.CurrencyCode}: refund {result.RefundId} (now {payment.Status}).");
        return new RefundOutcome(payment, refund);
    }

    // ---- Flow 1: my orders ----

    public async Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(
            new OrderPaymentsByBuyerSpecification(buyerId), cancellationToken);
        var byOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .Select(o => new OrderWithPayment(o, byOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    public async Task<OrderPayment?> GetPaymentForBuyerAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null || payment.BuyerId != buyerId) return null;
        return payment;
    }

    // ---- Flow 2: saved cards ----

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card,
        BillingAddress? billingAddress, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null) throw new PaymentValidationException("Card details are required to save a card.");

        // Group a shopper's cards under one PayPal customer where we already have one.
        var existing = await _savedCardRepository.ListAsync(
            new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var payPalCustomerId = existing
            .Select(m => m.PayPalCustomerId)
            .FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var result = await _processor.VaultCardAsync(
            new VaultCardRequest(buyerId, payPalCustomerId, card, billingAddress), cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, result.VaultToken, result.PayPalCustomerId,
            result.Brand, result.LastDigits, result.Expiry, result.CardholderName);
        await _savedCardRepository.AddAsync(saved, cancellationToken);
        _logger.LogInformation($"Shopper saved a card: {saved.Brand} ending {saved.LastDigits} (token {saved.VaultToken}).");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsForBuyerAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return (await _savedCardRepository.ListAsync(
            new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken)).ToList();
    }

    public async Task DeleteCardAsync(string buyerId, string paymentMethodId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var card = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(buyerId, paymentMethodId), cancellationToken)
            ?? throw new PaymentResourceNotFoundException($"Saved card '{paymentMethodId}' was not found.");

        await _processor.DeleteVaultedCardAsync(card.VaultToken, cancellationToken);
        await _savedCardRepository.DeleteAsync(card, cancellationToken);
        _logger.LogInformation($"Shopper deleted saved card {paymentMethodId} (token {card.VaultToken}).");
    }

    // ---- Flow 1: reconciliation — operator ----

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
            throw new PaymentValidationException("The reconciliation 'to' date must not be before 'from'.");

        var transactions = await _processor.ListTransactionsAsync(from, to, cancellationToken);

        var payments = await _paymentRepository.ListAsync(new ProcessedPaymentsSpecification(), cancellationToken);
        // Reconcile on the stable eShop reference PayPal echoes back as custom_field.
        var paymentsByReference = payments
            .GroupBy(p => p.ReconciliationReference)
            .ToDictionary(g => g.Key, g => g.First());

        var lines = new List<ReconciliationLine>();
        var matchedReferences = new HashSet<string>();

        foreach (var tx in transactions)
        {
            var reference = tx.CustomField ?? tx.InvoiceId;
            OrderPayment? match = null;
            if (!string.IsNullOrEmpty(reference))
                paymentsByReference.TryGetValue(reference!, out match);

            if (match is not null)
            {
                matchedReferences.Add(reference!);
                lines.Add(new ReconciliationLine(ReconciliationMatch.Matched, reference, match.OrderId,
                    tx.TransactionId, tx.Status, tx.Amount, tx.CurrencyCode ?? match.CurrencyCode,
                    match.CapturedAmount ?? match.Amount, tx.InitiatedAt));
            }
            else
            {
                lines.Add(new ReconciliationLine(ReconciliationMatch.MissingInEShop, reference, null,
                    tx.TransactionId, tx.Status, tx.Amount, tx.CurrencyCode, null, tx.InitiatedAt));
            }
        }

        // eShop payments in range that PayPal's report does not (yet) list.
        foreach (var payment in payments)
        {
            var invoice = payment.ReconciliationReference;
            if (matchedReferences.Contains(invoice)) continue;

            var effectiveDate = payment.FulfilledAt ?? payment.PaidAt;
            if (effectiveDate is null || effectiveDate < from || effectiveDate > to) continue;

            lines.Add(new ReconciliationLine(ReconciliationMatch.MissingInPayPal, invoice, payment.OrderId,
                null, payment.Status.ToString(), null, payment.CurrencyCode,
                payment.CapturedAmount ?? payment.Amount, effectiveDate));
        }

        var report = new ReconciliationReport(from, to,
            transactions.Count,
            payments.Count,
            lines.Count(l => l.Match == ReconciliationMatch.Matched),
            lines.Count(l => l.Match == ReconciliationMatch.MissingInEShop),
            lines.Count(l => l.Match == ReconciliationMatch.MissingInPayPal),
            lines);

        _logger.LogInformation($"Reconciliation {from:o}..{to:o}: {report.PayPalTransactionCount} PayPal tx, {report.MatchedCount} matched, {report.MissingInEShopCount} missing in eShop, {report.MissingInPayPalCount} missing in PayPal.");
        return report;
    }

    // ---- helpers ----

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken) =>
        await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new PaymentResourceNotFoundException($"No payment exists for order {orderId}.");

    private async Task<OrderPayment> LoadOwnedPaymentAsync(string buyerId, int orderId,
        CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);
        // Do not distinguish "not found" from "not yours" — one shopper must never learn of another's orders.
        if (payment is null || payment.BuyerId != buyerId)
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        return payment;
    }

    private async Task<PaymentInstrument> ResolveInstrumentAsync(string buyerId,
        PaymentInstrumentInput instrument, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(instrument.PaymentMethodId))
        {
            var card = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(buyerId, instrument.PaymentMethodId!), cancellationToken)
                ?? throw new PaymentResourceNotFoundException(
                    $"Saved card '{instrument.PaymentMethodId}' was not found.");
            return new PaymentInstrument(null, card.VaultToken, null);
        }

        if (instrument.Card is not null)
            return new PaymentInstrument(instrument.Card, null, instrument.BillingAddress);

        throw new PaymentValidationException(
            "A payment must supply either card details or the id of a saved card.");
    }
}
