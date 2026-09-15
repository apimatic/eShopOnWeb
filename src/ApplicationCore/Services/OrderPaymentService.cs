using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan AuthorizationRenewalHorizon = TimeSpan.FromDays(29);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentsClient _paypal;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentConfiguration _paymentConfiguration;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalPaymentsClient paypal,
        IUriComposer uriComposer,
        IPaymentConfiguration paymentConfiguration,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paypal = paypal;
        _uriComposer = uriComposer;
        _paymentConfiguration = paymentConfiguration;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLine> lines,
        Address? shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required to place an order.", 401);
        }

        if (lines == null || lines.Count == 0)
        {
            throw new PaymentException("The order must contain at least one catalog item.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException("Each order line must have a quantity greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Catalog item(s) not found: {string.Join(", ", missing)}.", 404);
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shippingAddress ?? DefaultAddress();
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPayment? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", 409);
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {order.Id} cannot be paid while it is {order.Status}.", 409);
        }

        if (card != null && paymentMethodId.HasValue)
        {
            throw new PaymentException("Provide either card details or a saved payment method, not both.");
        }

        string? vaultId = null;
        PayPalCardDetails? paypalCard = null;
        if (paymentMethodId.HasValue)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId.Value, buyerId), cancellationToken);
            if (method == null)
            {
                throw new PaymentMethodNotFoundException(paymentMethodId.Value);
            }

            vaultId = method.PaypalVaultId;
        }
        else if (card != null)
        {
            paypalCard = MapCard(card);
        }
        else
        {
            throw new PaymentException("Provide card details or a saved paymentMethodId to pay.");
        }

        var currency = RequireCurrency();
        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        if (string.IsNullOrEmpty(order.PayRequestId))
        {
            order.EnsurePayRequestId($"eshop-pay-{order.Id}-{Guid.NewGuid():N}");
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        var invoiceId = $"ESHOP-{order.Id}-{order.PayRequestId![^8..]}";

        PayPalAuthorizationResult authorization;
        try
        {
            authorization = await _paypal.AuthorizeCardPaymentAsync(new PayPalAuthorizeRequest
            {
                InvoiceId = invoiceId,
                CustomId = order.Id.ToString(CultureInfo.InvariantCulture),
                Currency = currency,
                Amount = amount,
                RequestId = order.PayRequestId!,
                Items = order.OrderItems.Select(i => new PayPalOrderItem
                {
                    Name = i.ItemOrdered.ProductName,
                    UnitAmount = decimal.Round(i.UnitPrice, 2, MidpointRounding.AwayFromZero),
                    Quantity = i.Units,
                    Sku = i.ItemOrdered.CatalogItemId.ToString(CultureInfo.InvariantCulture)
                }).ToList(),
                Shipping = MapShipping(order.ShipToAddress),
                Card = paypalCard,
                VaultId = vaultId
            }, cancellationToken);
        }
        catch (PayerActionRequiredException)
        {
            throw;
        }
        catch (PaymentException ex) when (ex.StatusCode is >= 400 and < 500 && ex.StatusCode is not (408 or 429))
        {
            order.ClearPayRequestId();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw;
        }

        var authorizedAmount = decimal.Round(authorization.Amount, 2, MidpointRounding.AwayFromZero);
        if (authorizedAmount != amount)
        {
            _logger.LogWarning(
                "PayPal authorized {Authorized} {Currency} for order {OrderId} whose total is {Total}.",
                authorizedAmount, currency, order.Id, amount);
        }

        order.MarkAuthorized(
            authorization.PaypalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.ExpirationTime,
            authorization.CreateTime,
            currency,
            invoiceId);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be fulfilled.", 409);
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.PaypalAuthorizationId))
        {
            throw new PaymentException("Only an authorized order can be fulfilled. The shopper must pay first.", 409);
        }

        var currency = order.Currency ?? RequireCurrency();
        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        var authorizationId = await EnsureFreshAuthorizationAsync(order, amount, currency, cancellationToken);

        order.EnsureCaptureRequestId($"eshop-capture-{order.Id}-{Guid.NewGuid():N}");
        await _orderRepository.UpdateAsync(order, cancellationToken);

        PayPalCaptureResult capture;
        try
        {
            capture = await _paypal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                currency,
                order.InvoiceId ?? BuildInvoiceId(order.Id),
                order.CaptureRequestId!,
                cancellationToken);
        }
        catch (PaymentException ex) when (IsStaleAuthorization(ex))
        {
            authorizationId = await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
            capture = await _paypal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                currency,
                order.InvoiceId ?? BuildInvoiceId(order.Id),
                $"{order.CaptureRequestId}-retry",
                cancellationToken);
        }

        order.MarkFulfilled(
            capture.CaptureId,
            capture.Status,
            decimal.Round(capture.CapturedAmount, 2, MidpointRounding.AwayFromZero),
            capture.PaypalFee.HasValue ? decimal.Round(capture.PaypalFee.Value, 2, MidpointRounding.AwayFromZero) : null,
            capture.NetAmount.HasValue ? decimal.Round(capture.NetAmount.Value, 2, MidpointRounding.AwayFromZero) : null);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
        }

        if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(order.PaypalAuthorizationId))
        {
            try
            {
                await _paypal.VoidAuthorizationAsync(
                    order.PaypalAuthorizationId,
                    $"eshop-void-{order.Id}-{Guid.NewGuid():N}",
                    cancellationToken);
            }
            catch (PaymentException ex) when (ex.Message.Contains("VOIDED", StringComparison.OrdinalIgnoreCase))
            {
                // Already released at PayPal; continue to mark cancelled locally.
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("Refunds require an idempotencyKey so a retry cannot refund twice.");
        }

        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw new PaymentException("Refunds can only be issued after the order has been fulfilled.", 409);
        }

        if (string.IsNullOrEmpty(order.PaypalCaptureId))
        {
            throw new PaymentException("This order has no captured PayPal payment to refund.", 409);
        }

        var remaining = decimal.Round(order.RemainingRefundable(), 2, MidpointRounding.AwayFromZero);
        if (remaining <= 0m)
        {
            throw new PaymentException("This order has already been refunded in full.", 409);
        }

        var refundAmount = amount.HasValue
            ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
            : remaining;

        if (refundAmount <= 0m)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund of {refundAmount.ToString("0.00", CultureInfo.InvariantCulture)} exceeds the remaining captured amount of {remaining.ToString("0.00", CultureInfo.InvariantCulture)}.");
        }

        var currency = order.Currency ?? RequireCurrency();
        var paypalRefund = await _paypal.RefundCaptureAsync(
            order.PaypalCaptureId,
            refundAmount == remaining && !amount.HasValue ? null : refundAmount,
            currency,
            $"ESHOP-{order.Id}-R-{SanitizeIdempotency(idempotencyKey)}",
            order.Id.ToString(CultureInfo.InvariantCulture),
            $"eshop-refund-{order.Id}-{idempotencyKey}",
            cancellationToken);

        var recordedAmount = paypalRefund.Amount > 0m ? paypalRefund.Amount : refundAmount;
        var refund = new OrderRefund(
            paypalRefund.RefundId,
            paypalRefund.Status,
            decimal.Round(recordedAmount, 2, MidpointRounding.AwayFromZero),
            paypalRefund.Currency.Length > 0 ? paypalRefund.Currency : currency,
            idempotencyKey);

        order.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders.OrderByDescending(o => o.OrderDate).ToList();
    }

    private async Task<string> EnsureFreshAuthorizationAsync(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var authorizationId = order.PaypalAuthorizationId!;
        if (order.AuthorizedAt.HasValue &&
            DateTimeOffset.UtcNow - order.AuthorizedAt.Value >= AuthorizationRenewalHorizon)
        {
            throw CreateUnrenewableAuthorizationError(order);
        }

        PayPalAuthorizationResult current;
        try
        {
            current = await _paypal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException)
        {
            return authorizationId;
        }

        order.UpdateAuthorization(current.AuthorizationId, current.Status, current.ExpirationTime);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        if (string.Equals(current.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(current.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"The PayPal authorization is {current.Status} and cannot be captured. Ask the shopper to pay again.",
                409);
        }

        if (string.Equals(current.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(current.Status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            return current.AuthorizationId;
        }

        var stale = current.ExpirationTime.HasValue && current.ExpirationTime.Value <= DateTimeOffset.UtcNow.AddMinutes(5);
        if (!stale)
        {
            return current.AuthorizationId;
        }

        return await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
    }

    private async Task<string> RenewAuthorizationAsync(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        if (order.AuthorizedAt.HasValue &&
            DateTimeOffset.UtcNow - order.AuthorizedAt.Value >= AuthorizationRenewalHorizon)
        {
            throw CreateUnrenewableAuthorizationError(order);
        }

        try
        {
            var renewed = await _paypal.ReauthorizeAsync(
                order.PaypalAuthorizationId!,
                amount,
                currency,
                $"eshop-reauth-{order.Id}-{Guid.NewGuid():N}",
                cancellationToken);

            order.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new AuthorizationExpiredException(
                "The PayPal authorization has expired and could not be renewed. " +
                "Ask the shopper to pay again, then fulfil the new authorization. " +
                $"PayPal said: {ex.Message}");
        }
    }

    private static AuthorizationExpiredException CreateUnrenewableAuthorizationError(Order order)
    {
        return new AuthorizationExpiredException(
            "The PayPal authorization is older than 29 days and can no longer be renewed. " +
            "Ask the shopper to pay again before fulfilment. " +
            $"Original authorization id: {order.PaypalAuthorizationId}.");
    }

    private static bool IsStaleAuthorization(PaymentException exception)
    {
        var message = exception.Message;
        return message.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
               || message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
               || message.Contains("AUTH_EXPIRED", StringComparison.OrdinalIgnoreCase)
               || message.Contains("honor period", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithPaymentSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new BuyerOrderByIdWithPaymentSpec(orderId, buyerId), cancellationToken);
        if (order == null)
        {
            var exists = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithPaymentSpec(orderId), cancellationToken);
            if (exists != null)
            {
                throw new PaymentException("You cannot act on another shopper's order.", 403);
            }

            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private string RequireCurrency()
    {
        var currency = _paymentConfiguration.Currency;
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new PaymentException("PayPal:Currency is not configured.", 500);
        }

        return currency.Trim().ToUpperInvariant();
    }

    private static string BuildInvoiceId(int orderId) => $"ESHOP-{orderId}";

    private static Address DefaultAddress() =>
        new("123 Main St.", "Kent", "OH", "US", "44240");

    private static PayPalCardDetails MapCard(CardPayment card)
    {
        var number = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (number.Length is < 13 or > 19)
        {
            throw new PaymentException("Card number must contain 13 to 19 digits.");
        }

        if (string.IsNullOrWhiteSpace(card.Expiry) || card.Expiry.Length != 7)
        {
            throw new PaymentException("Card expiry must be in YYYY-MM format, for example 2028-04.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentException("Card security code is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentException("Cardholder name is required.");
        }

        return new PayPalCardDetails
        {
            Number = number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = MapShipping(card.BillingAddress ?? DefaultAddress())
        };
    }

    private static PayPalShippingAddress MapShipping(Address address)
    {
        return new PayPalShippingAddress
        {
            AddressLine1 = address.Street,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.ZipCode,
            CountryCode = ToCountryCode(address.Country)
        };
    }

    private static string ToCountryCode(string country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return "US";
        }

        if (country.Length == 2)
        {
            return country.ToUpperInvariant();
        }

        return country.Trim().ToUpperInvariant() switch
        {
            "USA" or "UNITED STATES" or "UNITED STATES OF AMERICA" => "US",
            "UNITED KINGDOM" or "GREAT BRITAIN" or "UK" => "GB",
            "CANADA" => "CA",
            _ => country.Length >= 2 ? country[..2].ToUpperInvariant() : "US"
        };
    }

    private static string SanitizeIdempotency(string key)
    {
        var filtered = new string(key.Where(char.IsLetterOrDigit).ToArray());
        if (filtered.Length > 20)
        {
            filtered = filtered[..20];
        }

        return string.IsNullOrEmpty(filtered) ? "R" : filtered;
    }
}
