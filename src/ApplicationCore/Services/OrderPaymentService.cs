using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalOptions _payPalOptions;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        IOptions<PayPalOptions> payPalOptions,
        ILogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _payPalOptions = payPalOptions.Value;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address shipTo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required.", HttpStatusCode.Unauthorized);
        }

        if (items is null || items.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.");
        }

        var ids = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException("Quantity must be greater than zero.");
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.", HttpStatusCode.NotFound);
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipTo, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

            if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentException("A cancelled order cannot be paid.", HttpStatusCode.Conflict);
            }

            if (order.Status != OrderStatus.AwaitingPayment)
            {
                throw new PaymentException($"Order {orderId} cannot be paid in its current state ({order.Status}).", HttpStatusCode.Conflict);
            }

            var hasCard = request.Card is not null;
            var hasSaved = request.PaymentMethodId.HasValue;
            if (hasCard == hasSaved)
            {
                throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.");
            }

            string? vaultId = null;
            int? savedCardId = null;
            if (hasSaved)
            {
                var saved = await _savedCardRepository.FirstOrDefaultAsync(
                    new SavedCardByIdSpec(request.PaymentMethodId!.Value, buyerId),
                    cancellationToken);
                if (saved is null)
                {
                    throw new PaymentException("The saved card was not found or is no longer usable.", HttpStatusCode.NotFound);
                }

                vaultId = saved.PayPalPaymentTokenId;
                savedCardId = saved.Id;
            }

            var amount = FormatAmount(order.Total());
            var currency = RequireCurrency();
            var requestId = $"eshop-pay-{order.Id}-{Guid.NewGuid():N}";

            PayPalCheckoutOrder checkout;
            try
            {
                checkout = await _payPal.CreateAuthorizedCardOrderAsync(
                    new PayPalAuthorizeOrderRequest
                    {
                        CurrencyCode = currency,
                        Amount = amount,
                        CustomId = order.Id.ToString(CultureInfo.InvariantCulture),
                        InvoiceId = $"ESHOP-{order.Id}-{Guid.NewGuid():N}"[..24],
                        Description = $"eShopOnWeb order {order.Id}",
                        Card = hasCard ? ToPayPalCard(request.Card!) : null,
                        VaultId = vaultId,
                        ShippingAddress = ToPayPalAddress(order.ShipToAddress)
                    },
                    requestId,
                    cancellationToken);
            }
            catch (PayerActionRequiredException)
            {
                throw;
            }

            if (checkout.Authorization is null)
            {
                throw new PaymentException("PayPal did not authorize the payment.", HttpStatusCode.BadGateway);
            }

            var authorizedAmount = checkout.Authorization.Amount;
            if (!string.IsNullOrWhiteSpace(authorizedAmount) &&
                !string.Equals(authorizedAmount, amount, StringComparison.Ordinal))
            {
                throw new PaymentException(
                    $"PayPal authorized {authorizedAmount} {currency} but the order total is {amount} {currency}.",
                    HttpStatusCode.BadGateway);
            }

            var payment = new OrderPayment(checkout.Id, currency, requestId);
            payment.RecordAuthorization(
                checkout.Authorization.Id,
                checkout.Authorization.Status,
                checkout.Authorization.ExpirationTime,
                DateTimeOffset.UtcNow);
            if (savedCardId.HasValue)
            {
                payment.AssociateSavedCard(savedCardId.Value);
            }

            order.RecordAuthorization(payment);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation(
                "Authorized PayPal order {PayPalOrderId} authorization {AuthorizationId} for eShop order {OrderId}.",
                checkout.Id,
                checkout.Authorization.Id,
                order.Id);

            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentException("A cancelled order cannot be fulfilled.", HttpStatusCode.Conflict);
            }

            if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId is null)
            {
                throw new PaymentException("The order has no payment authorization to capture.", HttpStatusCode.Conflict);
            }

            var payment = order.Payment;
            var currency = payment.Currency ?? RequireCurrency();
            var amount = FormatAmount(order.Total());
            var authorizationId = payment.AuthorizationId;

            PayPalAuthorizationDetails authorization;
            try
            {
                authorization = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
            }
            catch (PaymentException ex) when (IsExpiredAuthorization(ex))
            {
                authorizationId = await RenewAuthorizationAsync(order, payment, currency, amount, cancellationToken);
                authorization = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
            }

            payment.UpdateAuthorizationStatus(authorization.Status, authorization.ExpirationTime);

            if (string.Equals(authorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentException(
                    $"The payment authorization is {authorization.Status} and cannot be captured. Ask the shopper to pay again.",
                    HttpStatusCode.Conflict);
            }

            if (string.Equals(authorization.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(payment.CaptureId))
            {
                order.RecordFulfilment();
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return order;
            }

            if (IsAuthorizationStale(authorization))
            {
                authorizationId = await RenewAuthorizationAsync(order, payment, currency, amount, cancellationToken);
            }

            var fulfilRequestId = payment.FulfilRequestId ?? $"eshop-fulfil-{order.Id}-{Guid.NewGuid():N}";
            PayPalCaptureDetails capture;
            try
            {
                capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId,
                    currency,
                    amount,
                    $"ESHOP-{order.Id}-C{Guid.NewGuid():N}"[..24],
                    fulfilRequestId,
                    cancellationToken);
            }
            catch (PaymentException ex) when (IsExpiredAuthorization(ex))
            {
                authorizationId = await RenewAuthorizationAsync(order, payment, currency, amount, cancellationToken);
                capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId,
                    currency,
                    amount,
                    $"ESHOP-{order.Id}-C{Guid.NewGuid():N}"[..24],
                    $"{fulfilRequestId}-retry",
                    cancellationToken);
            }

            if (capture.PaypalFee is null || capture.NetAmount is null)
            {
                capture = await _payPal.GetCaptureAsync(capture.Id, cancellationToken);
            }

            payment.RecordCapture(
                capture.Id,
                capture.Status,
                capture.CapturedAmount ?? order.Total(),
                capture.PaypalFee,
                capture.NetAmount,
                DateTimeOffset.UtcNow,
                fulfilRequestId);
            order.RecordFulfilment();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation(
                "Captured PayPal authorization {AuthorizationId} as {CaptureId} for eShop order {OrderId}.",
                authorizationId,
                capture.Id,
                order.Id);

            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                throw new PaymentException("A fulfilled order cannot be cancelled; issue a refund instead.", HttpStatusCode.Conflict);
            }

            if (order.Payment?.AuthorizationId is not null)
            {
                var cancelRequestId = order.Payment.CancelRequestId ?? $"eshop-cancel-{order.Id}-{Guid.NewGuid():N}";
                try
                {
                    await _payPal.VoidAuthorizationAsync(order.Payment.AuthorizationId, cancelRequestId, cancellationToken);
                    order.Payment.RecordVoid("VOIDED", cancelRequestId);
                }
                catch (PaymentException ex) when (ex.StatusCode == HttpStatusCode.UnprocessableEntity ||
                                                  ex.Message.Contains("VOIDED", StringComparison.OrdinalIgnoreCase))
                {
                    order.Payment.RecordVoid("VOIDED", cancelRequestId);
                }
            }

            order.RecordCancellation();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderRefund> RefundAsync(
        string buyerId,
        int orderId,
        RefundOrderRequest request,
        bool isAdministrator,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException("A refund idempotencyKey is required.");
        }

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (!isAdministrator && !order.BelongsTo(buyerId))
            {
                throw new PaymentException("The order was not found.", HttpStatusCode.NotFound);
            }

            var existing = order.FindRefundByIdempotencyKey(request.IdempotencyKey);
            if (existing is not null)
            {
                return existing;
            }

            if (order.Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded)
            {
                throw new PaymentException("Only a fulfilled order can be refunded.", HttpStatusCode.Conflict);
            }

            if (string.IsNullOrWhiteSpace(order.Payment?.CaptureId) || order.Payment.CapturedAmount is null)
            {
                throw new PaymentException("The order has no captured payment to refund.", HttpStatusCode.Conflict);
            }

            var remaining = order.RemainingRefundable();
            var amount = request.Amount ?? remaining;
            if (amount <= 0m)
            {
                throw new PaymentException("The refund amount must be greater than zero.");
            }

            if (amount > remaining)
            {
                throw new PaymentException(
                    $"The refund of {FormatAmount(amount)} exceeds the remaining refundable amount of {FormatAmount(remaining)}.",
                    HttpStatusCode.Conflict);
            }

            var currency = order.Payment.Currency ?? RequireCurrency();
            var isFull = amount == remaining && remaining == order.Payment.CapturedAmount;
            var refund = await _payPal.RefundCaptureAsync(
                order.Payment.CaptureId,
                currency,
                isFull && request.Amount is null ? null : FormatAmount(amount),
                request.IdempotencyKey,
                cancellationToken);

            var recorded = new OrderRefund(
                refund.Id,
                refund.Status,
                refund.Amount > 0 ? refund.Amount : amount,
                refund.Currency ?? currency,
                request.IdempotencyKey);
            order.AddRefund(recorded);
            if (!string.IsNullOrWhiteSpace(refund.Status))
            {
                order.Payment.UpdateCaptureStatus(
                    order.RemainingRefundable() <= 0m ? "REFUNDED" : "PARTIALLY_REFUNDED");
            }

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return recorded;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpec(buyerId), cancellationToken);
    }

    public Task<Order> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default) =>
        GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

    private async Task<string> RenewAuthorizationAsync(
        Order order,
        OrderPayment payment,
        string currency,
        string amount,
        CancellationToken cancellationToken)
    {
        if (payment.AuthorizationId is null)
        {
            throw new PaymentException(
                "The payment authorization is missing and cannot be renewed. Ask the shopper to pay again.",
                HttpStatusCode.Conflict);
        }

        var originalAuthorizedAt = payment.AuthorizedAt ?? order.OrderDate;
        if (DateTimeOffset.UtcNow - originalAuthorizedAt > TimeSpan.FromDays(29))
        {
            throw new PaymentException(
                "The payment authorization is older than 29 days and can no longer be renewed. Ask the shopper to pay for the order again, then fulfil the new authorization.",
                HttpStatusCode.Conflict);
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                payment.AuthorizationId,
                currency,
                amount,
                $"eshop-reauth-{order.Id}-{Guid.NewGuid():N}",
                cancellationToken);

            payment.RecordReauthorization(renewed.Id, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation(
                "Reauthorized PayPal payment {AuthorizationId} as {NewAuthorizationId} for eShop order {OrderId}.",
                payment.AuthorizationId,
                renewed.Id,
                order.Id);
            return renewed.Id;
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                "The payment authorization has gone stale and could not be renewed. Ask the shopper to pay for the order again, then fulfil the new authorization. " +
                ex.Message,
                HttpStatusCode.Conflict,
                ex.DebugId);
        }
    }

    private static bool IsAuthorizationStale(PayPalAuthorizationDetails authorization)
    {
        if (authorization.ExpirationTime is null)
        {
            return false;
        }

        return authorization.ExpirationTime <= DateTimeOffset.UtcNow.AddMinutes(5);
    }

    private static bool IsExpiredAuthorization(PaymentException ex)
    {
        var text = ex.Message;
        return text.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("AUTH_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("CANNOT_BE_CAPTURED", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!order.BelongsTo(buyerId))
        {
            throw new PaymentException("The order was not found.", HttpStatusCode.NotFound);
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderByIdWithPaymentSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException("The order was not found.", HttpStatusCode.NotFound);
        }

        return order;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_payPalOptions.Currency))
        {
            throw new PaymentException("PayPal:Currency is not configured.", HttpStatusCode.ServiceUnavailable);
        }

        return _payPalOptions.Currency;
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static PayPalCardDetails ToPayPalCard(CardPaymentRequest card) => new()
    {
        Name = card.Name,
        Number = NormalizeCardNumber(card.Number),
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = new PayPalBillingAddress
        {
            AddressLine1 = card.BillingAddress.AddressLine1,
            AddressLine2 = card.BillingAddress.AddressLine2,
            AdminArea2 = card.BillingAddress.AdminArea2,
            AdminArea1 = card.BillingAddress.AdminArea1,
            PostalCode = card.BillingAddress.PostalCode,
            CountryCode = card.BillingAddress.CountryCode
        }
    };

    private static PayPalBillingAddress ToPayPalAddress(Address address) => new()
    {
        AddressLine1 = address.Street,
        AdminArea2 = address.City,
        AdminArea1 = address.State,
        PostalCode = address.ZipCode,
        CountryCode = NormalizeCountry(address.Country)
    };

    private static string NormalizeCardNumber(string number) =>
        new string(number.Where(char.IsDigit).ToArray());

    private static string NormalizeCountry(string country)
    {
        if (string.Equals(country, "USA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(country, "United States", StringComparison.OrdinalIgnoreCase))
        {
            return "US";
        }

        return country.Length == 2 ? country.ToUpperInvariant() : country;
    }
}
