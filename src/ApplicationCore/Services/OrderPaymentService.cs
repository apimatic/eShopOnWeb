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

/// <summary>
/// Orchestrates the pay-for-an-order and saved-card flows: it enforces the domain rules and the
/// idempotency guarantees, and drives the payment gateway (PayPal) plus the repositories. It is the
/// single place the money movement is coordinated so each API endpoint stays thin.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IRepository<CatalogItem> itemRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _itemRepository = itemRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<OrderPayment> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines,
        Address? shipToAddress, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one line item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least 1.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var shipTo = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = await _orderRepository.AddAsync(new Order(buyerId, shipTo, items), cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _gateway.CurrencyCode);
        return await _paymentRepository.AddAsync(payment, cancellationToken);
    }

    public async Task<OrderPayment> AuthorizeAsync(string buyerId, int orderId,
        PaymentInstrument instrument, CancellationToken cancellationToken)
    {
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent: a double-click never authorizes twice. If the hold is already in place,
        // return the existing state.
        if (payment.Status == PaymentStatus.Authorized)
        {
            return payment;
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentException($"Order {orderId} has already been captured and cannot be authorized again.");
        }
        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new PaymentException($"Order {orderId} has been cancelled and can no longer be paid.");
        }

        // Deterministic, globally-unique key so a repeated request is a no-op on PayPal's side too
        // (the per-payment token keeps it from colliding with a prior run that reused this order id).
        var idempotencyKey = $"authorize-{payment.IdempotencyToken}";

        AuthorizationResult result;
        string? cardDescription;
        int? savedCardId = null;

        if (instrument.SavedCardId.HasValue)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdSpecification(instrument.SavedCardId.Value, buyerId), cancellationToken)
                ?? throw new PaymentResourceNotFoundException(
                    $"Saved card {instrument.SavedCardId} was not found for this shopper.");

            result = await _gateway.AuthorizeWithVaultedCardAsync(
                payment.Amount, savedCard.VaultId, idempotencyKey, cancellationToken);
            cardDescription = savedCard.Describe();
            savedCardId = savedCard.Id;
        }
        else if (instrument.Card is not null)
        {
            result = await _gateway.AuthorizeWithCardAsync(
                payment.Amount, instrument.Card, idempotencyKey, cancellationToken);
            cardDescription = DescribeCard(result);
        }
        else
        {
            throw new PaymentException("A payment must supply either card details or a saved card id.");
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, cardDescription, savedCardId);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Authorized order {orderId}: PayPal order {result.PayPalOrderId}, authorization {result.AuthorizationId}.");
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // Idempotent: fulfilling an already-captured order does not capture again.
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentException($"Order {orderId} is not authorized and cannot be fulfilled (status: {payment.Status}).");
        }

        var captureKey = $"capture-auth-{payment.AuthorizationId}";
        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId, captureKey, cancellationToken);
        }
        catch (AuthorizationExpiredException ex)
        {
            // The hold went stale before fulfilment: renew it rather than failing outright.
            // ReauthorizeAsync throws ReauthorizationNotAllowedException if it can no longer be renewed.
            _logger.LogInformation($"Authorization {payment.AuthorizationId} for order {orderId} is stale ({ex.Message}); reauthorizing.");
            var reauthorized = await _gateway.ReauthorizeAsync(payment.AuthorizationId, payment.Amount, cancellationToken);
            payment.MarkReauthorized(reauthorized.AuthorizationId);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            var renewedKey = $"capture-auth-{reauthorized.AuthorizationId}";
            capture = await _gateway.CaptureAsync(reauthorized.AuthorizationId, renewedKey, cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Captured order {orderId}: capture {capture.CaptureId}, gross {capture.GrossAmount}, fee {capture.PayPalFee}, net {capture.NetAmount}.");
        return payment;
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment; // idempotent
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentException($"Order {orderId} has already been captured; cancel is not possible — issue a refund instead.");
        }

        // Only an existing hold needs releasing; an unpaid order has no money to release.
        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Cancelled order {orderId}; any held funds were released.");
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent on the caller key: a repeat under the same key returns the same refund.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException($"Order {orderId} has no captured payment to refund (status: {payment.Status}).");
        }
        if (payment.CaptureId is null)
        {
            throw new PaymentException($"Order {orderId} has no capture to refund.");
        }

        var refundable = payment.RefundableAmount();
        if (amount.HasValue)
        {
            if (amount.Value <= 0)
            {
                throw new PaymentException("A refund amount must be greater than zero.");
            }
            if (amount.Value > refundable)
            {
                throw new PaymentException($"Refund of {amount.Value} exceeds the refundable amount {refundable} for order {orderId}.");
            }
        }

        var effectiveAmount = amount ?? refundable;

        // Our own per-order key already guarantees idempotency (the pre-check above). The id handed
        // to PayPal is namespaced by the globally-unique capture id so it never collides with a
        // different capture — or a prior run — that legitimately reused the same caller key.
        var payPalRequestId = $"refund-{payment.CaptureId}-{idempotencyKey}";
        var result = await _gateway.RefundAsync(payment.CaptureId, amount, payPalRequestId, cancellationToken);

        var refund = payment.AddRefund(result.RefundId, effectiveAmount, result.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Refunded order {orderId}: refund {result.RefundId}, amount {effectiveAmount}.");
        return refund;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), cancellationToken);
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id);

        var result = new List<OrderWithPayment>();
        foreach (var payment in payments.OrderByDescending(p => p.OrderId))
        {
            if (ordersById.TryGetValue(payment.OrderId, out var order))
            {
                result.Add(new OrderWithPayment(order, payment));
            }
        }
        return result;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardPaymentDetails card, string? alias,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var vaulted = await _gateway.VaultCardAsync(card, cancellationToken);

        var savedCard = new SavedCard(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.Last4,
            vaulted.ExpiryMonth, vaulted.ExpiryYear, alias);
        await _savedCardRepository.AddAsync(savedCard, cancellationToken);

        _logger.LogInformation($"Saved a card for a shopper: vault {vaulted.VaultId} ({savedCard.Describe()}).");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken cancellationToken)
    {
        var card = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdSpecification(savedCardId, buyerId), cancellationToken)
            ?? throw new PaymentResourceNotFoundException($"Saved card {savedCardId} was not found for this shopper.");

        // Best-effort removal from PayPal's vault; removing our own record is what makes it
        // unusable to pay through this API regardless.
        await _gateway.DeleteVaultedCardAsync(card.VaultId, cancellationToken);
        await _savedCardRepository.DeleteAsync(card, cancellationToken);

        _logger.LogInformation($"Deleted saved card {savedCardId} for a shopper.");
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var transactions = await _gateway.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new AllOrderPaymentsSpecification(), cancellationToken);

        // eShop's own view of the transactions that fall in the range: each capture and each refund.
        var eShopEntries = new Dictionary<string, (int OrderId, decimal Amount, string Kind)>();
        foreach (var payment in payments)
        {
            if (payment.CaptureId is not null && payment.CapturedAt is { } capturedAt
                && capturedAt >= from && capturedAt <= to)
            {
                eShopEntries[payment.CaptureId] = (payment.OrderId, payment.CapturedAmount ?? payment.Amount, "capture");
            }
            foreach (var refund in payment.Refunds)
            {
                if (refund.CreatedAt >= from && refund.CreatedAt <= to)
                {
                    eShopEntries[refund.PayPalRefundId] = (payment.OrderId, refund.Amount, "refund");
                }
            }
        }

        var payPalById = transactions
            .GroupBy(t => t.TransactionId)
            .ToDictionary(g => g.Key, g => g.First());

        var lines = new List<ReconciliationLine>();

        foreach (var txn in payPalById.Values)
        {
            if (eShopEntries.TryGetValue(txn.TransactionId, out var eShop))
            {
                lines.Add(new ReconciliationLine(txn.TransactionId, ReconciliationState.Matched,
                    eShop.OrderId, txn.Amount, eShop.Amount, txn.Status, eShop.Kind, txn.CurrencyCode));
            }
            else
            {
                lines.Add(new ReconciliationLine(txn.TransactionId, ReconciliationState.PayPalOnly,
                    null, txn.Amount, null, txn.Status, "unknown", txn.CurrencyCode));
            }
        }

        foreach (var kvp in eShopEntries)
        {
            if (!payPalById.ContainsKey(kvp.Key))
            {
                lines.Add(new ReconciliationLine(kvp.Key, ReconciliationState.EShopOnly,
                    kvp.Value.OrderId, null, kvp.Value.Amount, null, kvp.Value.Kind, _gateway.CurrencyCode));
            }
        }

        return new ReconciliationReport(
            from, to, _gateway.CurrencyCode,
            lines.Count(l => l.State == ReconciliationState.Matched),
            lines.Count(l => l.State == ReconciliationState.PayPalOnly),
            lines.Count(l => l.State == ReconciliationState.EShopOnly),
            lines);
    }

    // --- helpers ---

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new PaymentResourceNotFoundException($"No payment was found for order {orderId}.");
    }

    private async Task<OrderPayment> LoadOwnedPaymentAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Reported as not-found so a shopper cannot learn of another's order.
            throw new PaymentResourceNotFoundException($"No payment was found for order {orderId}.");
        }
        return payment;
    }

    private static string DescribeCard(AuthorizationResult result)
    {
        if (string.IsNullOrEmpty(result.CardLast4))
        {
            return "card";
        }
        return string.IsNullOrEmpty(result.CardBrand)
            ? $"****{result.CardLast4}"
            : $"{result.CardBrand} ****{result.CardLast4}";
    }
}
