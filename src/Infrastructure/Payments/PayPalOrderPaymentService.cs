using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using Address = PayPalServerSdk.Models.Address;
using Order = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalOrderPaymentService : IOrderPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderGates = new();
    private static readonly Regex ExpiryFormat = new(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<SavedPaymentMethod> _paymentMethods;
    private readonly PayPalGateway _gateway;
    private readonly PayPalSettings _settings;

    public PayPalOrderPaymentService(
        IRepository<Order> orders,
        IRepository<SavedPaymentMethod> paymentMethods,
        PayPalGateway gateway,
        IOptions<PayPalSettings> settings)
    {
        _orders = orders;
        _paymentMethods = paymentMethods;
        _gateway = gateway;
        _settings = settings.Value;
    }

    public Task<Order> PayAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken)
        => WithOrderGate(orderId, () => PayCoreAsync(buyerId, orderId, command, cancellationToken));

    public Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
        => WithOrderGate(orderId, () => FulfilCoreAsync(orderId, cancellationToken));

    public Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
        => WithOrderGate(orderId, () => CancelCoreAsync(orderId, cancellationToken));

    public Task<OrderRefund> RefundAsync(string buyerId, int orderId, RefundOrderCommand command, CancellationToken cancellationToken)
        => WithOrderGate(orderId, () => RefundCoreAsync(buyerId, orderId, command, cancellationToken));

    private async Task<Order> PayCoreAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var order = await LoadOwnedOrder(orderId, buyerId, cancellationToken);

        if (order.FulfillmentStatus is OrderFulfillmentStatus.Authorized
            or OrderFulfillmentStatus.Fulfilled
            or OrderFulfillmentStatus.PartiallyRefunded
            or OrderFulfillmentStatus.Refunded)
        {
            return order;
        }

        if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
        {
            throw new ApiException("A cancelled order cannot be paid.", 409);
        }

        var card = await BuildCardRequest(buyerId, command, cancellationToken);
        var amount = PayPalMoneyFormatter.Format(order.Total());
        var currency = RequireCurrency();
        var eshopOrderId = order.Id.ToString();

        if (string.IsNullOrEmpty(order.PayPalOrderId))
        {
            var created = await _gateway.CreateAuthorizeOrderAsync(
                payPalRequestId: $"eshop-order-{order.Id}-create-{Guid.NewGuid():N}",
                currency: currency,
                amountValue: amount,
                eshopOrderId: eshopOrderId,
                cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(created.Id))
            {
                throw new ApiException("PayPal did not return an order id.", 502);
            }

            order.RecordPayPalOrder(created.Id, created.Status?.Value);
            await _orders.UpdateAsync(order, cancellationToken);
        }

        var authorized = await _gateway.AuthorizeOrderAsync(
            payPalOrderId: order.PayPalOrderId!,
            payPalRequestId: $"eshop-order-{order.Id}-authorize",
            card: card,
            cancellationToken: cancellationToken);

        var hold = authorized.PurchaseUnits?
            .SelectMany(unit => unit.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

        if (hold is null || string.IsNullOrEmpty(hold.Id))
        {
            throw new ApiException("PayPal did not return an authorization for this order.", 502);
        }

        if (hold.Status == AuthorizationStatus.Denied)
        {
            throw new PaymentOperationException("PayPal denied the authorization. The card was not charged.", 402);
        }

        order.RecordAuthorization(
            hold.Id,
            hold.Status?.Value,
            PayPalMoneyFormatter.ParseTime(hold.ExpirationTime),
            authorized.Status?.Value,
            currency);

        await _orders.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<Order> FulfilCoreAsync(int orderId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var order = await LoadOrder(orderId, cancellationToken);

        if (order.FulfillmentStatus is OrderFulfillmentStatus.Fulfilled
            or OrderFulfillmentStatus.PartiallyRefunded
            or OrderFulfillmentStatus.Refunded)
        {
            return order;
        }

        if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
        {
            throw new ApiException("A cancelled order cannot be fulfilled.", 409);
        }

        if (order.FulfillmentStatus != OrderFulfillmentStatus.Authorized ||
            string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new ApiException("The order has no authorization to capture. The shopper must pay first.", 409);
        }

        var authorization = await _gateway.GetAuthorizationAsync(order.PayPalAuthorizationId, cancellationToken);

        if (authorization.Status == AuthorizationStatus.Voided ||
            authorization.Status == AuthorizationStatus.Denied)
        {
            throw new PaymentOperationException(
                $"This authorization cannot be captured (status {authorization.Status?.Value}). Ask the shopper to pay again.",
                409,
                issue: authorization.Status?.Value);
        }

        if (authorization.Status == AuthorizationStatus.Captured && !string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            return order;
        }

        var currency = order.PaymentCurrency ?? RequireCurrency();
        var amount = PayPalMoneyFormatter.Format(order.Total());
        var authorizationId = order.PayPalAuthorizationId;

        if (IsStale(PayPalMoneyFormatter.ParseTime(authorization.ExpirationTime)) && authorization.Status != AuthorizationStatus.Captured)
        {
            PaymentAuthorization renewed;
            try
            {
                renewed = await _gateway.ReauthorizeAsync(
                    authorizationId: authorizationId,
                    payPalRequestId: $"eshop-order-{order.Id}-reauthorize-{authorizationId}",
                    currency: currency,
                    amountValue: amount,
                    cancellationToken: cancellationToken);
            }
            catch (PaymentOperationException)
            {
                throw;
            }

            if (string.IsNullOrEmpty(renewed.Id))
            {
                throw new PaymentOperationException(
                    "PayPal reauthorized the hold but did not return a new authorization id. Do not retry capture against the old id.",
                    502);
            }

            order.ReplaceAuthorization(renewed.Id, renewed.Status?.Value, PayPalMoneyFormatter.ParseTime(renewed.ExpirationTime));
            await _orders.UpdateAsync(order, cancellationToken);
            authorizationId = renewed.Id;
        }

        CapturedPayment captured;
        try
        {
            captured = await _gateway.CaptureAsync(
                authorizationId: authorizationId,
                payPalRequestId: $"eshop-order-{order.Id}-capture",
                cancellationToken: cancellationToken);
        }
        catch (PaymentOperationException ex) when (LooksExpired(ex) && authorizationId == order.PayPalAuthorizationId)
        {
            var renewed = await _gateway.ReauthorizeAsync(
                authorizationId: authorizationId,
                payPalRequestId: $"eshop-order-{order.Id}-reauthorize-after-capture-{authorizationId}",
                currency: currency,
                amountValue: amount,
                cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(renewed.Id))
            {
                throw new PaymentOperationException(
                    "The original authorization is stale and PayPal did not return a renewed authorization id.",
                    502,
                    ex.DebugId,
                    ex.Issue);
            }

            order.ReplaceAuthorization(renewed.Id, renewed.Status?.Value, PayPalMoneyFormatter.ParseTime(renewed.ExpirationTime));
            await _orders.UpdateAsync(order, cancellationToken);

            captured = await _gateway.CaptureAsync(
                authorizationId: renewed.Id,
                payPalRequestId: $"eshop-order-{order.Id}-capture-renewed",
                cancellationToken: cancellationToken);
        }

        if (string.IsNullOrEmpty(captured.Id))
        {
            throw new ApiException("PayPal did not return a capture id.", 502);
        }

        var capturedAmount = PayPalMoneyFormatter.Parse(captured.Amount?.Value ?? captured.SellerReceivableBreakdown?.GrossAmount.Value);
        var fee = captured.SellerReceivableBreakdown?.PaypalFee is null
            ? (decimal?)null
            : PayPalMoneyFormatter.Parse(captured.SellerReceivableBreakdown.PaypalFee.Value);
        var net = captured.SellerReceivableBreakdown?.NetAmount is null
            ? (decimal?)null
            : PayPalMoneyFormatter.Parse(captured.SellerReceivableBreakdown.NetAmount.Value);

        order.RecordCapture(
            captured.Id,
            captured.Status?.Value,
            capturedAmount,
            fee,
            net,
            AuthorizationStatus.Captured.Value);

        await _orders.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<Order> CancelCoreAsync(int orderId, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        var order = await LoadOrder(orderId, cancellationToken);

        if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
        {
            return order;
        }

        if (order.FulfillmentStatus is OrderFulfillmentStatus.Fulfilled
            or OrderFulfillmentStatus.PartiallyRefunded
            or OrderFulfillmentStatus.Refunded)
        {
            throw new ApiException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
        }

        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId) &&
            order.FulfillmentStatus == OrderFulfillmentStatus.Authorized)
        {
            var voided = await _gateway.VoidAsync(
                authorizationId: order.PayPalAuthorizationId,
                payPalRequestId: $"eshop-order-{order.Id}-void",
                cancellationToken: cancellationToken);

            order.RecordVoid(voided.Status?.Value ?? AuthorizationStatus.Voided.Value, OrderStatus.Voided.Value);
        }
        else
        {
            order.RecordVoid(order.PayPalAuthorizationStatus, order.PayPalOrderStatus);
        }

        await _orders.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<OrderRefund> RefundCoreAsync(
        string buyerId,
        int orderId,
        RefundOrderCommand command,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new ApiException("idempotencyKey is required.", 400);
        }

        var order = await LoadOwnedOrder(orderId, buyerId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(command.IdempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (order.FulfillmentStatus is not OrderFulfillmentStatus.Fulfilled
            and not OrderFulfillmentStatus.PartiallyRefunded
            and not OrderFulfillmentStatus.Refunded)
        {
            throw new ApiException("Only a fulfilled order can be refunded.", 409);
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            throw new ApiException("The order has no captured payment to refund.", 409);
        }

        var remaining = order.RemainingRefundable();
        var refundAmount = command.Amount ?? remaining;

        if (refundAmount <= 0m)
        {
            throw new ApiException("There is no remaining captured amount to refund.", 409);
        }

        if (refundAmount > remaining)
        {
            throw new ApiException(
                $"Refund amount {PayPalMoneyFormatter.Format(refundAmount)} exceeds remaining captured amount {PayPalMoneyFormatter.Format(remaining)}.",
                422);
        }

        Money? amountBody = command.Amount is null
            ? null
            : new Money
            {
                CurrencyCode = order.PaymentCurrency ?? RequireCurrency(),
                Value = PayPalMoneyFormatter.Format(refundAmount)
            };

        var refund = await _gateway.RefundAsync(
            captureId: order.PayPalCaptureId,
            payPalRequestId: $"eshop-refund-{order.Id}-{command.IdempotencyKey}",
            amount: amountBody,
            cancellationToken: cancellationToken);

        if (string.IsNullOrEmpty(refund.Id))
        {
            throw new ApiException("PayPal did not return a refund id.", 502);
        }

        var recordedAmount = refund.Amount is null
            ? refundAmount
            : PayPalMoneyFormatter.Parse(refund.Amount.Value);

        var recorded = order.RecordRefund(
            refund.Id,
            command.IdempotencyKey,
            recordedAmount,
            refund.Status?.Value ?? RefundStatus.Completed.Value);

        await _orders.UpdateAsync(order, cancellationToken);
        return recorded;
    }

    private async Task<CardRequest> BuildCardRequest(string buyerId, PayOrderCommand command, CancellationToken cancellationToken)
    {
        var hasMethod = !string.IsNullOrWhiteSpace(command.PaymentMethodId);
        var hasCard = command.Card is not null;

        if (hasMethod == hasCard)
        {
            throw new ApiException("Provide either card details or paymentMethodId, not both or neither.", 400);
        }

        if (hasMethod)
        {
            var spec = new SavedPaymentMethodByTokenSpec(command.PaymentMethodId!);
            var saved = await _paymentMethods.FirstOrDefaultAsync(spec, cancellationToken);
            if (saved is null || !string.Equals(saved.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new ApiException("Saved card was not found.", 404);
            }

            return new CardRequest { VaultId = saved.PaymentTokenId };
        }

        return ToCardRequest(command.Card!);
    }

    internal static CardRequest ToCardRequest(CardPaymentInput card)
    {
        ValidateCard(card);
        return new CardRequest
        {
            Name = card.Name,
            Number = NormalizeCardNumber(card.Number),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToPayPalAddress(card.BillingAddress)
        };
    }

    internal static PaymentTokenRequestCard ToVaultCard(CardPaymentInput card)
    {
        ValidateCard(card);
        return new PaymentTokenRequestCard
        {
            Name = card.Name,
            Number = NormalizeCardNumber(card.Number),
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToPayPalAddress(card.BillingAddress)
        };
    }

    internal static void ValidateCard(CardPaymentInput card)
    {
        if (string.IsNullOrWhiteSpace(card.Name) ||
            string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) ||
            string.IsNullOrWhiteSpace(card.SecurityCode) ||
            card.BillingAddress is null ||
            string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
        {
            throw new ApiException("Card name, number, expiry (YYYY-MM), security code, and billingAddress.countryCode are required.", 400);
        }

        if (!ExpiryFormat.IsMatch(card.Expiry))
        {
            throw new ApiException("Card expiry must be YYYY-MM.", 400);
        }

        var digits = NormalizeCardNumber(card.Number);
        if (digits.Length is < 13 or > 19)
        {
            throw new ApiException("Card number must be 13 to 19 digits.", 400);
        }
    }

    internal static Address ToPayPalAddress(CardBillingAddressInput billing)
    {
        return new Address
        {
            CountryCode = billing.CountryCode,
            AddressLine1 = billing.AddressLine1,
            AddressLine2 = billing.AddressLine2,
            AdminArea2 = billing.AdminArea2,
            AdminArea1 = billing.AdminArea1,
            PostalCode = billing.PostalCode
        };
    }

    private static string NormalizeCardNumber(string number)
        => new string(number.Where(char.IsDigit).ToArray());

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new ApiException("PayPal is not configured. Set PayPal:ClientId and PayPal:ClientSecret.", 503);
        }

        RequireCurrency();
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_settings.Currency))
        {
            throw new ApiException("PayPal:Currency is not configured.", 503);
        }

        return _settings.Currency;
    }

    private async Task<Order> LoadOwnedOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await LoadOrder(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new ApiException("Order was not found.", 404);
        }

        return order;
    }

    private async Task<Order> LoadOrder(int orderId, CancellationToken cancellationToken)
    {
        var spec = new OrderWithPaymentByIdSpec(orderId);
        var order = await _orders.FirstOrDefaultAsync(spec, cancellationToken);
        if (order is null)
        {
            throw new ApiException("Order was not found.", 404);
        }

        return order;
    }

    private static bool IsStale(DateTimeOffset? expiration)
        => expiration is not null && expiration.Value <= DateTimeOffset.UtcNow;

    private static bool LooksExpired(PaymentOperationException ex)
    {
        var issue = ex.Issue ?? string.Empty;
        var message = ex.Message ?? string.Empty;
        return issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ||
               issue.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("expired", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<T> WithOrderGate<T>(int orderId, Func<Task<T>> action)
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
