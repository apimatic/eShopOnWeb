using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    public static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPal)
    {
        _orderRepository = orderRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        EnsureBuyer(order, buyerId);

        if (order.Status == OrderStatus.Authorized && order.Payment != null)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException(409, "A cancelled order cannot be paid.");
        }

        if (card == null && paymentMethodId == null)
        {
            throw new PaymentException(400, "Provide card details or a saved paymentMethodId.");
        }

        if (card != null && paymentMethodId != null)
        {
            throw new PaymentException(400, "Provide either card details or a saved paymentMethodId, not both.");
        }

        string? vaultId = null;
        if (paymentMethodId != null)
        {
            var saved = await _paymentMethodRepository.GetByIdAsync(paymentMethodId.Value, cancellationToken);
            if (saved == null || !saved.BelongsTo(buyerId))
            {
                throw new PaymentException(404, "Saved payment method was not found.");
            }

            vaultId = saved.PayPalPaymentTokenId;
        }

        var currency = _payPal.Currency;
        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        var invoiceId = $"eShop-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}";
        var result = await _payPal.AuthorizeCardAsync(new AuthorizePaymentRequest
        {
            InvoiceId = invoiceId,
            Currency = currency,
            Amount = amount,
            RequestId = PaymentRequestId(order, "pay"),
            Card = card,
            VaultId = vaultId
        }, cancellationToken);

        order.RecordAuthorization(new OrderPayment(
            result.PayPalOrderId,
            invoiceId,
            result.AuthorizationId,
            result.AuthorizationStatus,
            result.CreateTime ?? DateTimeOffset.UtcNow,
            result.ExpirationTime,
            currency));

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || order.Payment == null)
        {
            throw new PaymentException(409, "An order must be authorized before it can be fulfilled.");
        }

        var payment = order.Payment;
        var currency = payment.Currency;
        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        var authorizationId = await EnsureFreshAuthorization(order, amount, currency, cancellationToken);
        var fulfilRequestId = PaymentRequestId(order, "fulfil");

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                currency,
                amount,
                fulfilRequestId,
                cancellationToken);
        }
        catch (PaymentException ex) when (IsStaleAuthorization(ex))
        {
            authorizationId = await RenewAuthorization(order, amount, currency, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                currency,
                amount,
                fulfilRequestId,
                cancellationToken);
        }

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PayPalFee,
            capture.NetAmount);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetRequiredOrder(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status == OrderStatus.Authorized && order.Payment != null)
        {
            await VoidHold(order.Payment, PaymentRequestId(order, "cancel"), cancellationToken);
        }

        order.RecordCancellation();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(400, "A refund idempotency key is required.");
        }

        var order = await GetRequiredOrder(orderId, cancellationToken);
        EnsureBuyer(order, buyerId);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (order.Payment?.CaptureId == null)
        {
            throw new PaymentException(409, "This order has no captured payment to refund.");
        }

        var remaining = order.RemainingRefundableAmount();
        var refundAmount = amount.HasValue
            ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
            : remaining;

        if (refundAmount <= 0)
        {
            throw new PaymentException(400, remaining <= 0
                ? "This order has already been fully refunded."
                : "Refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException(400,
                $"Refund amount {refundAmount:0.00} exceeds the remaining refundable amount {remaining:0.00}.");
        }

        var result = await _payPal.RefundCaptureAsync(
            order.Payment.CaptureId,
            order.Payment.Currency,
            refundAmount,
            idempotencyKey,
            cancellationToken);

        var refund = order.RecordRefund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    private async Task<string> EnsureFreshAuthorization(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var payment = order.Payment!;
        var authorizationId = payment.AuthorizationId;

        PayPalAuthorizationDetails? details = null;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException)
        {
            // Capture will surface a precise PayPal error if the hold is no longer usable.
        }

        if (details != null && string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(409,
                "PayPal reports this authorization as voided, so the hold cannot be captured. Ask the shopper to pay again.");
        }

        var stale = payment.HonorPeriodElapsed(AuthorizationHonorPeriod)
            || payment.IsExpired
            || (details != null && details.ExpirationTime.HasValue && details.ExpirationTime.Value <= DateTimeOffset.UtcNow);

        if (stale)
        {
            return await RenewAuthorization(order, amount, currency, cancellationToken);
        }

        return authorizationId;
    }

    private async Task<string> RenewAuthorization(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var payment = order.Payment!;
        PayPalAuthorizationResult renewed;
        try
        {
            renewed = await _payPal.ReauthorizeAsync(
                payment.OriginalAuthorizationId,
                currency,
                amount,
                PaymentRequestId(order, "reauth"),
                cancellationToken);
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(409,
                "PayPal could not renew the authorization. The hold may be older than 29 days or can no longer be reused. Ask the shopper to pay again before fulfilling. " +
                ex.Message);
        }

        order.RecordReauthorization(
            renewed.AuthorizationId,
            renewed.AuthorizationStatus,
            renewed.CreateTime ?? DateTimeOffset.UtcNow,
            renewed.ExpirationTime);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return renewed.AuthorizationId;
    }

    private async Task VoidHold(OrderPayment payment, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, requestId, cancellationToken);
        }
        catch (PaymentException)
        {
            if (!string.Equals(payment.AuthorizationId, payment.OriginalAuthorizationId, StringComparison.Ordinal))
            {
                await _payPal.VoidAuthorizationAsync(payment.OriginalAuthorizationId, requestId + "-original", cancellationToken);
                return;
            }

            throw;
        }
    }

    private async Task<Order> GetRequiredOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithPaymentSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private static void EnsureBuyer(Order order, string buyerId)
    {
        if (!order.BelongsTo(buyerId))
        {
            throw new PaymentException(404, $"Order {order.Id} was not found.");
        }
    }

    private static string PaymentRequestId(Order order, string action) =>
        $"eshop-{action}-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}";

    private static bool IsStaleAuthorization(PaymentException ex)
    {
        var message = ex.Message;
        return message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("expired authorization", StringComparison.OrdinalIgnoreCase)
            || message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase);
    }
}
