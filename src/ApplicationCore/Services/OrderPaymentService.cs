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
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Buyer> buyerRepository,
        IRepository<CatalogItem> itemRepository,
        IPayPalPaymentGateway gateway,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _buyerRepository = buyerRepository;
        _itemRepository = itemRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    // ---------------------------------------------------------------- Place order

    public async Task<Order> PlaceOrderAsync(string identity, IReadOnlyList<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(identity, nameof(identity));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidPaymentOperationException("An order must contain at least one line item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new InvalidPaymentOperationException("Every line item must have a quantity of at least 1.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidPaymentOperationException(
                $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        // Amounts come from catalog prices (never from the caller).
        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(identity, shipToAddress, items);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    // ---------------------------------------------------------------- Pay (authorize)

    public async Task<Order> PayWithCardAsync(string identity, int orderId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(card, nameof(card));
        return await AuthorizeAsync(identity, orderId,
            (amount, currency, idempotencyKey, ct) => _gateway.AuthorizeWithCardAsync(amount, currency, card, idempotencyKey, ct),
            vaultId: null, cancellationToken);
    }

    public async Task<Order> PayWithSavedCardAsync(string identity, int orderId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerWithPaymentMethodsSpecification(identity), cancellationToken);
        var method = buyer?.FindPaymentMethod(paymentMethodId);
        if (buyer is null || method is null)
        {
            throw new PaymentMethodNotFoundException(paymentMethodId);
        }

        var vaultId = method.VaultId;
        return await AuthorizeAsync(identity, orderId,
            (amount, currency, idempotencyKey, ct) => _gateway.AuthorizeWithVaultAsync(amount, currency, vaultId, idempotencyKey, ct),
            vaultId, cancellationToken);
    }

    private async Task<Order> AuthorizeAsync(string identity, int orderId,
        Func<decimal, string, string, CancellationToken, Task<AuthorizationResult>> authorize,
        string? vaultId, CancellationToken cancellationToken)
    {
        var order = await LoadOwnedOrderAsync(identity, orderId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (order.Payment is { AuthorizationId: not null } existing
            && !IsVoided(existing.AuthorizationStatus))
        {
            return order;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} cannot be paid from state '{order.Status}'.");
        }

        var amount = order.Total();
        if (amount <= 0)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} has a non-positive total.");
        }

        // Deterministic key per order (from its stable PublicId) so a retry/double-submit is
        // de-duplicated at PayPal too, without colliding across distinct orders.
        var idempotencyKey = $"auth-{order.PublicId:N}";
        var result = await authorize(amount, Currency, idempotencyKey, cancellationToken);

        var payment = new Payment(result.PayPalOrderId, Currency, amount);
        var descriptor = DescribeCard(result.CardBrand, result.CardLast4);
        payment.SetAuthorization(result.AuthorizationId, result.Status, result.ExpiresAt, vaultId, descriptor);
        order.MarkAuthorized(payment);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    // ---------------------------------------------------------------- Fulfil (capture)

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = order.Payment;

        // Idempotent: fulfilling an already-captured order returns it unchanged.
        if (order.Status == OrderStatus.Fulfilled && payment?.CaptureId is not null)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || payment?.AuthorizationId is null)
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} cannot be fulfilled from state '{order.Status}'.");
        }

        var amount = payment.AuthorizedAmount;
        var authorizationId = await EnsureCapturableAuthorizationAsync(payment, amount, cancellationToken);

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, amount, payment.CurrencyCode,
                $"capture-auth-{authorizationId}", cancellationToken);
        }
        catch (PayPalApiException ex) when (IsStaleAuthorization(ex))
        {
            // The hold went stale between our check and the capture — renew and retry once.
            authorizationId = await RenewAuthorizationOrThrowAsync(payment, amount, cancellationToken);
            capture = await _gateway.CaptureAsync(authorizationId, amount, payment.CurrencyCode,
                $"capture-auth-{authorizationId}", cancellationToken);
        }

        payment.SetCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    /// <summary>
    /// Make sure the authorization can be captured: if the hold has gone stale (its PayPal
    /// expiration has passed), renew it first. If it can no longer be renewed, report it in
    /// operator-actionable terms.
    /// </summary>
    private async Task<string> EnsureCapturableAuthorizationAsync(Payment payment, decimal amount,
        CancellationToken cancellationToken)
    {
        var authorizationId = payment.AuthorizationId!;
        var isStale = payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow;

        if (!isStale)
        {
            // Confirm against PayPal's own view in case our cached expiry is out of date.
            try
            {
                var current = await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
                payment.UpdateAuthorizationStatus(current.Status);
                isStale = current.ExpiresAt is { } e && e <= DateTimeOffset.UtcNow;
            }
            catch (PayPalApiException ex)
            {
                _logger.LogWarning($"Could not read authorization {authorizationId} before capture: {ex.Message}");
            }
        }

        if (isStale)
        {
            authorizationId = await RenewAuthorizationOrThrowAsync(payment, amount, cancellationToken);
        }

        return authorizationId;
    }

    private async Task<string> RenewAuthorizationOrThrowAsync(Payment payment, decimal amount,
        CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, amount,
                payment.CurrencyCode, cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            return renewed.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The payment hold for this order has expired and PayPal can no longer renew it " +
                $"(issue: {ex.IssueName ?? "unknown"}). The funds were never captured. " +
                $"Ask the shopper to place and pay for a new order to complete fulfilment.");
        }
    }

    // ---------------------------------------------------------------- Cancel (void)

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }

        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} is already fulfilled; a captured order must be refunded, not cancelled.");
        }

        var payment = order.Payment;
        if (payment?.AuthorizationId is not null && !IsVoided(payment.AuthorizationStatus))
        {
            try
            {
                await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            }
            catch (PayPalApiException ex) when (ex.HttpStatusCode == 422)
            {
                // Already voided / not voidable at PayPal — safe to treat the hold as released.
                _logger.LogWarning($"Void of authorization {payment.AuthorizationId} returned 422 (treating as released): {ex.Message}");
            }
            payment.UpdateAuthorizationStatus("VOIDED");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    // ---------------------------------------------------------------- Refund

    public async Task<Refund> RefundAsync(string identity, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrderAsync(identity, orderId, cancellationToken);
        var payment = order.Payment;

        if (order.Status != OrderStatus.Fulfilled || payment?.CaptureId is null)
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} has not been fulfilled, so there is nothing captured to refund.");
        }

        // Idempotent by caller-supplied key: repeating the same request returns the same refund.
        var priorRefund = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (priorRefund is not null)
        {
            return priorRefund;
        }

        var remaining = payment.RefundableRemaining;
        if (remaining <= 0)
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} has already been fully refunded.");
        }
        if (amount.HasValue)
        {
            if (amount.Value <= 0)
            {
                throw new InvalidPaymentOperationException("A refund amount must be greater than zero.");
            }
            if (amount.Value > remaining)
            {
                throw new InvalidPaymentOperationException(
                    $"Refund of {amount.Value:0.00} exceeds the remaining refundable amount of {remaining:0.00}.");
            }
        }

        // The caller's idempotency key dedupes per capture (checked above). PayPal's PayPal-Request-Id
        // is a *global* value, so derive a request id namespaced to the (globally-unique) capture id;
        // this keeps the same (capture, key) request idempotent at PayPal while the same key reused
        // against a different capture never collides globally.
        var payPalRequestId = DerivePayPalRequestId(payment.CaptureId, idempotencyKey);
        var result = await _gateway.RefundAsync(payment.CaptureId, amount, payment.CurrencyCode,
            payPalRequestId, cancellationToken);

        var refund = new Refund(idempotencyKey, result.RefundId, result.Amount, payment.CurrencyCode, result.Status);
        payment.AddRefund(refund);

        // Reflect the new capture state locally.
        payment.UpdateCaptureStatus(payment.RefundableRemaining <= 0 ? "REFUNDED" : "PARTIALLY_REFUNDED");

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    // ---------------------------------------------------------------- Queries

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string identity,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithPaymentSpecification(identity), cancellationToken);
        return orders;
    }

    // ---------------------------------------------------------------- Reconciliation

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidPaymentOperationException("'to' must not be earlier than 'from'.");
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var payPalById = transactions
            .GroupBy(t => t.TransactionId)
            .ToDictionary(g => g.Key, g => g.First());

        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        // Build eShop's record of money movements (captures + refunds) within the range.
        var eShopRecords = new List<(string TxnId, string Type, int OrderId, string? Status, decimal Amount, DateTimeOffset? At)>();
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            if (payment.CaptureId is not null && payment.CapturedAmount is { } captured)
            {
                eShopRecords.Add((payment.CaptureId, "capture", order.Id, payment.CaptureStatus, captured, payment.CapturedAt));
            }
            foreach (var refund in payment.Refunds)
            {
                eShopRecords.Add((refund.PayPalRefundId, "refund", order.Id, refund.Status, refund.Amount, refund.CreatedAt));
            }
        }

        var eShopById = eShopRecords
            .GroupBy(r => r.TxnId)
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<MatchedReconciliationItem>();
        var payPalOnly = new List<PayPalOnlyReconciliationItem>();

        foreach (var txn in payPalById.Values)
        {
            if (eShopById.TryGetValue(txn.TransactionId, out var rec))
            {
                matched.Add(new MatchedReconciliationItem(txn.TransactionId, rec.Type, rec.OrderId,
                    txn.Status, txn.Amount, rec.Status, rec.Amount));
            }
            else
            {
                payPalOnly.Add(new PayPalOnlyReconciliationItem(txn.TransactionId, txn.Status, txn.Amount,
                    txn.CurrencyCode, txn.InitiationDate, txn.EventCode));
            }
        }

        // eShop records within the range that PayPal's report does not show.
        var eShopOnly = eShopRecords
            .Where(r => !payPalById.ContainsKey(r.TxnId))
            .Where(r => r.At is null || (r.At >= from && r.At <= to))
            .Select(r => new EShopOnlyReconciliationItem(r.TxnId, r.Type, r.OrderId, r.Status, r.Amount))
            .ToList();

        return new ReconciliationReport(from, to, matched, payPalOnly, eShopOnly);
    }

    // ---------------------------------------------------------------- Helpers

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(string identity, int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, identity, StringComparison.Ordinal))
        {
            // Don't reveal that the order exists to a shopper who doesn't own it.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private static bool IsVoided(string? status)
        => string.Equals(status, "VOIDED", StringComparison.OrdinalIgnoreCase);

    private static bool IsStaleAuthorization(PayPalApiException ex)
        => ex.IssueName is { } issue
           && issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Derive a stable, globally-unique PayPal-Request-Id from a capture id and the caller's
    /// idempotency key (SHA-256, hex-truncated to fit PayPal's 38-char limit).
    /// </summary>
    private static string DerivePayPalRequestId(string captureId, string callerKey)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{captureId}|{callerKey}"));
        return "rf-" + Convert.ToHexString(bytes).ToLowerInvariant()[..32];
    }

    private static string? DescribeCard(string? brand, string? last4)
    {
        if (brand is null && last4 is null) return null;
        if (last4 is null) return brand;
        return brand is null ? $"card ending {last4}" : $"{brand} ending {last4}";
    }
}
