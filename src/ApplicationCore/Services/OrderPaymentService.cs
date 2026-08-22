using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShipTo = new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentSettings _paymentSettings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        IPaymentSettings paymentSettings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _paymentSettings = paymentSettings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineRequest> lines,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException("A signed-in shopper is required to place an order.");
        }

        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.");
        }

        foreach (var line in lines)
        {
            if (line.CatalogItemId <= 0 || line.Quantity <= 0)
            {
                throw new PaymentException("Each order line must include a catalog item id and a quantity greater than zero.");
            }
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = catalogItemIds.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var quantities = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var orderItems = quantities.Select(pair =>
        {
            var catalogItem = catalogById[pair.Key];
            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrWhiteSpace(pictureUri))
            {
                pictureUri = "n/a";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, pair.Value);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, orderItems);
        if (!string.IsNullOrWhiteSpace(_paymentSettings.Currency))
        {
            order.SetCurrency(_paymentSettings.Currency);
        }

        if (order.Total() <= 0)
        {
            throw new PaymentException("The order total must be greater than zero.");
        }

        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for buyer {BuyerId} totaling {Total}", order.Id, buyerId, order.Total());
        return order;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        order.EnsureOwnedBy(buyerId);

        if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            _logger.LogInformation("Pay is idempotent for order {OrderId}; authorization {AuthorizationId} already exists", order.Id, order.PayPalAuthorizationId);
            return order;
        }

        order.EnsureCanPay();
        EnsureCurrency(order);

        if (card is null && paymentMethodId is null)
        {
            throw new PaymentException("Provide card details or a saved paymentMethodId.");
        }

        if (card is not null && paymentMethodId is not null)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.");
        }

        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        var instanceKey = order.OrderDate.UtcTicks.ToString();
        PaymentAuthorizationResult authorization;

        if (paymentMethodId is not null)
        {
            var vaultId = await ResolveVaultIdAsync(buyerId, paymentMethodId.Value, cancellationToken);
            authorization = await _paymentGateway.AuthorizeSavedCardAsync(order.Id, amount, order.Currency!, vaultId, instanceKey, cancellationToken);
        }
        else
        {
            ValidateCard(card!);
            authorization = await _paymentGateway.AuthorizeCardAsync(order.Id, amount, order.Currency!, card!, instanceKey, cancellationToken);
        }

        order.AttachPayPalOrder(authorization.PayPalOrderId);
        order.RecordAuthorization(
            authorization.AuthorizationId,
            authorization.Status,
            authorization.Created,
            authorization.Expiration,
            order.Currency!);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Authorized order {OrderId} with PayPal authorization {AuthorizationId}", order.Id, authorization.AuthorizationId);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded
            && !string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            _logger.LogInformation("Fulfil is idempotent for order {OrderId}; capture {CaptureId} already exists", order.Id, order.PayPalCaptureId);
            return order;
        }

        order.EnsureCanFulfil();
        if (string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            throw new PaymentConflictException("This order has no payment hold to capture. The shopper must pay first.");
        }

        EnsureCurrency(order);
        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        var authorizationId = order.PayPalAuthorizationId;
        var instanceKey = order.OrderDate.UtcTicks.ToString();

        var details = await _paymentGateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        if (string.Equals(details.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(details.Status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            var existingCapture = !string.IsNullOrEmpty(order.PayPalCaptureId)
                ? await _paymentGateway.GetCaptureAsync(order.PayPalCaptureId, cancellationToken)
                : null;
            if (existingCapture is not null)
            {
                order.RecordCapture(existingCapture.CaptureId, existingCapture.Status, existingCapture.CapturedAmount, existingCapture.PayPalFee, existingCapture.NetAmount);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return order;
            }
        }

        if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(details.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationNotRenewableException(
                $"The payment hold is {details.Status} and cannot be captured or renewed. Ask the shopper to pay the order again, then fulfil the new authorization.");
        }

        if (details.Expiration is not null && details.Expiration <= DateTimeOffset.UtcNow)
        {
            throw new AuthorizationNotRenewableException(
                "The payment hold has expired and PayPal will not renew it. Ask the shopper to pay again so a new hold can be placed, then retry fulfilment.");
        }

        var honorPeriodEnded = details.Created is not null && details.Created.Value.AddDays(3) <= DateTimeOffset.UtcNow;
        if (honorPeriodEnded)
        {
            authorizationId = await RenewAuthorizationAsync(order, amount, instanceKey, cancellationToken);
        }

        PaymentCaptureResult capture;
        try
        {
            capture = await _paymentGateway.CaptureAsync(
                authorizationId,
                amount,
                order.Currency!,
                FulfilIdempotencyKey(order.Id, instanceKey),
                cancellationToken);
        }
        catch (PaymentGatewayException ex) when (IsStaleAuthorization(ex))
        {
            authorizationId = await RenewAuthorizationAsync(order, amount, instanceKey, cancellationToken);
            capture = await _paymentGateway.CaptureAsync(
                authorizationId,
                amount,
                order.Currency!,
                FulfilIdempotencyKey(order.Id, instanceKey) + "-renewed",
                cancellationToken);
        }

        if (capture.PayPalFee is null || capture.NetAmount is null)
        {
            capture = await _paymentGateway.GetCaptureAsync(capture.CaptureId, cancellationToken);
        }

        order.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Fulfilled order {OrderId} with PayPal capture {CaptureId}", order.Id, capture.CaptureId);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        order.EnsureCanCancel();

        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId))
        {
            try
            {
                await _paymentGateway.VoidAuthorizationAsync(
                    order.PayPalAuthorizationId,
                    $"eshop-cancel-{order.Id}-{order.OrderDate.UtcTicks}",
                    cancellationToken);
            }
            catch (PaymentGatewayException ex) when (IsAlreadyVoided(ex))
            {
                _logger.LogInformation("PayPal authorization {AuthorizationId} was already released", order.PayPalAuthorizationId);
            }
        }

        order.Cancel("VOIDED");
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}", order.Id);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund idempotency key is required.");
        }

        var order = await GetOrderAsync(orderId, cancellationToken);
        order.EnsureOwnedBy(buyerId);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw new PaymentConflictException("Refunds can only be issued after the order has been fulfilled.");
        }

        if (string.IsNullOrEmpty(order.PayPalCaptureId) || order.CapturedAmount is null)
        {
            throw new PaymentConflictException("This order has no captured payment to refund.");
        }

        EnsureCurrency(order);
        var remaining = order.RefundableRemaining();
        if (remaining <= 0)
        {
            throw new PaymentConflictException("This order has already been refunded in full.");
        }

        var refundAmount = amount.HasValue
            ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
            : remaining;

        if (refundAmount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentConflictException($"Refund of {refundAmount} exceeds the remaining captured amount of {remaining}.");
        }

        var result = await _paymentGateway.RefundAsync(
            order.PayPalCaptureId,
            refundAmount,
            order.Currency!,
            idempotencyKey,
            cancellationToken);

        var refund = order.RecordRefund(result.RefundId, result.Status, idempotencyKey, result.Amount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Refunded {Amount} on order {OrderId} as PayPal refund {RefundId}", result.Amount, order.Id, result.RefundId);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var spec = new CustomerOrdersWithItemsSpecification(buyerId);
        return await _orderRepository.ListAsync(spec, cancellationToken);
    }

    public async Task<Order> GetMyOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken = default)
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
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<string> ResolveVaultIdAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpec(buyerId), cancellationToken);
        var method = buyer?.GetPaymentMethod(paymentMethodId);
        if (buyer is null || method is null || string.IsNullOrEmpty(method.CardId))
        {
            throw new PaymentNotFoundException("The saved card was not found for this shopper.");
        }

        return method.CardId;
    }

    private async Task<string> RenewAuthorizationAsync(Order order, decimal amount, string instanceKey, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(
                order.PayPalAuthorizationId!,
                amount,
                order.Currency!,
                $"eshop-reauth-{order.Id}-{instanceKey}",
                cancellationToken);

            order.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Created, renewed.Expiration);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Renewed payment hold for order {OrderId}; new authorization {AuthorizationId}", order.Id, renewed.AuthorizationId);
            return renewed.AuthorizationId;
        }
        catch (PaymentGatewayException ex) when (IsNotRenewable(ex))
        {
            throw new AuthorizationNotRenewableException(
                "The payment hold is stale and PayPal will not renew it. Ask the shopper to pay again so a new hold can be placed, then retry fulfilment.");
        }
    }

    private void EnsureCurrency(Order order)
    {
        if (string.IsNullOrWhiteSpace(order.Currency))
        {
            if (string.IsNullOrWhiteSpace(_paymentSettings.Currency))
            {
                throw new PaymentConfigurationException("PayPal:Currency is not configured.");
            }

            order.SetCurrency(_paymentSettings.Currency);
        }
    }

    private static void ValidateCard(CardPaymentDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || card.Number.Length is < 13 or > 19)
        {
            throw new PaymentException("A valid card number is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new PaymentException("Card expiry (YYYY-MM) is required.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentException("Card security code is required.");
        }
    }

    private static string FulfilIdempotencyKey(int orderId, string instanceKey) => $"eshop-fulfil-{orderId}-{instanceKey}";

    private static bool IsStaleAuthorization(PaymentGatewayException ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("EXPIRED_AUTHORIZATION", StringComparison.OrdinalIgnoreCase)
            || message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("DECINED_EXPIRED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNotRenewable(PaymentGatewayException ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("REAUTHORIZATION_NOT_ALLOWED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("AUTHORIZATION_ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("MAX_NUMBER_OF_REAUTHORIZATIONS", StringComparison.OrdinalIgnoreCase)
            || ex.StatusCode is 404 or 422;
    }

    private static bool IsAlreadyVoided(PaymentGatewayException ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("PREVIOUSLY_VOIDED", StringComparison.OrdinalIgnoreCase)
            || ex.StatusCode == 404;
    }
}
