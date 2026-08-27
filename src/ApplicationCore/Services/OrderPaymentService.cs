using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // PayPal recommends capturing within the 3-day honor period; an
    // authorization stays valid for 29 days and can be reauthorized in between.
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<PaymentRefund> _refundRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _gateway;
    private readonly PayPalSettings _settings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<PaymentRefund> refundRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway gateway,
        IOptions<PayPalSettings> settings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _refundRepository = refundRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _settings = settings.Value;
    }

    public async Task<Payment> AuthorizePaymentAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null && savedCardId is null)
        {
            throw new PaymentConflictException("Provide either card details or a saved card to pay with.");
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), cancellationToken);

        if (order.Status == OrderStatus.PaymentAuthorized && payment?.Status == PaymentStatus.Authorized)
        {
            // Idempotent replay: the hold already exists, do not authorize twice.
            return payment;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentConflictException($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        var amount = order.Total();
        if (amount <= 0)
        {
            throw new PaymentConflictException($"Order {orderId} has a zero total and cannot be paid.");
        }

        string? vaultTokenId = null;
        if (savedCardId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId.Value, cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new SavedCardNotFoundException(savedCardId.Value);
            }
            vaultTokenId = savedCard.VaultTokenId;
        }

        payment ??= await _paymentRepository.AddAsync(
            new Payment(order.Id, buyerId, amount, _settings.Currency), cancellationToken);

        // Deterministic per payment record and attempt: retries of the same
        // attempt replay at PayPal, while a fresh attempt (or a fresh database)
        // never collides with a request id PayPal has already seen.
        var requestId = $"eshop-pay-{payment.Id}-{payment.CreatedAt.Ticks}-{payment.AuthorizationAttempts + 1}";

        try
        {
            var authorization = vaultTokenId is not null
                ? await _gateway.AuthorizeVaultedCardAsync(amount, _settings.Currency, vaultTokenId,
                    order.Id.ToString(), requestId, cancellationToken)
                : await _gateway.AuthorizeCardAsync(amount, _settings.Currency, card!,
                    order.Id.ToString(), requestId, cancellationToken);

            payment.MarkAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId,
                authorization.Status, authorization.CreatedAt, authorization.ExpiresAt);
            order.MarkPaymentAuthorized();
        }
        catch (PaymentGatewayException)
        {
            payment.MarkAuthorizationFailed();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw;
        }

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<Payment> CapturePaymentForFulfilmentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), cancellationToken);

        if (order.Status == OrderStatus.Fulfilled &&
            payment is not null &&
            (payment.Status == PaymentStatus.Captured ||
             payment.Status == PaymentStatus.PartiallyRefunded ||
             payment.Status == PaymentStatus.Refunded))
        {
            // Idempotent replay: the money was already taken.
            return payment;
        }

        if (order.Status != OrderStatus.PaymentAuthorized ||
            payment is null || payment.Status != PaymentStatus.Authorized ||
            payment.AuthorizationId is null)
        {
            throw new PaymentConflictException(
                $"Order {orderId} is {order.Status} and has no active authorization to capture.");
        }

        var now = DateTimeOffset.UtcNow;

        if (payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= now)
        {
            // Past PayPal's 29-day authorization validity: the hold is gone for good.
            payment.MarkAuthorizationExpired();
            order.MarkAwaitingPayment();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw new PaymentConflictException(
                $"The PayPal authorization for order {orderId} has expired and can no longer be renewed. " +
                "The order was moved back to AwaitingPayment; ask the shopper to pay again, then fulfil.");
        }

        if (payment.AuthorizedAt.HasValue && now - payment.AuthorizedAt.Value > HonorPeriod)
        {
            // Stale but still renewable: reauthorize to restart the honor period,
            // then capture against the new authorization id.
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId,
                payment.AuthorizedAmount, payment.Currency,
                $"eshop-reauthorize-{payment.AuthorizationId}",
                cancellationToken);
            payment.MarkReauthorized(reauth.AuthorizationId, reauth.Status, reauth.CreatedAt, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        var capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId!,
            payment.AuthorizedAmount, payment.Currency,
            $"eshop-capture-{payment.AuthorizationId}",
            cancellationToken);

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent replay
        }

        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentConflictException(
                $"Order {orderId} is already fulfilled; issue a refund instead of cancelling.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), cancellationToken);

        if (order.Status == OrderStatus.PaymentAuthorized &&
            payment?.Status == PaymentStatus.Authorized &&
            payment.AuthorizationId is not null)
        {
            // Release the hold; no money ever moves.
            await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<PaymentRefund> RefundPaymentAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = await _refundRepository.FirstOrDefaultAsync(
            new PaymentRefundByIdempotencyKeySpecification(idempotencyKey), cancellationToken);
        if (existing is not null)
        {
            // Replay under the same key: return the original refund, never refund twice.
            var existingPayment = await _paymentRepository.GetByIdAsync(existing.PaymentId, cancellationToken);
            if (existingPayment is null || existingPayment.BuyerId != buyerId || existingPayment.OrderId != orderId)
            {
                throw new PaymentConflictException(
                    "The idempotency key has already been used for a different refund.");
            }
            return existing;
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.Status != OrderStatus.Fulfilled)
        {
            throw new PaymentConflictException(
                $"Order {orderId} is {order.Status}; only fulfilled orders can be refunded.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), cancellationToken);

        if (payment?.CaptureId is null)
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
        }

        var remaining = payment.RefundableAmount;
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0 || refundAmount > remaining)
        {
            throw new PaymentConflictException(
                $"Refund amount {refundAmount:0.00} exceeds the refundable balance {remaining:0.00} " +
                $"of the captured {payment.CapturedAmount:0.00} {payment.Currency}.");
        }

        var result = await _gateway.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency,
            $"eshop-refund-{idempotencyKey}", cancellationToken);

        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return refund;
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            // Same response whether the order does not exist or belongs to another shopper.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }
}
