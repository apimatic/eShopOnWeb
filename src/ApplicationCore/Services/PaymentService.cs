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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;

    // Default shipping address used when the caller does not supply one — the order model requires it.
    private static readonly ShippingAddressInput DefaultShipping =
        new("N/A", "N/A", "N/A", "US", "00000");

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
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

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput? shippingAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentValidationException("Every order line must have a quantity of at least one.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var addr = shippingAddress ?? DefaultShipping;
        var order = new Order(buyerId, new Address(addr.Street, addr.City, addr.State, addr.Country, addr.ZipCode), orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var invoiceId = $"ESHOP-{order.Id}-{Guid.NewGuid():N}";
        var payment = new Payment(order.Id, buyerId, _gateway.Currency, order.Total(), invoiceId);
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation($"Order {order.Id} placed for buyer {buyerId}, total {order.Total()} {_gateway.Currency}.");
        return order.Id;
    }

    public async Task<OrderPaymentView> PayAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken ct)
    {
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, ct);

        // Idempotent in effect: a hold already placed is not placed again.
        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
            return await BuildViewAsync(payment, ct);

        if (payment.Status != PaymentStatus.AwaitingPayment)
            throw new PaymentConflictException($"Order {orderId} cannot be paid in its current state ({payment.Status}).");

        CardInput? card = instruction.Card;
        string? vaultId = null;

        if (instruction.SavedCardId.HasValue && card is not null)
            throw new PaymentValidationException("Provide either card details or a saved card, not both.");

        if (instruction.SavedCardId.HasValue)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdSpecification(buyerId, instruction.SavedCardId.Value), ct);
            if (saved is null)
                throw new PaymentResourceNotFoundException($"Saved card {instruction.SavedCardId} was not found.");
            vaultId = saved.VaultId;
        }
        else if (card is null)
        {
            throw new PaymentValidationException("A payment source is required: supply card details or a saved card id.");
        }

        var authInstruction = new AuthorizeInstruction(
            Amount: payment.Amount,
            CurrencyCode: payment.CurrencyCode,
            InvoiceId: payment.InvoiceId,
            CustomId: $"order-{payment.OrderId}",
            IdempotencyKey: $"pay-{payment.InvoiceId}",
            Card: card,
            VaultId: vaultId);

        var result = await _gateway.AuthorizeAsync(authInstruction, ct);
        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Order {orderId} authorized (auth {result.AuthorizationId}).");
        return await BuildViewAsync(payment, ct);
    }

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Fulfilled && payment.CaptureId is not null)
            return await BuildViewAsync(payment, ct);

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentConflictException($"Order {orderId} cannot be fulfilled in its current state ({payment.Status}).");

        // Renew a stale authorization rather than failing the fulfilment outright.
        var snapshot = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, ct);
        var stale = IsAuthorizationStale(snapshot);
        _logger.LogWarning($"Fulfil order {orderId}: authorization {payment.AuthorizationId} status='{snapshot.Status}' expiresAt='{snapshot.ExpiresAt}' stale={stale}.");
        if (stale)
        {
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId, payment.Amount, $"reauth-{payment.InvoiceId}", ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        var capture = await _gateway.CaptureAsync(payment.AuthorizationId!, $"capture-{payment.InvoiceId}", ct);
        payment.MarkFulfilled(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Order {orderId} fulfilled (capture {capture.CaptureId}, net {capture.NetAmount}).");
        return await BuildViewAsync(payment, ct);
    }

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
            return await BuildViewAsync(payment, ct);

        if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            throw new PaymentConflictException($"Order {orderId} is already fulfilled; refund it instead of cancelling.");

        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            await _gateway.VoidAsync(payment.AuthorizationId, $"void-{payment.InvoiceId}", ct);
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Order {orderId} cancelled.");
        return await BuildViewAsync(payment, ct);
    }

    public async Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, ct);

        // Idempotent per key: repeating the same request returns the same refund, no second refund.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return ToOutcome(payment, existing);

        if (payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded) || payment.CaptureId is null)
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund (state {payment.Status}).");

        var requested = amount ?? payment.RemainingRefundable;
        if (requested <= 0m)
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        if (requested > payment.RemainingRefundable)
            throw new PaymentValidationException(
                $"Refund of {requested} exceeds the refundable remainder of {payment.RemainingRefundable} for order {orderId}.");

        // Local idempotency is keyed on the raw caller key (checked above). The PayPal-Request-Id is
        // derived from the globally-unique invoice id so it never collides across payments or app runs.
        var result = await _gateway.RefundAsync(payment.CaptureId, requested, $"refund-{payment.InvoiceId}-{idempotencyKey}", ct);
        var refund = new PaymentRefund(idempotencyKey, result.RefundId, result.Amount, result.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Order {orderId} refunded {result.Amount} (refund {result.RefundId}); total refunded {payment.TotalRefunded}.");
        return ToOutcome(payment, refund);
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), ct);
        var ordersById = orders.ToDictionary(o => o.Id);

        return payments
            .Select(p => BuildView(p, ordersById.TryGetValue(p.OrderId, out var o) ? o : null))
            .ToList();
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardInput card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(card, ct);
        var saved = new SavedCard(buyerId, vaulted.VaultId, vaulted.Brand ?? "UNKNOWN",
            vaulted.LastFourDigits, vaulted.Expiry ?? card.Expiry, card.CardholderName);
        saved = await _savedCardRepository.AddAsync(saved, ct);

        _logger.LogInformation($"Card vaulted for buyer {buyerId} (saved card {saved.Id}).");
        return ToView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken ct)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpecification(buyerId), ct);
        return cards.Select(ToView).ToList();
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var card = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpecification(buyerId, paymentMethodId), ct);
        if (card is null)
            throw new PaymentResourceNotFoundException($"Saved card {paymentMethodId} was not found.");

        await _gateway.DeleteVaultedCardAsync(card.VaultId, ct);
        await _savedCardRepository.DeleteAsync(card, ct);
        _logger.LogInformation($"Saved card {paymentMethodId} removed for buyer {buyerId}.");
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(new AllPaymentsSpecification(), ct);

        // Match on the invoice id we stamp onto each PayPal order at authorize time.
        var paymentsByInvoice = payments
            .Where(p => !string.IsNullOrEmpty(p.InvoiceId))
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First());

        var lines = new List<ReconciliationLine>();
        var matchedInvoices = new HashSet<string>();

        foreach (var tx in transactions)
        {
            Payment? match = null;
            if (!string.IsNullOrEmpty(tx.InvoiceId) && paymentsByInvoice.TryGetValue(tx.InvoiceId!, out var p))
            {
                match = p;
                matchedInvoices.Add(tx.InvoiceId!);
            }

            lines.Add(new ReconciliationLine(
                Match: match is null ? ReconciliationMatch.PayPalOnly : ReconciliationMatch.Matched,
                InvoiceId: tx.InvoiceId ?? match?.InvoiceId,
                PayPalTransactionId: tx.TransactionId,
                PayPalAmount: tx.Amount,
                PayPalStatus: tx.Status,
                EShopOrderId: match?.OrderId,
                EShopAmount: match?.CapturedGross ?? match?.Amount,
                EShopPaymentStatus: match?.Status.ToString()));
        }

        // Payments eShop created in the range that PayPal's report does not (yet) show.
        foreach (var p in payments.Where(p => p.CreatedAt >= from && p.CreatedAt <= to))
        {
            if (matchedInvoices.Contains(p.InvoiceId)) continue;
            lines.Add(new ReconciliationLine(
                Match: ReconciliationMatch.EShopOnly,
                InvoiceId: p.InvoiceId,
                PayPalTransactionId: null,
                PayPalAmount: null,
                PayPalStatus: null,
                EShopOrderId: p.OrderId,
                EShopAmount: p.CapturedGross ?? p.Amount,
                EShopPaymentStatus: p.Status.ToString()));
        }

        return new ReconciliationReport(
            From: from,
            To: to,
            PayPalTransactionCount: transactions.Count,
            MatchedCount: lines.Count(l => l.Match == ReconciliationMatch.Matched),
            PayPalOnlyCount: lines.Count(l => l.Match == ReconciliationMatch.PayPalOnly),
            EShopOnlyCount: lines.Count(l => l.Match == ReconciliationMatch.EShopOnly),
            Lines: lines);
    }

    // --- helpers ---

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        return payment;
    }

    private async Task<Payment> LoadOwnedPaymentAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);
        // Not-owned is reported as not-found so a shopper cannot probe for another's orders.
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        return payment;
    }

    // An authorization is stale only when it is definitively past its expiration, or PayPal reports it
    // in a terminal non-capturable state. A fresh hold with an unrecognised/absent status is NOT treated
    // as stale — reauthorizing a fresh hold is both unnecessary and rejected by PayPal (REAUTHORIZATION_TOO_SOON).
    private static bool IsAuthorizationStale(AuthorizationSnapshot snapshot)
    {
        if (snapshot.ExpiresAt.HasValue && snapshot.ExpiresAt.Value <= DateTimeOffset.UtcNow)
            return true;
        if (string.Equals(snapshot.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private async Task<OrderPaymentView> BuildViewAsync(Payment payment, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpecification(payment.OrderId), ct);
        return BuildView(payment, order);
    }

    private static OrderPaymentView BuildView(Payment payment, Order? order)
    {
        var items = order?.OrderItems.Select(i =>
            new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList()
            ?? new List<OrderLineView>();

        return new OrderPaymentView(
            OrderId: payment.OrderId,
            OrderDate: order?.OrderDate ?? payment.CreatedAt,
            PaymentStatus: payment.Status.ToString(),
            CurrencyCode: payment.CurrencyCode,
            Total: payment.Amount,
            InvoiceId: payment.InvoiceId,
            PayPalOrderId: payment.PayPalOrderId,
            AuthorizationId: payment.AuthorizationId,
            AuthorizationExpiresAt: payment.AuthorizationExpiresAt,
            CaptureId: payment.CaptureId,
            CapturedGross: payment.CapturedGross,
            PayPalFee: payment.PayPalFee,
            NetAmount: payment.NetAmount,
            TotalRefunded: payment.TotalRefunded,
            RemainingRefundable: payment.RemainingRefundable,
            Items: items);
    }

    private static RefundOutcome ToOutcome(Payment payment, PaymentRefund refund) =>
        new(refund.Id, refund.PayPalRefundId, refund.Amount, refund.Status, payment.TotalRefunded, payment.Status.ToString());

    private static SavedCardView ToView(SavedCard card) =>
        new(card.Id, card.Brand, card.LastFourDigits, card.Expiry, card.CardholderName, card.CreatedAt);
}
