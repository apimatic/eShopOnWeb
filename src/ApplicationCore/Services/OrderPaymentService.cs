using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<PaymentMethod> _paymentMethods;
    private readonly IPayPalPaymentService _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<PaymentMethod> paymentMethods,
        IPayPalPaymentService payPal,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _paymentMethods = paymentMethods;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.ResolvedCurrency;

    // A stable, per-order/per-operation reference we hand PayPal as PayPal-Request-Id so a
    // double-click can never authorize or capture twice, independent of our own state check.
    private static string AuthorizeKey(int orderId) => $"eshop-auth-{RunToken}-{orderId}";
    private static string CaptureKey(int orderId) => $"eshop-cap-{RunToken}-{orderId}";

    // Order ids restart from 1 whenever the in-memory database is recreated, so a per-process token
    // keeps PayPal invoice ids and idempotency keys unique across runs. The order id remains the
    // custom_id we reconcile on, so this suffix never affects matching.
    private static readonly string RunToken = System.Guid.NewGuid().ToString("N")[..8];
    private static string InvoiceId(int orderId) => $"ESHOP-{RunToken}-{orderId}";

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
            throw new PaymentException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentException("Every order line must have a quantity of at least 1.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        await _orders.AddAsync(order, cancellationToken);
        _logger.LogInformation("Placed order {OrderId} for {BuyerId}, total {Total} {Currency}",
            order.Id, buyerId, order.Total(), Currency);
        return order;
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        // Idempotent in effect: if the hold already exists, a repeat call is a no-op.
        if (order.Status == OrderStatus.Authorized && order.Payment != null)
            return order;
        if (order.Status != OrderStatus.AwaitingPayment)
            throw new PaymentException($"Order {orderId} cannot be paid from status {order.Status}.");

        var amount = order.Total();
        var reference = orderId.ToString();
        var requestId = AuthorizeKey(orderId);

        PayPalAuthorizationResult result;
        string instrumentDescription;

        if (savedPaymentMethodId.HasValue)
        {
            var spec = new PaymentMethodsByBuyerSpecification(buyerId, savedPaymentMethodId.Value);
            var paymentMethod = await _paymentMethods.FirstOrDefaultAsync(spec, cancellationToken)
                ?? throw new KeyNotFoundException($"Saved card {savedPaymentMethodId} was not found.");

            result = await _payPal.AuthorizeWithVaultedCardAsync(amount, Currency, paymentMethod.PayPalVaultId,
                reference, requestId, cancellationToken);
            instrumentDescription = paymentMethod.Describe();
        }
        else if (card != null)
        {
            result = await _payPal.AuthorizeWithCardAsync(amount, Currency, card, reference, requestId, cancellationToken);
            instrumentDescription = $"{result.CardBrand} ****{result.CardLastFour}";
        }
        else
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId to pay.");
        }

        var payment = new Payment(result.PayPalOrderId, result.AuthorizationId, result.Status, amount, Currency,
            result.ExpiresAt, instrumentDescription);
        order.SetAuthorized(payment);
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Authorized order {OrderId}: hold {AuthorizationId} for {Amount} {Currency}",
            orderId, result.AuthorizationId, amount, Currency);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled)
            return order; // idempotent
        if (order.Status != OrderStatus.Authorized || order.Payment is null)
            throw new PaymentException($"Order {orderId} cannot be fulfilled from status {order.Status}.");

        var payment = order.Payment;
        var invoiceId = InvoiceId(orderId);
        var captureKey = CaptureKey(orderId);

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(payment.AuthorizationId, invoiceId, captureKey, cancellationToken);
        }
        catch (StaleAuthorizationException)
        {
            // The hold went stale before fulfilment: renew it rather than failing the fulfilment.
            _logger.LogWarning("Authorization {AuthorizationId} for order {OrderId} is stale; re-authorizing.",
                payment.AuthorizationId, orderId);
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId, payment.AuthorizedAmount,
                payment.Currency, cancellationToken); // throws AuthorizationNotRenewableException if it cannot be renewed
            payment.UpdateAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            capture = await _payPal.CaptureAsync(reauth.AuthorizationId, invoiceId, captureKey, cancellationToken);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Fulfilled order {OrderId}: captured {Gross} {Currency}, fee {Fee}, net {Net}",
            orderId, capture.GrossAmount, capture.Currency, capture.PayPalFee, capture.NetAmount);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
            return order; // idempotent
        if (order.Status != OrderStatus.Authorized && order.Status != OrderStatus.AwaitingPayment)
            throw new PaymentException($"Order {orderId} cannot be cancelled from status {order.Status}.");

        // Release the hold so no money ever moved.
        if (order.Payment is { AuthorizationStatus: not "CAPTURED" and not "VOIDED" } payment)
            await _payPal.VoidAsync(payment.AuthorizationId, cancellationToken);

        order.MarkCancelled();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId}; any held funds released.", orderId);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.Payment?.CaptureId is null ||
            (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded))
            throw new PaymentException($"Order {orderId} has no captured payment to refund (status {order.Status}).");

        var payment = order.Payment;

        // Idempotent by caller key: repeating the same key returns the original refund, never a second one.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
            return existing;

        decimal refundAmount;
        if (amount.HasValue)
        {
            if (amount.Value <= 0m)
                throw new PaymentException("Refund amount must be greater than zero.");
            if (amount.Value > payment.RefundableRemaining)
                throw new PaymentException(
                    $"Refund of {amount.Value:0.00} {payment.Currency} exceeds the refundable remaining " +
                    $"{payment.RefundableRemaining:0.00} {payment.Currency}.");
            refundAmount = amount.Value;
        }
        else
        {
            refundAmount = payment.RefundableRemaining;
            if (refundAmount <= 0m)
                throw new PaymentException("Nothing remains to refund on this order.");
        }

        var result = await _payPal.RefundAsync(payment.CaptureId!, refundAmount, payment.Currency,
            InvoiceId(orderId), idempotencyKey, cancellationToken);

        var refund = payment.AddRefund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        order.ApplyRefundState();
        await _orders.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Refunded {Amount} {Currency} on order {OrderId} (refund {RefundId}).",
            result.Amount, payment.Currency, orderId, result.RefundId);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var spec = new OrdersWithPaymentByBuyerSpecification(buyerId);
        return await _orders.ListAsync(spec, cancellationToken);
    }

    public async Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken);
        return order is null || order.BuyerId != buyerId ? null : order;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orders.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), cancellationToken)
            ?? throw new KeyNotFoundException($"Order {orderId} was not found.");
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        // Not-owned is reported as not-found so one shopper can never probe another's orders.
        if (order.BuyerId != buyerId)
            throw new KeyNotFoundException($"Order {orderId} was not found.");
        return order;
    }
}
