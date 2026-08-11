using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the money movement for orders against PayPal: authorize (hold), fulfil (capture, renewing a
/// stale hold if needed), cancel (void) and refund. Operations are idempotent in effect so a double-click
/// never charges the shopper twice.
/// </summary>
public class PaymentService : IPaymentService
{
    private const decimal CentTolerance = 0.005m;

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalClient payPal,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<Payment> AuthorizeAsync(
        int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);

        // Idempotency: an order already holding funds (or already captured) is not authorized a second time.
        if (order.Payment is not null)
        {
            return order.Payment.Status switch
            {
                PaymentStatus.Authorized or PaymentStatus.Captured or
                PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded => order.Payment,
                PaymentStatus.Voided => throw new PaymentStateException("This order was cancelled and cannot be paid."),
                _ => throw new PaymentStateException("This order cannot be paid in its current state.")
            };
        }

        var usingCard = card is not null;
        var usingSaved = savedPaymentMethodId is not null;
        if (usingCard == usingSaved)
            throw new PaymentValidationException("Provide either card details or a saved payment method id, but not both.");

        var amount = new Money(order.Total(), _payPal.Currency);
        var reference = orderId.ToString();
        var idempotencyKey = $"auth-{orderId}";

        AuthorizationOutcome outcome;
        Payment payment;

        if (usingCard)
        {
            CardValidation.Validate(card);
            outcome = await _payPal.CreateAuthorizedOrderWithCardAsync(amount, card!, reference, idempotencyKey, ct);
            var brand = outcome.Card?.Brand;
            var last4 = outcome.Card?.LastDigits ?? LastFour(card!.Number);
            payment = BuildPayment(order, amount, outcome, brand, last4, usedSavedCard: false);
        }
        else
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(buyerId, savedPaymentMethodId!.Value), ct);
            if (saved is null)
                throw new PaymentMethodNotFoundException(savedPaymentMethodId.Value);

            outcome = await _payPal.CreateAuthorizedOrderWithVaultAsync(amount, saved.VaultId, reference, idempotencyKey, ct);
            payment = BuildPayment(order, amount, outcome, saved.Brand, saved.LastDigits, usedSavedCard: true);
        }

        order.AttachPayment(payment);
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Authorized order {0} for {1} {2} (auth {3}).",
            orderId, amount.Amount, amount.Currency, outcome.AuthorizationId);
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);
        var payment = order.Payment
            ?? throw new PaymentStateException("This order has not been paid; there is nothing to fulfil.");

        switch (payment.Status)
        {
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                return payment; // already fulfilled — idempotent
            case PaymentStatus.Voided:
                throw new PaymentStateException("This order was cancelled and cannot be fulfilled.");
            case PaymentStatus.Authorized:
                break;
            default:
                throw new PaymentStateException("This order is not in a state that can be fulfilled.");
        }

        var money = new Money(payment.Amount, payment.Currency);
        var capture = await CaptureWithRenewalAsync(payment, money, ct);

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Fulfilled order {0}: captured {1} {2}, fee {3}, net {4} (capture {5}).",
            orderId, capture.GrossAmount, capture.Currency, capture.PayPalFee, capture.NetAmount, capture.CaptureId);
        return payment;
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);
        var payment = order.Payment
            ?? throw new PaymentStateException("This order has no active authorization to cancel.");

        switch (payment.Status)
        {
            case PaymentStatus.Voided:
                return payment; // already cancelled — idempotent
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                throw new PaymentStateException("This order has already been fulfilled; refund it instead of cancelling.");
            case PaymentStatus.Authorized:
                break;
            default:
                throw new PaymentStateException("This order is not in a state that can be cancelled.");
        }

        await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, ct);
        payment.MarkVoided();
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Cancelled order {0}: voided authorization {1}.", orderId, payment.AuthorizationId);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(
        int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);
        var payment = order.Payment
            ?? throw new PaymentStateException("This order has not been paid; there is nothing to refund.");

        if (payment.CaptureId is null)
            throw new PaymentStateException("This order has not been fulfilled; it cannot be refunded before capture.");

        // Idempotency: a repeat under the same key returns the original refund rather than issuing a second.
        var existing = payment.Refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
        if (existing is not null)
            return existing;

        var remaining = payment.RefundableRemaining;
        if (remaining <= 0m)
            throw new PaymentStateException("This order has already been fully refunded.");

        decimal refundAmount;
        if (amount is null)
        {
            refundAmount = remaining; // full remaining refund
        }
        else
        {
            if (amount.Value <= 0m)
                throw new PaymentValidationException("Refund amount must be greater than zero.");
            refundAmount = amount.Value;
        }

        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount - remaining > CentTolerance)
            throw new PaymentStateException(
                $"Refund of {refundAmount:0.00} {payment.Currency} exceeds the {remaining:0.00} {payment.Currency} still refundable on this order.");

        // PayPal treats the idempotency key as its PayPal-Request-Id, so it will not refund twice under the same key.
        var refundResult = await _payPal.RefundCaptureAsync(
            payment.CaptureId,
            amount is null ? null : new Money(refundAmount, payment.Currency),
            idempotencyKey,
            ct);

        var refund = new PaymentRefund(idempotencyKey, refundAmount, payment.Currency);
        refund.SetResult(refundResult.RefundId, refundResult.Status);
        payment.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Refunded {0} {1} on order {2} (refund {3}, key {4}).",
            refundAmount, payment.Currency, orderId, refundResult.RefundId, idempotencyKey);
        return refund;
    }

    // ---- helpers ----

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
        return order ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct);
        // Treat "not yours" the same as "not found" so one shopper cannot probe another's orders.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new OrderNotFoundException(orderId);
        return order;
    }

    private static Payment BuildPayment(
        Order order, Money amount, AuthorizationOutcome outcome, string? brand, string? last4, bool usedSavedCard) =>
        new(
            amount.Amount,
            amount.Currency,
            outcome.PayPalOrderId,
            outcome.AuthorizationId,
            outcome.Status,
            outcome.ExpiresAt,
            brand,
            last4,
            usedSavedCard);

    private async Task<CaptureOutcome> CaptureWithRenewalAsync(Payment payment, Money money, CancellationToken ct)
    {
        // A hold that has gone stale before fulfilment must be renewed rather than failing the fulfilment.
        var snapshot = await _payPal.GetAuthorizationAsync(payment.AuthorizationId, ct);
        payment.UpdateAuthorizationStatus(snapshot.Status);

        var renewedProactively = false;
        if (IsStale(snapshot))
        {
            await RenewAsync(payment, money, ct);
            renewedProactively = true;
        }

        try
        {
            return await _payPal.CaptureAuthorizationAsync(
                payment.AuthorizationId, money, $"capture-{payment.AuthorizationId}", ct);
        }
        catch (PayPalApiException ex) when (!renewedProactively && IndicatesStaleAuthorization(ex))
        {
            // The hold expired between the check and the capture — renew once and retry.
            await RenewAsync(payment, money, ct);
            return await _payPal.CaptureAuthorizationAsync(
                payment.AuthorizationId, money, $"capture-{payment.AuthorizationId}", ct);
        }
    }

    private async Task RenewAsync(Payment payment, Money money, CancellationToken ct)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(
                payment.AuthorizationId, money, $"reauth-{payment.AuthorizationId}", ct);
            payment.ApplyReauthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            _logger.LogInformation("Renewed stale authorization; new hold {0}.", reauth.AuthorizationId);
        }
        catch (PayPalApiException ex)
        {
            // The hold can no longer be renewed — say so in terms an operator can act on.
            throw new PaymentStateException(
                "The payment authorization for this order has expired and can no longer be renewed " +
                $"({ex.Issue ?? "REAUTHORIZATION_FAILED"}: {ex.Message}). " +
                "A new payment authorization is required before this order can be fulfilled.");
        }
    }

    private static bool IsStale(AuthorizationSnapshot snapshot)
    {
        if (string.Equals(snapshot.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase))
            return true;
        return snapshot.ExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow;
    }

    private static bool IndicatesStaleAuthorization(PayPalApiException ex)
    {
        var issue = ex.Issue ?? string.Empty;
        return issue.Contains("EXPIRE", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("expired", StringComparison.OrdinalIgnoreCase);
    }

    private static string LastFour(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }
}
