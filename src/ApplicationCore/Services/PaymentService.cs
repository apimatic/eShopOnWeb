using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PayPalOptions _payPalOptions;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IPaymentGateway paymentGateway,
        PayPalOptions payPalOptions,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _paymentGateway = paymentGateway;
        _payPalOptions = payPalOptions;
        _logger = logger;
    }

    public async Task<Payment> PayAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order == null || order.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

        // Idempotent replay: the hold is already in place, so a repeated pay returns it unchanged.
        if (order.Status == OrderStatus.PaymentAuthorized && payment?.Status == PaymentStatus.Authorized)
        {
            return payment;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentStateConflictException($"Order {orderId} in state {order.Status} cannot be paid.");
        }

        string? vaultPaymentTokenId = null;
        if (savedPaymentMethodId.HasValue)
        {
            var method = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpec(savedPaymentMethodId.Value), ct);
            if (method == null || method.BuyerId != buyerId)
            {
                throw new ResourceNotFoundException($"Payment method {savedPaymentMethodId.Value} was not found.");
            }
            vaultPaymentTokenId = method.VaultPaymentTokenId;
        }

        if (payment == null)
        {
            payment = new Payment(order.Id, buyerId, order.Total(), _payPalOptions.Currency);
            await _paymentRepository.AddAsync(payment, ct);
        }
        else if (payment.Status == PaymentStatus.Declined || payment.Status == PaymentStatus.Voided)
        {
            payment.ResetForRetry();
            await _paymentRepository.UpdateAsync(payment, ct);
        }
        // A Pending payment keeps its persisted idempotency keys, so resuming after a transport
        // failure re-sends the same keys and PayPal de-duplicates instead of authorizing twice.

        var result = await _paymentGateway.AuthorizeAsync(new AuthorizePaymentRequest(
            order.Id,
            payment.Amount,
            payment.Currency,
            card,
            vaultPaymentTokenId,
            payment.PayPalOrderId,
            payment.CreateRequestKey,
            payment.AuthorizeRequestKey), ct);

        if (result.DeclineReason != null)
        {
            payment.MarkDeclined(result.DeclineReason);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentDeclinedException($"The card was declined: {result.DeclineReason}");
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
        order.MarkPaymentAuthorized();
        await _paymentRepository.UpdateAsync(payment, ct);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Order {orderId} authorized (authorization {authorizationId})", order.Id, payment.AuthorizationId);
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order == null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

        // Idempotent replay: the money was already taken.
        if (order.Status == OrderStatus.Fulfilled && payment != null &&
            payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }
        if (order.Status != OrderStatus.PaymentAuthorized ||
            payment == null || payment.Status != PaymentStatus.Authorized || payment.AuthorizationId == null)
        {
            throw new PaymentStateConflictException($"Order {orderId} is not awaiting fulfilment of an authorized payment.");
        }

        // Renew a stale authorization rather than failing the fulfilment outright.
        var authorization = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId, ct);
        var stale = authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow;
        if (stale || !string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            AuthorizationResult renewed;
            try
            {
                renewed = await _paymentGateway.ReauthorizeAsync(
                    payment.AuthorizationId, payment.Amount, payment.Currency,
                    payment.EnsureReauthorizeRequestKey(), ct);
            }
            catch (PaymentGatewayException ex) when (ex.ProviderStatusCode is >= 400 and < 500)
            {
                throw new AuthorizationNotRenewableException(
                    $"The authorization for order {orderId} has gone stale and can no longer be renewed; " +
                    $"ask the shopper to pay again so a new authorization can be created. PayPal reported: {ex.Message}");
            }
            payment.MarkAuthorizationRenewed(renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        // Persist the capture key before the money moves so a retried fulfil reuses it.
        var captureKey = payment.EnsureCaptureRequestKey();
        await _paymentRepository.UpdateAsync(payment, ct);

        var capture = await _paymentGateway.CaptureAsync(payment.AuthorizationId, captureKey, ct);
        if (string.Equals(capture.Status, "DECLINED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(capture.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException($"PayPal could not capture the payment for order {orderId}; capture status was {capture.Status}.");
        }

        payment.MarkCaptured(capture.CaptureId, capture.Amount, capture.Fee, capture.NetAmount, capture.Status);
        order.MarkFulfilled();
        await _paymentRepository.UpdateAsync(payment, ct);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Order {orderId} fulfilled; captured {amount} {currency} (capture {captureId})",
            order.Id, capture.Amount, capture.Currency, capture.CaptureId);
        return payment;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order == null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        // A fulfilled order can never be cancelled; guard before anything is voided.
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentStateConflictException($"Order {orderId} has already been fulfilled and cannot be cancelled; issue a refund instead.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        if (payment != null && payment.Status == PaymentStatus.Authorized && payment.AuthorizationId != null)
        {
            var voidKey = payment.EnsureVoidRequestKey();
            await _paymentRepository.UpdateAsync(payment, ct);
            await _paymentGateway.VoidAsync(payment.AuthorizationId, voidKey, ct);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Order {orderId} cancelled; authorization {authorizationId} voided", order.Id, payment.AuthorizationId);
        }
        else if (payment != null && payment.Status == PaymentStatus.Pending)
        {
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentStateConflictException("An idempotency key is required for a refund.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order == null || order.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        if (payment == null || payment.CaptureId == null ||
            payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentStateConflictException($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotent replay under the caller-supplied key.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        // Validates state and that the refund stays within what was captured.
        var refund = payment.AddRefund(idempotencyKey, refundAmount);

        // The caller's key is also the provider idempotency key, so if this call's outcome is lost
        // to a transport failure, the retry re-sends the same key and PayPal de-duplicates.
        var result = await _paymentGateway.RefundAsync(payment.CaptureId, refundAmount, payment.Currency, idempotencyKey, noteToPayer, ct);

        if (string.Equals(result.Status, PaymentRefundStatus.Failed, StringComparison.OrdinalIgnoreCase))
        {
            refund.MarkFailed(result.RefundId);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentStateConflictException($"PayPal could not complete the refund for order {orderId}.");
        }

        refund.MarkSettled(result.RefundId, result.Status);
        payment.ApplyRefundedStatus();
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {orderId} refunded {amount} {currency} (refund {refundId})",
            order.Id, refund.Amount, payment.Currency, refund.PayPalRefundId);
        return refund;
    }
}
