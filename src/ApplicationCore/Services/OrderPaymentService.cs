using System;
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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShipTo =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(request.BuyerId, nameof(request.BuyerId));
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new OrderPaymentException("An order must contain at least one item.", 400);
        }

        foreach (var item in request.Items)
        {
            if (item.CatalogItemId <= 0 || item.Quantity <= 0)
            {
                throw new OrderPaymentException("Each item must include a catalog item id and a quantity greater than zero.", 400);
            }
        }

        var catalogItemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = catalogItemIds.Where(id => !catalogById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new OrderPaymentException($"Catalog item(s) not found: {string.Join(", ", missing)}.", 404);
        }

        var quantities = request.Items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var orderItems = quantities.Select(pair =>
        {
            var catalogItem = catalogById[pair.Key];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, pair.Value);
        }).ToList();

        var order = new Order(request.BuyerId, request.ShipTo ?? DefaultShipTo, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(PayOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await GetOrderOrNotFound(request.OrderId, cancellationToken);
        order.EnsureOwnedBy(request.BuyerId);
        order.EnsureCanPay();

        if (order.AlreadyAuthorized)
        {
            return order;
        }

        if (request.PaymentMethodId is not null && request.Card is not null)
        {
            throw new OrderPaymentException("Provide either card details or a saved payment method, not both.", 400);
        }

        string? vaultId = null;
        if (request.PaymentMethodId is int paymentMethodId)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpec(paymentMethodId, request.BuyerId), cancellationToken);
            if (saved is null)
            {
                throw new OrderPaymentException("The saved payment method was not found.", 404);
            }

            vaultId = saved.PaypalPaymentTokenId;
        }
        else if (request.Card is null)
        {
            throw new OrderPaymentException("Provide card details or a saved payment method id.", 400);
        }

        var idempotencyKey = $"eshop-pay-{order.PaymentOperationKey}";
        AuthorizePaymentResult authorized;
        try
        {
            authorized = await _paymentGateway.AuthorizeAsync(
                new AuthorizePaymentRequest(
                    order.Id,
                    order.Total(),
                    _paymentGateway.Currency,
                    idempotencyKey,
                    request.Card,
                    vaultId),
                cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.IsChallengeRequired)
        {
            throw;
        }

        order.RecordAuthorization(
            authorized.PaypalOrderId,
            authorized.AuthorizationId,
            authorized.AuthorizationStatus,
            authorized.ExpirationTime,
            idempotencyKey);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderOrNotFound(orderId, cancellationToken);
        order.EnsureCanFulfil();

        if (order.AlreadyCaptured)
        {
            return order;
        }

        var authorizationId = await EnsureFreshAuthorization(order, cancellationToken);
        var idempotencyKey = $"eshop-capture-{order.PaymentOperationKey}";

        CapturePaymentResult captured;
        try
        {
            captured = await _paymentGateway.CaptureAsync(authorizationId, order.Total(), idempotencyKey, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 409 && !string.IsNullOrEmpty(order.CaptureId))
        {
            captured = await _paymentGateway.GetCaptureAsync(order.CaptureId!, cancellationToken);
        }

        order.RecordCapture(
            captured.CaptureId,
            captured.Status,
            captured.CapturedGross,
            captured.PaypalFee,
            captured.NetAmount,
            idempotencyKey);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderOrNotFound(orderId, cancellationToken);
        order.EnsureCanCancel();

        if (order.AlreadyCancelled)
        {
            return order;
        }

        var idempotencyKey = $"eshop-void-{order.PaymentOperationKey}";
        VoidPaymentResult voided;
        try
        {
            voided = await _paymentGateway.VoidAsync(order.AuthorizationId!, idempotencyKey, cancellationToken);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 409)
        {
            throw new OrderPaymentException(
                "PayPal reports this authorization is already voided or captured. Refresh the order before retrying.",
                409);
        }

        order.RecordVoid(voided.AuthorizationStatus, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(RefundOrderCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new OrderPaymentException("A refund idempotency key is required.", 400);
        }

        var order = await GetOrderOrNotFound(request.OrderId, cancellationToken);
        order.EnsureOwnedBy(request.BuyerId);

        var existing = order.FindRefundByIdempotencyKey(request.IdempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        order.EnsureCanRefund();

        var remaining = order.RemainingRefundable();
        var amount = request.Amount ?? remaining;
        if (amount <= 0)
        {
            throw new OrderPaymentException("Refund amount must be greater than zero.", 400);
        }

        if (amount > remaining)
        {
            throw new OrderPaymentException(
                $"Refund of {amount} exceeds remaining refundable amount {remaining}.", 400);
        }

        var result = await _paymentGateway.RefundAsync(
            new RefundPaymentRequest(
                order.CaptureId!,
                _paymentGateway.Currency,
                request.Amount,
                request.IdempotencyKey),
            cancellationToken);

        var refund = order.RecordRefund(result.RefundId, result.Status, result.Amount, request.IdempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new OrderPaymentException("'to' must be on or after 'from'.", 400);
        }

        var paypalTransactions = await _paymentGateway.SearchTransactionsAsync(from, to, cancellationToken);
        var eshopOrders = await _orderRepository.ListAsync(new OrdersWithPaypalStateSpecification(), cancellationToken);

        var matches = new List<ReconciliationMatch>();
        var matchedTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var order in eshopOrders)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddId(ids, order.PaypalOrderId);
            AddId(ids, order.AuthorizationId);
            AddId(ids, order.CaptureId);
            foreach (var refund in order.Refunds)
            {
                AddId(ids, refund.PaypalRefundId);
            }

            var orderIdText = order.Id.ToString();
            foreach (var tx in paypalTransactions)
            {
                var matched =
                    (!string.IsNullOrEmpty(tx.InvoiceId) && (tx.InvoiceId == orderIdText || tx.InvoiceId.StartsWith($"eShop-{order.Id}", StringComparison.Ordinal))) ||
                    (!string.IsNullOrEmpty(tx.CustomField) && tx.CustomField == orderIdText) ||
                    (!string.IsNullOrEmpty(tx.TransactionId) && ids.Contains(tx.TransactionId)) ||
                    (!string.IsNullOrEmpty(tx.PaypalReferenceId) && ids.Contains(tx.PaypalReferenceId));

                if (!matched)
                {
                    continue;
                }

                var reason = !string.IsNullOrEmpty(tx.CustomField) && tx.CustomField == orderIdText
                    ? "custom_field"
                    : !string.IsNullOrEmpty(tx.InvoiceId) && (tx.InvoiceId == orderIdText || tx.InvoiceId.StartsWith($"eShop-{order.Id}", StringComparison.Ordinal))
                        ? "invoice_id"
                        : "paypal_id";

                matches.Add(new ReconciliationMatch(order.Id, tx.TransactionId, reason));
                matchedOrderIds.Add(order.Id);
                if (!string.IsNullOrEmpty(tx.TransactionId))
                {
                    matchedTransactionIds.Add(tx.TransactionId);
                }
            }
        }

        var paypalOnly = paypalTransactions
            .Where(tx => string.IsNullOrEmpty(tx.TransactionId) || !matchedTransactionIds.Contains(tx.TransactionId))
            .ToList();

        var eshopOnly = eshopOrders
            .Where(o => !matchedOrderIds.Contains(o.Id))
            .Select(o => new EshopPaymentRecord(o.Id, o.PaypalOrderId, o.AuthorizationId, o.CaptureId, o.Status.ToString()))
            .ToList();

        return new ReconciliationReport(from, to, matches, paypalOnly, eshopOnly);
    }

    private async Task<string> EnsureFreshAuthorization(Order order, CancellationToken cancellationToken)
    {
        var authorizationId = order.AuthorizationId!;
        AuthorizationDetails details;
        try
        {
            details = await _paymentGateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw new OrderPaymentException(
                $"PayPal could not load authorization {authorizationId}. {ex.Message} The operator should confirm the hold still exists in PayPal or ask the shopper to pay again.",
                ex.StatusCode >= 400 && ex.StatusCode < 500 ? ex.StatusCode : 409);
        }

        var status = details.Status?.ToUpperInvariant() ?? string.Empty;
        if (status is "VOIDED" or "DENIED")
        {
            throw new OrderPaymentException(
                $"Authorization {authorizationId} is {status} and cannot be captured. Ask the shopper to pay again.",
                409);
        }

        if (status is "CAPTURED" or "PARTIALLY_CAPTURED")
        {
            return authorizationId;
        }

        var stale = details.ExpirationTime is DateTimeOffset expiration &&
                    expiration <= DateTimeOffset.UtcNow.AddMinutes(5);

        if (!stale)
        {
            return authorizationId;
        }

        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(
                authorizationId,
                order.Total(),
                _paymentGateway.Currency,
                $"eshop-reauth-{order.PaymentOperationKey}-{authorizationId}",
                cancellationToken);

            order.ReplaceAuthorization(renewed.AuthorizationId, renewed.AuthorizationStatus, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentGatewayException ex)
        {
            throw new OrderPaymentException(
                $"PayPal cannot renew authorization {authorizationId}. {ex.Message} Capture was not attempted. Ask the shopper to pay again, then retry fulfilment.",
                409);
        }
    }

    private async Task<Order> GetOrderOrNotFound(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderPaymentException("The requested order was not found.", 404);
        }

        return order;
    }

    private static void AddId(HashSet<string> ids, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            ids.Add(value);
        }
    }
}
