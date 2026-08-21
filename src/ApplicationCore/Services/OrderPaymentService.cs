using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan AuthorizationMaxAge = TimeSpan.FromDays(30);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IPaymentSettings _paymentSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IPaymentSettings paymentSettings)
    {
        _orderRepository = orderRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _paymentSettings = paymentSettings;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        if ((card is null) == !paymentMethodId.HasValue)
        {
            throw new PaymentException(400, "Provide either card details or a saved paymentMethodId, not both.");
        }

        var order = await LoadOwnedOrder(orderId, buyerId, cancellationToken);

        if (order.PaymentStatus is OrderPaymentStatus.Authorized or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException(409, "This order has been cancelled and cannot be paid.");
        }

        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
        {
            throw new PaymentException(400, "Order total must be greater than zero.");
        }

        var currency = RequireCurrency();
        var payment = order.EnsurePayment();
        payment.EnsureAuthorizeRequestId(Guid.NewGuid().ToString("N"));
        await _orderRepository.UpdateAsync(order, cancellationToken);

        string? vaultId = null;
        if (paymentMethodId.HasValue)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdForBuyerSpec(paymentMethodId.Value, buyerId), cancellationToken);
            if (method is null)
            {
                throw new PaymentException(404, "Saved payment method was not found.");
            }

            vaultId = method.CardId;
        }

        AuthorizationResult result;
        if (vaultId is not null)
        {
            result = await _paymentGateway.AuthorizeVaultedCardAsync(
                order.Id, amount, currency, vaultId, payment.AuthorizeRequestId!, cancellationToken);
        }
        else
        {
            result = await _paymentGateway.AuthorizeCardAsync(
                order.Id, amount, currency, card!, payment.AuthorizeRequestId!, cancellationToken);
        }

        payment.ApplyAuthorization(
            result.PayPalOrderId,
            result.PayPalOrderStatus,
            result.AuthorizationId,
            result.AuthorizationStatus,
            result.ExpirationTime,
            result.CreateTime,
            currency);
        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrder(orderId, cancellationToken);

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException(409, "A cancelled order cannot be fulfilled.");
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.Payment?.AuthorizationId is null)
        {
            throw new PaymentException(409, "The order must be authorized before it can be fulfilled.");
        }

        var payment = order.Payment;
        var currency = payment.Currency ?? RequireCurrency();
        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);

        var snapshot = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        payment.ReplaceAuthorization(snapshot.AuthorizationId, snapshot.Status, snapshot.ExpirationTime);

        if (IsTerminalHold(snapshot.Status))
        {
            throw new PaymentException(409,
                $"The payment hold cannot be captured (status: {snapshot.Status}). Ask the shopper to pay again.");
        }

        if (IsStale(snapshot) && !AlreadyCaptured(snapshot.Status))
        {
            if (IsBeyondRenewalWindow(snapshot.CreateTime ?? payment.AuthorizationCreatedAt))
            {
                throw new PaymentException(409,
                    "The payment hold has expired and can no longer be renewed. Ask the shopper to authorize a new payment.");
            }

            try
            {
                var renewed = await _paymentGateway.ReauthorizeAsync(
                    payment.AuthorizationId,
                    amount,
                    currency,
                    Guid.NewGuid().ToString("N"),
                    cancellationToken);
                payment.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }
            catch (PaymentException ex) when (ex.StatusCode is 409 or 422)
            {
                throw new PaymentException(409,
                    "The payment hold could not be renewed. " + ex.Message +
                    " Ask the shopper to authorize a new payment.");
            }
        }

        if (AlreadyCaptured(payment.AuthorizationStatus) && payment.CaptureId is not null)
        {
            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }

        payment.EnsureCaptureRequestId(Guid.NewGuid().ToString("N"));
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var capture = await _paymentGateway.CaptureAsync(
            payment.AuthorizationId!,
            amount,
            currency,
            payment.CaptureRequestId!,
            cancellationToken);

        payment.ApplyCapture(
            capture.CaptureId,
            capture.CaptureStatus,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetAmount,
            null);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrder(orderId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return order;
        }

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (order.Payment?.AuthorizationId is not null
            && !string.Equals(order.Payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            var payment = order.Payment;
            payment.EnsureVoidRequestId(Guid.NewGuid().ToString("N"));
            await _orderRepository.UpdateAsync(order, cancellationToken);
            await _paymentGateway.VoidAsync(payment.AuthorizationId, payment.VoidRequestId!, cancellationToken);
            payment.ApplyVoid("VOIDED", "VOIDED");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await LoadOwnedOrder(orderId, buyerId, cancellationToken);

        if (order.PaymentStatus is not OrderPaymentStatus.Fulfilled and not OrderPaymentStatus.PartiallyRefunded
            and not OrderPaymentStatus.Refunded)
        {
            throw new PaymentException(409, "An order can only be refunded after it has been fulfilled.");
        }

        var payment = order.Payment ?? throw new PaymentException(409, "This order has no captured payment to refund.");
        if (string.IsNullOrEmpty(payment.CaptureId) || payment.CapturedAmount is null)
        {
            throw new PaymentException(409, "This order has no captured payment to refund.");
        }

        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (string.Equals(payment.CaptureStatus, "REFUNDED", StringComparison.OrdinalIgnoreCase)
            || order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new PaymentException(409, "This capture has already been fully refunded.");
        }

        var remaining = decimal.Round(payment.RemainingRefundable(), 2, MidpointRounding.AwayFromZero);
        if (remaining <= 0)
        {
            throw new PaymentException(409, "There is no remaining captured amount to refund.");
        }

        decimal refundAmount;
        if (amount is null)
        {
            refundAmount = remaining;
        }
        else
        {
            refundAmount = decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero);
            if (refundAmount <= 0)
            {
                throw new PaymentException(400, "Refund amount must be greater than zero.");
            }

            if (refundAmount > remaining)
            {
                throw new PaymentException(400,
                    $"Refund amount {refundAmount} exceeds the remaining captured amount {remaining}.");
            }
        }

        var currency = payment.Currency ?? RequireCurrency();
        var result = await _paymentGateway.RefundAsync(
            payment.CaptureId,
            refundAmount,
            currency,
            idempotencyKey,
            cancellationToken);

        var refund = new OrderRefund(result.RefundId, idempotencyKey, result.Amount, result.Status ?? "COMPLETED");
        payment.AddRefund(refund);

        var stillRemaining = decimal.Round(payment.RemainingRefundable(), 2, MidpointRounding.AwayFromZero);
        order.MarkRefunded(stillRemaining <= 0);
        if (stillRemaining <= 0)
        {
            payment.ApplyCapture(
                payment.CaptureId,
                "REFUNDED",
                payment.CapturedAmount.Value,
                payment.PaypalFee,
                payment.NetAmount,
                payment.PayPalOrderStatus);
        }
        else
        {
            payment.ApplyCapture(
                payment.CaptureId,
                "PARTIALLY_REFUNDED",
                payment.CapturedAmount.Value,
                payment.PaypalFee,
                payment.NetAmount,
                payment.PayPalOrderStatus);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new BuyerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public Task<Order?> GetBuyerOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        return LoadOwnedOrderOrDefault(orderId, buyerId, cancellationToken);
    }

    private async Task<Order> LoadOwnedOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await LoadOwnedOrderOrDefault(orderId, buyerId, cancellationToken);
        if (order is null)
        {
            throw new PaymentException(404, "Order was not found.");
        }

        return order;
    }

    private async Task<Order?> LoadOwnedOrderOrDefault(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }

        return order;
    }

    private async Task<Order> LoadOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException(404, "Order was not found.");
        }

        return order;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_paymentSettings.Currency))
        {
            throw new PaymentException(500, "PayPal:Currency is not configured.");
        }

        return _paymentSettings.Currency.Trim().ToUpperInvariant();
    }

    private static bool IsStale(AuthorizationSnapshot snapshot)
    {
        return snapshot.ExpirationTime is { } expiry && expiry <= DateTimeOffset.UtcNow;
    }

    private static bool IsBeyondRenewalWindow(DateTimeOffset? createdAt)
    {
        if (createdAt is null)
        {
            return false;
        }

        return DateTimeOffset.UtcNow - createdAt.Value.ToUniversalTime() >= AuthorizationMaxAge;
    }

    private static bool IsTerminalHold(string? status)
    {
        return status is not null && status.ToUpperInvariant() is "VOIDED" or "DENIED";
    }

    private static bool AlreadyCaptured(string? status)
    {
        return status is not null && status.ToUpperInvariant() is "CAPTURED" or "PARTIALLY_CAPTURED";
    }
}
