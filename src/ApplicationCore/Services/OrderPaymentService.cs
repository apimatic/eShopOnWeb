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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the money movement for an order: place, authorize (hold), fulfil (capture),
/// cancel (release) and refund, plus the caller's order list and the operator reconciliation report.
/// All PayPal interaction goes through <see cref="IPayPalClient"/>; this service owns the domain rules
/// (state transitions, ownership, idempotency, refund limits).
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalClient _payPalClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    // A short id, stable for the lifetime of the process, that makes each run's PayPal invoice ids
    // unique. The in-memory database resets order ids to 1 on every restart, so without this a capture
    // in a later run would collide with an earlier run's invoice id at PayPal.
    private static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 8);

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IUriComposer uriComposer,
        IPayPalClient payPalClient,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _payPalClient = payPalClient;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => string.IsNullOrWhiteSpace(_settings.Currency) ? "USD" : _settings.Currency.Trim().ToUpperInvariant();

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new PaymentConflictException("An order must contain at least one item.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentConflictException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentConflictException($"Catalog item {line.CatalogItemId} does not exist.");

            // Amounts always come from catalog prices, never from the caller.
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} placed by {buyerId}, awaiting payment.");
        return order.Id;
    }

    public async Task<OrderView> AuthorizeAsync(string buyerId, int orderId, PayOrderInput input, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment is not null && payment.IsAuthorized && order.Status == OrderStatus.Authorized)
        {
            return ToOrderView(order, payment);
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Cancelled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException($"Order {orderId} can no longer be paid (status: {order.Status}).");
        }

        // Resolve exactly one payment source.
        var hasCard = input.Card is not null;
        var hasSaved = input.SavedPaymentMethodId is not null;
        if (hasCard == hasSaved)
        {
            throw new PaymentConflictException("Provide either card details or a saved payment method id, but not both.");
        }

        string? vaultId = null;
        if (hasSaved)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpecification(input.SavedPaymentMethodId!.Value), cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new SavedCardNotFoundException(input.SavedPaymentMethodId!.Value);
            }
            vaultId = savedCard.VaultId;
        }

        var amount = order.Total();
        if (amount <= 0m)
        {
            throw new PaymentConflictException($"Order {orderId} has a non-positive total and cannot be paid.");
        }

        var invoiceId = $"eshop-{RunId}-{orderId}";

        // Create (or reuse) the PayPal order for this eShop order.
        if (payment is null)
        {
            var ppOrder = await _payPalClient.CreateAuthorizeOrderAsync(
                amount, Currency, invoiceId, orderId.ToString(CultureInfo.InvariantCulture), $"create-{RunId}-{orderId}", cancellationToken);
            payment = new Payment(orderId, buyerId, amount, Currency, ppOrder.Id, invoiceId);
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }

        // Authorize (hold) the funds. The amount PayPal holds equals the order total to the cent
        // because the amount on the PayPal order is the order total.
        PayPalAuthorizationResult auth;
        try
        {
            auth = hasSaved
                ? await _payPalClient.AuthorizeOrderWithVaultAsync(payment.PayPalOrderId, vaultId!, $"authorize-{RunId}-{orderId}", cancellationToken)
                : await _payPalClient.AuthorizeOrderWithCardAsync(payment.PayPalOrderId, input.Card!, $"authorize-{RunId}-{orderId}", cancellationToken);
        }
        catch (PayerActionRequiredException)
        {
            throw; // surfaced to the operator; we do not build a browser approval round-trip
        }

        payment.SetAuthorization(auth.Id, auth.Status, auth.ExpiresAt, auth.CardLast4, auth.CardBrand);
        order.SetAuthorized();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} authorized (authorization {auth.Id}, status {auth.Status}).");
        return ToOrderView(order, payment);
    }

    public async Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent: a re-fulfil of an already-fulfilled order does not capture twice.
        if (order.Status == OrderStatus.Fulfilled && payment is not null && payment.IsCaptured)
        {
            return ToOrderView(order, payment);
        }

        if (order.Status != OrderStatus.Authorized || payment is null || !payment.IsAuthorized)
        {
            throw new PaymentConflictException($"Order {orderId} cannot be fulfilled (status: {order.Status}). It must be authorized first.");
        }

        var authorizationId = payment.AuthorizationId!;

        // If the hold has gone stale, renew it rather than failing the fulfilment outright.
        var expiry = payment.AuthorizationExpiresAt;
        if (expiry.HasValue && expiry.Value <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            authorizationId = await RenewAuthorizationAsync(payment, cancellationToken);
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPalClient.CaptureAuthorizationAsync(
                authorizationId, payment.Amount, payment.Currency, payment.InvoiceId, $"capture-{RunId}-{orderId}", cancellationToken);
        }
        catch (PayPalApiException ex) when (IsStaleAuthorization(ex))
        {
            // The hold expired between our check and the capture; renew and try once more.
            authorizationId = await RenewAuthorizationAsync(payment, cancellationToken);
            capture = await _payPalClient.CaptureAuthorizationAsync(
                authorizationId, payment.Amount, payment.Currency, payment.InvoiceId, $"capture-{RunId}-{orderId}", cancellationToken);
        }

        payment.SetCapture(capture.Id, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.SetFulfilled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} fulfilled (capture {capture.Id}, gross {capture.GrossAmount}, fee {capture.PayPalFee}, net {capture.NetAmount}).");
        return ToOrderView(order, payment);
    }

    private async Task<string> RenewAuthorizationAsync(Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _payPalClient.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.Currency, cancellationToken);
            payment.RenewAuthorization(reauth.Id, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Renewed stale authorization for order {payment.OrderId}: new authorization {reauth.Id}.");
            return reauth.Id;
        }
        catch (PayPalApiException ex)
        {
            throw new ReauthorizationFailedException(
                $"The authorization for order {payment.OrderId} has expired and can no longer be renewed " +
                $"(PayPal: {ex.Name ?? "error"} - {ex.Message}). Ask the shopper to pay for the order again.");
        }
    }

    private static bool IsStaleAuthorization(PayPalApiException ex)
    {
        var text = (ex.Name + " " + ex.Message + " " + string.Join(" ", ex.Issues)).ToUpperInvariant();
        return text.Contains("AUTHORIZATION_EXPIRED")
            || text.Contains("EXPIRED")
            || text.Contains("INVALID_AUTHORIZATION_ID")
            || text.Contains("AUTHORIZATION_VOIDED");
    }

    public async Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return ToOrderView(order, payment);
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException($"Order {orderId} has been fulfilled and cannot be cancelled; issue a refund instead.");
        }

        // Release any hold so no money moves.
        if (payment is not null && payment.IsAuthorized && !string.Equals(payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            await _payPalClient.VoidAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.SetCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {orderId} cancelled; any held funds released.");
        return ToOrderView(order, payment);
    }

    public async Task<(RefundView refund, OrderView order)> RefundAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        if (payment is null || !payment.IsCaptured)
        {
            throw new PaymentConflictException($"Order {orderId} has not been fulfilled, so there is nothing to refund.");
        }

        // Idempotent per key: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return (ToRefundView(existing), ToOrderView(order, payment));
        }

        var refundAmount = amount ?? payment.RefundableRemaining();
        if (refundAmount <= 0m)
        {
            throw new PaymentConflictException($"Order {orderId} has no remaining amount to refund.");
        }
        if (refundAmount > payment.RefundableRemaining())
        {
            throw new PaymentConflictException(
                $"Refund of {Format(refundAmount)} {payment.Currency} exceeds the refundable remaining amount of " +
                $"{Format(payment.RefundableRemaining())} {payment.Currency}.");
        }

        // Scope the caller's key to this capture for PayPal's PayPal-Request-Id, so the same key reused
        // against a different capture is not falsely rejected as a duplicate. The raw key is still what
        // we store and de-duplicate on for this payment.
        var payPalRequestId = $"refund-{payment.CaptureId}-{idempotencyKey}";
        var result = await _payPalClient.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.Currency, payPalRequestId, cancellationToken);

        var refund = payment.AddRefund(result.Id, result.Amount, result.Status, idempotencyKey);
        order.SetRefunded(payment.IsFullyRefunded());
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Order {orderId} refunded {Format(result.Amount)} {payment.Currency} (refund {result.Id}).");
        return (ToRefundView(refund), ToOrderView(order, payment));
    }

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderView>();
        }

        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(orderIds), cancellationToken);
        var paymentByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => ToOrderView(o, paymentByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentConflictException("Reconciliation 'to' must be on or after 'from'.");
        }

        var transactions = await _payPalClient.SearchTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(cancellationToken);

        // Index every PayPal id eShop knows about (captures and refunds) so we can spot both directions
        // of mismatch: a payment PayPal has that eShop lacks, and one eShop has that PayPal lacks.
        var eShopByPayPalId = new Dictionary<string, Payment>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in payments)
        {
            if (!string.IsNullOrEmpty(p.CaptureId)) eShopByPayPalId[p.CaptureId!] = p;
            if (!string.IsNullOrEmpty(p.AuthorizationId)) eShopByPayPalId[p.AuthorizationId!] = p;
            foreach (var r in p.Refunds)
            {
                if (!string.IsNullOrEmpty(r.RefundId)) eShopByPayPalId[r.RefundId] = p;
            }
        }
        var eShopByInvoice = payments
            .GroupBy(p => p.InvoiceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<ReconciliationEntry>();
        var matchedPaymentIds = new HashSet<int>();
        int matched = 0, inPayPalNotInEShop = 0;

        foreach (var t in transactions)
        {
            Payment? match = null;
            if (eShopByPayPalId.TryGetValue(t.TransactionId, out var byId)) match = byId;
            else if (!string.IsNullOrEmpty(t.CustomField) && int.TryParse(t.CustomField, out var oid))
                match = payments.FirstOrDefault(p => p.OrderId == oid);
            else if (!string.IsNullOrEmpty(t.InvoiceId) && eShopByInvoice.TryGetValue(t.InvoiceId!, out var byInv)) match = byInv;

            if (match is not null)
            {
                matched++;
                matchedPaymentIds.Add(match.Id);
                entries.Add(new ReconciliationEntry("Matched", t.TransactionId, t.Status, t.EventCode, t.Amount, t.Currency, t.InitiationDate,
                    match.OrderId, match.CaptureId, null));
            }
            else
            {
                inPayPalNotInEShop++;
                entries.Add(new ReconciliationEntry("InPayPalNotInEShop", t.TransactionId, t.Status, t.EventCode, t.Amount, t.Currency, t.InitiationDate,
                    null, null, null));
            }
        }

        // eShop captures/authorizations that PayPal's report does not (yet) show for this range.
        int inEShopNotInPayPal = 0;
        foreach (var p in payments.Where(p => p.IsCaptured || p.IsAuthorized))
        {
            if (matchedPaymentIds.Contains(p.Id)) continue;
            inEShopNotInPayPal++;
            entries.Add(new ReconciliationEntry("InEShopNotInPayPal", null, null, null, p.CapturedAmount ?? p.Amount, p.Currency, null,
                p.OrderId, p.CaptureId, null));
        }

        return new ReconciliationReport(from, to, transactions.Count, matched, inPayPalNotInEShop, inEShopNotInPayPal, entries);
    }

    // --- helpers ---

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        return order ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        // Not-owned is reported as not-found so a shopper cannot probe for another's orders.
        if (order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private OrderView ToOrderView(Order order, Payment? payment)
    {
        var items = order.OrderItems
            .Select(i => new OrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();
        return new OrderView(order.Id, order.OrderDate, order.Status, order.Total(), items, payment is null ? null : ToPaymentView(payment));
    }

    private static PaymentView ToPaymentView(Payment p) => new(
        p.Currency,
        p.Amount,
        p.PayPalOrderId,
        p.AuthorizationId,
        p.AuthorizationStatus,
        p.AuthorizationExpiresAt,
        p.CaptureId,
        p.CaptureStatus,
        p.CapturedAmount,
        p.PayPalFee,
        p.NetAmount,
        p.IsCaptured ? p.RefundableRemaining() : null,
        p.CardBrand,
        p.CardLast4,
        p.Refunds.Select(ToRefundView).ToList());

    private static RefundView ToRefundView(PaymentRefund r) => new(r.Id, r.RefundId, r.Amount, r.Status);
}
