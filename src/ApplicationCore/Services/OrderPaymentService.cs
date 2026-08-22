using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
    private static readonly TimeSpan ReauthorizeWindow = TimeSpan.FromDays(29);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<ShopperPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<ShopperPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPal)
    {
        _orderRepository = orderRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
    }

    public async Task<Order> AuthorizePaymentAsync(
        int orderId,
        string buyerId,
        CardPaymentRequest? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new InvalidOrderStateException($"Order {orderId} has been cancelled and cannot be paid.");
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException($"Order {orderId} cannot be paid from status {order.Status}.");
        }

        var hasCard = card is not null && !string.IsNullOrWhiteSpace(card.Number);
        var hasSavedCard = paymentMethodId.HasValue && paymentMethodId.Value > 0;
        if (hasCard == hasSavedCard)
        {
            throw new PaymentValidationException("Provide either card details or a saved paymentMethodId, not both.");
        }

        var currency = _payPal.Currency;
        var amount = order.Total();
        if (amount <= 0m)
        {
            throw new PaymentValidationException("The order total must be greater than zero to take payment.");
        }

        var requestId = TruncateRequestId($"eshop-pay-{order.Id}-{order.OrderDate.UtcTicks}");
        var invoiceId = InvoiceIdFor(order);
        PayPalOrderAuthorization authorization;
        if (hasSavedCard)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new ShopperPaymentMethodByIdSpecification(paymentMethodId!.Value, buyerId),
                cancellationToken);
            if (saved is null)
            {
                throw new EntityNotFoundException($"Payment method {paymentMethodId} was not found.");
            }

            authorization = await _payPal.AuthorizeVaultedCardAsync(new PayPalAuthorizeVaultedCardCommand
            {
                OrderId = order.Id,
                Amount = amount,
                Currency = currency,
                RequestId = requestId,
                InvoiceId = invoiceId,
                VaultId = saved.PayPalVaultId
            }, cancellationToken);
        }
        else
        {
            authorization = await _payPal.AuthorizeCardPaymentAsync(new PayPalAuthorizeCardCommand
            {
                OrderId = order.Id,
                Amount = amount,
                Currency = currency,
                RequestId = requestId,
                InvoiceId = invoiceId,
                Card = ToPayPalCard(card!)
            }, cancellationToken);
        }

        if (authorization.AuthorizedAmount != decimal.Round(amount, 2, MidpointRounding.AwayFromZero))
        {
            throw new PayPalGatewayException(
                $"PayPal authorized {authorization.AuthorizedAmount} {authorization.Currency} but the order total is {amount} {currency}.");
        }

        order.MarkAuthorized(
            authorization.PayPalOrderId,
            authorization.PayPalOrderStatus,
            authorization.AuthorizationId,
            authorization.AuthorizationStatus,
            authorization.CreatedAt,
            authorization.ExpiresAt,
            authorization.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
        {
            throw new InvalidOrderStateException(
                $"Order {orderId} cannot be fulfilled from status {order.Status}. Authorize payment first.");
        }

        var authorizationId = await EnsureFreshAuthorizationAsync(order, cancellationToken);
        var captureRequestId = TruncateRequestId($"eshop-capture-{order.Id}-{order.OrderDate.UtcTicks}");
        var invoiceId = InvoiceIdFor(order);

        PayPalCaptureDetails capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(authorizationId, captureRequestId, invoiceId, cancellationToken);
        }
        catch (PayPalGatewayException ex) when (IsExpiredAuthorization(ex))
        {
            authorizationId = await RenewAuthorizationAsync(order, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                TruncateRequestId($"eshop-capture-{order.Id}-{order.OrderDate.UtcTicks}-retry"),
                invoiceId,
                cancellationToken);
        }

        if (capture.PayPalFee is null || capture.NetAmount is null)
        {
            capture = await _payPal.GetCaptureAsync(capture.CaptureId, cancellationToken);
        }

        order.MarkFulfilled(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PayPalFee,
            capture.NetAmount,
            capture.CreateTime,
            "CAPTURED");

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new InvalidOrderStateException(
                $"Order {orderId} has already been fulfilled. Cancel is only available before fulfilment; issue a refund instead.");
        }

        if (order.Status == OrderStatus.Authorized && !string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
        {
            try
            {
                await _payPal.VoidAuthorizationAsync(order.Payment.AuthorizationId, cancellationToken);
                order.MarkCancelled("VOIDED", "VOIDED");
            }
            catch (PayPalGatewayException ex) when (ex.HttpStatus == 422 || IsAlreadyVoided(ex))
            {
                order.MarkCancelled("VOIDED", "VOIDED");
            }
        }
        else
        {
            order.MarkCancelled(null, null);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<PaymentRefund> RefundOrderAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);
        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (order.Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded and not OrderStatus.Refunded)
        {
            throw new InvalidOrderStateException(
                $"Order {orderId} cannot be refunded from status {order.Status}. Refunds are available after fulfilment.");
        }

        if (string.IsNullOrWhiteSpace(order.Payment.CaptureId))
        {
            throw new InvalidOrderStateException($"Order {orderId} has no captured payment to refund.");
        }

        var remaining = order.Payment.RemainingRefundableAmount;
        if (remaining <= 0m)
        {
            throw new PaymentValidationException("This order has already been refunded in full.");
        }

        var refundAmount = amount.HasValue ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero) : remaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentValidationException(
                $"Refund of {refundAmount} exceeds the remaining refundable amount of {remaining} {order.Payment.Currency}.");
        }

        var currency = string.IsNullOrWhiteSpace(order.Payment.Currency) ? _payPal.Currency : order.Payment.Currency;
        var paypalRefund = await _payPal.RefundCaptureAsync(
            order.Payment.CaptureId,
            amount.HasValue ? refundAmount : null,
            currency,
            TruncateRequestId($"eshop-rf-{order.Payment.CaptureId}-{idempotencyKey}"),
            order.Id.ToString(CultureInfo.InvariantCulture),
            cancellationToken);

        var recorded = order.RecordRefund(
            paypalRefund.RefundId,
            paypalRefund.Status,
            idempotencyKey,
            paypalRefund.Amount,
            paypalRefund.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return recorded;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public Task<Order> GetBuyerOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
        => GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.EnsureOwnedBy(buyerId);
        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var authorizationId = order.Payment.AuthorizationId
            ?? throw new InvalidOrderStateException($"Order {order.Id} has no PayPal authorization to capture.");

        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PayPalGatewayException ex) when (ex.HttpStatus == 404)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"PayPal no longer has authorization {authorizationId} for order {order.Id}. Ask the shopper to pay again, then fulfil the new authorization.");
        }

        if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(details.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationCannotBeRenewedException(
                $"PayPal authorization {authorizationId} is {details.Status} and cannot be captured. Ask the shopper to pay again.");
        }

        if (string.Equals(details.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(details.Status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            return details.AuthorizationId;
        }

        var now = DateTimeOffset.UtcNow;
        var createdAt = details.CreatedAt ?? order.Payment.AuthorizationCreatedAt;
        var expiresAt = details.ExpiresAt ?? order.Payment.AuthorizationExpiresAt;
        var honorExpired = createdAt.HasValue && now - createdAt.Value >= HonorPeriod;
        var expired = expiresAt.HasValue && expiresAt.Value <= now;

        if (expired || honorExpired)
        {
            return await RenewAuthorizationAsync(order, details, cancellationToken);
        }

        return details.AuthorizationId;
    }

    private Task<string> RenewAuthorizationAsync(Order order, CancellationToken cancellationToken)
        => RenewAuthorizationAsync(order, null, cancellationToken);

    private async Task<string> RenewAuthorizationAsync(
        Order order,
        PayPalAuthorizationDetails? current,
        CancellationToken cancellationToken)
    {
        var authorizationId = current?.AuthorizationId ?? order.Payment.AuthorizationId
            ?? throw new InvalidOrderStateException($"Order {order.Id} has no PayPal authorization to renew.");

        var createdAt = current?.CreatedAt ?? order.Payment.AuthorizationCreatedAt;
        if (createdAt.HasValue && DateTimeOffset.UtcNow - createdAt.Value > ReauthorizeWindow)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The PayPal authorization for order {order.Id} is older than 29 days and can no longer be renewed. Ask the shopper to pay again, then fulfil the new hold.");
        }

        var amount = order.Total();
        var currency = string.IsNullOrWhiteSpace(order.Payment.Currency) ? _payPal.Currency : order.Payment.Currency;

        PayPalAuthorizationDetails renewed;
        try
        {
            renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                amount,
                currency,
                TruncateRequestId($"eshop-reauth-{order.Id}-{order.OrderDate.UtcTicks}"),
                cancellationToken);
        }
        catch (PayPalGatewayException ex)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"PayPal could not renew the authorization for order {order.Id} ({ex.Message}). Ask the shopper to pay again, then fulfil the new hold.");
        }

        order.MarkReauthorized(renewed.AuthorizationId, renewed.Status, renewed.CreatedAt, renewed.ExpiresAt);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return renewed.AuthorizationId;
    }

    private static bool IsExpiredAuthorization(PayPalGatewayException exception)
    {
        var haystack = $"{exception.PayPalName} {exception.Message}".ToUpperInvariant();
        return haystack.Contains("EXPIRED", StringComparison.Ordinal) ||
               haystack.Contains("AUTHORIZATION_EXPIRED", StringComparison.Ordinal) ||
               haystack.Contains("AUTH_EXPIRED", StringComparison.Ordinal);
    }

    private static bool IsAlreadyVoided(PayPalGatewayException exception)
    {
        var haystack = $"{exception.PayPalName} {exception.Message}".ToUpperInvariant();
        return haystack.Contains("VOIDED", StringComparison.Ordinal) ||
               haystack.Contains("ALREADY", StringComparison.Ordinal);
    }

    internal static PayPalCardDetails ToPayPalCard(CardPaymentRequest card)
    {
        var number = SanitizeCardNumber(card.Number);
        if (number.Length is < 13 or > 19)
        {
            throw new PaymentValidationException("Card number must be 13 to 19 digits.");
        }

        return new PayPalCardDetails
        {
            Number = number,
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode.Trim(),
            Name = string.IsNullOrWhiteSpace(card.Name) ? null : card.Name.Trim(),
            BillingAddress = ToPayPalAddress(card.BillingAddress)
        };
    }

    internal static PayPalBillingAddress? ToPayPalAddress(BillingAddressRequest? address)
    {
        if (address is null)
        {
            return new PayPalBillingAddress { CountryCode = "US" };
        }

        var country = string.IsNullOrWhiteSpace(address.CountryCode) ? "US" : address.CountryCode.Trim().ToUpperInvariant();
        return new PayPalBillingAddress
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = country
        };
    }

    internal static string SanitizeCardNumber(string number)
    {
        if (string.IsNullOrWhiteSpace(number))
        {
            throw new PaymentValidationException("Card number is required.");
        }

        var digits = new string(number.Where(char.IsDigit).ToArray());
        if (string.IsNullOrEmpty(digits))
        {
            throw new PaymentValidationException("Card number is required.");
        }

        return digits;
    }

    internal static string NormalizeExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            throw new PaymentValidationException("Card expiry is required in YYYY-MM format.");
        }

        var trimmed = expiry.Trim();
        if (trimmed.Length == 7 && trimmed[4] == '-')
        {
            return trimmed;
        }

        throw new PaymentValidationException("Card expiry must be in YYYY-MM format.");
    }

    internal static string InvoiceIdFor(Order order) =>
        $"ESHOP-{order.Id}-{order.OrderDate.ToUnixTimeSeconds()}";

    internal static string TruncateRequestId(string requestId)
    {
        if (requestId.Length <= 108)
        {
            return requestId;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestId)));
        return hash[..108];
    }
}
