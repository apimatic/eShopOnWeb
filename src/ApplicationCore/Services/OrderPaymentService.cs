using System;
using System.Collections.Concurrent;
using System.Linq;
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
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<OrderPaymentService> _logger;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPal,
        IAppLogger<OrderPaymentService> logger,
        IPayPalSettings paypalSettings)
    {
        _orderRepository = orderRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _logger = logger;
        _currency = paypalSettings.Currency;
    }

    public async Task<Order> AuthorizePaymentAsync(
        int orderId,
        string buyerId,
        PayPalCardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        using (await LockAsync($"pay-{orderId}", cancellationToken))
        {
            var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);

            if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(order.Payment.AuthorizationId))
            {
                return order;
            }

            if (order.Status != OrderStatus.AwaitingPayment)
            {
                throw new InvalidOrderStateException($"Order {orderId} cannot be paid because it is {order.Status}.");
            }

            var amount = MoneyFormatter.ToMajorUnits(order.Total(), _currency);
            if (amount <= 0)
            {
                throw new PaymentRequestException("Order total must be greater than zero.");
            }

            string? vaultId = null;
            SavedPaymentMethod? saved = null;
            if (savedPaymentMethodId.HasValue)
            {
                saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdAndBuyerSpec(savedPaymentMethodId.Value, buyerId), cancellationToken);
                if (saved == null)
                {
                    throw new EntityNotFoundException("Saved payment method was not found for this shopper.");
                }

                vaultId = saved.PayPalVaultId;
            }
            else if (card == null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
            {
                throw new PaymentRequestException("Provide card details or a saved paymentMethodId to pay.");
            }

            var invoiceId = InvoiceId(order);
            var requestId = $"pay-{order.Payment.MerchantInvoiceId}";

            PayPalAuthorizationResult auth;
            if (vaultId != null)
            {
                auth = await _payPal.AuthorizeVaultedCardPaymentAsync(
                    invoiceId, order.Id.ToString(), amount, _currency, vaultId, requestId, cancellationToken);
            }
            else
            {
                auth = await _payPal.AuthorizeCardPaymentAsync(
                    invoiceId, order.Id.ToString(), amount, _currency, card!, requestId, cancellationToken);
            }

            if (auth.Amount != amount)
            {
                throw new PaymentGatewayException(
                    $"PayPal authorized {auth.Amount} {_currency} but the order total is {amount} {_currency}.");
            }

            order.RecordAuthorization(
                auth.PayPalOrderId,
                auth.PayPalOrderStatus,
                auth.AuthorizationId,
                auth.AuthorizationStatus,
                auth.Amount,
                auth.Currency,
                auth.ExpiresAt,
                vaultId,
                saved?.Id);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Authorized order {OrderId} with PayPal authorization {AuthorizationId}.", order.Id, auth.AuthorizationId);
            return order;
        }
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using (await LockAsync($"fulfil-{orderId}", cancellationToken))
        {
            var order = await GetOrder(orderId, cancellationToken);

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded
                && !string.IsNullOrEmpty(order.Payment.CaptureId))
            {
                return order;
            }

            if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.Payment.AuthorizationId))
            {
                throw new InvalidOrderStateException($"Order {orderId} cannot be fulfilled because it is {order.Status}. Authorize payment first.");
            }

            var amount = MoneyFormatter.ToMajorUnits(order.Payment.AuthorizedAmount ?? order.Total(), _currency);
            var authorizationId = order.Payment.AuthorizationId;
            authorizationId = await EnsureFreshAuthorization(order, authorizationId, amount, cancellationToken);

            PayPalCaptureResult capture;
            try
            {
                capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId,
                    amount,
                    _currency,
                    InvoiceId(order),
                    $"fulfil-{order.Payment.MerchantInvoiceId}",
                    cancellationToken);
            }
            catch (PaymentGatewayException ex) when (IsExpiredAuthorization(ex))
            {
                authorizationId = await RenewAuthorization(order, amount, cancellationToken);
                capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId,
                    amount,
                    _currency,
                    InvoiceId(order),
                    $"fulfil-{order.Payment.MerchantInvoiceId}-retry",
                    cancellationToken);
            }

            order.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.PaypalFee, capture.NetProceeds);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Fulfilled order {OrderId} with PayPal capture {CaptureId}.", order.Id, capture.CaptureId);
            return order;
        }
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using (await LockAsync($"cancel-{orderId}", cancellationToken))
        {
            var order = await GetOrder(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }

            if (!string.IsNullOrEmpty(order.Payment.AuthorizationId) && order.Status == OrderStatus.Authorized)
            {
                try
                {
                    await _payPal.VoidAuthorizationAsync(
                        order.Payment.AuthorizationId,
                        $"cancel-{order.Payment.MerchantInvoiceId}",
                        cancellationToken);
                }
                catch (PaymentGatewayException ex) when (IsAlreadyVoided(ex))
                {
                    _logger.LogWarning("PayPal authorization {AuthorizationId} was already released.", order.Payment.AuthorizationId);
                }
            }

            order.Cancel("VOIDED");
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundOrderAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentRequestException("A refund idempotencyKey is required.");
        }

        using (await LockAsync($"refund-{orderId}-{idempotencyKey}", cancellationToken))
        {
            var order = await GetOwnedOrder(orderId, buyerId, cancellationToken);
            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing != null)
            {
                return (order, existing);
            }

            if (string.IsNullOrEmpty(order.Payment.CaptureId))
            {
                throw new InvalidOrderStateException($"Order {orderId} has no captured payment to refund.");
            }

            var remaining = MoneyFormatter.ToMajorUnits(order.RemainingRefundable(), _currency);
            var refundAmount = amount.HasValue
                ? MoneyFormatter.ToMajorUnits(amount.Value, _currency)
                : remaining;

            if (refundAmount <= 0)
            {
                throw new InvalidOrderStateException($"Order {orderId} has no remaining captured amount to refund.");
            }

            if (refundAmount > remaining)
            {
                throw new InvalidOrderStateException(
                    $"Refund of {refundAmount:0.00} exceeds the remaining refundable amount of {remaining:0.00} on order {orderId}.");
            }

            var paypalRefund = await _payPal.RefundCaptureAsync(
                order.Payment.CaptureId,
                refundAmount == remaining && !amount.HasValue ? null : refundAmount,
                _currency,
                Truncate($"rf-{order.Payment.MerchantInvoiceId}-{idempotencyKey}", 108),
                cancellationToken);

            var recordedAmount = paypalRefund.Amount > 0 ? paypalRefund.Amount : refundAmount;
            var refund = order.RecordRefund(
                paypalRefund.RefundId,
                paypalRefund.Status,
                recordedAmount,
                paypalRefund.Currency,
                idempotencyKey);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return (order, refund);
        }
    }

    private async Task<string> EnsureFreshAuthorization(Order order, string authorizationId, decimal amount, CancellationToken cancellationToken)
    {
        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentGatewayException)
        {
            return await RenewAuthorization(order, amount, cancellationToken);
        }

        var expired = details.ExpiresAt.HasValue && details.ExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(5);
        var staleStatus = !string.Equals(details.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(details.Status, "PENDING", StringComparison.OrdinalIgnoreCase);

        if (!expired && !staleStatus)
        {
            return details.AuthorizationId;
        }

        return await RenewAuthorization(order, amount, cancellationToken);
    }

    private async Task<string> RenewAuthorization(Order order, decimal amount, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(order.Payment.AuthorizationId))
        {
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(
                    order.Payment.AuthorizationId,
                    amount,
                    _currency,
                    $"reauth-{order.Payment.MerchantInvoiceId}",
                    cancellationToken);

                order.RecordRenewedAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                _logger.LogInformation("Reauthorized order {OrderId} as {AuthorizationId}.", order.Id, renewed.AuthorizationId);
                return renewed.AuthorizationId;
            }
            catch (PaymentGatewayException ex)
            {
                _logger.LogWarning("PayPal reauthorize failed for order {OrderId}: {Message}", order.Id, ex.Message);
            }
        }

        if (!string.IsNullOrEmpty(order.Payment.VaultId))
        {
            var invoiceId = $"{InvoiceId(order)}-R{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var auth = await _payPal.AuthorizeVaultedCardPaymentAsync(
                invoiceId,
                order.Id.ToString(),
                amount,
                _currency,
                order.Payment.VaultId,
                $"renew-{order.Payment.MerchantInvoiceId}",
                cancellationToken);

            order.RecordRenewedAuthorization(auth.AuthorizationId, auth.AuthorizationStatus, auth.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Renewed order {OrderId} hold via saved card as {AuthorizationId}.", order.Id, auth.AuthorizationId);
            return auth.AuthorizationId;
        }

        throw new AuthorizationRenewalException(
            $"The payment hold on order {order.Id} has expired and PayPal could not renew it. " +
            "Ask the shopper to pay the order again (or pay with a saved card) before fulfilling.");
    }

    private async Task<Order> GetOwnedOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new ForbiddenOperationException("This order does not belong to the signed-in shopper.");
        }

        return order;
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private static string InvoiceId(Order order) =>
        string.IsNullOrEmpty(order.Payment.MerchantInvoiceId)
            ? $"ESHOP-{order.Id}-{Guid.NewGuid():N}"
            : order.Payment.MerchantInvoiceId;

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static bool IsExpiredAuthorization(PaymentGatewayException ex)
    {
        var blob = $"{ex.PayPalErrorName} {ex.Message}".ToUpperInvariant();
        return blob.Contains("EXPIRED") || blob.Contains("AUTHORIZATION_EXPIRED") || blob.Contains("INVALID_RESOURCE_ID");
    }

    private static bool IsAlreadyVoided(PaymentGatewayException ex)
    {
        var blob = $"{ex.PayPalErrorName} {ex.Message}".ToUpperInvariant();
        return blob.Contains("VOIDED") || blob.Contains("AUTHORIZATION_VOIDED") || blob.Contains("INVALID_RESOURCE_ID");
    }

    private static async Task<IDisposable> LockAsync(string key, CancellationToken cancellationToken)
    {
        var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => _semaphore.Release();
    }
}
