using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IPayPalGateway payPalGateway,
        IPaymentCurrencyAccessor currencyAccessor)
    {
        _orderRepository = orderRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _payPalGateway = payPalGateway;
        _currency = currencyAccessor.Currency;
    }

    public Task<Order> PayWithCardAsync(int orderId, string buyerId, CardPaymentSource card, CancellationToken cancellationToken = default) =>
        WithOrderLock(orderId, () => PayCoreAsync(orderId, buyerId, card, paymentMethodId: null, cancellationToken));

    public Task<Order> PayWithSavedCardAsync(int orderId, string buyerId, int paymentMethodId, CancellationToken cancellationToken = default) =>
        WithOrderLock(orderId, () => PayCoreAsync(orderId, buyerId, card: null, paymentMethodId, cancellationToken));

    public Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default) =>
        WithOrderLock(orderId, () => FulfilCoreAsync(orderId, cancellationToken));

    public Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default) =>
        WithOrderLock(orderId, () => CancelCoreAsync(orderId, cancellationToken));

    public Task<Order> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default) =>
        WithOrderLock(orderId, () => RefundCoreAsync(orderId, buyerId, amount, idempotencyKey, cancellationToken));

    private async Task<Order> PayCoreAsync(
        int orderId,
        string buyerId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken)
    {
        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException(409, "This order has been cancelled and cannot be paid.");
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException(409, $"Order {order.Id} cannot be paid from status {order.Status}.");
        }

        var amount = order.RoundedTotal();
        if (amount <= 0)
        {
            throw new PaymentException(400, "The order total must be greater than zero.");
        }

        string? vaultId = null;
        if (paymentMethodId.HasValue)
        {
            var saved = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(paymentMethodId.Value, buyerId),
                cancellationToken);
            if (saved is null)
            {
                throw new PaymentException(404, "The saved card was not found, or it does not belong to the caller.");
            }

            vaultId = saved.PayPalVaultId;
        }
        else if (card is null)
        {
            throw new PaymentException(400, "Provide either card details or a saved paymentMethodId.");
        }

        var items = order.OrderItems.Select(i => new PayPalCheckoutItem(
            i.ItemOrdered.ProductName,
            i.Units.ToString(CultureInfo.InvariantCulture),
            i.UnitPrice,
            i.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture))).ToList();

        var invoiceId = MerchantInvoiceId(order);
        var requestId = $"eshop-pay-{order.Id}-{order.OrderDate.UtcTicks}";

        AuthorizedPaymentResult authorized;
        try
        {
            authorized = vaultId is null
                ? await _payPalGateway.CreateAndAuthorizeWithCardAsync(order.Id, amount, _currency, items, card!, invoiceId, requestId, cancellationToken)
                : await _payPalGateway.CreateAndAuthorizeWithVaultIdAsync(order.Id, amount, _currency, items, vaultId, invoiceId, requestId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.Message.Contains("ORDER_ALREADY_AUTHORIZED", StringComparison.OrdinalIgnoreCase)
                                          && !string.IsNullOrWhiteSpace(order.PayPalOrderId))
        {
            authorized = await _payPalGateway.GetAuthorizedOrderAsync(order.PayPalOrderId, cancellationToken);
        }

        if (!string.Equals(authorized.Currency, _currency, StringComparison.OrdinalIgnoreCase)
            || authorized.Amount != amount)
        {
            throw new PaymentException(502,
                $"PayPal authorized {authorized.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {authorized.Currency}, which does not match the order total {amount.ToString("0.00", CultureInfo.InvariantCulture)} {_currency}.");
        }

        order.MarkAuthorized(
            authorized.PayPalOrderId,
            authorized.AuthorizationId,
            authorized.AuthorizationStatus,
            authorized.ExpirationTime,
            authorized.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<Order> FulfilCoreAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException(409, "A cancelled order cannot be fulfilled.");
        }

        if (order.Status != OrderStatus.Authorized
            || string.IsNullOrWhiteSpace(order.PayPalAuthorizationId)
            || string.IsNullOrWhiteSpace(order.Currency))
        {
            throw new PaymentException(409, "An order must be authorized before it can be fulfilled.");
        }

        var amount = order.RoundedTotal();
        var authorizationId = await EnsureFreshAuthorizationAsync(order, amount, cancellationToken);

        var capture = await _payPalGateway.CaptureAuthorizationAsync(
            authorizationId,
            amount,
            order.Currency!,
            MerchantCaptureInvoiceId(order),
            $"eshop-capture-{order.Id}-{order.OrderDate.UtcTicks}",
            cancellationToken);

        order.MarkFulfilled(
            capture.CaptureId,
            capture.CaptureStatus,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetAmount);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, decimal amount, CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        AuthorizationDetails details;
        try
        {
            details = await _payPalGateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == 404)
        {
            throw new PaymentException(409,
                "PayPal no longer has this authorization. It cannot be renewed. Ask the shopper to pay the order again.");
        }

        order.RefreshAuthorization(details.Id, details.Status, details.ExpirationTime);

        var stale = details.ExpirationTime.HasValue && details.ExpirationTime.Value <= DateTimeOffset.UtcNow.AddMinutes(5);
        var capturable = string.Equals(details.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(details.Status, "PENDING", StringComparison.OrdinalIgnoreCase);

        if (capturable && !stale)
        {
            return details.Id;
        }

        if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(details.Status, "DENIED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(details.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(409,
                $"PayPal reports this authorization as {details.Status}, so it cannot be captured or renewed. Ask the shopper to pay the order again.");
        }

        try
        {
            var renewed = await _payPalGateway.ReauthorizeAsync(
                details.Id,
                amount,
                order.Currency!,
                $"eshop-reauthorize-{order.Id}-{order.OrderDate.UtcTicks}",
                cancellationToken);

            order.RefreshAuthorization(renewed.Id, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.Id;
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(409,
                "This authorization has gone stale and PayPal will not renew it. Ask the shopper to pay the order again before fulfilment. PayPal said: "
                + ex.Message);
        }
    }

    private async Task<Order> CancelCoreAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (order.Status == OrderStatus.Authorized && !string.IsNullOrWhiteSpace(order.PayPalAuthorizationId))
        {
            await _payPalGateway.VoidAuthorizationAsync(
                order.PayPalAuthorizationId,
                $"eshop-void-{order.Id}-{order.OrderDate.UtcTicks}",
                cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<Order> RefundCoreAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(400, "A caller-supplied idempotency key is required for refunds.");
        }

        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return order;
        }

        if (order.Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException(409, "Only a fulfilled order can be refunded.");
        }

        if (string.IsNullOrWhiteSpace(order.PayPalCaptureId) || !order.CapturedAmount.HasValue || string.IsNullOrWhiteSpace(order.Currency))
        {
            throw new PaymentException(409, "This order has no captured PayPal payment to refund.");
        }

        var remaining = order.RefundableRemaining();
        if (remaining <= 0)
        {
            throw new PaymentException(409, "This order has already been refunded in full.");
        }

        decimal refundAmount;
        if (amount.HasValue)
        {
            refundAmount = decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero);
            if (refundAmount <= 0)
            {
                throw new PaymentException(400, "Refund amount must be greater than zero.");
            }

            if (refundAmount > remaining)
            {
                throw new PaymentException(400,
                    $"Refund of {refundAmount.ToString("0.00", CultureInfo.InvariantCulture)} exceeds the remaining refundable amount of {remaining.ToString("0.00", CultureInfo.InvariantCulture)}.");
            }
        }
        else
        {
            refundAmount = remaining;
        }

        var paypalRefund = await _payPalGateway.RefundCaptureAsync(
            order.PayPalCaptureId,
            refundAmount,
            order.Currency,
            $"eshop-refund-{order.PayPalCaptureId}-{idempotencyKey}",
            cancellationToken);

        order.AddRefund(
            paypalRefund.RefundId,
            paypalRefund.Status,
            paypalRefund.Amount,
            paypalRefund.Currency,
            idempotencyKey);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException(404, "Order not found.");
        }

        return order;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException(404, "Order not found.");
        }

        return order;
    }

    private static string MerchantInvoiceId(Order order) =>
        $"ESHOP-{order.Id}-{order.OrderDate.UtcTicks}";

    private static string MerchantCaptureInvoiceId(Order order) =>
        $"ESHOP-CAP-{order.Id}-{order.OrderDate.UtcTicks}";

    private static async Task<T> WithOrderLock<T>(int orderId, Func<Task<T>> action)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
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
