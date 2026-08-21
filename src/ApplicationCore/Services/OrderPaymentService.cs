using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan AuthorizationWindow = TimeSpan.FromDays(29);

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

    public async Task<Order> PayAsync(int orderId, string buyerId, CardPaymentSource? card, int? paymentMethodId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await RequireOrderAsync(orderId);
        EnsureBuyer(order, buyerId);

        if (order.Status == OrderStatus.Authorized || order.IsCaptured())
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentConflictException("A cancelled order cannot be paid.");
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentConflictException($"Order in status {order.Status} cannot be paid.");
        }

        var amount = Money.Round(order.Total());
        if (amount <= 0)
        {
            throw new PaymentException("Order total must be greater than zero to authorize payment.");
        }

        var invoiceId = Money.InvoiceId(order);
        var customId = Money.CustomId(order.Id);
        var requestId = Money.AuthorizeRequestId(order);

        AuthorizePaymentResult authorization;
        if (paymentMethodId.HasValue)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId.Value, buyerId));
            if (method == null)
            {
                throw new ResourceNotFoundException("Saved payment method was not found.");
            }

            authorization = await _payPal.AuthorizeVaultedCardAsync(
                amount, invoiceId, customId, method.PayPalPaymentTokenId, requestId);
        }
        else
        {
            if (card == null || string.IsNullOrWhiteSpace(card.Number))
            {
                throw new PaymentException("Provide card details or a saved paymentMethodId.");
            }

            authorization = await _payPal.AuthorizeCardAsync(amount, invoiceId, customId, card, requestId);
        }

        order.MarkAuthorized(PaymentDetails.FromAuthorization(
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.CreatedAt,
            authorization.ExpirationTime,
            authorization.Currency));

        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId)
    {
        var order = await RequireOrderAsync(orderId);

        if (order.IsCaptured())
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || order.Payment == null || string.IsNullOrEmpty(order.Payment.AuthorizationId))
        {
            throw new PaymentConflictException("An order can only be fulfilled after payment has been authorized.");
        }

        var amount = Money.Round(order.Total());
        var authorizationId = await EnsureFreshAuthorizationAsync(order, amount);

        var capture = await CaptureWithRenewalAsync(order, authorizationId, amount);

        order.MarkFulfilled(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PayPalFee,
            capture.NetAmount,
            capture.CapturedAt ?? DateTimeOffset.UtcNow);

        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId)
    {
        var order = await RequireOrderAsync(orderId);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status == OrderStatus.Authorized &&
            order.Payment != null &&
            !string.IsNullOrEmpty(order.Payment.AuthorizationId))
        {
            await _payPal.VoidAuthorizationAsync(
                order.Payment.AuthorizationId,
                Money.VoidRequestId(order));
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string callerBuyerId, bool isAdministrator, string idempotencyKey, decimal? amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await RequireOrderAsync(orderId);
        if (!isAdministrator)
        {
            EnsureBuyer(order, callerBuyerId);
        }

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (!order.IsCaptured() || order.Payment == null || string.IsNullOrEmpty(order.Payment.CaptureId))
        {
            throw new PaymentConflictException("Refunds can only be issued after the payment has been captured at fulfilment.");
        }

        var remaining = order.RemainingRefundableAmount();
        var refundAmount = amount.HasValue ? Money.Round(amount.Value) : remaining;
        if (refundAmount <= 0)
        {
            throw new PaymentException("There is no remaining captured amount to refund.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException($"Refund amount {refundAmount.ToString("0.00", CultureInfo.InvariantCulture)} exceeds the remaining captured amount {remaining.ToString("0.00", CultureInfo.InvariantCulture)}.");
        }

        var paypalRefund = await _payPal.RefundCaptureAsync(
            order.Payment.CaptureId,
            refundAmount,
            idempotencyKey);

        var refund = order.RecordRefund(
            paypalRefund.RefundId,
            idempotencyKey,
            paypalRefund.Amount > 0 ? paypalRefund.Amount : refundAmount,
            paypalRefund.Status);

        await _orderRepository.UpdateAsync(order);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
    }

    public async Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
        if (order == null || !order.BelongsTo(buyerId))
        {
            return null;
        }

        return order;
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, decimal amount)
    {
        var payment = order.Payment!;
        var authorizationId = payment.AuthorizationId!;
        AuthorizationSnapshot snapshot;
        try
        {
            snapshot = await _payPal.GetAuthorizationAsync(authorizationId);
        }
        catch (PayPalGatewayException ex) when (IsExpired(ex))
        {
            return await RenewOrFailAsync(order, authorizationId, amount, ex.Message);
        }

        order.RefreshAuthorization(snapshot.AuthorizationId, snapshot.Status, snapshot.CreatedAt, snapshot.ExpirationTime);

        if (string.Equals(snapshot.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase) ||
            IsPastExpiration(snapshot) ||
            IsPastHonorPeriod(snapshot, payment))
        {
            return await RenewOrFailAsync(order, snapshot.AuthorizationId, amount, snapshot.Status);
        }

        if (string.Equals(snapshot.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(snapshot.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationCannotBeRenewedException(
                $"PayPal reports this authorization as {snapshot.Status}. It cannot be captured or renewed. Ask the shopper to place a new order and pay again.");
        }

        return snapshot.AuthorizationId;
    }

    private async Task<CapturePaymentResult> CaptureWithRenewalAsync(Order order, string authorizationId, decimal amount)
    {
        try
        {
            return await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                Money.CaptureInvoiceId(order),
                Money.CaptureRequestId(order));
        }
        catch (PayPalGatewayException ex) when (IsExpired(ex) || IsStale(ex))
        {
            var renewedId = await RenewOrFailAsync(order, authorizationId, amount, ex.Message);
            return await _payPal.CaptureAuthorizationAsync(
                renewedId,
                amount,
                Money.CaptureInvoiceId(order),
                Money.CaptureRetryRequestId(order));
        }
    }

    private async Task<string> RenewOrFailAsync(Order order, string authorizationId, decimal amount, string reason)
    {
        var originalCreated = order.Payment?.AuthorizationCreatedAt;
        if (originalCreated.HasValue && DateTimeOffset.UtcNow - originalCreated.Value > AuthorizationWindow)
        {
            throw new AuthorizationCannotBeRenewedException(
                "The PayPal authorization is older than 29 days and can no longer be renewed. Ask the shopper to place a new order and pay again, then fulfil that order.");
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                amount,
                Money.ReauthorizeRequestId(order));

            order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.CreatedAt, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order);
            return renewed.AuthorizationId;
        }
        catch (PayPalGatewayException ex) when (IsHonorPeriodActive(ex))
        {
            return authorizationId;
        }
        catch (PayPalGatewayException ex)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"PayPal could not renew the authorization ({ex.Issue ?? reason}). Ask the shopper to place a new order and pay again, then fulfil that order.");
        }
    }

    private async Task<Order> RequireOrderAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
        if (order == null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private static void EnsureBuyer(Order order, string buyerId)
    {
        if (!order.BelongsTo(buyerId))
        {
            throw new ResourceNotFoundException($"Order {order.Id} was not found.");
        }
    }

    private static bool IsPastExpiration(AuthorizationSnapshot snapshot) =>
        snapshot.ExpirationTime.HasValue && snapshot.ExpirationTime.Value <= DateTimeOffset.UtcNow;

    private static bool IsPastHonorPeriod(AuthorizationSnapshot snapshot, PaymentDetails payment)
    {
        var created = snapshot.CreatedAt ?? payment.AuthorizationCreatedAt;
        return created.HasValue && DateTimeOffset.UtcNow - created.Value > HonorPeriod;
    }

    private static bool IsExpired(PayPalGatewayException ex) =>
        ContainsIssue(ex, "AUTHORIZATION_EXPIRED", "EXPIRED_AUTHORIZATION", "AUTH_EXPIRED");

    private static bool IsStale(PayPalGatewayException ex) =>
        ContainsIssue(ex, "AUTHORIZATION_VOIDED", "MAX_CAPTURE_COUNT_EXCEEDED") ||
        (ex.Message?.Contains("expired", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsHonorPeriodActive(PayPalGatewayException ex) =>
        ContainsIssue(ex, "AUTHORIZATION_IN_HONOR_PERIOD", "CANNOT_REAUTHORIZE", "REAUTHORIZATION_NOT_ALLOWED") ||
        (ex.Message?.Contains("honor period", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool ContainsIssue(PayPalGatewayException ex, params string[] issues)
    {
        if (string.IsNullOrEmpty(ex.Issue))
        {
            return false;
        }

        return issues.Any(i => string.Equals(ex.Issue, i, StringComparison.OrdinalIgnoreCase));
    }
}

internal static class Money
{
    public static decimal Round(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static string InvoiceId(Order order) =>
        $"ESHOP-{order.Id}-A-{order.OrderDate.UtcTicks}";

    public static string CaptureInvoiceId(Order order) =>
        $"ESHOP-{order.Id}-C-{order.OrderDate.UtcTicks}";

    public static string InvoicePrefix(int orderId) => $"ESHOP-{orderId}-";

    public static string CustomId(int orderId) => $"order:{orderId}";

    public static string AuthorizeRequestId(Order order) =>
        $"eshop-order-{order.Id}-authorize-{order.OrderDate.UtcTicks}";

    public static string CaptureRequestId(Order order) =>
        $"eshop-order-{order.Id}-capture-{order.OrderDate.UtcTicks}";

    public static string CaptureRetryRequestId(Order order) =>
        $"eshop-order-{order.Id}-capture-retry-{order.OrderDate.UtcTicks}";

    public static string ReauthorizeRequestId(Order order) =>
        $"eshop-order-{order.Id}-reauthorize-{order.OrderDate.UtcTicks}";

    public static string VoidRequestId(Order order) =>
        $"eshop-order-{order.Id}-void-{order.OrderDate.UtcTicks}";
}
