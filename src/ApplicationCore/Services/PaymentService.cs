using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // Serializes money-moving operations per order so a double-click never authorizes or captures twice.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _orderLocks = new();

    // A per-process nonce mixed into PayPal-Request-Id keys. Double-click safety is enforced app-side
    // (the persisted-payment check plus the per-order lock), so the request-id only needs to be stable
    // WITHIN a process run — the nonce keeps it stable across double-clicks yet distinct across restarts,
    // which matters because the in-memory database resets order ids to 1 on every restart and PayPal
    // caches request-ids for hours.
    private static readonly string InstanceNonce = Guid.NewGuid().ToString("N").Substring(0, 8);

    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<Order> _orderRepository;
    private readonly IReadRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<PaymentService> _logger;
    private readonly string _currency;

    public PaymentService(
        IRepository<Payment> paymentRepository,
        IReadRepository<Order> orderRepository,
        IReadRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPal,
        IOptions<PayPalSettings> settings,
        IAppLogger<PaymentService> logger)
    {
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _logger = logger;
        _currency = settings.Value.Currency;
    }

    public async Task<Payment> AuthorizeAsync(int orderId, string buyerId, int? savedCardId, CardDetails? card,
        CancellationToken cancellationToken = default)
    {
        if (savedCardId.HasValue == (card is not null))
        {
            throw new PaymentValidationException(
                "Provide exactly one of a saved card id or one-off card details to pay with.");
        }

        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

            var existing = await _paymentRepository.FirstOrDefaultAsync(
                new PaymentByOrderIdSpecification(orderId), cancellationToken);
            if (existing is not null)
            {
                if (existing.Status == PaymentStatus.Voided)
                {
                    throw new InvalidOperationException(
                        $"Order {orderId} was cancelled and can no longer be paid.");
                }
                // Idempotent: already authorized (or beyond) — do not hold funds again.
                return existing;
            }

            var amount = order.Total();
            if (amount <= 0m)
            {
                throw new PaymentValidationException($"Order {orderId} has a non-positive total and cannot be paid.");
            }

            var money = new Money(_currency, amount);
            var orderReference = orderId.ToString();
            var idempotencyKey = $"auth-order-{orderId}-{InstanceNonce}";

            AuthorizationResult result;
            if (savedCardId.HasValue)
            {
                var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                    new SavedCardByIdSpecification(savedCardId.Value, buyerId), cancellationToken)
                    ?? throw new SavedCardNotFoundException(savedCardId.Value);

                result = await _payPal.AuthorizeWithVaultedCardAsync(
                    money, savedCard.VaultId, orderReference, idempotencyKey, cancellationToken);
            }
            else
            {
                result = await _payPal.AuthorizeWithCardAsync(
                    money, card!, orderReference, idempotencyKey, cancellationToken);
            }

            var payment = new Payment(orderId, buyerId, _currency, amount,
                result.PayPalOrderId, result.AuthorizationId, result.ExpiresAt);
            await _paymentRepository.AddAsync(payment, cancellationToken);

            _logger.LogInformation(
                $"Authorized order {orderId}: PayPal order {result.PayPalOrderId}, authorization {result.AuthorizationId}.");
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var payment = await LoadPaymentAsync(orderId, cancellationToken);

            if (payment.Status == PaymentStatus.Captured
                || payment.Status == PaymentStatus.PartiallyRefunded
                || payment.Status == PaymentStatus.Refunded)
            {
                // Idempotent: money already taken.
                return payment;
            }

            if (payment.Status != PaymentStatus.Authorized)
            {
                throw new InvalidOperationException(
                    $"Order {orderId} cannot be fulfilled because its payment is '{payment.Status}'.");
            }

            var capture = await CaptureWithRenewalAsync(payment, cancellationToken);
            payment.MarkCaptured(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            _logger.LogInformation(
                $"Fulfilled order {orderId}: captured {capture.GrossAmount} {capture.CurrencyCode} " +
                $"(fee {capture.PayPalFee}, net {capture.NetAmount}), capture {capture.CaptureId}.");
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CaptureResult> CaptureWithRenewalAsync(Payment payment, CancellationToken cancellationToken)
    {
        var captureKey = $"capture-order-{payment.OrderId}-{InstanceNonce}";

        // Proactively renew a hold we already know is stale before attempting the capture.
        if (payment.AuthorizationExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow)
        {
            _logger.LogInformation(
                $"Authorization {payment.AuthorizationId} for order {payment.OrderId} is stale; renewing before capture.");
            await RenewAuthorizationAsync(payment, cancellationToken);
        }

        try
        {
            return await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId!, captureKey, cancellationToken);
        }
        catch (AuthorizationUnusableException)
        {
            // PayPal rejected the capture as stale despite our timestamp: renew once, then retry.
            _logger.LogWarning(
                $"Capture of authorization {payment.AuthorizationId} for order {payment.OrderId} was rejected as stale; renewing.");
            await RenewAuthorizationAsync(payment, cancellationToken);
            return await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId!, captureKey, cancellationToken);
        }
    }

    private async Task RenewAuthorizationAsync(Payment payment, CancellationToken cancellationToken)
    {
        // Throws AuthorizationUnusableException (operator-actionable) when it can no longer be renewed.
        var reauth = await _payPal.ReauthorizeAsync(
            payment.AuthorizationId!, new Money(_currency, payment.Amount), cancellationToken);
        payment.RenewAuthorization(reauth.AuthorizationId, reauth.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var payment = await LoadPaymentAsync(orderId, cancellationToken);

            if (payment.Status == PaymentStatus.Voided)
            {
                return payment; // Idempotent.
            }

            if (payment.Status != PaymentStatus.Authorized)
            {
                throw new InvalidOperationException(
                    $"Order {orderId} cannot be cancelled because its payment is '{payment.Status}'. " +
                    "A captured order must be refunded, not cancelled.");
            }

            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            _logger.LogInformation($"Cancelled order {orderId}: voided authorization {payment.AuthorizationId}.");
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("A refund idempotency key is required.");
        }

        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var payment = await LoadPaymentAsync(orderId, cancellationToken);
            if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new OrderNotFoundException(orderId); // Not the caller's order.
            }

            // Idempotent: a refund already recorded under this key is returned unchanged.
            var priorRefund = payment.FindRefundByIdempotencyKey(idempotencyKey);
            if (priorRefund is not null)
            {
                return priorRefund;
            }

            var refundAmount = amount ?? payment.RefundableRemaining;
            payment.GuardCanRefund(refundAmount);

            var result = await _payPal.RefundCaptureAsync(
                payment.CaptureId!, new Money(_currency, refundAmount), idempotencyKey, cancellationToken);

            var refund = payment.AddRefund(result.RefundId, idempotencyKey, refundAmount, result.Status);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            _logger.LogInformation(
                $"Refunded {refundAmount} {_currency} on order {orderId}: refund {result.RefundId} ({result.Status}).");
            return refund;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new InvalidOperationException(
                $"Order {orderId} has no payment. It must be authorized (paid) before this operation.");
    }
}
