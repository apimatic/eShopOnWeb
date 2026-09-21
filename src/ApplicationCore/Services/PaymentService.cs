using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // How close to expiry an authorization is treated as stale and renewed before capture.
    private static readonly TimeSpan StaleAuthorizationBuffer = TimeSpan.FromMinutes(5);

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    /// <summary>The reconciliation reference stamped onto the PayPal purchase unit for an order.</summary>
    public static string OrderReference(int orderId) => $"ESHOP-ORDER-{orderId}";

    private static bool TryParseOrderReference(string? reference, out int orderId)
    {
        orderId = 0;
        const string prefix = "ESHOP-ORDER-";
        if (string.IsNullOrEmpty(reference) || !reference.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        return int.TryParse(reference.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out orderId);
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));
        if (lines.Any(l => l.Quantity <= 0))
            throw new ArgumentException("Every order line must have a quantity of at least 1.", nameof(lines));

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new NotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var items = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            // Amounts come from catalog prices (server-side), never from the caller.
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, address, items);
        order = await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation("Placed order {0} for buyer with {1} line(s), total {2}.", order.Id, items.Count, order.Total());
        return order.Id;
    }

    public async Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId, CancellationToken ct)
    {
        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);

        // Idempotent in effect: a double-click never authorizes twice.
        if (order.PaymentStatus == PaymentStatus.Authorized)
        {
            _logger.LogInformation("Order {0} is already authorized; returning existing hold.", orderId);
            return MapOrder(order);
        }
        if (order.PaymentStatus != PaymentStatus.AwaitingPayment)
            throw new InvalidOperationException($"Order {orderId} is {order.PaymentStatus} and can no longer be paid.");

        string? vaultTokenId = null;
        string? savedCardDescription = null;
        if (savedCardId.HasValue)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(savedCardId.Value, buyerId), ct)
                ?? throw new NotFoundException($"Saved card {savedCardId.Value} not found.");
            vaultTokenId = saved.PayPalVaultTokenId;
            savedCardDescription = $"{saved.CardBrand} ****{saved.LastFourDigits}";
        }
        else if (card is null)
        {
            throw new ArgumentException("Supply either card details or a saved card id to pay.");
        }

        var amount = order.Total();
        var reference = OrderReference(orderId);
        var auth = await _gateway.AuthorizeAsync(reference, amount, _gateway.Currency, card, vaultTokenId, $"auth-{orderId}", ct);

        var description = auth.PaymentMethodDescription ?? savedCardDescription ?? DescribeCard(card);
        var payment = new Payment(_gateway.Currency, amount, auth.PayPalOrderId, auth.AuthorizationId,
            auth.Status, auth.ExpiresAt, description);
        order.Authorize(payment);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Authorized order {0}: paypalOrder={1} authorization={2} status={3}.",
            orderId, auth.PayPalOrderId, auth.AuthorizationId, auth.Status);
        return MapOrder(order);
    }

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.PaymentStatus == PaymentStatus.Paid)
            return MapOrder(order); // idempotent: already fulfilled
        if (order.PaymentStatus != PaymentStatus.Authorized)
            throw new InvalidOperationException($"Order {orderId} is {order.PaymentStatus}; only an authorized order can be fulfilled.");

        var payment = order.Payment!;

        // An authorization that has gone stale before fulfilment is renewed rather than failing the capture.
        var reauthorized = false;
        if (IsAuthorizationStale(payment))
        {
            await RenewAuthorizationAsync(order, orderId, ct);
            reauthorized = true;
        }

        GatewayCapture capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId, $"capture-{orderId}", ct);
        }
        catch (PaymentGatewayException) when (!reauthorized)
        {
            // The hold may have expired between our check and the capture; renew once and retry.
            _logger.LogWarning("Capture of order {0} failed; attempting to renew the authorization and retry.", orderId);
            await RenewAuthorizationAsync(order, orderId, ct);
            capture = await _gateway.CaptureAsync(order.Payment!.AuthorizationId, $"capture-{orderId}-retry", ct);
        }

        order.Fulfil(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Fulfilled order {0}: capture={1} amount={2} fee={3} net={4}.",
            orderId, capture.CaptureId, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        return MapOrder(order);
    }

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.PaymentStatus == PaymentStatus.Cancelled)
            return MapOrder(order); // idempotent
        if (order.PaymentStatus != PaymentStatus.Authorized)
            throw new InvalidOperationException($"Order {orderId} is {order.PaymentStatus}; only an authorized (unfulfilled) order can be cancelled.");

        await _gateway.VoidAsync(order.Payment!.AuthorizationId, $"void-{orderId}", ct);
        order.Cancel();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Cancelled order {0}: released hold on authorization {1}.", orderId, order.Payment!.AuthorizationId);
        return MapOrder(order);
    }

    public async Task<RefundResult> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);
        if (order.PaymentStatus is not (PaymentStatus.Paid or PaymentStatus.PartiallyRefunded))
            throw new InvalidOperationException($"Order {orderId} is {order.PaymentStatus}; only a fulfilled order can be refunded.");

        var payment = order.Payment!;

        // Idempotency: a repeated request under the same key must not refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation("Refund key {0} already applied to order {1}; returning existing refund {2}.",
                idempotencyKey, orderId, existing.PayPalRefundId);
            return new RefundResult(existing.PayPalRefundId, MapOrder(order));
        }

        var remaining = payment.RefundableRemaining;
        if (remaining <= 0m)
            throw new InvalidOperationException($"Order {orderId} has already been fully refunded.");

        if (amount.HasValue)
        {
            if (amount.Value <= 0m)
                throw new ArgumentException("A refund amount must be positive.");
            if (amount.Value > remaining)
                throw new InvalidOperationException($"Refund of {amount.Value:0.00} exceeds the refundable remaining {remaining:0.00}.");
        }

        var recordedAmount = amount ?? remaining;
        var refund = await _gateway.RefundAsync(payment.CaptureId!, amount, _gateway.Currency, idempotencyKey, ct);

        order.AddRefund(new PaymentRefund(idempotencyKey, refund.RefundId, recordedAmount, refund.Status));
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Refunded order {0}: refund={1} amount={2} status={3}.",
            orderId, refund.RefundId, recordedAmount, refund.Status);
        return new RefundResult(refund.RefundId, MapOrder(order));
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), ct);
        return orders.Select(MapOrder).ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            throw new ArgumentException("'to' must not be earlier than 'from'.");

        var payPalTxns = await _gateway.ListTransactionsAsync(from, to, ct);
        var orders = await _orderRepository.ListAsync(new AllOrdersWithPaymentSpecification(), ct);

        // eShop side: orders that have been captured (money taken) — those PayPal should also report.
        var capturedOrders = orders
            .Where(o => o.Payment?.CaptureId is not null)
            .ToDictionary(o => o.Id);

        var lines = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in payPalTxns)
        {
            if ((TryParseOrderReference(txn.InvoiceId, out var orderId) || TryParseOrderReference(txn.CustomField, out orderId))
                && capturedOrders.TryGetValue(orderId, out var order))
            {
                matchedOrderIds.Add(orderId);
                lines.Add(new ReconciliationLine("Matched", orderId, txn.TransactionId, txn.Amount, txn.Status,
                    order.Payment!.CapturedAmount, order.PaymentStatus.ToString(), null));
            }
            else
            {
                lines.Add(new ReconciliationLine("PayPalOnly", null, txn.TransactionId, txn.Amount, txn.Status,
                    null, null, "PayPal transaction with no matching eShop captured order."));
            }
        }

        foreach (var order in capturedOrders.Values)
        {
            if (matchedOrderIds.Contains(order.Id))
                continue;
            lines.Add(new ReconciliationLine("EShopOnly", order.Id, order.Payment!.CaptureId, null, null,
                order.Payment!.CapturedAmount, order.PaymentStatus.ToString(),
                "Captured in eShop but not present in PayPal's report for this range (PayPal reporting can lag by up to a few hours)."));
        }

        return new ReconciliationReport(
            from, to,
            MatchedCount: lines.Count(l => l.Category == "Matched"),
            PayPalOnlyCount: lines.Count(l => l.Category == "PayPalOnly"),
            EShopOnlyCount: lines.Count(l => l.Category == "EShopOnly"),
            Lines: lines);
    }

    public async Task<SavedCardView> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var saved = await _gateway.VaultCardAsync(card, Guid.NewGuid().ToString("N"), ct);
        var entity = new SavedPaymentMethod(buyerId, saved.VaultTokenId, saved.Brand, saved.LastFourDigits, saved.Expiry);
        entity = await _savedCardRepository.AddAsync(entity, ct);

        _logger.LogInformation("Saved card for buyer: id={0} brand={1} last4={2}.", entity.Id, saved.Brand, saved.LastFourDigits);
        return new SavedCardView(entity.Id, entity.CardBrand, entity.LastFourDigits, entity.Expiry, entity.CreatedAt);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken ct)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
        return cards.Select(c => new SavedCardView(c.Id, c.CardBrand, c.LastFourDigits, c.Expiry, c.CreatedAt)).ToList();
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var saved = await _savedCardRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(paymentMethodId, buyerId), ct)
            ?? throw new NotFoundException($"Saved card {paymentMethodId} not found.");

        await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultTokenId, ct);
        await _savedCardRepository.DeleteAsync(saved, ct);
        _logger.LogInformation("Deleted saved card {0} for buyer.", paymentMethodId);
    }

    // ---- helpers -------------------------------------------------------------------------------

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct) =>
        await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), ct)
            ?? throw new NotFoundException($"Order {orderId} not found.");

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new NotFoundException($"Order {orderId} not found."); // do not reveal another shopper's order
        return order;
    }

    private static bool IsAuthorizationStale(Payment payment) =>
        payment.AuthorizationExpiresAt.HasValue &&
        payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow.Add(StaleAuthorizationBuffer);

    private async Task RenewAuthorizationAsync(Order order, int orderId, CancellationToken ct)
    {
        GatewayReauthorization reauth;
        try
        {
            reauth = await _gateway.ReauthorizeAsync(order.Payment!.AuthorizationId, $"reauth-{orderId}", ct);
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentGatewayException(
                "The payment authorization has expired and can no longer be renewed. Ask the shopper to pay for this order again (create a new authorization).",
                isClientError: false, providerDebugId: ex.ProviderDebugId, inner: ex);
        }
        order.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Renewed authorization for order {0}: new authorization={1}.", orderId, reauth.AuthorizationId);
    }

    private static string? DescribeCard(CardDetails? card)
    {
        if (card is null || string.IsNullOrEmpty(card.Number) || card.Number.Length < 4)
            return null;
        return $"Card ****{card.Number.Substring(card.Number.Length - 4)}";
    }

    private OrderPaymentView MapOrder(Order order)
    {
        var payment = order.Payment;
        PaymentDetailView? detail = payment is null ? null : new PaymentDetailView(
            payment.PayPalOrderId,
            payment.AuthorizationId,
            payment.AuthorizationStatus,
            payment.AuthorizationExpiresAt,
            payment.CaptureId,
            payment.CaptureStatus,
            payment.CapturedAmount,
            payment.PayPalFee,
            payment.NetAmount,
            payment.RefundedAmount,
            payment.PaymentMethodDescription,
            payment.Refunds.Select(r => new RefundView(r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt)).ToList());

        var currency = payment?.CurrencyCode ?? _gateway.Currency;
        var items = order.OrderItems
            .Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();

        return new OrderPaymentView(order.Id, order.BuyerId, order.Total(), currency,
            order.PaymentStatus.ToString(), order.OrderDate, detail, items);
    }
}
