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

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // Unique per application run: keeps PayPal idempotency keys stable within a
    // run (double-click safe) without colliding with keys from previous runs.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly PayPalSettings _payPalSettings;

    public PaymentService(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPalGateway,
        PayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _payPalGateway = payPalGateway;
        _payPalSettings = payPalSettings;
    }

    public async Task<OrderPayment> AuthorizePaymentAsync(string buyerId, int orderId,
        PayPalCardDetails? card, int? paymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment != null && !string.IsNullOrEmpty(payment.AuthorizationId))
        {
            // Idempotent replay: the order is already authorized.
            return payment;
        }

        string? vaultTokenId = null;
        if (paymentMethodId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(paymentMethodId.Value, cancellationToken);
            if (savedCard == null || savedCard.BuyerId != buyerId)
            {
                throw new SavedCardNotFoundException(paymentMethodId.Value);
            }
            vaultTokenId = savedCard.VaultTokenId;
        }
        else if (card == null)
        {
            throw new PaymentConflictException("Provide either card details or a saved paymentMethodId to pay for the order.");
        }

        var currency = Currency;
        var total = order.Total();

        var payPalOrderId = payment?.PayPalOrderId;
        if (string.IsNullOrEmpty(payPalOrderId))
        {
            payPalOrderId = await _payPalGateway.CreateOrderAsync(total, currency,
                order.Id.ToString(), $"eshop-{RunId}-order-{order.Id}-create", cancellationToken);
        }

        var authorization = await _payPalGateway.AuthorizeOrderAsync(payPalOrderId, card, vaultTokenId,
            $"eshop-{RunId}-order-{order.Id}-authorize", cancellationToken);

        if (authorization.Amount != total)
        {
            throw new PaymentConflictException(
                $"PayPal authorized {authorization.Amount} {authorization.Currency} which does not match the order total of {total} {currency}. The hold was not accepted.");
        }

        if (payment == null)
        {
            payment = new OrderPayment(order.Id, buyerId, total, currency,
                payPalOrderId, authorization.AuthorizationId, authorization.Status, authorization.ExpirationTime);
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            payment.RenewAuthorization(authorization.AuthorizationId, authorization.Status, authorization.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<OrderPayment> CapturePaymentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment == null)
        {
            throw new PaymentConflictException($"Order {orderId} has not been paid yet; there is no authorization to capture.");
        }

        if (!string.IsNullOrEmpty(payment.CaptureId))
        {
            // Idempotent replay: the order is already captured.
            return payment;
        }

        var authorization = await _payPalGateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        var usable = (authorization.Status == "CREATED" || authorization.Status == "PENDING")
            && (authorization.ExpirationTime == null || authorization.ExpirationTime > DateTimeOffset.UtcNow);

        if (!usable)
        {
            // The hold has gone stale before fulfilment: renew it rather than failing.
            try
            {
                var renewed = await _payPalGateway.ReauthorizeAsync(payment.AuthorizationId,
                    payment.Amount, payment.Currency, $"eshop-{RunId}-order-{orderId}-reauthorize", cancellationToken);
                payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                throw new PaymentConflictException(
                    $"The PayPal authorization for order {orderId} is {authorization.Status} and can no longer be renewed " +
                    $"(PayPal error {ex.ErrorName ?? ex.StatusCode.ToString()}). Ask the shopper to pay for the order again.");
            }
        }

        var capture = await _payPalGateway.CaptureAuthorizationAsync(payment.AuthorizationId,
            payment.Amount, payment.Currency, $"eshop-{RunId}-order-{orderId}-capture", cancellationToken);

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException(orderId);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return; // Idempotent replay.
        }
        if (order.Status != OrderStatus.PendingPayment && order.Status != OrderStatus.AwaitingFulfillment)
        {
            throw new PaymentConflictException(
                $"Order {orderId} is {order.Status} and cannot be cancelled; use a refund for fulfilled orders.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment != null && !payment.AuthorizationVoided && string.IsNullOrEmpty(payment.CaptureId))
        {
            await _payPalGateway.VoidAuthorizationAsync(payment.AuthorizationId,
                $"eshop-{RunId}-order-{orderId}-void", cancellationToken);
            payment.MarkAuthorizationVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
    }

    public async Task<PaymentRefund> RefundPaymentAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment == null || string.IsNullOrEmpty(payment.CaptureId))
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
        }

        foreach (var existing in payment.Refunds)
        {
            if (existing.IdempotencyKey == idempotencyKey)
            {
                // Idempotent replay under the caller-supplied key.
                return existing;
            }
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0 || refundAmount > payment.RefundableAmount)
        {
            throw new PaymentConflictException(
                $"Refund of {refundAmount} {payment.Currency} exceeds the refundable balance of {payment.RefundableAmount} {payment.Currency} on order {orderId}.");
        }

        // Namespace the caller-supplied key with the run id: PayPal remembers
        // PayPal-Request-Id values across application runs, so a bare caller key
        // would collide with the same key used in a previous run.
        var result = await _payPalGateway.RefundCaptureAsync(payment.CaptureId, refundAmount,
            payment.Currency, noteToPayer, $"eshop-{RunId}-refund-{idempotencyKey}", cancellationToken);

        var refund = payment.AddRefund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        if (payment.TotalRefunded >= payment.CapturedAmount)
        {
            order.MarkRefunded();
        }
        else
        {
            order.MarkPartiallyRefunded();
        }
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return refund;
    }

    private string Currency => string.IsNullOrWhiteSpace(_payPalSettings.Currency)
        ? throw new InvalidOperationException("PayPal:Currency is not configured.")
        : _payPalSettings.Currency!;
}
