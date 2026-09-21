using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Orchestrates the payment flows over the existing order model and the PayPal gateway. Records the local
/// payment before calling PayPal, gates every transition on current state, and derives deterministic
/// idempotency keys so a repeated request never moves money twice.
/// </summary>
public sealed class PaymentService : IPaymentService
{
    private static readonly Regex CustomerRefInvalidChars = new("[^0-9a-zA-Z\\-_.^*$@#]", RegexOptions.Compiled);

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<Order> _orderReadRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IReadRepository<OrderPayment> _paymentReadRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardReadRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _gateway;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<Order> orderReadRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderPayment> paymentRepository,
        IReadRepository<OrderPayment> paymentReadRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IReadRepository<SavedPaymentMethod> savedCardReadRepository,
        IUriComposer uriComposer,
        IPayPalGateway gateway,
        ILogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _orderReadRepository = orderReadRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _paymentReadRepository = paymentReadRepository;
        _savedCardRepository = savedCardRepository;
        _savedCardReadRepository = savedCardReadRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _logger = logger;
    }

    // ------------------------------ Place order ------------------------------

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, ShippingAddressInput? shipping, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentOperationException(PaymentError.InvalidRequest, "An order must contain at least one line item.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentOperationException(PaymentError.InvalidRequest, $"Quantity for catalog item {line.CatalogItemId} must be positive.");
            }
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentOperationException(PaymentError.InvalidRequest, $"Catalog item {line.CatalogItemId} was not found.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            // Amounts come from catalog prices, never from the caller.
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = BuildShippingAddress(shipping);
        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        return new PlaceOrderResult(order.Id, order.Total(), _gateway.Currency, PaymentStatus.PendingPayment.ToString());
    }

    // ------------------------------ Pay (authorize / hold) ------------------------------

    public async Task<PaymentActionResult> PayAsync(string buyerId, int orderId, PayInput input, CancellationToken ct)
    {
        var order = await _orderReadRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new PaymentOperationException(PaymentError.NotFound, $"Order {orderId} was not found.");
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(PaymentError.Forbidden, "This order belongs to another shopper.");
        }

        var total = order.Total();

        // Resolve funding: a saved card (owner-scoped) or raw card details. Exactly one.
        string? vaultId = null;
        PayPalCardInput? card = null;
        if (input.SavedPaymentMethodId.HasValue)
        {
            var saved = await _savedCardReadRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdForBuyerSpec(input.SavedPaymentMethodId.Value, buyerId), ct)
                ?? throw new PaymentOperationException(PaymentError.NotFound, "The saved card was not found.");
            vaultId = saved.PayPalVaultId;
        }
        else if (input.Card is not null)
        {
            card = MapCard(input.Card);
        }
        else
        {
            throw new PaymentOperationException(PaymentError.InvalidRequest, "Provide either card details or a saved payment method id.");
        }

        // Local claim first (before PayPal). The unique index on OrderId rejects a concurrent second pay.
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct);
        if (payment is null)
        {
            payment = new OrderPayment(orderId, buyerId, total, _gateway.Currency);
            if (vaultId is not null)
            {
                payment.SetFundingVaultId(vaultId);
            }
            try
            {
                payment = await _paymentRepository.AddAsync(payment, ct);
            }
            catch (DbUpdateException)
            {
                payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct);
                if (payment is null)
                {
                    throw;
                }
            }
        }

        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
                return ToActionResult(payment, "Order is already authorized.");
            case PaymentStatus.Fulfilled:
            case PaymentStatus.Cancelled:
            case PaymentStatus.Refunded:
            case PaymentStatus.PartiallyRefunded:
                throw new PaymentOperationException(PaymentError.Conflict, $"Order payment is already {payment.Status}.");
        }

        var request = new PayPalAuthorizeRequest(
            Amount: total,
            InvoiceId: null,
            CustomId: $"eShopOrder-{orderId}",
            IdempotencyKey: $"pay-{payment.IdempotencySeed}",
            Card: card,
            VaultId: vaultId,
            Description: $"eShopOnWeb order {orderId}");

        PayPalAuthorizationOutcome outcome;
        try
        {
            outcome = await _gateway.AuthorizeAsync(request, ct);
        }
        catch (PayPalGatewayException ex) when (ex.Kind == PayPalFailureKind.Unknown)
        {
            // Outcome unknown; leave the payment PendingPayment so a retry (same deterministic key) is safe.
            _logger.LogWarning(ex, "Authorize outcome unknown for order {OrderId}; left pending for retry.", orderId);
            throw;
        }
        catch (PayPalGatewayException ex)
        {
            payment.MarkFailed(DescribeFailure(ex));
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }

        if (outcome.RequiresBuyerApproval)
        {
            payment.MarkFailed("PayPal requires browser approval (3DS challenge).");
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentOperationException(PaymentError.ChallengeRequired,
                "PayPal requires the shopper to approve this payment in a browser (3DS challenge); this integration does not perform an approval round-trip.");
        }

        if (string.IsNullOrEmpty(outcome.AuthorizationId))
        {
            payment.MarkFailed($"No authorization created (order status {outcome.OrderStatus}, auth status {outcome.AuthorizationStatus}).");
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentOperationException(PaymentError.Conflict,
                $"PayPal did not create an authorization (order status {outcome.OrderStatus}).");
        }

        payment.MarkAuthorized(outcome.PayPalOrderId, outcome.AuthorizationId!, outcome.CreatedAtUtc);
        if (vaultId is not null)
        {
            payment.SetFundingVaultId(vaultId);
        }
        await _paymentRepository.UpdateAsync(payment, ct);
        return ToActionResult(payment, "Order authorized (funds held).");
    }

    // ------------------------------ Fulfil (capture) ------------------------------

    public async Task<PaymentActionResult> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct)
            ?? throw new PaymentOperationException(PaymentError.NotFound, $"No payment exists for order {orderId}.");

        if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            // Already captured: no second capture.
            return ToActionResult(payment, "Order is already fulfilled.");
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentOperationException(PaymentError.Conflict, $"Cannot fulfil an order in state {payment.Status}.");
        }

        var authId = payment.AuthorizationId!;
        var captureKey = $"cap-{payment.IdempotencySeed}";

        PayPalCaptureOutcome capture;
        try
        {
            capture = await _gateway.CaptureAsync(authId, payment.AuthorizedAmount, captureKey, ct);
        }
        catch (PayPalGatewayException ex) when (ex.Kind == PayPalFailureKind.AuthorizationExpired)
        {
            // Stale hold: renew it rather than failing fulfilment outright.
            _logger.LogInformation("Authorization {AuthId} expired for order {OrderId}; reauthorizing.", authId, orderId);
            try
            {
                var reauth = await _gateway.ReauthorizeAsync(authId, payment.AuthorizedAmount, $"reauth-{payment.IdempotencySeed}", ct);
                payment.RenewAuthorization(reauth.AuthorizationId, reauth.CreatedAtUtc);
                await _paymentRepository.UpdateAsync(payment, ct);
                authId = reauth.AuthorizationId;
            }
            catch (PayPalGatewayException reauthEx)
            {
                var reason = $"Authorization expired and can no longer be renewed ({reauthEx.IssueCode ?? reauthEx.Kind.ToString()}). " +
                             $"Re-authorize the order via POST /api/orders/{orderId}/pay to obtain a fresh hold, then fulfil again.";
                payment.MarkFailed(reason);
                await _paymentRepository.UpdateAsync(payment, ct);
                throw new PaymentOperationException(PaymentError.Conflict, reason);
            }

            capture = await _gateway.CaptureAsync(authId, payment.AuthorizedAmount, captureKey, ct);
        }
        catch (PayPalGatewayException ex) when (ex.Kind == PayPalFailureKind.Unknown)
        {
            // Outcome unknown; retry once. The idempotency key returns the existing capture if it landed.
            _logger.LogWarning(ex, "Capture outcome unknown for order {OrderId}; retrying with the same key.", orderId);
            capture = await _gateway.CaptureAsync(authId, payment.AuthorizedAmount, captureKey, ct);
        }

        // A 2xx is not necessarily success: only COMPLETED (or PENDING settlement) means money is taken.
        if (capture.Status is not ("COMPLETED" or "PENDING"))
        {
            payment.MarkFailed($"Capture returned status {capture.Status}.");
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentOperationException(PaymentError.Conflict, $"PayPal capture returned status {capture.Status}.");
        }

        payment.MarkFulfilled(capture.CaptureId, capture.Gross, capture.Fee, capture.Net, capture.CreatedAtUtc);
        await _paymentRepository.UpdateAsync(payment, ct);

        var message = capture.Status == "PENDING" ? "Payment captured (pending settlement)." : "Order fulfilled (payment captured).";
        return ToActionResult(payment, message);
    }

    // ------------------------------ Cancel (void) ------------------------------

    public async Task<PaymentActionResult> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct)
            ?? throw new PaymentOperationException(PaymentError.NotFound, $"No payment exists for order {orderId}.");

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return ToActionResult(payment, "Order is already cancelled.");
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentOperationException(PaymentError.Conflict, $"Cannot cancel an order in state {payment.Status}.");
        }

        var authId = payment.AuthorizationId!;
        try
        {
            await _gateway.VoidAsync(authId, $"void-{payment.IdempotencySeed}", ct);
        }
        catch (PayPalGatewayException ex) when (ex.Kind == PayPalFailureKind.Conflict)
        {
            // Already voided at PayPal; treat as cancelled locally.
            _logger.LogInformation("Void conflict for order {OrderId}; treating as already cancelled.", orderId);
        }
        catch (PayPalGatewayException ex) when (ex.Kind == PayPalFailureKind.Unknown)
        {
            var status = await _gateway.GetAuthorizationAsync(authId, ct);
            if (!string.Equals(status.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
            {
                throw;
            }
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);
        return ToActionResult(payment, "Order cancelled (held funds released).");
    }

    // ------------------------------ Refund ------------------------------

    public async Task<RefundResult> RefundAsync(string buyerId, int orderId, RefundInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            throw new PaymentOperationException(PaymentError.InvalidRequest, "A refund requires an idempotency key.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdForBuyerSpec(orderId, buyerId), ct)
            ?? throw new PaymentOperationException(PaymentError.NotFound, $"No payment for order {orderId} was found.");

        if (payment.CaptureId is null || payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentOperationException(PaymentError.Conflict, "Only a captured (fulfilled) order can be refunded.");
        }

        // Idempotency: a repeat under the same key returns the existing refund.
        var existing = payment.FindRefundByKey(input.IdempotencyKey);
        if (existing is not null)
        {
            return ToRefundResult(payment, existing);
        }

        var amount = input.Amount ?? payment.RefundableRemaining();
        if (amount <= 0m)
        {
            throw new PaymentOperationException(PaymentError.Conflict, "Nothing remains to refund on this order.");
        }

        PaymentRefund refund;
        try
        {
            // Rejects a refund that would exceed the captured amount.
            refund = payment.AddRefund(input.IdempotencyKey, amount);
        }
        catch (InvalidOperationException ex)
        {
            throw new PaymentOperationException(PaymentError.Conflict, ex.Message);
        }

        try
        {
            await _paymentRepository.UpdateAsync(payment, ct);
        }
        catch (DbUpdateException)
        {
            // Concurrent request with the same key won the unique (payment, key) index; return its refund.
            var reloaded = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdForBuyerSpec(orderId, buyerId), ct);
            var raced = reloaded?.FindRefundByKey(input.IdempotencyKey);
            if (reloaded is not null && raced is not null)
            {
                return ToRefundResult(reloaded, raced);
            }
            throw;
        }

        try
        {
            // The caller's idempotency key is unique per order; namespace it by the payment so the
            // PayPal-Request-Id (which is unique per PayPal account) does not collide across orders.
            var providerRequestId = $"refund-{payment.IdempotencySeed}-{input.IdempotencyKey}";
            var outcome = await _gateway.RefundAsync(payment.CaptureId!, amount, providerRequestId, ct);
            refund.Settle(outcome.RefundId, MapRefundState(outcome.Status));
            payment.RecomputeRefundStatus();
            await _paymentRepository.UpdateAsync(payment, ct);
            return ToRefundResult(payment, refund);
        }
        catch (PayPalGatewayException ex) when (ex.Kind == PayPalFailureKind.Unknown)
        {
            // Leave the refund pending; the same idempotency key settles it on a later attempt.
            _logger.LogWarning(ex, "Refund outcome unknown for order {OrderId}; left pending.", orderId);
            throw;
        }
        catch (PayPalGatewayException ex)
        {
            refund.Settle(null, RefundState.Failed);
            payment.RecomputeRefundStatus();
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }
    }

    // ------------------------------ My orders ------------------------------

    public async Task<IReadOnlyList<MyOrderView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderReadRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentReadRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), ct);
        var paymentByOrder = payments.ToDictionary(p => p.OrderId);

        var views = new List<MyOrderView>();
        foreach (var order in orders)
        {
            paymentByOrder.TryGetValue(order.Id, out var payment);
            var items = order.OrderItems
                .Select(i => new MyOrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.Units, i.UnitPrice))
                .ToList();

            views.Add(new MyOrderView(
                order.Id,
                order.OrderDate,
                order.Total(),
                payment?.Currency ?? _gateway.Currency,
                (payment?.Status ?? PaymentStatus.PendingPayment).ToString(),
                payment?.PayPalOrderId,
                payment?.AuthorizationId,
                payment?.CaptureId,
                payment?.CapturedGross,
                payment?.PayPalFee,
                payment?.NetAmount,
                payment?.TotalRefunded() ?? 0m,
                items));
        }

        return views;
    }

    // ------------------------------ Reconciliation ------------------------------

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var search = await _gateway.SearchTransactionsAsync(fromUtc, toUtc, ct);
        // Filter the local side on the SAME clock the PayPal report uses (PayPal event time), never row-creation.
        var local = await _paymentReadRepository.ListAsync(new OrderPaymentsInEventWindowSpec(fromUtc, toUtc), ct);

        var records = search.Records;
        var usedRecord = new bool[records.Count];
        var matched = new List<ReconciliationMatch>();
        var onlyInEShop = new List<ReconciliationLocalOnly>();

        foreach (var payment in local)
        {
            var localIds = new HashSet<string>(StringComparer.Ordinal);
            AddIfPresent(localIds, payment.PayPalOrderId);
            AddIfPresent(localIds, payment.AuthorizationId);
            AddIfPresent(localIds, payment.CaptureId);

            var found = -1;
            for (var i = 0; i < records.Count; i++)
            {
                if (usedRecord[i])
                {
                    continue;
                }
                var r = records[i];
                if ((r.TransactionId is not null && localIds.Contains(r.TransactionId)) ||
                    (r.ReferenceId is not null && localIds.Contains(r.ReferenceId)))
                {
                    found = i;
                    break;
                }
            }

            if (found >= 0)
            {
                usedRecord[found] = true;
                matched.Add(new ReconciliationMatch(payment.OrderId, payment.Status.ToString(), records[found].TransactionId, records[found].Amount));
            }
            else
            {
                onlyInEShop.Add(new ReconciliationLocalOnly(payment.OrderId, payment.Status.ToString(),
                    payment.PayPalOrderId, payment.AuthorizationId, payment.CaptureId));
            }
        }

        var onlyInPayPal = new List<ReconciliationPayPalOnly>();
        for (var i = 0; i < records.Count; i++)
        {
            if (usedRecord[i])
            {
                continue;
            }
            var r = records[i];
            onlyInPayPal.Add(new ReconciliationPayPalOnly(r.TransactionId, r.ReferenceId, r.Amount, r.Currency, r.InitiatedAtUtc, r.EventCode));
        }

        return new ReconciliationReport(
            fromUtc, toUtc,
            matched.Count, onlyInEShop.Count, onlyInPayPal.Count,
            search.Truncated, search.WindowsCovered,
            matched, onlyInEShop, onlyInPayPal);
    }

    // ------------------------------ Saved cards ------------------------------

    public async Task<SavedCardView> SaveCardAsync(string buyerId, SaveCardInput input, CancellationToken ct)
    {
        // Reuse the PayPal customer id from an earlier saved card so a shopper's cards group together.
        var latest = await _savedCardReadRepository.FirstOrDefaultAsync(new LatestSavedPaymentMethodForBuyerSpec(buyerId), ct);
        var customerId = latest?.PayPalCustomerId;

        var request = new PayPalVaultCardRequest(
            BuyerReference: SanitizeCustomerRef(buyerId),
            ExistingCustomerId: customerId,
            Card: MapCard(input.Card),
            IdempotencyKey: Guid.NewGuid().ToString("N"));

        var outcome = await _gateway.VaultCardAsync(request, ct);

        var saved = new SavedPaymentMethod(
            buyerId,
            outcome.VaultId,
            outcome.CustomerId,
            outcome.Brand,
            outcome.LastDigits,
            outcome.Expiry,
            outcome.CardholderName);

        try
        {
            saved = await _savedCardRepository.AddAsync(saved, ct);
        }
        catch (DbUpdateException)
        {
            // Duplicate vault id (same card vaulted twice): reload the existing record for this buyer.
            var existing = (await _savedCardReadRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct))
                .FirstOrDefault(m => m.PayPalVaultId == outcome.VaultId);
            if (existing is not null)
            {
                saved = existing;
            }
        }

        return ToSavedCardView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string buyerId, CancellationToken ct)
    {
        var cards = await _savedCardReadRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), ct);
        return cards.Select(ToSavedCardView).ToList();
    }

    public async Task<bool> DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var saved = await _savedCardRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdForBuyerSpec(paymentMethodId, buyerId), ct);
        if (saved is null)
        {
            return false;
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultId, ct);
        }
        catch (PayPalGatewayException ex) when (ex.Kind == PayPalFailureKind.NotFound)
        {
            // Already gone at PayPal; still remove the local record so it can no longer be used to pay.
            _logger.LogInformation("Vault token {VaultId} already absent at PayPal; removing local record.", saved.PayPalVaultId);
        }

        await _savedCardRepository.DeleteAsync(saved, ct);
        return true;
    }

    // ------------------------------ Helpers ------------------------------

    private static Address BuildShippingAddress(ShippingAddressInput? shipping)
    {
        // Order requires a non-null shipping address; default a placeholder when the caller omits one so the
        // flow is drivable with only item ids + quantities.
        return new Address(
            shipping?.Street ?? "N/A",
            shipping?.City ?? "N/A",
            shipping?.State ?? "N/A",
            shipping?.Country ?? "N/A",
            shipping?.ZipCode ?? "00000");
    }

    private static PayPalCardInput MapCard(CardInput card)
    {
        PayPalBillingAddress? billing = null;
        if (card.BillingAddress is not null)
        {
            billing = new PayPalBillingAddress(
                card.BillingAddress.AddressLine1,
                card.BillingAddress.City,
                card.BillingAddress.State,
                card.BillingAddress.PostalCode,
                card.BillingAddress.CountryCode);
        }
        return new PayPalCardInput(card.Number, card.Expiry, card.SecurityCode, card.CardholderName, billing);
    }

    private static string SanitizeCustomerRef(string buyerId)
    {
        var cleaned = CustomerRefInvalidChars.Replace(buyerId, "_");
        return cleaned.Length <= 64 ? cleaned : cleaned.Substring(0, 64);
    }

    private static string DescribeFailure(PayPalGatewayException ex) =>
        $"{ex.IssueCode ?? ex.Kind.ToString()}: {ex.Message}";

    private static RefundState MapRefundState(string status) => status.ToUpperInvariant() switch
    {
        "COMPLETED" => RefundState.Completed,
        "PENDING" => RefundState.Pending,
        "CANCELLED" => RefundState.Cancelled,
        "FAILED" => RefundState.Failed,
        _ => RefundState.Pending
    };

    private PaymentActionResult ToActionResult(OrderPayment payment, string message) => new(
        payment.OrderId,
        payment.Status.ToString(),
        payment.PayPalOrderId,
        payment.AuthorizationId,
        payment.CaptureId,
        payment.AuthorizedAmount,
        payment.Currency,
        payment.CapturedGross,
        payment.PayPalFee,
        payment.NetAmount,
        payment.FailureReason ?? message);

    private static RefundResult ToRefundResult(OrderPayment payment, PaymentRefund refund) => new(
        payment.OrderId,
        refund.PayPalRefundId ?? string.Empty,
        refund.Amount,
        refund.Currency,
        refund.State.ToString(),
        payment.Status.ToString(),
        payment.TotalRefunded());

    private static SavedCardView ToSavedCardView(SavedPaymentMethod card) => new(
        card.Id,
        card.Brand,
        card.LastDigits,
        card.Expiry,
        card.CardholderName,
        card.CreatedAtUtc);

    private static void AddIfPresent(HashSet<string> set, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            set.Add(value);
        }
    }
}
