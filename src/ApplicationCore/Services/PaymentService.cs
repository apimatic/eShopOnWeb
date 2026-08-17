using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using static Microsoft.eShopWeb.ApplicationCore.Services.PaymentResults;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Implements the pay-for-an-order flow against PayPal via <see cref="IPayPalClient"/>. Money movement is
/// staged exactly as the task requires: authorize a hold at pay time, capture at fulfilment (renewing a
/// stale hold rather than failing), void on cancel, refund after. Operations are idempotent in effect —
/// a per-order lock plus deterministic PayPal-Request-Ids stop a double-click charging twice.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<OrderPayment> _orderPaymentRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IPayPalClient _payPal;
    private readonly PayPalSettings _settings;
    private readonly IUriComposer _uriComposer;
    private readonly KeyedLock _keyedLock;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogItemRepository,
        IRepository<OrderPayment> orderPaymentRepository,
        IReadRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IPayPalClient payPal,
        PayPalSettings settings,
        IUriComposer uriComposer,
        KeyedLock keyedLock,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _orderPaymentRepository = orderPaymentRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _payPal = payPal;
        _settings = settings;
        _uriComposer = uriComposer;
        _keyedLock = keyedLock;
        _logger = logger;
    }

    private static string OrderLockKey(int orderId) => $"order:{orderId}";

    public async Task<Result<PlacedOrder>> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderItem> items,
        ShippingAddressInput? shipping, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Result<PlacedOrder>.Unauthorized();
        }

        if (items is null || items.Count == 0)
        {
            return Invalid<PlacedOrder>("At least one order item is required.");
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            return Invalid<PlacedOrder>("Item quantities must be greater than zero.");
        }

        if (items.Any(i => i.CatalogItemId <= 0))
        {
            return Invalid<PlacedOrder>("Catalog item ids must be positive.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return Invalid<PlacedOrder>($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri ?? string.Empty);
            if (string.IsNullOrEmpty(pictureUri))
            {
                pictureUri = "eCatalog-item-default.png";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipping is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shipping.Street, shipping.City, shipping.State, shipping.Country, shipping.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var payment = new OrderPayment(order.Id, buyerId, _settings.CurrencyCode, PaymentMapping.Round2(order.Total()));
        await _orderPaymentRepository.AddAsync(payment, ct);

        _logger.LogInformation("Placed order {0} for {1}: total {2} {3}", order.Id, buyerId, payment.Amount, payment.CurrencyCode);
        return Result<PlacedOrder>.Success(new PlacedOrder(order.Id, payment.Amount, payment.CurrencyCode, payment.PaymentReference, payment.Status.ToString()));
    }

    public async Task<Result<PaymentView>> AuthorizeAsync(int orderId, string buyerId, PayInput input, CancellationToken ct = default)
    {
        using var _ = await _keyedLock.LockAsync(OrderLockKey(orderId), ct);

        var payment = await _orderPaymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            return Result<PaymentView>.NotFound($"No order found with id {orderId}.");
        }

        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return Result<PaymentView>.Forbidden();
        }

        if (payment.Status == PaymentStatus.Authorized)
        {
            // Idempotent: a second pay request for an already-authorized order returns the existing hold.
            return Result<PaymentView>.Success(PaymentMapping.ToView(payment));
        }

        if (payment.Status != PaymentStatus.AwaitingPayment)
        {
            return Invalid<PaymentView>($"Order {orderId} cannot be paid because it is {payment.Status}.");
        }

        var hasCard = input?.Card is not null;
        var hasSaved = input?.SavedPaymentMethodId is > 0;
        if (hasCard == hasSaved)
        {
            return Invalid<PaymentView>("Provide either card details or a savedPaymentMethodId (exactly one).");
        }

        string? vaultId = null;
        string? sourceDescription = null;
        CardDetails? card = null;

        if (hasSaved)
        {
            var saved = await _savedPaymentMethodRepository.GetByIdAsync(input!.SavedPaymentMethodId!.Value, ct);
            if (saved is null || !string.Equals(saved.BuyerId, buyerId, StringComparison.Ordinal))
            {
                return Result<PaymentView>.NotFound($"No saved card found with id {input.SavedPaymentMethodId}.");
            }

            vaultId = saved.PayPalVaultId;
            sourceDescription = $"{saved.Brand} ****{saved.LastFourDigits}";
        }
        else
        {
            var normalized = PaymentMapping.NormalizeExpiry(input!.Card!.Expiry);
            if (normalized is null)
            {
                return Invalid<PaymentView>("Card expiry must be a valid date (YYYY-MM or MM/YY).");
            }

            card = input.Card with { Expiry = normalized, Number = input.Card.Number.Replace(" ", string.Empty) };
        }

        var money = new Money(payment.CurrencyCode, payment.Amount);
        var customId = OrderPayment.BuildCustomId(orderId);
        var requestId = $"auth-{payment.PaymentReference}";

        try
        {
            var result = hasSaved
                ? await _payPal.AuthorizeOrderWithVaultedCardAsync(money, payment.PaymentReference, payment.PaymentReference, customId, vaultId!, requestId, ct)
                : await _payPal.AuthorizeOrderWithCardAsync(money, payment.PaymentReference, payment.PaymentReference, customId, card!, requestId, ct);

            if (!IsUsableAuthorizationStatus(result.Status))
            {
                return Invalid<PaymentView>($"The payment could not be authorized (status {result.Status}).");
            }

            sourceDescription ??= BuildCardDescription(result, card);
            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt, sourceDescription);
            await _orderPaymentRepository.UpdateAsync(payment, ct);

            _logger.LogInformation("Authorized order {0}: paypalOrder={1} auth={2}", orderId, result.PayPalOrderId, result.AuthorizationId);
            return Result<PaymentView>.Success(PaymentMapping.ToView(payment));
        }
        catch (PayPalApiException ex) when (ex.IsInstrumentDeclined)
        {
            _logger.LogWarning("Authorization declined for order {0}: issue={1} debug_id={2}", orderId, ex.IssueCode, ex.DebugId);
            return Invalid<PaymentView>("The card was declined. Please use a different card.");
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Authorization failed for order {0}: {1} issue={2} debug_id={3}", orderId, ex.Message, ex.IssueCode, ex.DebugId);
            return Result<PaymentView>.Error($"PayPal could not authorize the payment: {ex.Message}");
        }
    }

    public async Task<Result<PaymentView>> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        using var _ = await _keyedLock.LockAsync(OrderLockKey(orderId), ct);

        var payment = await _orderPaymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            return Result<PaymentView>.NotFound($"No order found with id {orderId}.");
        }

        if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            // Idempotent: already captured.
            return Result<PaymentView>.Success(PaymentMapping.ToView(payment));
        }

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            return Invalid<PaymentView>($"Order {orderId} cannot be fulfilled because it is {payment.Status}.");
        }

        var money = new Money(payment.CurrencyCode, payment.Amount);
        var customId = OrderPayment.BuildCustomId(orderId);
        var captureRequestId = $"capture-{payment.PaymentReference}";

        try
        {
            var status = await _payPal.GetAuthorizationStatusAsync(payment.AuthorizationId, ct);
            if (IsExpiredStatus(status))
            {
                var renew = await TryRenewAuthorizationAsync(payment, money, ct);
                if (!renew.ok)
                {
                    return renew.result!;
                }
            }
            else if (IsUnusableStatus(status))
            {
                return Invalid<PaymentView>($"The payment authorization is no longer valid ({status}); ask the shopper to pay for the order again.");
            }

            CaptureResult capture;
            try
            {
                capture = await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId!, money, payment.PaymentReference, customId, captureRequestId, ct);
            }
            catch (PayPalApiException ex) when (ex.IsAuthorizationExpired)
            {
                var renew = await TryRenewAuthorizationAsync(payment, money, ct);
                if (!renew.ok)
                {
                    return renew.result!;
                }

                capture = await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId!, money, payment.PaymentReference, customId, captureRequestId, ct);
            }

            payment.MarkFulfilled(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            await _orderPaymentRepository.UpdateAsync(payment, ct);

            _logger.LogInformation("Fulfilled order {0}: capture={1} gross={2} fee={3} net={4}", orderId, capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            return Result<PaymentView>.Success(PaymentMapping.ToView(payment));
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Fulfilment failed for order {0}: {1} issue={2} debug_id={3}", orderId, ex.Message, ex.IssueCode, ex.DebugId);
            return Result<PaymentView>.Error($"PayPal could not capture the payment: {ex.Message}");
        }
    }

    public async Task<Result<PaymentView>> CancelAsync(int orderId, CancellationToken ct = default)
    {
        using var _ = await _keyedLock.LockAsync(OrderLockKey(orderId), ct);

        var payment = await _orderPaymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            return Result<PaymentView>.NotFound($"No order found with id {orderId}.");
        }

        if (payment.Status == PaymentStatus.Canceled)
        {
            return Result<PaymentView>.Success(PaymentMapping.ToView(payment));
        }

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            var hint = payment.Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded
                ? " Use a refund to return captured funds."
                : string.Empty;
            return Invalid<PaymentView>($"Order {orderId} cannot be cancelled because it is {payment.Status}.{hint}");
        }

        try
        {
            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, ct);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Cancel failed for order {0}: {1} debug_id={2}", orderId, ex.Message, ex.DebugId);
            return Result<PaymentView>.Error($"PayPal could not release the authorization: {ex.Message}");
        }

        payment.MarkCanceled();
        await _orderPaymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Cancelled order {0}: released authorization {1}", orderId, payment.AuthorizationId);
        return Result<PaymentView>.Success(PaymentMapping.ToView(payment));
    }

    public async Task<Result<RefundView>> RefundAsync(int orderId, string buyerId, RefundInput input, CancellationToken ct = default)
    {
        using var _ = await _keyedLock.LockAsync(OrderLockKey(orderId), ct);

        var payment = await _orderPaymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            return Result<RefundView>.NotFound($"No order found with id {orderId}.");
        }

        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return Result<RefundView>.Forbidden();
        }

        if (input is null || string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            return Invalid<RefundView>("An idempotencyKey is required for refunds.");
        }

        // Idempotent: a repeat under the same key returns the original refund rather than refunding again.
        var existing = payment.FindRefundByIdempotencyKey(input.IdempotencyKey);
        if (existing is not null)
        {
            return Result<RefundView>.Success(new RefundView(existing.PayPalRefundId, existing.Amount, existing.Status, existing.CreatedAt, existing.Reason));
        }

        if (payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded) || payment.CaptureId is null)
        {
            return Invalid<RefundView>($"Order {orderId} cannot be refunded because it is {payment.Status}. Only fulfilled orders can be refunded.");
        }

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0)
        {
            return Invalid<RefundView>("The captured amount has already been fully refunded.");
        }

        var amount = input.Amount.HasValue ? PaymentMapping.Round2(input.Amount.Value) : remaining;
        if (amount <= 0)
        {
            return Invalid<RefundView>("Refund amount must be greater than zero.");
        }

        if (amount > remaining)
        {
            return Invalid<RefundView>($"Refund amount {amount} exceeds the refundable remaining {remaining}.");
        }

        var requestId = $"refund-{payment.PaymentReference}-{input.IdempotencyKey}";
        try
        {
            var result = await _payPal.RefundCaptureAsync(payment.CaptureId, new Money(payment.CurrencyCode, amount), payment.PaymentReference, input.Reason, requestId, ct);
            var refund = new PaymentRefund(result.RefundId, amount, result.Status, input.IdempotencyKey, input.Reason);
            payment.AddRefund(refund);
            await _orderPaymentRepository.UpdateAsync(payment, ct);

            _logger.LogInformation("Refunded {0} on order {1}: refund={2} status={3} remaining={4}", amount, orderId, result.RefundId, result.Status, payment.RefundableRemaining());
            return Result<RefundView>.Success(new RefundView(result.RefundId, amount, result.Status, refund.CreatedAt, input.Reason));
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Refund failed for order {0}: {1} debug_id={2}", orderId, ex.Message, ex.DebugId);
            return Result<RefundView>.Error($"PayPal could not process the refund: {ex.Message}");
        }
    }

    public async Task<Result<IReadOnlyList<OrderSummaryView>>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Result<IReadOnlyList<OrderSummaryView>>.Unauthorized();
        }

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _orderPaymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), ct);
        var paymentByOrder = payments
            .GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.First());

        var summaries = orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderSummaryView(
                o.Id,
                o.OrderDate,
                o.Total(),
                o.OrderItems
                    .Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
                    .ToList(),
                paymentByOrder.TryGetValue(o.Id, out var p) ? PaymentMapping.ToView(p) : null))
            .ToList();

        return Result<IReadOnlyList<OrderSummaryView>>.Success(summaries);
    }

    public async Task<Result<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        if (to < from)
        {
            return Invalid<ReconciliationReport>("'to' must be on or after 'from'.");
        }

        // Cover the whole range, not just its first 31 days or first page: PayPal caps a report window at
        // 31 days, so walk the range in <=31-day windows; the client pages through each window fully.
        var transactions = new List<PayPalTransaction>();
        try
        {
            var windowStart = from;
            while (windowStart < to)
            {
                var windowEnd = windowStart.AddDays(31);
                if (windowEnd > to)
                {
                    windowEnd = to;
                }

                var batch = await _payPal.ListTransactionsAsync(windowStart, windowEnd, ct);
                transactions.AddRange(batch);

                if (windowEnd >= to)
                {
                    break;
                }

                windowStart = windowEnd;
            }
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Reconciliation transaction fetch failed: {0} debug_id={1}", ex.Message, ex.DebugId);
            return Result<ReconciliationReport>.Error($"PayPal transaction reporting failed: {ex.Message}");
        }

        transactions = transactions
            .GroupBy(t => t.TransactionId)
            .Select(g => g.First())
            .ToList();

        var payments = await _orderPaymentRepository.ListAsync(new AllOrderPaymentsSpecification(), ct);
        var relevant = payments.Where(p => p.PayPalOrderId != null).ToList();
        var byReference = relevant
            .GroupBy(p => p.PaymentReference)
            .ToDictionary(g => g.Key, g => g.First());
        var byCustomId = relevant
            .GroupBy(p => OrderPayment.BuildCustomId(p.OrderId))
            .ToDictionary(g => g.Key, g => g.First());

        var matched = new List<MatchedReconciliationEntry>();
        var paypalOnly = new List<PayPalOnlyEntry>();
        var matchedReferences = new HashSet<string>();

        foreach (var tx in transactions)
        {
            OrderPayment? p = null;
            if (!string.IsNullOrWhiteSpace(tx.InvoiceId) && byReference.TryGetValue(tx.InvoiceId!, out var byInvoice))
            {
                p = byInvoice;
            }
            else if (!string.IsNullOrWhiteSpace(tx.CustomField) && byCustomId.TryGetValue(tx.CustomField!, out var byCustom))
            {
                p = byCustom;
            }

            if (p is not null)
            {
                matchedReferences.Add(p.PaymentReference);
                matched.Add(new MatchedReconciliationEntry(
                    p.OrderId, p.PaymentReference, tx.TransactionId, tx.Status, tx.Amount, p.Amount,
                    AmountsMatch(tx.Amount, p), tx.EventCode, tx.Date));
            }
            else
            {
                paypalOnly.Add(new PayPalOnlyEntry(tx.TransactionId, tx.InvoiceId, tx.CustomField, tx.Amount, tx.Status, tx.Date, tx.EventCode));
            }
        }

        var eShopOnly = relevant
            .Where(p => IsWithinRange(p, from, to))
            .Where(p => !matchedReferences.Contains(p.PaymentReference))
            .Select(p => new EShopOnlyEntry(p.OrderId, p.PaymentReference, p.Status.ToString(), p.Amount, p.CaptureId, p.AuthorizationId))
            .ToList();

        var report = new ReconciliationReport(from, to, transactions.Count, matched.Count, paypalOnly.Count, eShopOnly.Count, matched, paypalOnly, eShopOnly);
        return Result<ReconciliationReport>.Success(report);
    }

    private async Task<(bool ok, Result<PaymentView>? result)> TryRenewAuthorizationAsync(OrderPayment payment, Money money, CancellationToken ct)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, money, $"reauth-{payment.PaymentReference}", ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _orderPaymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Renewed authorization for order {0}: {1}", payment.OrderId, reauth.AuthorizationId);
            return (true, null);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Could not renew authorization for order {0}: {1} debug_id={2}", payment.OrderId, ex.Message, ex.DebugId);
            return (false, Invalid<PaymentView>("The payment authorization has expired and could not be renewed. Ask the shopper to pay for the order again."));
        }
    }

    private static bool IsUsableAuthorizationStatus(string status) =>
        status.Equals("CREATED", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("PENDING", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpiredStatus(string status) => status.Equals("EXPIRED", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnusableStatus(string status) =>
        status.Equals("VOIDED", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("DENIED", StringComparison.OrdinalIgnoreCase);

    private static string BuildCardDescription(AuthorizeResult result, CardDetails? card)
    {
        var brand = !string.IsNullOrWhiteSpace(result.CardBrand) ? result.CardBrand! : "CARD";
        var last4 = !string.IsNullOrWhiteSpace(result.CardLastDigits)
            ? result.CardLastDigits!
            : (card is not null && card.Number.Length >= 4 ? card.Number[^4..] : "****");
        return $"{brand} ****{last4}";
    }

    private static bool AmountsMatch(decimal transactionAmount, OrderPayment payment)
    {
        var abs = PaymentMapping.Round2(Math.Abs(transactionAmount));
        return abs == payment.Amount || (payment.CapturedAmount.HasValue && abs == PaymentMapping.Round2(payment.CapturedAmount.Value));
    }

    private static bool IsWithinRange(OrderPayment payment, DateTimeOffset from, DateTimeOffset to)
    {
        if (payment.AuthorizedAt is DateTimeOffset a && a >= from && a <= to)
        {
            return true;
        }

        return payment.FulfilledAt is DateTimeOffset f && f >= from && f <= to;
    }
}
