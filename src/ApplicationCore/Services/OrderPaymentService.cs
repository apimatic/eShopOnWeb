using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan AuthorizationSafetyMargin = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxAuthorizationLifetime = TimeSpan.FromDays(30);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _payPalSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        PayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _payPalSettings = payPalSettings;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<CatalogOrderLine> lines,
        Address shippingAddress,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("At least one catalog item is required.", 400);
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException("Quantity must be greater than zero.", 400);
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            throw new PaymentException("One or more catalog items were not found.", 400);
        }

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shippingAddress, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null == paymentMethodId is null)
        {
            throw new PaymentException("Provide either card details or a saved payment method, not both.", 400);
        }

        var order = await GetBuyerOrder(orderId, buyerId, cancellationToken);

        if (order.Status == OrderPaymentStatus.Authorized ||
            order.Status == OrderPaymentStatus.Fulfilled ||
            order.Status == OrderPaymentStatus.PartiallyRefunded ||
            order.Status == OrderPaymentStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException("A cancelled order cannot be paid.", 409);
        }

        var currency = RequireCurrency();
        var amount = order.Total();
        if (amount <= 0)
        {
            throw new PaymentException("Order total must be greater than zero.", 400);
        }

        order.EnsurePayIdempotencyKeys();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        string vaultId = string.Empty;
        if (paymentMethodId is int methodId)
        {
            var buyer = await GetBuyer(buyerId, cancellationToken);
            var method = buyer.GetPaymentMethod(methodId);
            if (method is null || string.IsNullOrEmpty(method.CardId))
            {
                throw new PaymentException("Saved payment method was not found.", 404);
            }

            vaultId = method.CardId;
        }

        var invoiceId = UniqueInvoiceId(order);
        if (string.IsNullOrEmpty(order.PayPalOrderId))
        {
            var payPalOrderId = await _paymentGateway.CreateAuthorizedOrderAsync(
                amount,
                currency,
                invoiceId,
                UniqueInvoiceId(order),
                order.CreateOrderRequestId!,
                cancellationToken);
            order.RecordPayPalOrder(payPalOrderId, currency, invoiceId);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        AuthorizationHold hold;
        if (card is not null)
        {
            hold = await _paymentGateway.AuthorizeWithCardAsync(
                order.PayPalOrderId!,
                card,
                order.AuthorizeRequestId!,
                cancellationToken);
        }
        else
        {
            hold = await _paymentGateway.AuthorizeWithVaultIdAsync(
                order.PayPalOrderId!,
                vaultId,
                order.AuthorizeRequestId!,
                cancellationToken);
        }

        order.MarkAuthorized(
            hold.PayPalOrderId,
            hold.AuthorizationId,
            hold.Status,
            hold.ExpirationTime,
            hold.CreateTime,
            hold.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.Status == OrderPaymentStatus.Fulfilled ||
            order.Status == OrderPaymentStatus.PartiallyRefunded ||
            order.Status == OrderPaymentStatus.Refunded)
        {
            return order;
        }

        if (order.Status != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(order.AuthorizationId))
        {
            throw new PaymentException("Order has no authorization to capture.", 409);
        }

        var currency = order.CurrencyCode ?? RequireCurrency();
        var amount = order.Total();
        var authorizationId = await EnsureFreshAuthorization(order, amount, currency, cancellationToken);

        order.EnsureCaptureRequestId();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var capture = await _paymentGateway.CaptureAsync(
            authorizationId,
            amount,
            currency,
            InvoiceIdFor(order),
            order.CaptureRequestId!,
            cancellationToken);

        order.MarkFulfilled(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetAmount);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);

        if (order.Status == OrderPaymentStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            throw new PaymentException("A fulfilled order cannot be cancelled. Issue a refund instead.", 409);
        }

        if (order.Status == OrderPaymentStatus.Authorized && !string.IsNullOrEmpty(order.AuthorizationId))
        {
            order.EnsureVoidRequestId();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            await _paymentGateway.VoidAuthorizationAsync(
                order.AuthorizationId,
                order.VoidRequestId!,
                cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetBuyerOrder(orderId, buyerId, cancellationToken);
        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return order;
        }

        if (order.Status is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException("Only a fulfilled order can be refunded.", 409);
        }

        if (string.IsNullOrEmpty(order.CaptureId) || order.CapturedAmount is null)
        {
            throw new PaymentException("Order has no captured payment to refund.", 409);
        }

        var remaining = order.RefundableRemaining();
        if (remaining <= 0m)
        {
            throw new PaymentException("This capture has already been fully refunded.", 409);
        }

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentException("Refund amount must be greater than zero.", 400);
        }

        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund of {refundAmount:0.00} exceeds remaining refundable amount {remaining:0.00}.", 400);
        }

        var currency = order.CurrencyCode ?? RequireCurrency();
        var result = await _paymentGateway.RefundAsync(
            order.CaptureId,
            amount.HasValue ? refundAmount : null,
            currency,
            idempotencyKey,
            cancellationToken);

        order.RecordRefund(
            result.PayPalRefundId,
            idempotencyKey,
            result.Amount,
            result.Currency,
            result.Status);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    private async Task<string> EnsureFreshAuthorization(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var current = await _paymentGateway.GetAuthorizationAsync(order.AuthorizationId!, cancellationToken);
        order.ReplaceAuthorization(current.AuthorizationId, current.Status, current.ExpirationTime, current.CreateTime);

        if (string.Equals(current.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(current.Status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException("This authorization has already been captured.", 409);
        }

        if (string.Equals(current.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(current.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"Authorization is {current.Status} and cannot be captured. Take a new payment from the shopper.",
                409,
                operatorActionable: true);
        }

        if (IsPastRenewalWindow(current.CreateTime))
        {
            throw new PaymentException(
                "The authorization is older than 30 days and can no longer be renewed. Take a new payment from the shopper.",
                409,
                operatorActionable: true);
        }

        if (!NeedsReauthorization(current.ExpirationTime))
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return current.AuthorizationId;
        }

        order.EnsureReauthorizeRequestId();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(
                current.AuthorizationId,
                amount,
                currency,
                order.ReauthorizeRequestId!,
                cancellationToken);
            order.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime, renewed.CreateTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex) when (ex.StatusCode is 404 or 409 or 422)
        {
            throw new PaymentException(
                "The authorization can no longer be renewed. Take a new payment from the shopper.",
                409,
                ex,
                operatorActionable: true);
        }
    }

    private static bool NeedsReauthorization(string? expirationTime)
    {
        if (string.IsNullOrWhiteSpace(expirationTime))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(expirationTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiration))
        {
            return false;
        }

        return expiration <= DateTimeOffset.UtcNow.Add(AuthorizationSafetyMargin);
    }

    private static bool IsPastRenewalWindow(string? createTime)
    {
        if (string.IsNullOrWhiteSpace(createTime))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(createTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created))
        {
            return false;
        }

        return DateTimeOffset.UtcNow - created >= MaxAuthorizationLifetime;
    }

    private async Task<Order> GetOrder(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentException("Order was not found.", 404);
        }

        return order;
    }

    private async Task<Order> GetBuyerOrder(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId, cancellationToken);
        if (!order.BelongsTo(buyerId))
        {
            throw new PaymentException("Order was not found.", 404);
        }

        return order;
    }

    private async Task<Buyer> GetBuyer(string buyerId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        if (buyer is null)
        {
            throw new PaymentException("Saved payment method was not found.", 404);
        }

        return buyer;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_payPalSettings.Currency))
        {
            throw new PaymentException("PayPal:Currency is not configured.", 500);
        }

        return _payPalSettings.Currency.Trim().ToUpperInvariant();
    }

    internal static string UniqueInvoiceId(Order order) =>
        $"ESHOP-{order.Id}-{order.CreateOrderRequestId}";

    internal static string InvoiceIdFor(Order order) =>
        order.PayPalInvoiceId ?? UniqueInvoiceId(order);
}
