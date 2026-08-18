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
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates order payment (place → authorize → fulfil → cancel/refund), saved cards, and
/// reconciliation, sitting between the API and the PayPal gateway. It owns the payment state machine and
/// the ownership checks; the gateway owns the PayPal wire calls.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IReadRepository<CatalogItem> _catalogItemRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IReadRepository<CatalogItem> catalogItemRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _catalogItemRepository = catalogItemRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one item.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException($"Quantity for catalog item {line.CatalogItemId} must be positive.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId);
            if (catalogItem is null)
            {
                throw new PaymentValidationException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        var createdOrder = await _orderRepository.AddAsync(order, ct);

        var payment = new Payment(createdOrder.Id, buyerId, createdOrder.Total(), _payPal.Currency);
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation($"Placed order {createdOrder.Id} for {buyerId}, total {createdOrder.Total()} {_payPal.Currency}.");
        return createdOrder.Id;
    }

    public async Task<Payment> AuthorizeOrderAsync(int orderId, string buyerId, PaymentInstruction instruction,
        CancellationToken ct)
    {
        var (_, payment) = await LoadOwnedOrderAndPaymentAsync(orderId, buyerId, ct);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured)
        {
            return payment;
        }
        if (payment.Status is PaymentStatus.Cancelled or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} can no longer be paid (status {payment.Status}).");
        }

        var gatewayInstrument = await ResolveInstrumentAsync(buyerId, instruction, ct);

        var result = await _payPal.AuthorizeAsync(payment.Amount, gatewayInstrument,
            IdempotencyKeys.Authorize(payment.Id), ct);

        payment.SetAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Authorized order {orderId}: hold {result.AuthorizationId} ({result.Status}).");
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Captured)
        {
            return payment; // already fulfilled — idempotent
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} cannot be fulfilled from status {payment.Status}; it must be authorized first.");
        }

        var authorizationId = payment.AuthorizationId!;

        // Renew a hold that has already gone stale before we try to take the money.
        if (IsAuthorizationStale(payment.AuthorizationExpiresAt))
        {
            authorizationId = await RenewAuthorizationAsync(payment, ct);
        }

        CaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(authorizationId, IdempotencyKeys.Capture(payment.Id, authorizationId), ct);
        }
        catch (PayPalApiException ex) when (ex.IsClientError)
        {
            // A hold that lapsed between our check and the capture surfaces as a provider rejection.
            // Renew once and retry; if it still fails, say so in terms the operator can act on.
            _logger.LogWarning($"Capture of order {orderId} rejected ({ex.ProviderStatusCode}); attempting to renew the authorization.");
            authorizationId = await RenewAuthorizationAsync(payment, ct);
            try
            {
                capture = await _payPal.CaptureAsync(authorizationId, IdempotencyKeys.Capture(payment.Id, authorizationId), ct);
            }
            catch (PayPalApiException retryEx) when (retryEx.IsClientError)
            {
                throw new InvalidPaymentOperationException(
                    $"Order {orderId} could not be fulfilled: the authorization is no longer valid and the renewed " +
                    "authorization was also rejected. Ask the shopper to pay for this order again.");
            }
        }

        payment.SetCaptured(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation(
            $"Fulfilled order {orderId}: captured {capture.CapturedAmount} (fee {capture.PayPalFee}, net {capture.NetAmount}).");
        return payment;
    }

    public async Task<Payment> CancelOrderAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment; // already cancelled — idempotent
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} cannot be cancelled from status {payment.Status}. " +
                "A fulfilled order must be refunded, not cancelled.");
        }

        await _payPal.VoidAsync(payment.AuthorizationId!, ct);

        payment.SetCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Cancelled order {orderId}: released hold {payment.AuthorizationId}.");
        return payment;
    }

    public async Task<Refund> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} cannot be refunded from status {payment.Status}; only a fulfilled order can be refunded.");
        }

        // Repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (amount.HasValue)
        {
            if (amount.Value <= 0)
            {
                throw new PaymentValidationException("Refund amount must be positive.");
            }
            if (amount.Value > payment.RemainingRefundable)
            {
                throw new PaymentValidationException(
                    $"Refund of {amount.Value} exceeds the remaining refundable amount {payment.RemainingRefundable}.");
            }
        }
        else if (payment.RemainingRefundable <= 0)
        {
            throw new PaymentValidationException("Nothing remains to refund on this order.");
        }

        var result = await _payPal.RefundAsync(payment.CaptureId!, amount,
            IdempotencyKeys.Refund(payment.Id, idempotencyKey), ct);

        var refund = payment.RecordRefund(idempotencyKey, result.Amount, result.RefundId, result.Status);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Refunded {result.Amount} on order {orderId} (refund {result.RefundId}, {result.Status}).");
        return refund;
    }

    public async Task<IReadOnlyList<Payment>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), ct);
        return payments;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var result = await _payPal.VaultCardAsync(IdempotencyKeys.Customer(buyerId), card, ct);

        var savedCard = new SavedCard(buyerId, result.TokenId, result.Brand, result.LastFourDigits, result.Expiry);
        await _savedCardRepository.AddAsync(savedCard, ct);

        _logger.LogInformation($"Saved a {result.Brand} card ending {result.LastFourDigits} for {buyerId}.");
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken ct)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId, ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Saved card {savedCardId} was not found.");
        }

        await _payPal.DeleteVaultedCardAsync(savedCard.VaultTokenId, ct);
        await _savedCardRepository.DeleteAsync(savedCard, ct);

        _logger.LogInformation($"Deleted saved card {savedCardId} for {buyerId}.");
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        if (to < from)
        {
            throw new PaymentValidationException("Reconciliation 'to' must not be earlier than 'from'.");
        }

        var transactions = await _payPal.SearchTransactionsAsync(from, to, ct);
        var byTransactionId = transactions
            .GroupBy(t => t.TransactionId)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var payments = await _paymentRepository.ListAsync(ct);

        var entries = new List<ReconciliationEntry>();
        var matchedPayPalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // eShop-side references that moved money: captures and refunds.
        foreach (var payment in payments)
        {
            if (payment.CaptureId is not null && WithinRange(payment.CapturedAt, from, to))
            {
                AddEShopReference(entries, matchedPayPalIds, byTransactionId, payment.OrderId,
                    payment.CaptureId, "capture", payment.CapturedAmount, payment.CaptureStatus);
            }

            foreach (var refund in payment.Refunds)
            {
                if (refund.PayPalRefundId is not null && WithinRange(refund.CreatedAt, from, to))
                {
                    AddEShopReference(entries, matchedPayPalIds, byTransactionId, payment.OrderId,
                        refund.PayPalRefundId, "refund", refund.Amount, refund.Status);
                }
            }
        }

        // PayPal transactions eShop has no record of.
        foreach (var tx in transactions)
        {
            if (matchedPayPalIds.Contains(tx.TransactionId))
            {
                continue;
            }
            entries.Add(new ReconciliationEntry(ReconciliationMatch.PayPalOnly, tx.TransactionId, null, null,
                "unknown", tx.Amount, null, tx.Status));
        }

        var matched = entries.Count(e => e.Match == ReconciliationMatch.Matched);
        var payPalOnly = entries.Count(e => e.Match == ReconciliationMatch.PayPalOnly);
        var eShopOnly = entries.Count(e => e.Match == ReconciliationMatch.EShopOnly);

        return new ReconciliationReport(from, to, transactions.Count, matched, payPalOnly, eShopOnly, entries);
    }

    private static void AddEShopReference(List<ReconciliationEntry> entries, HashSet<string> matchedPayPalIds,
        IReadOnlyDictionary<string, PayPalTransaction> byTransactionId, int orderId, string reference,
        string kind, decimal? eShopAmount, string? status)
    {
        if (byTransactionId.TryGetValue(reference, out var tx))
        {
            matchedPayPalIds.Add(reference);
            entries.Add(new ReconciliationEntry(ReconciliationMatch.Matched, tx.TransactionId, reference, orderId,
                kind, tx.Amount, eShopAmount, tx.Status));
        }
        else
        {
            entries.Add(new ReconciliationEntry(ReconciliationMatch.EShopOnly, null, reference, orderId, kind,
                null, eShopAmount, status));
        }
    }

    private static bool WithinRange(DateTimeOffset? when, DateTimeOffset from, DateTimeOffset to)
        => when.HasValue && when.Value >= from && when.Value <= to;

    private async Task<string> RenewAuthorizationAsync(Payment payment, CancellationToken ct)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            return reauth.AuthorizationId;
        }
        catch (PayPalApiException ex) when (ex.IsClientError)
        {
            throw new InvalidPaymentOperationException(
                $"The authorization for order {payment.OrderId} has expired and can no longer be renewed. " +
                "Ask the shopper to pay for this order again.");
        }
    }

    private static bool IsAuthorizationStale(DateTimeOffset? expiresAt)
        // Treat a hold as stale a little before its stated expiry to avoid racing the capture against it.
        => expiresAt.HasValue && expiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(1);

    private async Task<CardPaymentInstrument> ResolveInstrumentAsync(string buyerId, PaymentInstruction instruction,
        CancellationToken ct)
    {
        if (instruction.SavedCardId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(instruction.SavedCardId.Value, ct);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new ResourceNotFoundException($"Saved card {instruction.SavedCardId.Value} was not found.");
            }
            return new CardPaymentInstrument(null, savedCard.VaultTokenId);
        }

        if (instruction.Card is not null)
        {
            return new CardPaymentInstrument(instruction.Card, null);
        }

        throw new PaymentValidationException("Provide either card details or a saved card id to pay with.");
    }

    private async Task<(Order order, Payment payment)> LoadOwnedOrderAndPaymentAsync(int orderId, string buyerId,
        CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            throw new ResourceNotFoundException($"No payment exists for order {orderId}.");
        }
        return (order, payment);
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }
}
