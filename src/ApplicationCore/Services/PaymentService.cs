using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<Entities.SavedCardAggregate.SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPal;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<Entities.SavedCardAggregate.SavedCard> savedCardRepository,
        IPayPalClient payPal,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Payment> PayAsync(string buyerId, int orderId, PayPalCardDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null && savedCardId is null)
        {
            throw new PaymentDomainException("Provide either card details or a saved paymentMethodId to pay.");
        }
        if (card is not null && savedCardId is not null)
        {
            throw new PaymentDomainException("Provide either card details or a saved paymentMethodId, not both.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Existence of other shoppers' orders is never revealed.
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentDomainException($"Order {orderId} has been cancelled and can no longer be paid.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is not null)
        {
            // Idempotent replay: the hold already exists, return it instead of charging again.
            if (payment.Status == PaymentStatus.Authorized)
            {
                return payment;
            }
            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                throw new PaymentDomainException($"Order {orderId} has already been captured.");
            }
            if (payment.Status == PaymentStatus.Voided)
            {
                throw new PaymentDomainException($"The payment for order {orderId} was voided; place a new order.");
            }
        }
        else
        {
            payment = new Payment(orderId, buyerId, order.Total(), _settings.Currency);
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }

        string? vaultTokenId = null;
        if (savedCardId is not null)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpec(savedCardId.Value), cancellationToken);
            if (savedCard is null || !string.Equals(savedCard.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new NotFoundException($"Payment method {savedCardId} was not found.");
            }
            vaultTokenId = savedCard.PayPalPaymentTokenId;
        }

        if (payment.PayPalOrderId is null)
        {
            // PayPal rejects a reused invoice id, so it must be unique per payment, not per order
            // (order ids restart when the store is reset). reference_id/custom_id stay order-scoped
            // so PayPal's transaction reports can be lined up against eShop orders.
            var invoiceId = $"eshop-{payment.CorrelationId}";
            var payPalOrderId = await _payPal.CreateOrderAsync(payment.Amount, payment.Currency,
                referenceId: ReferenceFor(orderId), invoiceId: invoiceId,
                idempotencyKey: $"eshop-{payment.CorrelationId}-order", cancellationToken);
            payment.SetPayPalOrderId(payPalOrderId, invoiceId);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        // Recovery: a previous attempt may have authorized at PayPal without us saving it.
        var authorization = await TryFindExistingAuthorizationAsync(payment.PayPalOrderId!, cancellationToken);
        if (authorization is null)
        {
            try
            {
                authorization = vaultTokenId is not null
                    ? await _payPal.AuthorizeOrderWithVaultedCardAsync(payment.PayPalOrderId, vaultTokenId,
                        $"eshop-{payment.CorrelationId}-authorize", cancellationToken)
                    : await _payPal.AuthorizeOrderWithCardAsync(payment.PayPalOrderId, card!,
                        $"eshop-{payment.CorrelationId}-authorize", cancellationToken);
            }
            catch (PayPalApiException ex) when (ex.HasIssue("ORDER_ALREADY_AUTHORIZED"))
            {
                authorization = await TryFindExistingAuthorizationAsync(payment.PayPalOrderId, cancellationToken);
                if (authorization is null)
                {
                    throw;
                }
            }
        }

        if (authorization.RequiresPayerAction)
        {
            throw new PaymentRequiresActionException(
                $"PayPal requires the shopper to approve this payment in a browser (order {orderId}). " +
                "This integration does not support approval round-trips; the payment was not completed.");
        }
        if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentDomainException(
                $"PayPal did not authorize order {orderId} (authorization status: {authorization.Status}).");
        }
        if (authorization.Amount != decimal.Round(payment.Amount, 2))
        {
            _logger.LogWarning($"PayPal authorized {authorization.Amount} {authorization.Currency} for order {orderId}, expected {payment.Amount:0.00} {payment.Currency}.");
        }

        payment.MarkAuthorized(authorization.Id, authorization.Status, authorization.ExpirationTime);
        order.MarkPaymentAuthorized();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentDomainException($"Order {orderId} has no payment; it must be paid before it can be fulfilled.");
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            // Idempotent replay: the money was already taken.
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentDomainException($"Order {orderId} is not paid; it must be paid before it can be fulfilled.");
        }

        var authorization = await _payPal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);

        if (authorization.Status is "CAPTURED" or "PARTIALLY_CAPTURED")
        {
            // Recovery: a previous fulfil captured at PayPal without us saving it.
            var capture = await TryFindExistingCaptureAsync(payment, cancellationToken);
            if (capture is not null)
            {
                CompleteCapture(payment, order, capture);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }
            return payment;
        }
        if (authorization.Status is "VOIDED" or "DENIED")
        {
            throw new PaymentDomainException(
                $"The PayPal hold for order {orderId} is {authorization.Status.ToLowerInvariant()} and cannot be captured. " +
                "Cancel this order and ask the shopper to pay again.");
        }

        if (authorization.ExpirationTime.HasValue && authorization.ExpirationTime.Value <= DateTimeOffset.UtcNow)
        {
            // The hold went stale before fulfilment: renew it rather than failing outright.
            try
            {
                authorization = await _payPal.ReauthorizeAsync(payment.AuthorizationId, payment.Amount, payment.Currency,
                    $"eshop-{payment.CorrelationId}-reauthorize-{payment.AuthorizationRenewals}", cancellationToken);
                payment.MarkAuthorizationRenewed(authorization.Status, authorization.ExpirationTime);
            }
            catch (PayPalApiException ex) when (ex.StatusCode == 422 || ex.StatusCode == 400)
            {
                throw new PaymentDomainException(
                    $"The PayPal authorization for order {orderId} expired and can no longer be renewed " +
                    $"(PayPal: {ex.Message}). Cancel this order and ask the shopper to pay again.");
            }
        }

        var captureResult = await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId, payment.Amount, payment.Currency,
            idempotencyKey: $"eshop-{payment.CorrelationId}-capture", cancellationToken);

        if (!string.Equals(captureResult.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentDomainException(
                $"PayPal did not complete the capture for order {orderId} (capture status: {captureResult.Status}). " +
                "Retry the fulfilment; the shopper has not been charged twice.");
        }

        CompleteCapture(payment, order, captureResult);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<Payment?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            // Never paid: nothing held at PayPal, just cancel the order.
            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return null;
        }
        if (payment.Status == PaymentStatus.Voided)
        {
            // Idempotent replay: the hold was already released.
            return payment;
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentDomainException(
                $"Order {orderId} has already been fulfilled and captured; issue a refund instead of cancelling.");
        }

        if (payment.AuthorizationId is not null)
        {
            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId,
                $"eshop-{payment.CorrelationId}-void", cancellationToken);
        }

        payment.MarkVoided();
        order.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, decimal? amount, string? noteToPayer, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null || payment.CaptureId is null)
        {
            throw new PaymentDomainException($"Order {orderId} has no captured payment to refund.");
        }
        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
        {
            throw new PaymentDomainException($"Order {orderId} has not been captured; only fulfilled orders can be refunded.");
        }

        // Idempotent replay under the caller-supplied key.
        foreach (var existing in payment.Refunds)
        {
            if (existing.IdempotencyKey == idempotencyKey)
            {
                return existing;
            }
        }

        var refundable = payment.RefundableAmount;
        if (refundable <= 0m)
        {
            throw new PaymentDomainException($"Order {orderId} has already been fully refunded.");
        }

        var refundAmount = amount.HasValue ? decimal.Round(amount.Value, 2) : refundable;
        if (refundAmount <= 0m)
        {
            throw new PaymentDomainException("Refund amount must be greater than zero.");
        }
        if (refundAmount > refundable)
        {
            throw new PaymentDomainException(
                $"Refund of {refundAmount:0.00} {payment.Currency} exceeds the remaining refundable amount of {refundable:0.00} {payment.Currency} for order {orderId}.");
        }

        // The PayPal request id is scoped to this payment so the same caller key on a different
        // order can never replay another capture's refund.
        var result = await _payPal.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency,
            noteToPayer, $"eshop-{payment.CorrelationId}-refund-{idempotencyKey}", cancellationToken);

        var refund = payment.AddRefund(result.Id, idempotencyKey, result.Amount, result.Status, noteToPayer);
        order.MarkRefunded(partial: payment.Status == PaymentStatus.PartiallyRefunded);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    private static string ReferenceFor(int orderId) => $"eshop-order-{orderId}";

    private void CompleteCapture(Payment payment, Order order, PayPalCaptureResult capture)
    {
        payment.MarkCaptured(capture.Id, capture.Status, capture.Amount,
            capture.FeeAmount, capture.NetAmount, capture.CreateTime ?? DateTimeOffset.UtcNow);
        order.MarkFulfilled();
    }

    private async Task<PayPalAuthorizationResult?> TryFindExistingAuthorizationAsync(string payPalOrderId, CancellationToken cancellationToken)
    {
        var details = await _payPal.GetOrderAsync(payPalOrderId, cancellationToken);
        foreach (var authorization in details.Authorizations)
        {
            if (authorization.Status is "CREATED" or "PENDING")
            {
                return authorization;
            }
        }
        return null;
    }

    private async Task<PayPalCaptureResult?> TryFindExistingCaptureAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (payment.PayPalOrderId is null)
        {
            return null;
        }
        var details = await _payPal.GetOrderAsync(payment.PayPalOrderId, cancellationToken);
        foreach (var capture in details.Captures)
        {
            if (capture.Status is "COMPLETED" or "PENDING")
            {
                return capture;
            }
        }
        return null;
    }
}
