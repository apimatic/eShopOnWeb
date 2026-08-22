using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutService : ICheckoutService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly ISavedPaymentMethodService _savedPaymentMethods;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalOptions _payPalOptions;

    public CheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        ISavedPaymentMethodService savedPaymentMethods,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        PayPalOptions payPalOptions)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedPaymentMethods = savedPaymentMethods;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _payPalOptions = payPalOptions;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<CatalogOrderLine> lines, Address? shipToAddress)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new CheckoutException(400, "The order must contain at least one catalog item.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new CheckoutException(400, "Quantity must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids));
        if (catalogItems.Count != ids.Length)
        {
            throw new CheckoutException(400, "One or more catalog items were not found.");
        }

        var grouped = lines
            .GroupBy(l => l.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        var items = grouped.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipToAddress ?? new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var order = new Order(buyerId, address, items);
        order.SetCurrency(RequireCurrency());
        await _orderRepository.AddAsync(order);
        return order;
    }

    public async Task<Order> PayWithCardAsync(int orderId, string buyerId, CardInput card, CancellationToken cancellationToken)
    {
        var order = await GetOwnedOrder(orderId, buyerId);
        if (order.AlreadyAuthorized())
        {
            return order;
        }

        order.EnsureCanAuthorize();
        order.GetOrCreateAuthorizeRequestId();
        await _orderRepository.UpdateAsync(order);
        return await AuthorizeAsync(order, () => _payPal.AuthorizeCardAsync(
            order.Id,
            order.Total(),
            RequireCurrency(),
            InvoiceId(order),
            order.Id.ToString(),
            card,
            order.PayAuthorizeRequestId!,
            cancellationToken));
    }

    public async Task<Order> PayWithSavedCardAsync(int orderId, string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var order = await GetOwnedOrder(orderId, buyerId);
        if (order.AlreadyAuthorized())
        {
            return order;
        }

        order.EnsureCanAuthorize();
        var method = await _savedPaymentMethods.GetOwnedAsync(buyerId, paymentMethodId);
        order.GetOrCreateAuthorizeRequestId();
        await _orderRepository.UpdateAsync(order);
        return await AuthorizeAsync(order, () => _payPal.AuthorizeSavedCardAsync(
            order.Id,
            order.Total(),
            RequireCurrency(),
            InvoiceId(order),
            order.Id.ToString(),
            method.PayPalPaymentTokenId,
            order.PayAuthorizeRequestId!,
            cancellationToken));
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId);
        order.EnsureCanCapture();
        if (order.AlreadyCaptured())
        {
            return order;
        }

        var currency = order.CurrencyCode ?? RequireCurrency();
        var authorizationId = order.PayPalAuthorizationId!;

        try
        {
            var liveAuth = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
            order.UpdateAuthorization(liveAuth.AuthorizationId, liveAuth.Status, liveAuth.ExpirationTime);
            await _orderRepository.UpdateAsync(order);

            if (string.Equals(liveAuth.Status, "VOIDED", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(liveAuth.Status, "DENIED", System.StringComparison.OrdinalIgnoreCase))
            {
                throw new CheckoutException(409,
                    $"PayPal reports this authorization as {liveAuth.Status}. It cannot be captured. Ask the shopper to pay again.");
            }

            if (order.AuthorizationLooksStale(System.DateTimeOffset.UtcNow)
                || string.Equals(liveAuth.Status, "PENDING", System.StringComparison.OrdinalIgnoreCase))
            {
                authorizationId = await RenewAuthorization(order, authorizationId, currency, cancellationToken);
            }
        }
        catch (CheckoutException)
        {
            throw;
        }
        catch (System.Exception ex) when (ex is not CheckoutException)
        {
            authorizationId = await RenewAuthorization(order, authorizationId, currency, cancellationToken);
        }

        try
        {
            order.GetOrCreateCaptureRequestId();
            await _orderRepository.UpdateAsync(order);
            var capture = await _payPal.CaptureAsync(
                authorizationId,
                order.Total(),
                currency,
                order.PayCaptureRequestId!,
                cancellationToken);
            order.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PaypalFee, capture.NetAmount);
            await _orderRepository.UpdateAsync(order);
            return order;
        }
        catch (CheckoutException ex) when (ex.StatusCode == 409 && !string.IsNullOrEmpty(order.PayPalCaptureId))
        {
            return order;
        }
        catch (CheckoutException ex)
        {
            if (LooksStale(ex.Message))
            {
                authorizationId = await RenewAuthorization(order, order.PayPalAuthorizationId!, currency, cancellationToken);
                var capture = await _payPal.CaptureAsync(
                    authorizationId,
                    order.Total(),
                    currency,
                    order.GetOrCreateCaptureRequestId() + "-retry",
                    cancellationToken);
                order.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PaypalFee, capture.NetAmount);
                await _orderRepository.UpdateAsync(order);
                return order;
            }

            throw;
        }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrder(orderId);
        order.EnsureCanVoid();
        if (order.PaymentStatus == OrderPaymentStatus.Voided
            || string.Equals(order.PayPalAuthorizationStatus, "VOIDED", System.StringComparison.OrdinalIgnoreCase))
        {
            order.RecordVoid("VOIDED");
            await _orderRepository.UpdateAsync(order);
            return order;
        }

        if (!string.IsNullOrEmpty(order.PayPalAuthorizationId)
            && order.PaymentStatus == OrderPaymentStatus.Authorized)
        {
            order.GetOrCreateVoidRequestId();
            await _orderRepository.UpdateAsync(order);
            var result = await _payPal.VoidAsync(order.PayPalAuthorizationId, order.PayVoidRequestId!, cancellationToken);
            order.RecordVoid(result.Status);
        }
        else
        {
            order.RecordVoid("VOIDED");
        }

        await _orderRepository.UpdateAsync(order);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string buyerId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await GetOwnedOrder(orderId, buyerId);

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refundAmount = amount ?? order.RemainingRefundable();
        order.EnsureCanRefund(refundAmount);

        var result = await _payPal.RefundAsync(
            order.PayPalCaptureId!,
            amount,
            order.CurrencyCode ?? RequireCurrency(),
            idempotencyKey,
            cancellationToken);

        var refund = order.RecordRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _orderRepository.UpdateAsync(order);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId));
    }

    public async Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
        if (order is null || !order.BelongsTo(buyerId))
        {
            return null;
        }

        return order;
    }

    private async Task<Order> AuthorizeAsync(Order order, System.Func<Task<PayPalAuthorizationResult>> authorize)
    {
        try
        {
            var result = await authorize();
            order.RecordAuthorization(
                result.PayPalOrderId,
                result.AuthorizationId,
                result.AuthorizationStatus,
                result.ExpirationTime,
                RequireCurrency());
            await _orderRepository.UpdateAsync(order);
            return order;
        }
        catch (CheckoutException ex)
        {
            order.MarkPaymentFailed(ex.Message);
            await _orderRepository.UpdateAsync(order);
            throw;
        }
    }

    private async Task<string> RenewAuthorization(Order order, string authorizationId, string currency, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                order.Total(),
                currency,
                order.GetOrCreateReauthorizeRequestId(),
                cancellationToken);
            order.UpdateAuthorization(renewed.AuthorizationId, renewed.AuthorizationStatus, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order);
            return renewed.AuthorizationId;
        }
        catch (CheckoutException ex)
        {
            throw new CheckoutException(409,
                "PayPal could not renew the payment hold, so fulfilment was not completed. " +
                "Ask the shopper to pay again, or retry after confirming the authorization is still valid. " +
                ex.Message,
                ex);
        }
    }

    private async Task<Order> GetOwnedOrder(int orderId, string buyerId)
    {
        var order = await GetOrder(orderId);
        order.EnsureOwnedBy(buyerId);
        return order;
    }

    private async Task<Order> GetOrder(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId));
        if (order is null)
        {
            throw new CheckoutException(404, "Order not found.");
        }

        return order;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_payPalOptions.Currency))
        {
            throw new CheckoutException(500, "PayPal:Currency is not configured.");
        }

        return _payPalOptions.Currency.Trim().ToUpperInvariant();
    }

    private static string InvoiceId(Order order) => $"ESHOP-{order.Id}-{order.OrderDate.UtcTicks}";

    private static bool LooksStale(string message)
    {
        var text = message.ToUpperInvariant();
        return text.Contains("EXPIRED") || text.Contains("AUTHORIZATION_EXPIRED") || text.Contains("AUTH_EXPIRED");
    }
}
