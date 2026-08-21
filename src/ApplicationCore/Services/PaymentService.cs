using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentService;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // Renew an authorization proactively if it expires within this window.
    private static readonly TimeSpan StaleAuthorizationBuffer = TimeSpan.FromMinutes(5);

    private readonly IOrderService _orderService;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentConfiguration _configuration;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IOrderService orderService,
        IRepository<OrderPayment> paymentRepository,
        IReadRepository<Order> orderRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway gateway,
        IPaymentConfiguration configuration,
        IAppLogger<PaymentService> logger)
    {
        _orderService = orderService;
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _configuration = configuration;
        _logger = logger;
    }

    // ---------------------------------------------------------------- place & pay

    public async Task<int> PlaceOrderAsync(string buyerId, Address shipToAddress,
        IReadOnlyCollection<PlaceOrderItem> items, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.", 400);
        }

        var order = await _orderService.CreateOrderAsync(buyerId, shipToAddress,
            items.Select(i => (i.CatalogItemId, i.Units)).ToList());

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _configuration.CurrencyCode);
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation("Placed order {0} for {1}, awaiting payment of {2} {3}",
            order.Id, buyerId, payment.Amount, payment.CurrencyCode);
        return order.Id;
    }

    public async Task<PaymentResult> AuthorizeOrderAsync(string buyerId, int orderId,
        CardDetails? card, int? savedPaymentMethodId, CancellationToken ct = default)
    {
        var payment = await GetOwnedPaymentAsync(buyerId, orderId, ct);

        // Idempotent in effect: a second pay for an already-authorized (or captured) order is a no-op.
        if (payment.AuthorizationId != null)
        {
            return ToPaymentResult(payment);
        }
        if (payment.Status != PaymentStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {orderId} cannot be paid in its current state ({payment.Status}).", 409);
        }

        var (resolvedCard, vaultId) = await ResolveInstrumentAsync(buyerId, card, savedPaymentMethodId, ct);

        var request = new CreateAuthorizationRequest(
            Amount: payment.Amount,
            CurrencyCode: payment.CurrencyCode,
            OrderReference: orderId.ToString(),
            InvoiceId: InvoiceIdFor(payment),
            CreateRequestId: payment.CreateRequestId,
            AuthorizeRequestId: payment.AuthorizeRequestId,
            Card: resolvedCard,
            VaultId: vaultId);

        var auth = await _gateway.CreateAndAuthorizeAsync(request, ct);

        if (auth.RequiresBuyerAction)
        {
            // A 3-D Secure / browser challenge is required. We deliberately do not build an approval
            // round-trip; surface a clear, caller-actionable error instead.
            throw new PaymentGatewayException(
                "This card requires additional buyer authentication (3-D Secure) that cannot be completed without a browser step. Use a card that does not trigger a challenge.",
                422, issue: "PAYER_ACTION_REQUIRED");
        }

        payment.RecordPayPalOrder(auth.PayPalOrderId);
        payment.MarkAuthorized(auth.AuthorizationId, auth.Status, auth.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Authorized order {0}: authorization {1} ({2})", orderId, auth.AuthorizationId, auth.Status);
        return ToPaymentResult(payment);
    }

    // ---------------------------------------------------------------- operator: fulfil / cancel

    public async Task<PaymentResult> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await GetPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Captured)
        {
            return ToPaymentResult(payment); // already fulfilled — idempotent
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId == null)
        {
            throw new PaymentException($"Order {orderId} is not awaiting fulfilment (state {payment.Status}).", 409);
        }

        var authorizationId = await EnsureFreshAuthorizationAsync(payment, ct);

        GatewayCapture capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, payment.CaptureRequestId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.Issue == GatewayIssues.AuthorizationExpired)
        {
            // The hold went stale between our check and the capture — renew once and retry.
            _logger.LogWarning("Authorization {0} expired at capture for order {1}; renewing.", authorizationId, orderId);
            authorizationId = await RenewAuthorizationAsync(payment, ct);
            capture = await _gateway.CaptureAsync(authorizationId, payment.CaptureRequestId, ct);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Fulfilled order {0}: captured {1} {2} (fee {3}, net {4})",
            orderId, capture.GrossAmount, capture.CurrencyCode, capture.PayPalFee, capture.NetAmount);
        return ToPaymentResult(payment);
    }

    public async Task<PaymentResult> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await GetPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Voided)
        {
            return ToPaymentResult(payment); // already cancelled — idempotent
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId == null)
        {
            throw new PaymentException(
                $"Order {orderId} cannot be cancelled: only an authorized order that has not been fulfilled can be cancelled (state {payment.Status}).", 409);
        }

        await _gateway.VoidAsync(payment.AuthorizationId, ct);
        payment.MarkVoided();
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Cancelled order {0}: authorization {1} voided, held funds released.", orderId, payment.AuthorizationId);
        return ToPaymentResult(payment);
    }

    // ---------------------------------------------------------------- refund

    public async Task<RefundResult> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await GetOwnedPaymentAsync(buyerId, orderId, ct);

        if (payment.CaptureId == null ||
            (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException($"Order {orderId} has no captured payment to refund (state {payment.Status}).", 409);
        }

        // Idempotent: repeating a request under the same key returns the original refund, never a second one.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return new RefundResult(existing.Id, existing.PayPalRefundId, existing.Status,
                existing.Amount, payment.TotalRefunded(), payment.Status.ToString());
        }

        var remaining = payment.RefundableRemaining();
        var refundAmount = amount ?? remaining;

        if (refundAmount <= 0m)
        {
            throw new PaymentException("Refund amount must be greater than zero.", 422);
        }
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund of {refundAmount} {payment.CurrencyCode} exceeds the refundable remaining {remaining} {payment.CurrencyCode}.", 422);
        }

        var refund = await _gateway.RefundAsync(payment.CaptureId, refundAmount, payment.CurrencyCode, idempotencyKey, ct);
        var recorded = payment.AddRefund(refund.RefundId, refundAmount, refund.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Refunded {0} {1} on order {2} (refund {3}); total refunded {4}",
            refundAmount, payment.CurrencyCode, orderId, refund.RefundId, payment.TotalRefunded());
        return new RefundResult(recorded.Id, refund.RefundId, refund.Status, refundAmount,
            payment.TotalRefunded(), payment.Status.ToString());
    }

    // ---------------------------------------------------------------- reads

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), ct);
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var ordersById = orders.ToDictionary(o => o.Id);

        var views = new List<OrderPaymentView>();
        foreach (var payment in payments)
        {
            ordersById.TryGetValue(payment.OrderId, out var order);
            var items = order?.OrderItems
                .Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
                .ToList() ?? new List<OrderLineView>();

            views.Add(new OrderPaymentView(
                payment.OrderId,
                order?.OrderDate ?? default,
                order?.Total() ?? payment.Amount,
                payment.CurrencyCode,
                payment.Status.ToString(),
                payment.PayPalOrderId,
                payment.AuthorizationId,
                payment.AuthorizationStatus,
                payment.AuthorizationExpiresAt,
                payment.CaptureId,
                payment.CaptureStatus,
                payment.CapturedAmount,
                payment.PayPalFee,
                payment.NetAmount,
                payment.TotalRefunded(),
                items));
        }

        return views.OrderByDescending(v => v.OrderDate).ToList();
    }

    // ---------------------------------------------------------------- reconciliation (operator)

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
        {
            throw new PaymentException("The reconciliation 'to' date must not be before 'from'.", 400);
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);

        // eShop side: payments that reached PayPal, whose order falls in the range.
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsWithRefundsSpec(), ct);
        var orders = (await _orderRepository.ListAsync(ct))
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .ToDictionary(o => o.Id);

        var eShopByOrderId = payments
            .Where(p => orders.ContainsKey(p.OrderId))
            .ToDictionary(p => p.OrderId);

        var rows = new List<ReconciliationRow>();
        var matchedOrderIds = new HashSet<int>();

        // Walk PayPal's record and line each transaction up against an eShop order.
        foreach (var txn in transactions)
        {
            var orderId = ParseOrderId(txn.CustomField) ?? ParseOrderId(txn.InvoiceId);
            if (orderId != null && eShopByOrderId.TryGetValue(orderId.Value, out var matchedPayment))
            {
                matchedOrderIds.Add(orderId.Value);
                rows.Add(new ReconciliationRow("Matched", orderId, txn.TransactionId, txn.InvoiceId, txn.CustomField,
                    txn.Amount, txn.Status, matchedPayment.CapturedAmount ?? matchedPayment.Amount, matchedPayment.Status.ToString()));
            }
            else
            {
                rows.Add(new ReconciliationRow("InPayPalNotEShop", orderId, txn.TransactionId, txn.InvoiceId, txn.CustomField,
                    txn.Amount, txn.Status, null, null));
            }
        }

        // eShop payments PayPal's report doesn't (yet) show — expected during sandbox reporting lag.
        foreach (var payment in eShopByOrderId.Values.Where(p => !matchedOrderIds.Contains(p.OrderId)))
        {
            rows.Add(new ReconciliationRow("InEShopNotPayPal", payment.OrderId, null, InvoiceIdFor(payment), payment.OrderId.ToString(),
                null, null, payment.CapturedAmount ?? payment.Amount, payment.Status.ToString()));
        }

        var matched = rows.Count(r => r.MatchState == "Matched");
        var inPayPalOnly = rows.Count(r => r.MatchState == "InPayPalNotEShop");
        var inEShopOnly = rows.Count(r => r.MatchState == "InEShopNotPayPal");

        return new ReconciliationReport(from, to, transactions.Count, eShopByOrderId.Count,
            matched, inPayPalOnly, inEShopOnly, rows);
    }

    // ---------------------------------------------------------------- saved cards

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var vaulted = await _gateway.VaultCardAsync(card, CustomerIdFor(buyerId), ct);
        var saved = new SavedPaymentMethod(buyerId, vaulted.VaultId, vaulted.Brand, vaulted.LastFourDigits, vaulted.Expiry);
        await _savedCardRepository.AddAsync(saved, ct);

        _logger.LogInformation("Saved card for {0}: {1} ending {2}", buyerId, vaulted.Brand, vaulted.LastFourDigits);
        return ToSavedCardView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        return cards.Select(ToSavedCardView).ToList();
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var card = await _savedCardRepository.GetByIdAsync(paymentMethodId, ct);
        // A saved card belongs to the shopper who saved it — one shopper never deletes another's.
        if (card == null || card.BuyerId != buyerId)
        {
            throw new PaymentException($"Saved card {paymentMethodId} was not found.", 404);
        }

        await _gateway.DeleteVaultedCardAsync(card.PayPalVaultId, ct);
        await _savedCardRepository.DeleteAsync(card, ct);
        _logger.LogInformation("Deleted saved card {0} for {1}", paymentMethodId, buyerId);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<(CardDetails? card, string? vaultId)> ResolveInstrumentAsync(
        string buyerId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct)
    {
        var hasCard = card != null;
        var hasSaved = savedPaymentMethodId != null;
        if (hasCard == hasSaved)
        {
            throw new PaymentException("Provide exactly one of a card or a saved payment method to pay with.", 400);
        }

        if (hasSaved)
        {
            var saved = await _savedCardRepository.GetByIdAsync(savedPaymentMethodId!.Value, ct);
            if (saved == null || saved.BuyerId != buyerId)
            {
                throw new PaymentException($"Saved card {savedPaymentMethodId} was not found.", 404);
            }
            return (null, saved.PayPalVaultId);
        }

        return (card, null);
    }

    /// <summary>Returns the current authorization id, renewing the hold first if it has gone stale.</summary>
    private async Task<string> EnsureFreshAuthorizationAsync(OrderPayment payment, CancellationToken ct)
    {
        if (payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow.Add(StaleAuthorizationBuffer))
        {
            _logger.LogWarning("Authorization {0} for order {1} is stale (expires {2}); renewing before capture.",
                payment.AuthorizationId!, payment.OrderId, expiry);
            return await RenewAuthorizationAsync(payment, ct);
        }
        return payment.AuthorizationId!;
    }

    private async Task<string> RenewAuthorizationAsync(OrderPayment payment, CancellationToken ct)
    {
        var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, ct);
        payment.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, ct);
        return renewed.AuthorizationId;
    }

    private async Task<OrderPayment> GetPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct);
        if (payment == null)
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }
        return payment;
    }

    private async Task<OrderPayment> GetOwnedPaymentAsync(string buyerId, int orderId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct);
        // One shopper must never see or act on another's order — hide existence with a 404.
        if (payment == null || payment.BuyerId != buyerId)
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }
        return payment;
    }

    // Unique per payment (so it never collides with an invoice already used on the merchant account),
    // yet stable across idempotent retries of the same payment. custom_id carries the plain order id,
    // which stays authoritative for reconciliation matching.
    private static string InvoiceIdFor(OrderPayment payment) => $"eshop-order-{payment.OrderId}-{payment.CreateRequestId}";

    // A stable, opaque per-shopper customer id for PayPal vault scoping (no PII on the wire).
    private static string CustomerIdFor(string buyerId) =>
        "eshop-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(buyerId))).Substring(0, 16).ToLowerInvariant();

    private static int? ParseOrderId(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }
        if (int.TryParse(reference, out var direct))
        {
            return direct;
        }
        const string prefix = "eshop-order-";
        if (reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            // "eshop-order-{id}" or "eshop-order-{id}-{uniqueToken}"
            var rest = reference.Substring(prefix.Length);
            var dash = rest.IndexOf('-');
            var idPart = dash >= 0 ? rest.Substring(0, dash) : rest;
            if (int.TryParse(idPart, out var fromInvoice))
            {
                return fromInvoice;
            }
        }
        return null;
    }

    private static PaymentResult ToPaymentResult(OrderPayment p) => new(
        p.OrderId, p.Status.ToString(), p.CurrencyCode, p.PayPalOrderId, p.AuthorizationId, p.AuthorizationStatus,
        p.AuthorizationExpiresAt, p.CaptureId, p.CaptureStatus, p.CapturedAmount, p.PayPalFee, p.NetAmount);

    private static SavedCardView ToSavedCardView(SavedPaymentMethod m) =>
        new(m.Id, m.CardBrand, m.LastFourDigits, m.Expiry, m.SavedAt);
}
