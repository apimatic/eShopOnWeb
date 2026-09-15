using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderGates = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentsGateway _payPal;
    private readonly IPaymentSettings _paymentSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Buyer> buyerRepository,
        IPayPalPaymentsGateway payPal,
        IPaymentSettings paymentSettings)
    {
        _orderRepository = orderRepository;
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _paymentSettings = paymentSettings;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        int? paymentMethodId,
        CardPaymentDetails? card,
        CancellationToken cancellationToken)
    {
        if (paymentMethodId is null && card is null)
        {
            throw new PaymentException("Provide card details or a saved paymentMethodId.", 400);
        }

        if (paymentMethodId is not null && card is not null)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.", 400);
        }

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetShopperOrder(orderId, buyerId, cancellationToken);

            if (order.PaymentStatus == OrderPaymentStatus.Authorized
                || order.PaymentStatus == OrderPaymentStatus.Fulfilled
                || order.PaymentStatus == OrderPaymentStatus.PartiallyRefunded
                || order.PaymentStatus == OrderPaymentStatus.Refunded)
            {
                return order;
            }

            if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            {
                throw new PaymentException("A cancelled order cannot be paid.", 409);
            }

            var amount = order.Total();
            if (amount <= 0m)
            {
                throw new PaymentException("The order total must be greater than zero.", 400);
            }

            var currency = _paymentSettings.Currency;
            var idempotencyKey = $"authorize-order-{order.Id}-{Guid.NewGuid():N}";

            PayPalAuthorizationResult result;
            if (paymentMethodId is int methodId)
            {
                var vaultId = await ResolveVaultToken(buyerId, methodId, cancellationToken);
                result = await _payPal.AuthorizeVaultedCardAsync(
                    order.Id, amount, currency, vaultId, idempotencyKey, cancellationToken);
            }
            else
            {
                result = await _payPal.AuthorizeCardAsync(
                    order.Id, amount, currency, card!, idempotencyKey, cancellationToken);
            }

            order.MarkAuthorized(
                result.PayPalOrderId,
                result.AuthorizationId,
                result.AuthorizationStatus,
                result.Expiration,
                currency,
                idempotencyKey);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrder(orderId, cancellationToken);

            if (order.PaymentStatus is OrderPaymentStatus.Fulfilled
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                return order;
            }

            if (order.PaymentStatus != OrderPaymentStatus.Authorized
                || string.IsNullOrEmpty(order.PayPalAuthorizationId))
            {
                throw new PaymentException(
                    $"Order {order.Id} cannot be fulfilled because it is {order.PaymentStatus}. Authorize payment first.",
                    409);
            }

            var authorizationId = await EnsureAuthorizationReadyToCapture(order, cancellationToken);
            var captureKey = $"capture-order-{order.Id}-{Guid.NewGuid():N}";
            var capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                InvoiceId(order.Id),
                captureKey,
                cancellationToken);

            order.MarkFulfilled(
                capture.CaptureId,
                capture.Status,
                capture.CapturedAmount,
                capture.PaypalFee,
                capture.NetAmount,
                captureKey);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrder(orderId, cancellationToken);

            if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            {
                return order;
            }

            if (order.PaymentStatus is OrderPaymentStatus.Fulfilled
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                throw new PaymentException(
                    "A fulfilled order cannot be cancelled. Issue a refund instead.",
                    409);
            }

            if (order.PaymentStatus == OrderPaymentStatus.Authorized
                && !string.IsNullOrEmpty(order.PayPalAuthorizationId))
            {
                var voidKey = $"void-order-{order.Id}-{Guid.NewGuid():N}";
                await _payPal.VoidAuthorizationAsync(order.PayPalAuthorizationId, voidKey, cancellationToken);
                order.MarkCancelled(voidKey);
            }
            else
            {
                order.MarkCancelled($"void-order-{order.Id}");
            }

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund idempotencyKey is required.", 400);
        }

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetShopperOrder(orderId, buyerId, cancellationToken);

            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
            {
                return existing;
            }

            if (order.PaymentStatus is not OrderPaymentStatus.Fulfilled
                and not OrderPaymentStatus.PartiallyRefunded)
            {
                throw new PaymentException(
                    $"Order {order.Id} cannot be refunded because it is {order.PaymentStatus}.",
                    409);
            }

            if (string.IsNullOrEmpty(order.PayPalCaptureId) || order.CapturedAmount is null)
            {
                throw new PaymentException("This order has no captured payment to refund.", 409);
            }

            var remaining = order.RefundableRemaining();
            if (remaining <= 0m)
            {
                throw new PaymentException("This order has already been refunded in full.", 409);
            }

            decimal refundAmount;
            if (amount is null)
            {
                refundAmount = remaining;
            }
            else
            {
                if (amount.Value <= 0m)
                {
                    throw new PaymentException("Refund amount must be greater than zero.", 400);
                }

                if (amount.Value > remaining)
                {
                    throw new PaymentException(
                        $"Refund of {amount.Value:0.00} exceeds the remaining refundable amount {remaining:0.00}.",
                        400);
                }

                refundAmount = amount.Value;
            }

            var currency = order.Currency ?? _paymentSettings.Currency;
            var payPalRequestId = $"refund-{order.Id}-{idempotencyKey}-{Guid.NewGuid():N}";
            var result = await _payPal.RefundCaptureAsync(
                order.PayPalCaptureId,
                refundAmount,
                currency,
                payPalRequestId,
                cancellationToken);

            var refund = order.RecordRefund(
                result.RefundId,
                idempotencyKey,
                result.Amount,
                result.Status ?? "COMPLETED");

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return refund;
        });
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    private async Task<string> EnsureAuthorizationReadyToCapture(Order order, CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        PayPalAuthorizationSnapshot snapshot;
        try
        {
            snapshot = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            throw Unrenewable(order, "PayPal no longer has this authorization (it was not found).", ex);
        }

        if (string.Equals(snapshot.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(snapshot.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw Unrenewable(order, $"PayPal reports authorization status {snapshot.Status}.", null);
        }

        if (string.Equals(snapshot.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(snapshot.Status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"Authorization {authorizationId} is already {snapshot.Status}. Refresh the order before fulfilling again.",
                409,
                issues: new[] { snapshot.Status ?? "CAPTURED" });
        }

        var now = DateTimeOffset.UtcNow;
        var expired = snapshot.Expiration is DateTimeOffset expiration && expiration <= now;
        var originalAge = (order.AuthorizedAt ?? snapshot.CreateTime);
        var tooOldToReauthorize = originalAge is DateTimeOffset created
            && (now - created.ToUniversalTime()) >= TimeSpan.FromDays(30);

        if (!expired)
        {
            return snapshot.AuthorizationId;
        }

        if (tooOldToReauthorize)
        {
            throw Unrenewable(
                order,
                "The authorization is more than 30 days old, so PayPal cannot reauthorize it. Ask the shopper to pay again.",
                null);
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                snapshot.AuthorizationId,
                order.Total(),
                order.Currency ?? _paymentSettings.Currency,
                $"reauthorize-order-{order.Id}",
                cancellationToken);

            order.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Expiration);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex) when (ex.StatusCode is 400 or 403 or 422)
        {
            throw Unrenewable(
                order,
                "PayPal refused to renew the authorization. Ask the shopper to pay again.",
                ex);
        }
    }

    private static PaymentException Unrenewable(Order order, string operatorMessage, PaymentException? inner)
    {
        var details = $"Order {order.Id} authorization {order.PayPalAuthorizationId} cannot be captured. {operatorMessage}";
        if (order.AuthorizationExpiration is not null)
        {
            details += $" Expiration: {order.AuthorizationExpiration:o}.";
        }

        if (inner is not null)
        {
            details += $" PayPal: {inner.Message}";
            if (!string.IsNullOrEmpty(inner.DebugId))
            {
                details += $" debug_id={inner.DebugId}.";
            }
        }

        return new PaymentException(
            details,
            409,
            inner?.ProviderName,
            inner?.DebugId,
            inner?.Issues,
            inner)
        {
            IsUnrenewableAuthorization = true
        };
    }

    private async Task<string> ResolveVaultToken(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(
            new BuyerByIdentitySpecification(buyerId), cancellationToken);
        var method = buyer?.FindPaymentMethod(paymentMethodId);
        if (method is null || string.IsNullOrEmpty(method.CardId))
        {
            throw new PaymentException("Saved payment method was not found.", 404);
        }

        return method.CardId;
    }

    private async Task<Order> GetShopperOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException("Order not found.", 404);
        }

        return order;
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException("Order not found.", 404);
        }

        return order;
    }

    private static string InvoiceId(int orderId) => $"ESHOP-{orderId}";

    private static async Task<T> WithOrderLock<T>(int orderId, Func<Task<T>> action)
    {
        var gate = OrderGates.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
