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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the order lifecycle and the money that follows it: place, authorize, capture at
/// fulfilment, void on cancel, refund on return. The processor itself sits behind
/// <see cref="IPaymentGateway"/>, so nothing here depends on a PayPal type.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<CatalogItem> _catalogItemRepository;
    private readonly IReadRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly OrderLockProvider _orderLocks;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<CatalogItem> catalogItemRepository,
        IReadRepository<SavedCard> savedCardRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        OrderLockProvider orderLocks,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogItemRepository = catalogItemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _orderLocks = orderLocks;
        _logger = logger;
    }

    public string CurrencyCode => _gateway.CurrencyCode;

    public async Task<OrderView> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if (lines is null || lines.Count == 0)
        {
            throw new PaymentValidationException("An order needs at least one item.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentValidationException("Every order line needs a quantity greater than zero.");
        }

        // Collapse duplicate lines for the same catalog item, so a caller sending the same id twice
        // gets one order item with the combined quantity rather than two priced separately.
        var requested = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(requested.Keys.ToArray()), cancellationToken);

        var missing = requested.Keys.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentValidationException(
                $"No catalog item exists with id {string.Join(", ", missing)}.");
        }

        // Prices come from the catalog, never from the request — a caller cannot name their own price.
        var orderItems = catalogItems.Select(catalogItem => new OrderItem(
            new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri)),
            catalogItem.Price,
            requested[catalogItem.Id])).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation(
            $"Order {order.Id} placed by buyer with {orderItems.Count} line(s), total {Format(order.Total())} {CurrencyCode}.");

        return OrderView.From(order, null, CurrencyCode);
    }

    public async Task<PaymentView> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument,
        CancellationToken cancellationToken)
    {
        using var _ = await _orderLocks.AcquireAsync(orderId, cancellationToken);

        var order = await LoadOrderForBuyerAsync(orderId, buyerId, cancellationToken);
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // A repeat of a call that already succeeded returns what it produced, rather than holding
        // the shopper's money a second time.
        if (payment is not null && payment.Status is not (PaymentStatus.PendingAuthorization or PaymentStatus.Failed))
        {
            _logger.LogInformation($"Order {orderId} is already {payment.Status}; returning the existing payment.");
            return PaymentView.From(payment);
        }

        if (order.Status != OrderLifecycleStatus.AwaitingPayment)
        {
            throw new OrderStateException(
                $"Order {orderId} is {order.Status} and cannot be paid for.");
        }

        var total = order.Total();
        if (total <= 0m)
        {
            throw new PaymentValidationException($"Order {orderId} has a total of zero; there is nothing to pay.");
        }

        var resolvedInstrument = await ResolveInstrumentAsync(buyerId, instrument, cancellationToken);

        if (payment is null)
        {
            payment = new Payment(orderId, buyerId, total, CurrencyCode, InvoiceIdFor(orderId));
            payment = await _paymentRepository.AddAsync(payment, cancellationToken);
        }

        var attempt = payment.BeginAuthorizationAttempt();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        AuthorizationResult result;
        try
        {
            result = await _gateway.AuthorizeAsync(new AuthorizationRequest
            {
                OrderId = orderId,
                InvoiceId = payment.InvoiceId,
                Amount = total,
                Description = $"eShopOnWeb order {orderId}",
                Instrument = resolvedInstrument,
                CreateIdempotencyKey = ProviderKey(payment, $"create-{attempt}"),
                AuthorizeIdempotencyKey = ProviderKey(payment, $"auth-{attempt}")
            }, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            // "We could not tell what happened" is not "it failed" — freezing the payment is what
            // stops a retry from placing a second hold on money that may already be held.
            if (ex.Kind == PaymentGatewayFailure.OutcomeUnknown)
            {
                payment.MarkOutcomeUnknown();
            }
            else
            {
                payment.MarkFailed();
            }

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogWarning(
                $"Authorization for order {orderId} failed ({ex.Kind}, provider code {ex.ProviderCode ?? "none"}, debug id {ex.DebugId ?? "none"}).");
            throw;
        }

        // The hold must be for the order total to the cent. Anything else is a defect, and leaving
        // the shopper holding a mismatched authorization would be worse than releasing it.
        if (result.Amount != total || !string.Equals(result.CurrencyCode, CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                $"Order {orderId}: processor held {Format(result.Amount)} {result.CurrencyCode} against a total of " +
                $"{Format(total)} {CurrencyCode}. Releasing the hold.");

            await TryReleaseMismatchedHoldAsync(payment, result, cancellationToken);

            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            throw new PaymentGatewayException(
                $"The payment processor held {Format(result.Amount)} {result.CurrencyCode} but order {orderId} " +
                $"totals {Format(total)} {CurrencyCode}. The hold has been released and no money was taken.",
                PaymentGatewayFailure.Unavailable);
        }

        payment.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
        order.MarkAuthorized();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            $"Order {orderId} authorized: {Format(total)} {CurrencyCode} held, PayPal order {result.PayPalOrderId}, " +
            $"authorization {result.AuthorizationId} ({result.Status}).");

        return PaymentView.From(payment);
    }

    public async Task<PaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var _ = await _orderLocks.AcquireAsync(orderId, cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = await LoadPaymentAsync(orderId, cancellationToken)
            ?? throw new OrderStateException(
                $"Order {orderId} has no payment; it must be paid for before it can be fulfilled.");

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            _logger.LogInformation($"Order {orderId} is already captured; returning the existing capture.");
            return PaymentView.From(payment);
        }

        if (order.Status != OrderLifecycleStatus.Authorized || payment.Status != PaymentStatus.Authorized)
        {
            throw new OrderStateException(
                $"Order {orderId} cannot be fulfilled: the order is {order.Status} and its payment is {payment.Status}. " +
                "Fulfilment captures a held authorization, so the order must be authorized first.");
        }

        var authorizationId = payment.AuthorizationId!;

        // A hold that has passed its expiry gets renewed before we try to take the money; the
        // capture below is still guarded, because expiry is the processor's call, not ours.
        if (payment.IsAuthorizationStale(DateTimeOffset.UtcNow))
        {
            authorizationId = await RenewAuthorizationAsync(payment, "the stored expiry has passed", cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await CaptureAsync(payment, authorizationId, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.Kind is PaymentGatewayFailure.Conflict or PaymentGatewayFailure.Rejected)
        {
            // The capture was refused. Ask the processor what state the hold is actually in, rather
            // than guessing from an error string: anything outside "capturable" means renew and retry.
            var snapshot = await SafeGetAuthorizationAsync(authorizationId, cancellationToken);

            if (snapshot is null || IsCapturable(snapshot.Status))
            {
                throw;
            }

            _logger.LogWarning(
                $"Order {orderId}: capture refused and the authorization reads {snapshot.Status}; renewing it.");

            authorizationId = await RenewAuthorizationAsync(payment,
                $"the processor reports the authorization as {snapshot.Status}", cancellationToken);

            capture = await CaptureAsync(payment, authorizationId, cancellationToken);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            $"Order {orderId} fulfilled and captured: {Format(capture.Amount)} {capture.CurrencyCode} " +
            $"(fee {Format(capture.PayPalFee)}, net {Format(capture.NetAmount)}), capture {capture.CaptureId} ({capture.Status}).");

        return PaymentView.From(payment);
    }

    public async Task<PaymentView?> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var _ = await _orderLocks.AcquireAsync(orderId, cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (order.Status == OrderLifecycleStatus.Cancelled)
        {
            _logger.LogInformation($"Order {orderId} is already cancelled.");
            return payment is null ? null : PaymentView.From(payment);
        }

        if (order.Status is OrderLifecycleStatus.Fulfilled or OrderLifecycleStatus.PartiallyRefunded
            or OrderLifecycleStatus.Refunded)
        {
            throw new OrderStateException(
                $"Order {orderId} is {order.Status}: the money has already been taken, so it cannot be cancelled. " +
                $"Refund it instead (POST /api/orders/{orderId}/refunds).");
        }

        if (payment is not null && payment.Status == PaymentStatus.Authorized)
        {
            await _gateway.VoidAsync(payment.AuthorizationId!, ProviderKey(payment, "void"), cancellationToken);
            payment.MarkVoided("VOIDED");
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            _logger.LogInformation(
                $"Order {orderId} cancelled: authorization {payment.AuthorizationId} released, no money moved.");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment is null ? null : PaymentView.From(payment);
    }

    public async Task<RefundView> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("A refund needs an idempotency key, so a repeated request cannot refund twice.");
        }

        using var _ = await _orderLocks.AcquireAsync(orderId, cancellationToken);

        var order = await LoadOrderForBuyerAsync(orderId, buyerId, cancellationToken);
        var payment = await LoadPaymentAsync(orderId, cancellationToken)
            ?? throw new OrderStateException($"Order {orderId} has no payment to refund.");

        // The caller's key is the whole guarantee: same key, same refund, no second call out.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation(
                $"Refund for order {orderId} under key '{idempotencyKey}' already exists ({existing.PayPalRefundId}); returning it.");
            return RefundView.From(existing);
        }

        // Rejects anything that would take the total refunded past what was captured.
        var refundAmount = payment.ValidateRefundAmount(amount);

        // A first, whole-balance refund is sent as a full refund; anything else names its amount.
        var isWholeCapture = amount is null && payment.Refunds.Count == 0;

        var result = await _gateway.RefundAsync(
            payment.CaptureId!,
            isWholeCapture ? null : refundAmount,
            ProviderKey(payment, $"rf-{idempotencyKey}"),
            cancellationToken);

        var refund = payment.AddRefund(idempotencyKey, result.RefundId, result.Status, result.Amount);

        // A refund the processor did not complete leaves the order exactly where it was.
        if (refund.ReducesRefundableBalance)
        {
            order.MarkRefunded(fully: payment.Status == PaymentStatus.Refunded);
        }

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            $"Order {orderId} refunded {Format(result.Amount)} {result.CurrencyCode} " +
            $"(refund {result.RefundId}, {result.Status}); {Format(payment.RefundableRemaining)} {CurrencyCode} still refundable.");

        return RefundView.From(refund);
    }

    public async Task<IReadOnlyList<OrderView>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => OrderView.From(o, paymentsByOrder.GetValueOrDefault(o.Id), CurrencyCode))
            .ToList();
    }

    /// <summary>
    /// The reference we send to PayPal and match on when reconciling. It carries a timestamp because
    /// it also seeds every idempotency key below, and order ids alone are not unique over time —
    /// with an in-memory database they restart at 1 on every run.
    /// </summary>
    internal static string InvoiceIdFor(int orderId) =>
        $"eshop-{orderId}-{DateTime.UtcNow:yyyyMMddHHmmss}";

    /// <summary>
    /// Builds the PayPal-Request-Id for one operation on one payment.
    ///
    /// PayPal enforces that header's uniqueness per merchant account, not per resource, and retains
    /// Payments keys for 45 days — so a key built from a caller-supplied string alone (or from an
    /// id that restarts) collides with an unrelated earlier request and gets rejected. Seeding every
    /// key with the payment's own unique invoice reference keeps them globally distinct while
    /// staying deterministic, which is what makes a replay of the *same* request deduplicate.
    /// </summary>
    private static string ProviderKey(Payment payment, string operation)
    {
        var key = $"{payment.InvoiceId}-{operation}";

        // PayPal caps the header's length; a long caller-supplied key is folded into a stable digest
        // rather than truncated, so two long keys cannot collapse into one.
        return key.Length <= MaxProviderKeyLength
            ? key
            : $"{payment.InvoiceId}-{ShortHash(operation)}";
    }

    private const int MaxProviderKeyLength = 100;

    private static string ShortHash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes, 0, 16).ToLowerInvariant();
    }

    private async Task<CaptureResult> CaptureAsync(Payment payment, string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.CaptureAsync(
                authorizationId,
                payment.Amount,
                payment.InvoiceId,
                ProviderKey(payment, $"cap-{payment.AuthorizationAttempt}"),
                cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.Kind == PaymentGatewayFailure.OutcomeUnknown)
        {
            payment.MarkOutcomeUnknown();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw;
        }
    }

    private async Task<string> RenewAuthorizationAsync(Payment payment, string reason,
        CancellationToken cancellationToken)
    {
        var attempt = payment.BeginReauthorizationAttempt();

        AuthorizationSnapshot renewed;
        try
        {
            renewed = await _gateway.ReauthorizeAsync(
                payment.AuthorizationId!,
                payment.Amount,
                ProviderKey(payment, $"reauth-{attempt}"),
                cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            if (ex.Kind == PaymentGatewayFailure.OutcomeUnknown)
            {
                payment.MarkOutcomeUnknown();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw;
            }

            // Something an operator can act on: the hold is gone for good and the shopper has to pay again.
            throw new OrderStateException(
                $"Order {payment.OrderId} cannot be fulfilled: {reason}, and the authorization can no longer be " +
                $"renewed ({ex.Message}). An authorization can only be renewed within 29 days of the original " +
                $"payment. Ask the shopper to pay again (POST /api/orders/{payment.OrderId}/pay) — no money has " +
                "been taken, and nothing is held.", ex);
        }

        payment.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogWarning(
            $"Order {payment.OrderId}: authorization renewed ({reason}). New authorization " +
            $"{renewed.AuthorizationId} ({renewed.Status}), expires {renewed.ExpiresAt:u}.");

        return renewed.AuthorizationId;
    }

    /// <summary>
    /// Only a freshly created hold can be captured; everything else — voided, denied, already
    /// captured, or a status this SDK version does not declare — is treated as not capturable.
    /// </summary>
    private static bool IsCapturable(string status) =>
        string.Equals(status, "CREATED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase);

    private async Task<AuthorizationSnapshot?> SafeGetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            _logger.LogWarning($"Could not read authorization {authorizationId}: {ex.Message}");
            return null;
        }
    }

    private async Task TryReleaseMismatchedHoldAsync(Payment payment, AuthorizationResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await _gateway.VoidAsync(result.AuthorizationId, ProviderKey(payment, "void-mismatch"), cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            // We could not undo it, so the money may still be held — that is exactly the case
            // reconciliation exists for, and it must not be swallowed.
            payment.MarkOutcomeUnknown();
            _logger.LogWarning(
                $"Order {payment.OrderId}: could not release the mismatched hold {result.AuthorizationId} ({ex.Message}). " +
                "Flagged for reconciliation.");
        }
    }

    private async Task<PaymentInstrument> ResolveInstrumentAsync(string buyerId, PaymentInstrument instrument,
        CancellationToken cancellationToken)
    {
        // A saved card is named by its local id; we translate it to a vault id only after confirming
        // the card belongs to the caller, so one shopper can never pay with another's card.
        if (instrument is PaymentInstrument.SavedCardReference saved)
        {
            var card = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdForBuyerSpecification(saved.PaymentMethodId, buyerId), cancellationToken)
                ?? throw new EntityNotFoundException($"No saved payment method with id {saved.PaymentMethodId}.");

            return new PaymentInstrument.VaultToken(card.PayPalVaultId);
        }

        return instrument;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new EntityNotFoundException($"No order with id {orderId}.");

    private async Task<Order> LoadOrderForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        // Deliberately the same message as "no such order": a shopper must not be able to learn that
        // someone else's order exists.
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new EntityNotFoundException($"No order with id {orderId}.");
        }

        return order;
    }

    private Task<Payment?> LoadPaymentAsync(int orderId, CancellationToken cancellationToken) =>
        _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

    private static string Format(decimal? value) =>
        value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "n/a";
}
