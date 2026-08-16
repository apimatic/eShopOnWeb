using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the money movement for an order against PayPal: authorize (hold) at pay time, capture
/// at fulfilment (renewing a stale hold if needed), void on cancel, and refund on return. Per-order
/// operations are serialized (<see cref="IPaymentOperationLock"/>) and every step first checks whether
/// PayPal-owned state already reflects the request, so a double-click never charges the shopper twice.
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IPaymentOperationLock _operationLock;
    private readonly IPaymentSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalClient payPalClient,
        IPaymentOperationLock operationLock,
        IPaymentSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _savedCardRepository = savedCardRepository;
        _payPalClient = payPalClient;
        _operationLock = operationLock;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    // A globally-unique reference sent to PayPal as custom_id/invoice_id. The order id alone is not
    // unique across runs (the in-memory store restarts ids at 1), so we add a random token; the exact
    // value is stored on the Payment and is what reconciliation matches on.
    private static string NewOrderReference(int orderId) => $"ESHOP-{orderId}-{Guid.NewGuid():N}";

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, PaymentInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(instruction, nameof(instruction));

        var usingSavedCard = instruction.SavedCardId is not null;
        var usingOneOffCard = instruction.Card is not null;
        if (usingSavedCard == usingOneOffCard)
            throw new PaymentException("Provide either card details or a saved card id to pay with — exactly one.");

        using var _ = await _operationLock.AcquireAsync(LockKey(orderId), cancellationToken);

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentForBuyerSpec(orderId, buyerId), cancellationToken)
            ?? throw new ResourceNotFoundException($"Order {orderId} was not found.");

        if (order.Status == OrderStatus.Cancelled)
            throw new PaymentException("This order was cancelled and can no longer be paid.");

        // Idempotent double-click: an authorized/captured order already has its hold — return it.
        if (order.Payment is not null)
            return order;

        var amount = Round(order.Total());
        if (amount <= 0)
            throw new PaymentException("Order total must be greater than zero to authorize a payment.");

        var requestId = Guid.NewGuid().ToString("N");
        var reference = NewOrderReference(orderId);

        AuthorizeResult result;
        int? savedCardId = null;

        if (usingSavedCard)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdForBuyerSpecification(instruction.SavedCardId!.Value, buyerId), cancellationToken)
                ?? throw new ResourceNotFoundException($"Saved card {instruction.SavedCardId} was not found.");

            result = await _payPalClient.AuthorizeWithVaultedCardAsync(
                amount, Currency, reference, savedCard.VaultTokenId, requestId, cancellationToken);
            savedCardId = savedCard.Id;
        }
        else
        {
            result = await _payPalClient.AuthorizeWithCardAsync(
                amount, Currency, reference, instruction.Card!, instruction.SaveCard, requestId, cancellationToken);
        }

        EnsureAuthorized(result);

        var payment = new Payment(orderId, Currency, reference, amount, result.PayPalOrderId, result.AuthorizationId,
            result.AuthorizationStatus, result.ExpiresAt, requestId, result.CardBrand, result.CardLast4, savedCardId);

        order.SetAuthorizedPayment(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        // If the shopper asked to keep a one-off card and PayPal vaulted it, persist it for reuse.
        if (usingOneOffCard && instruction.SaveCard && result.VaultTokenId is not null)
        {
            try
            {
                var card = new SavedCard(buyerId, result.VaultTokenId, result.VaultCustomerId,
                    result.CardBrand ?? "CARD", result.CardLast4 ?? "0000",
                    instruction.Card!.Expiry, label: null);
                await _savedCardRepository.AddAsync(card, cancellationToken);
            }
            catch (Exception ex)
            {
                // Saving the card is a convenience; never fail an otherwise-successful payment over it.
                _logger.LogWarning($"Order {orderId} authorized but the card could not be saved: {ex.Message}");
            }
        }

        _logger.LogInformation($"Order {orderId} authorized: hold {result.AuthorizationId} for {amount} {Currency}.");
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var _ = await _operationLock.AcquireAsync(LockKey(orderId), cancellationToken);

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId), cancellationToken)
            ?? throw new ResourceNotFoundException($"Order {orderId} was not found.");

        var payment = order.Payment
            ?? throw new PaymentException("This order has not been paid; there is nothing to fulfil.");

        if (order.Status == OrderStatus.Cancelled)
            throw new PaymentException("This order was cancelled and cannot be fulfilled.");

        // Idempotent: already captured — nothing more to take.
        if (payment.IsCaptured)
        {
            if (order.Status != OrderStatus.Fulfilled)
            {
                order.MarkFulfilled();
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }
            return order;
        }

        if (order.Status != OrderStatus.PaymentAuthorized)
            throw new PaymentException($"Order cannot be fulfilled from status {order.Status}.");

        var amount = payment.AuthorizedAmount;
        var captureRequestId = payment.CaptureRequestId ?? Guid.NewGuid().ToString("N");
        if (payment.CaptureRequestId is null)
        {
            payment.SetCaptureRequestId(captureRequestId);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        // Renew a stale hold before attempting to take the money, rather than letting the capture fail.
        if (payment.IsAuthorizationStale(DateTimeOffset.UtcNow))
        {
            _logger.LogInformation($"Order {orderId} hold {payment.AuthorizationId} is stale; attempting to renew.");
            await RenewAuthorizationOrThrowAsync(order, payment, amount, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _payPalClient.CaptureAuthorizationAsync(
                payment.AuthorizationId, amount, Currency, captureRequestId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.IndicatesAuthorizationNoLongerCapturable)
        {
            // The hold lapsed between our staleness check and the capture. Try to renew once, then recapture.
            _logger.LogWarning($"Order {orderId} capture rejected ({ex.IssueName}); attempting to renew the hold.");
            await RenewAuthorizationOrThrowAsync(order, payment, amount, cancellationToken);
            capture = await _payPalClient.CaptureAuthorizationAsync(
                payment.AuthorizationId, amount, Currency, captureRequestId + "-r", cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.CaptureStatus, capture.GrossAmount,
            capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            $"Order {orderId} fulfilled: captured {capture.GrossAmount} {Currency} " +
            $"(fee {capture.PayPalFee}, net {capture.NetAmount}) via capture {capture.CaptureId}.");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var _ = await _operationLock.AcquireAsync(LockKey(orderId), cancellationToken);

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId), cancellationToken)
            ?? throw new ResourceNotFoundException($"Order {orderId} was not found.");

        if (order.Status == OrderStatus.Cancelled)
            return order; // idempotent

        if (order.Status == OrderStatus.Fulfilled || (order.Payment?.IsCaptured ?? false))
            throw new PaymentException("This order was already fulfilled; use a refund to return money instead of cancelling.");

        var payment = order.Payment;
        if (payment is not null)
        {
            try
            {
                await _payPalClient.VoidAuthorizationAsync(
                    payment.AuthorizationId, Guid.NewGuid().ToString("N"), cancellationToken);
            }
            catch (PayPalApiException ex) when (ex.IndicatesAuthorizationNoLongerCapturable)
            {
                // Already voided or expired — the funds are released either way; treat as success.
                _logger.LogInformation($"Order {orderId} hold already released ({ex.IssueName}).");
            }

            payment.MarkAuthorizationVoided();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {orderId} cancelled; any hold was released.");
        return order;
    }

    public async Task<(Refund Refund, Order Order)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        using var _ = await _operationLock.AcquireAsync(LockKey(orderId), cancellationToken);

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentForBuyerSpec(orderId, buyerId), cancellationToken)
            ?? throw new ResourceNotFoundException($"Order {orderId} was not found.");

        var payment = order.Payment;
        if (payment is null || !payment.IsCaptured)
            throw new PaymentException("This order has not been fulfilled; there is nothing to refund.");

        // Idempotent: the same key must resolve to the same refund, never a second one.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return (existing, order);

        var remaining = payment.RefundableRemaining;
        var refundAmount = Round(amount ?? remaining);

        if (refundAmount <= 0)
            throw new PaymentException("There is nothing left to refund on this order.");
        if (refundAmount > remaining)
            throw new PaymentException(
                $"Refund of {refundAmount} {Currency} exceeds the refundable balance of {remaining} {Currency}.");

        var result = await _payPalClient.RefundCaptureAsync(
            payment.CaptureId!, refundAmount, Currency, payment.PayPalCustomId, idempotencyKey, cancellationToken);

        var refund = new Refund(idempotencyKey, result.RefundId, refundAmount, Currency, result.Status);
        payment.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            $"Order {orderId} refunded {refundAmount} {Currency} (refund {result.RefundId}); " +
            $"{payment.RefundableRemaining} {Currency} remains refundable.");
        return (refund, order);
    }

    private async Task RenewAuthorizationOrThrowAsync(Order order, Payment payment, decimal amount,
        CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPalClient.ReauthorizeAsync(
                payment.AuthorizationId, amount, Currency, Guid.NewGuid().ToString("N"), cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.AuthorizationStatus, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Order {order.Id} hold renewed as {renewed.AuthorizationId}.");
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning($"Order {order.Id} hold could not be renewed ({ex.IssueName ?? ex.Message}).");
            throw new PaymentException(
                "The authorization for this order has expired and could not be renewed. " +
                "Ask the shopper to pay for the order again before fulfilling it.", ex);
        }
    }

    private static void EnsureAuthorized(AuthorizeResult result)
    {
        // A direct-card authorization that PayPal accepts comes back CREATED. Anything else (e.g. PENDING
        // awaiting a shopper action, or DENIED) is not a usable hold — fail with an actionable message.
        if (!string.Equals(result.AuthorizationStatus, Payment.AuthCreated, StringComparison.OrdinalIgnoreCase))
            throw new PaymentException(
                $"The card could not be authorized (status {result.AuthorizationStatus}). " +
                "Try a different card.");
    }

    private static string LockKey(int orderId) =>
        string.Create(CultureInfo.InvariantCulture, $"order:{orderId}");
}
