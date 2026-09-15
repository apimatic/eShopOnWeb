using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentsClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalOptions _payPalOptions;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalPaymentsClient payPal,
        IUriComposer uriComposer,
        PayPalOptions payPalOptions,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _payPalOptions = payPalOptions;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        OrderAddressInput? shipTo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw OrderPaymentException.Forbidden("A signed-in shopper is required to place an order.");
        }

        if (lines == null || lines.Count == 0)
        {
            throw OrderPaymentException.BadRequest("At least one catalog item is required.");
        }

        var merged = lines
            .GroupBy(l => l.CatalogItemId)
            .Select(g => new CatalogOrderLine(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (merged.Any(l => l.CatalogItemId <= 0 || l.Quantity <= 0))
        {
            throw OrderPaymentException.BadRequest("Each line must have a catalog item id and a quantity greater than zero.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(merged.Select(l => l.CatalogItemId).ToArray()),
            cancellationToken);

        var missing = merged.Select(l => l.CatalogItemId).Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw OrderPaymentException.NotFound($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = merged.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = ToAddress(shipTo);
        var order = new Order(buyerId, address, items);
        order.SetPaymentCurrency(RequireCurrency());

        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentInput? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.Status == OrderStatus.Authorized)
        {
            return order;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw OrderPaymentException.Conflict($"Order {orderId} cannot be paid from status {order.Status}.");
        }

        if (card != null && paymentMethodId != null)
        {
            throw OrderPaymentException.BadRequest("Send either card details or a saved paymentMethodId, not both.");
        }

        if (card == null && paymentMethodId == null)
        {
            throw OrderPaymentException.BadRequest("A card or a saved paymentMethodId is required to pay.");
        }

        var amount = new PayPalMoney(RequireCurrency(), order.Total());
        if (PayPalMoneyFormat.ToCents(amount.Value) <= 0)
        {
            throw OrderPaymentException.BadRequest("The order total must be greater than zero to authorize payment.");
        }

        order.SetPaymentCurrency(amount.CurrencyCode);
        var requestId = order.EnsureAuthorizeRequestId();
        var invoiceId = order.EnsurePayPalInvoiceId();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        PayPalAuthorizationResult authorization;
        try
        {
            if (paymentMethodId != null)
            {
                var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdSpecification(paymentMethodId.Value),
                    cancellationToken);

                if (saved == null || saved.IsDeleted || !saved.BelongsTo(buyerId))
                {
                    throw OrderPaymentException.NotFound("Saved payment method was not found.");
                }

                authorization = await _payPal.AuthorizeVaultedCardPaymentAsync(
                    invoiceId,
                    invoiceId,
                    amount,
                    saved.PayPalPaymentTokenId,
                    requestId,
                    cancellationToken);
            }
            else
            {
                authorization = await _payPal.AuthorizeCardPaymentAsync(
                    invoiceId,
                    invoiceId,
                    amount,
                    ToCardRequest(card!),
                    requestId,
                    cancellationToken);
            }

            if (PayPalMoneyFormat.ToCents(authorization.Amount.Value) != PayPalMoneyFormat.ToCents(amount.Value) ||
                !string.Equals(authorization.Amount.CurrencyCode, amount.CurrencyCode, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "PayPal authorized {Authorized} {Currency} for order {OrderId} but the order total is {Total} {OrderCurrency}. Voiding the hold.",
                    authorization.Amount.Value,
                    authorization.Amount.CurrencyCode,
                    order.Id,
                    amount.Value,
                    amount.CurrencyCode);

                await _payPal.VoidAuthorizationAsync(authorization.AuthorizationId, $"eshop-void-mismatch-{order.Id}-{Guid.NewGuid():N}", cancellationToken);
                order.ClearAuthorizeRequestId();
                throw OrderPaymentException.Conflict(
                    "PayPal did not hold an amount equal to the order total. The authorization was released; try paying again.");
            }

            order.RecordAuthorization(
                authorization.PayPalOrderId,
                authorization.AuthorizationId,
                authorization.AuthorizationStatus,
                authorization.CreatedAt,
                authorization.ExpiresAt);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
        catch
        {
            if (string.IsNullOrEmpty(order.Payment.AuthorizationId))
            {
                order.ClearAuthorizeRequestId();
                order.ClearPayPalInvoiceId();
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }

            throw;
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.Payment.AuthorizationId))
        {
            throw OrderPaymentException.Conflict(
                $"Order {orderId} cannot be fulfilled from status {order.Status}. Authorize payment first.");
        }

        var amount = new PayPalMoney(RequireCurrency(), order.Total());
        var authorizationId = order.Payment.AuthorizationId;
        var captureRequestId = order.EnsureCaptureRequestId();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        try
        {
            authorizationId = await EnsureFreshAuthorizationAsync(order, amount, cancellationToken);
            var capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                captureRequestId,
                cancellationToken);

            order.RecordCapture(
                capture.CaptureId,
                capture.Status,
                capture.CapturedAmount,
                capture.PayPalFee,
                capture.NetProceeds);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
        catch (OrderPaymentException ex) when (ex is not PayerActionRequiredException)
        {
            if (IsAuthorizationStaleError(ex.Message))
            {
                authorizationId = await RenewAuthorizationOrThrowAsync(order, amount, cancellationToken);
                var capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId,
                    amount,
                    captureRequestId,
                    cancellationToken);

                order.RecordCapture(
                    capture.CaptureId,
                    capture.Status,
                    capture.CapturedAmount,
                    capture.PayPalFee,
                    capture.NetProceeds);

                await _orderRepository.UpdateAsync(order, cancellationToken);
                return order;
            }

            if (string.IsNullOrEmpty(order.Payment.CaptureId))
            {
                order.ClearCaptureRequestId();
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }

            throw;
        }
        catch
        {
            if (string.IsNullOrEmpty(order.Payment.CaptureId))
            {
                order.ClearCaptureRequestId();
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }

            throw;
        }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(order.Payment.AuthorizationId))
        {
            await _payPal.VoidAuthorizationAsync(
                order.Payment.AuthorizationId,
                $"eshop-void-{order.Id}-{Guid.NewGuid():N}",
                cancellationToken);
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, PaymentRefund Refund)> RefundAsync(
        int orderId,
        string buyerId,
        bool isAdministrator,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw OrderPaymentException.BadRequest("An idempotencyKey is required for refunds.");
        }

        var order = isAdministrator
            ? await GetOrderAsync(orderId, cancellationToken)
            : await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        var existing = order.Payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return (order, existing);
        }

        if (string.IsNullOrEmpty(order.Payment.CaptureId))
        {
            throw OrderPaymentException.Conflict($"Order {orderId} has no captured payment to refund.");
        }

        var refundAmount = amount ?? order.Payment.RemainingRefundable;
        if (PayPalMoneyFormat.ToCents(refundAmount) <= 0)
        {
            throw OrderPaymentException.BadRequest("A refund amount greater than zero is required.");
        }

        if (PayPalMoneyFormat.ToCents(refundAmount) > PayPalMoneyFormat.ToCents(order.Payment.RemainingRefundable))
        {
            throw OrderPaymentException.BadRequest(
                $"Refund of {PayPalMoneyFormat.ToApiValue(refundAmount)} exceeds the remaining refundable amount {PayPalMoneyFormat.ToApiValue(order.Payment.RemainingRefundable)}.");
        }

        var money = new PayPalMoney(order.Payment.Currency, refundAmount);
        var result = await _payPal.RefundCaptureAsync(
            order.Payment.CaptureId,
            amount.HasValue ? money : null,
            idempotencyKey,
            cancellationToken);

        var recordedAmount = result.Amount > 0 ? result.Amount : refundAmount;
        var refund = order.RecordRefund(result.RefundId, idempotencyKey, recordedAmount, result.Status);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw OrderPaymentException.Forbidden("A signed-in shopper is required.");
        }

        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    private async Task<string> EnsureFreshAuthorizationAsync(
        Order order,
        PayPalMoney amount,
        CancellationToken cancellationToken)
    {
        var authorizationId = order.Payment.AuthorizationId!;
        PayPalAuthorizationResult current;
        try
        {
            current = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (OrderPaymentException)
        {
            return await RenewAuthorizationOrThrowAsync(order, amount, cancellationToken);
        }

        order.RecordReauthorization(
            current.AuthorizationId,
            current.AuthorizationStatus,
            current.CreatedAt ?? order.Payment.AuthorizationCreatedAt,
            current.ExpiresAt);

        if (order.Payment.AuthorizationIsStale(DateTimeOffset.UtcNow) ||
            !string.Equals(current.AuthorizationStatus, "CREATED", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(current.AuthorizationStatus, "CAPTURED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.AuthorizationStatus, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
            {
                return current.AuthorizationId;
            }

            return await RenewAuthorizationOrThrowAsync(order, amount, cancellationToken);
        }

        return current.AuthorizationId;
    }

    private async Task<string> RenewAuthorizationOrThrowAsync(
        Order order,
        PayPalMoney amount,
        CancellationToken cancellationToken)
    {
        var authorizationId = order.Payment.AuthorizationId
            ?? throw OrderPaymentException.Conflict($"Order {order.Id} has no PayPal authorization to renew.");

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                amount,
                $"eshop-reauthorize-{order.Id}",
                cancellationToken);

            order.RecordReauthorization(
                renewed.AuthorizationId,
                renewed.AuthorizationStatus,
                renewed.CreatedAt,
                renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation(
                "Renewed PayPal authorization for order {OrderId}. Previous id {PreviousId}, new id {NewId}.",
                order.Id,
                authorizationId,
                renewed.AuthorizationId);
            return renewed.AuthorizationId;
        }
        catch (OrderPaymentException ex)
        {
            throw OrderPaymentException.Unprocessable(
                "The PayPal authorization for this order is no longer valid and could not be renewed. " +
                "Do not retry fulfilment against the same hold. Ask the shopper to place and pay a new order " +
                $"(PayPal authorization {authorizationId}). {ex.Message}");
        }
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!order.BelongsTo(buyerId))
        {
            throw OrderPaymentException.Forbidden("You cannot act on another shopper's order.");
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw OrderPaymentException.NotFound($"Order {orderId} was not found.");
        }

        return order;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_payPalOptions.Currency))
        {
            throw OrderPaymentException.Unprocessable("PayPal:Currency is not configured.");
        }

        return _payPalOptions.Currency.Trim().ToUpperInvariant();
    }

    private static Address ToAddress(OrderAddressInput? shipTo)
    {
        if (shipTo == null)
        {
            return new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        }

        return new Address(
            shipTo.Street,
            shipTo.City,
            shipTo.State,
            shipTo.Country,
            shipTo.ZipCode);
    }

    internal static CardAuthorizationRequest ToCardRequest(CardPaymentInput card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw OrderPaymentException.BadRequest("Card number and expiry (YYYY-MM) are required.");
        }

        CardBillingAddress? billing = null;
        if (card.BillingAddress != null)
        {
            billing = new CardBillingAddress(
                card.BillingAddress.Street,
                null,
                card.BillingAddress.State,
                card.BillingAddress.City,
                card.BillingAddress.ZipCode,
                ToCountryCode(card.BillingAddress.Country));
        }
        else
        {
            billing = new CardBillingAddress(
                "2211 N First Street",
                null,
                "CA",
                "San Jose",
                "95131",
                "US");
        }

        return new CardAuthorizationRequest(
            card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
            card.Expiry.Trim(),
            card.SecurityCode,
            card.Name,
            billing);
    }

    private static string? ToCountryCode(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return null;
        }

        return country.Trim() switch
        {
            "United States" or "USA" or "US" => "US",
            "United Kingdom" or "UK" or "GB" => "GB",
            _ when country.Trim().Length == 2 => country.Trim().ToUpperInvariant(),
            _ => country.Trim()
        };
    }

    private static bool IsAuthorizationStaleError(string message)
    {
        return message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);
    }
}
