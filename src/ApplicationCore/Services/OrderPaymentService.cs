using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IPayPalGateway payPalGateway,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _payPalGateway = payPalGateway;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    // A human-readable but globally-unique reference (order id + its payment GUID) used as the
    // PayPal custom_id / invoice_id and to derive idempotency keys. Unique across in-memory
    // restarts, so PayPal never replays a previous run's order that reused the same database id.
    private static string OrderReference(Order order) => $"eShop-Order-{order.Id}-{order.PaymentReference:N}";

    // Capture/void keys are derived from the globally-unique PayPal authorization id.
    private static string CaptureKey(string authorizationId) => $"cap-{authorizationId}";
    private static string VoidKey(string authorizationId) => $"void-{authorizationId}";

    // ---------------------------------------------------------------------
    // Place order
    // ---------------------------------------------------------------------

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines,
        ShippingAddressInput? shipTo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
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
            // Amounts come from catalog prices.
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, BuildAddress(shipTo), items);
        order = await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    private static Address BuildAddress(ShippingAddressInput? shipTo) => new(
        string.IsNullOrWhiteSpace(shipTo?.Street) ? "N/A" : shipTo!.Street!,
        string.IsNullOrWhiteSpace(shipTo?.City) ? "N/A" : shipTo!.City!,
        shipTo?.State ?? "N/A",
        string.IsNullOrWhiteSpace(shipTo?.Country) ? "N/A" : shipTo!.Country!,
        string.IsNullOrWhiteSpace(shipTo?.ZipCode) ? "00000" : shipTo!.ZipCode!);

    // ---------------------------------------------------------------------
    // Authorize (hold)
    // ---------------------------------------------------------------------

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, AuthorizePaymentInput input,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(input, nameof(input));
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (order.Status == OrderStatus.Authorized && order.Payment is not null)
        {
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {orderId} cannot be paid from status {order.Status}.");
        }

        var hasCard = input.Card is not null;
        var hasSaved = input.SavedPaymentMethodId.HasValue;
        if (hasCard == hasSaved)
        {
            throw new PaymentException("Provide either card details or a saved card id — exactly one.");
        }

        var amount = order.Total();
        var currency = _settings.Currency;
        var reference = OrderReference(order);
        var idempotencyKey = $"auth-{order.PaymentReference:N}";

        PayPalAuthorizationResult authResult;
        int? paymentMethodId = null;

        if (hasSaved)
        {
            var vaultId = await ResolveVaultIdAsync(buyerId, input.SavedPaymentMethodId!.Value, cancellationToken);
            paymentMethodId = input.SavedPaymentMethodId;
            authResult = await _payPalGateway.AuthorizeWithVaultedCardAsync(
                amount, currency, reference, vaultId, idempotencyKey, cancellationToken);
        }
        else
        {
            authResult = await _payPalGateway.AuthorizeWithCardAsync(
                amount, currency, reference, input.Card!, idempotencyKey, cancellationToken);
        }

        var payment = new Payment(currency, amount);
        payment.RecordAuthorization(authResult.PayPalOrderId, authResult.AuthorizationId,
            authResult.AuthorizationStatus, authResult.ExpiresAt, paymentMethodId);
        order.SetAuthorized(payment);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<string> ResolveVaultIdAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        var pm = buyer?.FindPaymentMethod(paymentMethodId);
        if (pm is null || string.IsNullOrEmpty(pm.VaultId))
        {
            // Not the caller's card, or it doesn't exist / was removed.
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }
        return pm.VaultId!;
    }

    // ---------------------------------------------------------------------
    // Fulfil (capture) — operator
    // ---------------------------------------------------------------------

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order; // idempotent: already captured
        }
        if (order.Status != OrderStatus.Authorized || order.Payment is null)
        {
            throw new PaymentException($"Order {orderId} cannot be fulfilled from status {order.Status}.");
        }

        var payment = order.Payment;
        var amount = payment.Amount;
        var currency = payment.Currency;
        var reference = OrderReference(order);
        var authorizationId = payment.AuthorizationId!;
        var renewed = false;

        // Renew a hold that has gone stale before fulfilment rather than failing outright.
        var currentAuth = await TryGetAuthorizationAsync(authorizationId, cancellationToken);
        if (currentAuth is not null && IsStale(currentAuth))
        {
            authorizationId = await RenewHoldAsync(order, payment, authorizationId, amount, currency, cancellationToken);
            renewed = true;
        }

        PayPalCaptureResult capture;
        try
        {
            // The capture key is derived from the (globally-unique) authorization id, so a
            // double-click captures once, while a genuinely new authorization captures afresh.
            capture = await _payPalGateway.CaptureAsync(
                authorizationId, amount, currency, reference, CaptureKey(authorizationId), cancellationToken);
        }
        catch (PayPalException ex) when (!renewed && IsExpiryIssue(ex))
        {
            // The hold expired between our check and the capture: renew, then capture the new hold.
            authorizationId = await RenewHoldAsync(order, payment, authorizationId, amount, currency, cancellationToken);
            capture = await _payPalGateway.CaptureAsync(
                authorizationId, amount, currency, reference, CaptureKey(authorizationId), cancellationToken);
        }

        payment.RecordCapture(capture.CaptureId, capture.CaptureStatus,
            capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<string> RenewHoldAsync(Order order, Payment payment, string authorizationId,
        decimal amount, string currency, CancellationToken ct)
    {
        try
        {
            var re = await _payPalGateway.ReauthorizeAsync(
                authorizationId, amount, currency, $"reauth-{authorizationId}", ct);
            payment.RecordReauthorization(re.AuthorizationId, re.AuthorizationStatus, re.ExpiresAt);
            _logger.LogInformation($"Order {order.Id}: renewed stale authorization {authorizationId} -> {re.AuthorizationId}.");
            return re.AuthorizationId;
        }
        catch (PayPalException ex)
        {
            // One that can no longer be renewed must say so in terms an operator can act on.
            throw new PayPalException(
                $"Order {order.Id}: the payment hold has expired and could not be renewed " +
                $"({ex.IssueName ?? ex.Message}). Ask the shopper to authorize payment again " +
                "(POST /api/orders/{id}/pay) before fulfilling this order.", ex);
        }
    }

    private async Task<PayPalAuthorizationResult?> TryGetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            return await _payPalGateway.GetAuthorizationAsync(authorizationId, ct);
        }
        catch (PayPalException ex)
        {
            _logger.LogWarning($"Could not read authorization {authorizationId}: {ex.Message}");
            return null;
        }
    }

    private static bool IsStale(PayPalAuthorizationResult auth) =>
        string.Equals(auth.AuthorizationStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase) ||
        (auth.ExpiresAt.HasValue && auth.ExpiresAt.Value <= DateTimeOffset.UtcNow);

    private static bool IsExpiryIssue(PayPalException ex) =>
        ex.IssueName is not null &&
        ex.IssueName.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------------
    // Cancel (void) — operator
    // ---------------------------------------------------------------------

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }
        if (order.Status == OrderStatus.AwaitingPayment)
        {
            order.MarkCancelled(); // nothing held, nothing to release
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
        if (order.Status != OrderStatus.Authorized || order.Payment is null)
        {
            throw new PaymentException(
                $"Order {orderId} cannot be cancelled from status {order.Status}; a fulfilled order must be refunded instead.");
        }

        await _payPalGateway.VoidAsync(order.Payment.AuthorizationId!, VoidKey(order.Payment.AuthorizationId!), cancellationToken);
        order.Payment.RecordVoid();
        order.MarkCancelled();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    // ---------------------------------------------------------------------
    // Refund — shopper-scoped
    // ---------------------------------------------------------------------

    public async Task<Refund> RefundAsync(string buyerId, int orderId, RefundInput input,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(input, nameof(input));
        Guard.Against.NullOrEmpty(input.IdempotencyKey, nameof(input.IdempotencyKey));

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = order.Payment;
        if (payment is null || payment.CaptureId is null ||
            (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException($"Order {orderId} must be fulfilled (captured) before it can be refunded.");
        }

        // Idempotent in effect: repeating a request under the same key does not refund twice.
        var existing = payment.FindRefundByKey(input.IdempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var amount = input.Amount ?? payment.RefundableAmount;
        if (amount <= 0)
        {
            throw new PaymentException($"Order {orderId} has nothing left to refund.");
        }

        Refund refund;
        try
        {
            // Validates that a partly-refunded order never becomes refundable beyond what was captured.
            refund = payment.AddRefund(input.IdempotencyKey, amount);
        }
        catch (InvalidOperationException ex)
        {
            throw new PaymentException(ex.Message);
        }

        // No invoice_id on refunds: PayPal enforces invoice_id uniqueness, and two legitimate
        // distinct partial refunds of the same capture would otherwise collide. Idempotency is
        // carried by the caller-supplied key (the PayPal-Request-Id).
        var result = await _payPalGateway.RefundAsync(
            payment.CaptureId!, amount, payment.Currency, invoiceId: null, input.IdempotencyKey, cancellationToken);

        refund.MarkAccepted(result.RefundId, result.RefundStatus);
        payment.ApplyRefundSettlement();
        order.SyncRefundState();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    // ---------------------------------------------------------------------
    // My orders
    // ---------------------------------------------------------------------

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    // ---------------------------------------------------------------------
    // Reconciliation — operator
    // ---------------------------------------------------------------------

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("Reconciliation 'to' must not be earlier than 'from'.");
        }

        var transactions = await _payPalGateway.SearchTransactionsAsync(from, to, cancellationToken);

        var allOrders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);
        var capturedInRange = allOrders
            .Where(o => o.Payment?.CaptureId is not null &&
                        o.Payment.CapturedAt.HasValue &&
                        o.Payment.CapturedAt.Value >= from &&
                        o.Payment.CapturedAt.Value <= to)
            .ToList();

        var txnById = transactions
            .GroupBy(t => t.TransactionId)
            .ToDictionary(g => g.Key, g => g.First());

        var captureToOrder = capturedInRange.ToDictionary(o => o.Payment!.CaptureId!, o => o);
        var knownRefundIds = capturedInRange
            .SelectMany(o => o.Payment!.Refunds)
            .Where(r => !string.IsNullOrEmpty(r.PayPalRefundId))
            .Select(r => r.PayPalRefundId!)
            .ToHashSet();

        var matched = new List<ReconciliationMatch>();
        var inEShopNotInPayPal = new List<ReconciliationEShopOnly>();

        foreach (var order in capturedInRange)
        {
            var captureId = order.Payment!.CaptureId!;
            if (txnById.TryGetValue(captureId, out var txn))
            {
                matched.Add(new ReconciliationMatch(
                    txn.TransactionId, order.Id, txn.Amount, order.Payment.CapturedAmount ?? 0m,
                    txn.TransactionStatus, txn.InitiationDate));
            }
            else
            {
                inEShopNotInPayPal.Add(new ReconciliationEShopOnly(
                    order.Id, captureId, order.Payment.CapturedAmount ?? 0m, order.Payment.CapturedAt));
            }
        }

        var inPayPalNotInEShop = new List<ReconciliationPayPalOnly>();
        foreach (var txn in transactions)
        {
            if (captureToOrder.ContainsKey(txn.TransactionId)) continue;     // a capture we matched
            if (knownRefundIds.Contains(txn.TransactionId)) continue;        // a refund eShop issued
            inPayPalNotInEShop.Add(new ReconciliationPayPalOnly(
                txn.TransactionId, txn.Amount, txn.TransactionStatus, txn.InvoiceId, txn.CustomField, txn.InitiationDate));
        }

        var totals = new ReconciliationTotals(
            PayPalTransactionCount: transactions.Count,
            EShopCapturedOrderCount: capturedInRange.Count,
            MatchedCount: matched.Count,
            PayPalOnlyCount: inPayPalNotInEShop.Count,
            EShopOnlyCount: inEShopNotInPayPal.Count);

        return new ReconciliationReport(from, to, matched, inEShopNotInPayPal, inPayPalNotInEShop, totals);
    }

    // ---------------------------------------------------------------------
    // Loading helpers
    // ---------------------------------------------------------------------

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // One shopper must never see or act on another's order.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }
}
