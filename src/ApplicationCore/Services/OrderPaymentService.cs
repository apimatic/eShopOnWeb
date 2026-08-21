using System;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentSettings _settings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway gateway,
        IPaymentSettings settings)
    {
        _orderRepository = orderRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _settings = settings;
    }

    public async Task<Order> AuthorizeAsync(
        int orderId,
        string buyerId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken ct)
    {
        var order = await LoadOwnedOrder(orderId, buyerId, ct);

        if (order.PaymentStatus is OrderPaymentStatus.Authorized
            or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException(409, "A cancelled order cannot be paid. Place a new order.");
        }

        var hasCard = card != null;
        var hasSaved = paymentMethodId.HasValue;
        if (hasCard == hasSaved)
        {
            throw new PaymentException(400, "Provide either card details or a saved paymentMethodId, not both.");
        }

        var currency = _settings.Currency;
        var amount = PayPalMoney.Round(order.Total(), currency);
        if (amount <= 0)
        {
            throw new PaymentException(400, "The order total must be greater than zero.");
        }

        string? vaultId = null;
        if (hasSaved)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId!.Value, buyerId), ct);
            if (method == null)
            {
                throw new PaymentException(404, "Saved payment method was not found.");
            }

            vaultId = method.VaultId;
        }

        if (string.IsNullOrEmpty(order.PayPalOrderId))
        {
            var createRequestId = $"eshop-order-{order.Id}-create";
            var payPalOrderId = vaultId != null
                ? await _gateway.CreateOrderWithVaultIdAsync(order.Id, amount, currency, vaultId, createRequestId, ct)
                : await _gateway.CreateOrderWithCardAsync(order.Id, amount, currency, card!, createRequestId, ct);

            order.AttachPayPalOrder(payPalOrderId, null, currency);
            await _orderRepository.UpdateAsync(order, ct);
        }

        var result = await _gateway.AuthorizeExistingOrderAsync(
            order.PayPalOrderId!,
            $"eshop-order-{order.Id}-authorize",
            ct);

        if (result.RequiresPayerAction)
        {
            throw new PaymentException(409,
                "PayPal required a shopper approval challenge (3DS / payer-action). This integration does not implement a browser round-trip.");
        }

        var held = PayPalMoney.Round(result.HeldAmount, currency);
        if (held != amount)
        {
            throw new PaymentException(502,
                $"PayPal held {held} {currency} but the order total is {amount} {currency}.");
        }

        order.RecordAuthorization(
            result.PayPalOrderId,
            result.OrderStatus,
            result.AuthorizationId,
            result.AuthorizationStatus,
            result.ExpirationTime,
            currency);

        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrder(orderId, ct);

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException(409, "A cancelled order cannot be fulfilled.");
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized
            || string.IsNullOrEmpty(order.AuthorizationId))
        {
            throw new PaymentException(409, "The order has no authorized payment to capture.");
        }

        var currency = order.Currency ?? _settings.Currency;
        var amount = PayPalMoney.Round(order.Total(), currency);
        var authorizationId = order.AuthorizationId;

        if (order.IsAuthorizationStale(DateTimeOffset.UtcNow))
        {
            try
            {
                var renewed = await _gateway.ReauthorizeAsync(
                    authorizationId,
                    amount,
                    currency,
                    $"eshop-order-{order.Id}-reauthorize",
                    ct);
                order.ReplaceAuthorization(renewed.AuthorizationId, renewed.AuthorizationStatus, renewed.ExpirationTime);
                authorizationId = renewed.AuthorizationId;
                await _orderRepository.UpdateAsync(order, ct);
            }
            catch (PaymentException ex)
            {
                throw new PaymentException(
                    ex.StatusCode >= 400 && ex.StatusCode < 500 ? ex.StatusCode : 409,
                    "The authorization hold has expired and cannot be renewed. Ask the shopper to authorize a new payment. "
                    + ex.Message,
                    ex);
            }
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(
                authorizationId,
                amount,
                currency,
                order.Id.ToString(),
                $"eshop-order-{order.Id}-capture",
                ct);
        }
        catch (PaymentException ex) when (ex.StatusCode == 409 && !string.IsNullOrEmpty(order.CaptureId))
        {
            capture = await _gateway.GetCaptureAsync(order.CaptureId, ct);
        }

        if (capture.IsPending)
        {
            order.RecordCapture(
                capture.CaptureId,
                capture.Status,
                capture.CapturedAmount,
                paypalFee: null,
                netAmount: null,
                grossAmount: capture.GrossAmount);
            await _orderRepository.UpdateAsync(order, ct);
            throw new PaymentException(409,
                "PayPal accepted the capture but it is still pending. Fee and net proceeds are not available until the capture completes. Retry fulfilment shortly.");
        }

        var fee = capture.PaypalFee;
        var net = capture.NetAmount;
        var gross = capture.GrossAmount ?? capture.CapturedAmount;

        if ((fee == null || net == null) && !string.IsNullOrEmpty(capture.CaptureId))
        {
            var refreshed = await _gateway.GetCaptureAsync(capture.CaptureId, ct);
            fee ??= refreshed.PaypalFee;
            net ??= refreshed.NetAmount;
            gross = refreshed.GrossAmount ?? gross;
            capture = refreshed;
        }

        order.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, fee, net, gross);
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrder(orderId, ct);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return order;
        }

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (string.IsNullOrEmpty(order.AuthorizationId))
        {
            order.RecordVoid();
            await _orderRepository.UpdateAsync(order, ct);
            return order;
        }

        await _gateway.VoidAsync(order.AuthorizationId, $"eshop-order-{order.Id}-void", ct);
        order.RecordVoid();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(400, "A refund requires an idempotencyKey.");
        }

        var order = await LoadOwnedOrder(orderId, buyerId, ct);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (order.PaymentStatus is not OrderPaymentStatus.Fulfilled
            and not OrderPaymentStatus.PartiallyRefunded)
        {
            if (order.PaymentStatus == OrderPaymentStatus.Refunded)
            {
                throw new PaymentException(409, "This order has already been fully refunded.");
            }

            throw new PaymentException(409, "Refunds are only allowed after the order has been fulfilled.");
        }

        if (string.IsNullOrEmpty(order.CaptureId) || order.CapturedAmount is null)
        {
            throw new PaymentException(409, "This order has no captured payment to refund.");
        }

        var currency = order.Currency ?? _settings.Currency;
        var remaining = PayPalMoney.Round(order.RemainingRefundable(), currency);
        var refundAmount = amount.HasValue
            ? PayPalMoney.Round(amount.Value, currency)
            : remaining;

        if (refundAmount <= 0)
        {
            throw new PaymentException(400, "Refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException(400,
                $"Refund of {refundAmount} exceeds the remaining refundable amount of {remaining}.");
        }

        var result = await _gateway.RefundAsync(
            order.CaptureId,
            refundAmount,
            currency,
            idempotencyKey,
            ct);

        var refund = order.RecordRefund(result.RefundId, result.Status, result.Amount, idempotencyKey);
        await _orderRepository.UpdateAsync(order, ct);
        return refund;
    }

    private async Task<Order> LoadOwnedOrder(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await LoadOrder(orderId, ct);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException(403, "This order does not belong to the signed-in shopper.");
        }

        return order;
    }

    private async Task<Order> LoadOrder(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order == null)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        return order;
    }
}
