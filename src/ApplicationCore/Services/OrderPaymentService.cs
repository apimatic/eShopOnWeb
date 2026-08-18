using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(IRepository<Order> orderRepository, IRepository<SavedCard> savedCardRepository,
        IPayPalPaymentGateway gateway, IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<OrderPayment> AuthorizeAsync(int orderId, string buyerId, PaymentInstrument instrument,
        CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            throw new OrderNotFoundException(orderId);

        var payment = order.Payment ?? throw new PaymentValidationException("Order has no payment record.");

        // Idempotent in effect: a double-click on an already-authorized order returns the existing hold.
        if (payment.Status == PaymentStatus.Authorized)
            return payment;
        if (payment.Status != PaymentStatus.PendingAuthorization)
            throw new PaymentValidationException($"Order {orderId} cannot be paid in its current state ({payment.Status}).");

        var (card, vaultId) = await ResolveInstrumentAsync(instrument, buyerId, cancellationToken);

        var auth = await _gateway.AuthorizeAsync(payment.Amount, payment.CurrencyCode, card, vaultId,
            idempotencyKey: payment.IdempotencyToken, customId: $"eshop-order-{orderId}", cancellationToken);

        payment.MarkAuthorized(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} authorized (authorization {1}, status {2}).", orderId, auth.AuthorizationId, auth.Status);
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = order.Payment ?? throw new PaymentValidationException("Order has no payment record.");

        if (payment.Status == PaymentStatus.Captured)
            return payment; // idempotent: never capture twice
        if (payment.Status != PaymentStatus.Authorized)
            throw new PaymentValidationException($"Order {orderId} cannot be fulfilled in its current state ({payment.Status}).");
        if (string.IsNullOrEmpty(payment.AuthorizationId))
            throw new PaymentValidationException("Order has no authorization to capture.");

        // Renew a hold that has gone stale before fulfilment, rather than failing the fulfilment outright.
        var renewedProactively = false;
        if (payment.AuthorizationExpiresAt is DateTimeOffset expiry && DateTimeOffset.UtcNow >= expiry.AddMinutes(-2))
        {
            await RenewAuthorizationAsync(order, payment, cancellationToken);
            renewedProactively = true;
        }

        PayPalCapture capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, $"capture-{payment.AuthorizationId}", cancellationToken);
        }
        catch (PaymentGatewayException) when (!renewedProactively)
        {
            // The hold may have gone stale without a known expiry. Renew once and retry; if it can no longer
            // be renewed, RenewAuthorizationAsync surfaces an operator-actionable message.
            await RenewAuthorizationAsync(order, payment, cancellationToken);
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, $"capture-{payment.AuthorizationId}", cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount,
            DateTimeOffset.UtcNow);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} fulfilled and captured (capture {1}, net {2} {3}).",
            orderId, capture.CaptureId, (object?)capture.NetAmount ?? "n/a", capture.Currency);
        return payment;
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = order.Payment ?? throw new PaymentValidationException("Order has no payment record.");

        if (payment.Status == PaymentStatus.Voided)
            return payment; // idempotent
        if (payment.Status == PaymentStatus.Captured || payment.Status == PaymentStatus.PartiallyRefunded
            || payment.Status == PaymentStatus.Refunded)
            throw new PaymentValidationException($"Order {orderId} has already been fulfilled; it must be refunded, not cancelled.");
        if (payment.Status != PaymentStatus.Authorized)
            throw new PaymentValidationException($"Order {orderId} cannot be cancelled in its current state ({payment.Status}).");
        if (string.IsNullOrEmpty(payment.AuthorizationId))
            throw new PaymentValidationException("Order has no authorization to void.");

        await _gateway.VoidAsync(payment.AuthorizationId!, $"void-{payment.AuthorizationId}", cancellationToken);
        payment.MarkVoided();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} cancelled; authorization {1} voided.", orderId, payment.AuthorizationId);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            throw new OrderNotFoundException(orderId);
        var payment = order.Payment ?? throw new PaymentValidationException("Order has no payment record.");

        if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded)
            throw new PaymentValidationException($"Order {orderId} has no captured payment to refund.");
        if (string.IsNullOrEmpty(payment.CaptureId))
            throw new PaymentValidationException("Order has no capture to refund.");

        // Idempotency: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
            return existing;

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
            throw new PaymentValidationException("Nothing remains to refund on this order.");

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > remaining)
            throw new PaymentValidationException(
                $"Refund of {refundAmount} {payment.CurrencyCode} exceeds the refundable balance of {remaining} {payment.CurrencyCode}.");

        var result = await _gateway.RefundAsync(payment.CaptureId!, refundAmount, payment.CurrencyCode,
            $"refund-{payment.CaptureId}-{idempotencyKey}", cancellationToken);

        var refund = new PaymentRefund(result.RefundId, refundAmount, payment.CurrencyCode, result.Status, idempotencyKey);
        payment.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} refunded {1} {2} (refund {3}).", orderId, refundAmount, payment.CurrencyCode, result.RefundId);
        return refund;
    }

    private async Task<(CardDetails? card, string? vaultId)> ResolveInstrumentAsync(PaymentInstrument instrument,
        string buyerId, CancellationToken cancellationToken)
    {
        if (instrument.SavedCardId is int savedCardId)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdAndBuyerSpecification(savedCardId, buyerId), cancellationToken)
                ?? throw new PaymentValidationException($"Saved card {savedCardId} was not found.");
            return (null, saved.PayPalVaultId);
        }

        if (instrument.Card is not null)
            return (instrument.Card, null);

        throw new PaymentValidationException("A card or a saved card id must be supplied to pay.");
    }

    private async Task RenewAuthorizationAsync(Order order, OrderPayment payment, CancellationToken cancellationToken)
    {
        var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode,
            cancellationToken);
        payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Order {0} authorization renewed (new authorization {1}).", order.Id, reauth.AuthorizationId);
    }
}
