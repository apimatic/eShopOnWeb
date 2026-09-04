using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _gateway;
    private readonly PayPalOptions _options;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(IRepository<Order> orderRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway gateway,
        PayPalOptions options,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _options = options;
        _logger = logger;
    }

    public async Task<AuthorizeOrderResult> AuthorizeOrderAsync(int orderId, string buyerId, CardDetails? card, int? savedCardId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithPaymentSpec(orderId));
        if (order is null) throw new NotFoundException($"Order {orderId} not found.");
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("Order belongs to another shopper.");

        if (order.Status == OrderStatus.Fulfilled || order.Status == OrderStatus.Refunded)
            throw new PaymentStateException($"Order {orderId} is already fulfilled and cannot be paid.");

        // Idempotent: an authorization already in place is returned as-is (never authorize twice).
        if (order.Payment is not null && !string.IsNullOrEmpty(order.Payment.AuthorizationId))
            return ToAuthorizeResult(order);

        var amount = order.Total();
        var currency = _options.Currency;

        var cardSource = await BuildCardSourceAsync(buyerId, card, savedCardId);

        // custom_id stays a stable, mappable reference (used by reconciliation). invoice_id
        // is unique per order instance so PayPal's duplicate-invoice check can never reject
        // a retry of the same order (e.g. after the in-memory store resets).
        var customId = $"eshop-order-{order.Id}";
        var invoiceId = $"eshop-order-{order.Id}-{order.OrderDate.UtcTicks}";
        // Stable for the lifetime of this order instance (idempotent under double-click), yet
        // distinct from a previous instance of the same order id after an in-memory reset.
        var requestId = $"eshop-pay-{order.Id}-{order.OrderDate.UtcTicks}";
        var result = await _gateway.CreateOrderAndAuthorizeAsync(customId, invoiceId, amount, currency, cardSource, requestId);

        var payment = order.Payment ?? new OrderPayment(order.Id, result.PayPalOrderId, result.AuthorizationId,
            result.AuthorizationStatus, result.ExpirationTime, amount, currency,
            DescribeSource(cardSource), savedCardId);

        if (order.Payment is null)
        {
            payment.SetAuthorized(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus, result.ExpirationTime);
            order.SetPayment(payment);
        }
        else
        {
            order.Payment.SetAuthorized(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus, result.ExpirationTime);
        }

        order.MarkPaid();
        await _orderRepository.UpdateAsync(order);

        return ToAuthorizeResult(order);
    }

    public async Task<CaptureOrderResult> CaptureOrderAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithPaymentSpec(orderId));
        if (order is null) throw new NotFoundException($"Order {orderId} not found.");

        // Idempotent: already captured.
        if (order.Status == OrderStatus.Fulfilled && order.Payment?.CaptureId is not null)
            return ToCaptureResult(order);

        if (order.Status != OrderStatus.Paid || order.Payment is null || string.IsNullOrEmpty(order.Payment.AuthorizationId))
            throw new PaymentStateException($"Order {orderId} is not paid; there is no authorization to capture.");

        var payment = order.Payment;
        var amount = payment.Amount;
        var currency = payment.Currency;

        try
        {
            await CaptureAsync(order, payment, amount, currency);
        }
        catch (PayPalApiException ex) when (ex.HasIssue("AUTHORIZATION_EXPIRED"))
        {
            _logger.LogWarning($"Authorization for order {orderId} expired; attempting to reauthorize before capture. ({ex.ErrorName})");
            try
            {
                var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId, amount, currency);
                payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpirationTime);
                await CaptureAsync(order, payment, amount, currency);
            }
            catch (PayPalApiException renewEx)
            {
                throw new PaymentRenewalFailedException(
                    $"The payment authorization for order {orderId} has expired and PayPal can no longer renew it " +
                    $"({renewEx.ErrorName}). Re-collect payment from the shopper by calling POST /api/orders/{orderId}/pay again.",
                    renewEx.ErrorName);
            }
        }

        await _orderRepository.UpdateAsync(order);
        return ToCaptureResult(order);
    }

    public async Task<VoidOrderResult> CancelOrderAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithPaymentSpec(orderId));
        if (order is null) throw new NotFoundException($"Order {orderId} not found.");

        if (order.Status == OrderStatus.Cancelled)
            return new VoidOrderResult(order.Id, order.Status.ToString(), order.Payment?.AuthorizationId);

        if (order.Status == OrderStatus.Fulfilled || order.Status == OrderStatus.Refunded)
            throw new PaymentStateException($"Order {orderId} is already fulfilled; refund it instead of cancelling.");

        var payment = order.Payment;
        if (payment is not null && !string.IsNullOrEmpty(payment.AuthorizationId) && payment.CaptureId is null)
        {
            var voidResult = await _gateway.VoidAuthorizationAsync(payment.AuthorizationId);
            payment.MarkAuthorizationVoided(voidResult.Status);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);

        return new VoidOrderResult(order.Id, order.Status.ToString(), payment?.AuthorizationId);
    }

    public async Task<RefundOrderResult> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentStateException("An idempotencyKey is required for refunds.");

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithPaymentSpec(orderId));
        if (order is null) throw new NotFoundException($"Order {orderId} not found.");

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.Refunded)
            throw new PaymentStateException($"Order {orderId} is not fulfilled; only fulfilled orders can be refunded.");

        var payment = order.Payment;
        if (payment?.CaptureId is null)
            throw new PaymentStateException($"Order {orderId} has no captured payment to refund.");

        // Idempotent under the caller-supplied key: repeating the same request never refunds twice.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
            return ToRefundResult(order, existing);

        var captured = payment.CapturedAmount ?? payment.Amount;
        var refundable = captured - payment.TotalRefundedAmount;
        if (refundable <= 0m)
            throw new PaymentStateException($"Order {orderId} has nothing left to refund (captured {captured}).");

        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0m)
            throw new PaymentStateException("Refund amount must be positive.");
        if (refundAmount > refundable)
            throw new PaymentStateException($"Refund amount {refundAmount} exceeds the refundable balance {refundable}.");

        // Same-key idempotency is enforced at the database (above), so the PayPal request id is
        // scoped to the capture to avoid PayPal rejecting a caller-supplied key that was reused
        // against a different capture within its idempotency window.
        var requestId = $"eshop-refund-{payment.CaptureId}-{SanitizeForHeader(idempotencyKey)}";
        var refund = await _gateway.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency, requestId);
        var refundEntity = new PaymentRefund(payment.Id, refund.RefundId, refund.Amount, refund.Currency, idempotencyKey, refund.Status);
        payment.AddRefund(refundEntity);

        if (payment.TotalRefundedAmount >= captured)
            order.MarkFullyRefunded();

        await _orderRepository.UpdateAsync(order);
        return ToRefundResult(order, refundEntity);
    }

    private async Task<PayPalCardSource> BuildCardSourceAsync(string buyerId, CardDetails? card, int? savedCardId)
    {
        if (savedCardId.HasValue)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpec(savedCardId.Value));
            if (saved is null) throw new NotFoundException($"Saved card {savedCardId} not found.");
            if (!string.Equals(saved.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
                throw new ForbiddenException("Saved card belongs to another shopper.");

            return new PayPalCardSource { VaultId = saved.PayPalTokenId, SavedCardId = saved.Id };
        }

        if (card is not null)
            return new PayPalCardSource { Card = card };

        throw new PaymentStateException("Either card details or a saved payment method id must be provided.");
    }

    private async Task CaptureAsync(Order order, OrderPayment payment, decimal amount, string currency)
    {
        var requestId = $"eshop-capture-{order.Id}-{payment.AuthorizationId}";
        var capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId, amount, currency, requestId);
        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.CapturedAmount,
            capture.PayPalFee ?? 0m, capture.NetAmount ?? 0m);
        order.MarkFulfilled();
    }

    private static string DescribeSource(PayPalCardSource source)
    {
        if (source.IsSavedCard)
        {
            return "Saved card";
        }

        var card = source.Card;
        if (card is null) return "Card";
        var last4 = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
        return $"Card ending {last4}";
    }

    private static string SanitizeForHeader(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-');
        }
        return builder.ToString();
    }

    private static AuthorizeOrderResult ToAuthorizeResult(Order order)
    {
        var payment = order.Payment;
        return new AuthorizeOrderResult(order.Id, order.Status.ToString(),
            payment?.AuthorizationStatus ?? "NONE",
            payment?.Amount ?? 0m,
            payment?.Currency ?? string.Empty,
            payment?.AuthorizationId,
            payment?.PaymentSourceDescription ?? string.Empty);
    }

    private static CaptureOrderResult ToCaptureResult(Order order)
    {
        var payment = order.Payment;
        return new CaptureOrderResult(order.Id, order.Status.ToString(),
            payment?.CaptureId,
            payment?.CaptureStatus,
            payment?.CapturedAmount,
            payment?.PayPalFee,
            payment?.NetAmount,
            payment?.Currency);
    }

    private static RefundOrderResult ToRefundResult(Order order, PaymentRefund refund)
    {
        var payment = order.Payment!;
        return new RefundOrderResult(order.Id, refund.Id, refund.PayPalRefundId, refund.Amount,
            payment.TotalRefundedAmount, payment.CapturedAmount ?? payment.Amount, refund.Currency,
            order.Status.ToString());
    }
}