using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutPaymentService : ICheckoutPaymentService
{
    private static readonly TimeSpan AuthorizationRenewalWindow = TimeSpan.FromDays(29);
    private static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly OrderOperationGate _gate;

    public CheckoutPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        OrderOperationGate gate)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _gate = gate;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address? shipTo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentDomainException("The caller identity is required.", HttpStatusCode.Unauthorized);
        }

        if (items == null || items.Count == 0)
        {
            throw new PaymentDomainException("At least one catalog item is required.");
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => (CatalogItemId: g.Key, Quantity: g.Sum(x => x.Quantity)))
            .ToList();

        foreach (var line in grouped)
        {
            if (line.CatalogItemId <= 0)
            {
                throw new PaymentDomainException("Catalog item id must be a positive integer.");
            }

            if (line.Quantity <= 0)
            {
                throw new PaymentDomainException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var catalogIds = grouped.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = catalogIds.Where(id => !catalogById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentDomainException($"Catalog item(s) not found: {string.Join(", ", missing)}.", HttpStatusCode.NotFound);
        }

        var orderItems = grouped.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo ?? new Address("2211 N First St", "San Jose", "CA", "US", "95131");
        var order = new Order(buyerId, address, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        return _gate.RunAsync(orderId, () => PayCoreAsync(orderId, buyerId, card, paymentMethodId, cancellationToken), cancellationToken);
    }

    public Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return _gate.RunAsync(orderId, () => FulfilCoreAsync(orderId, cancellationToken), cancellationToken);
    }

    public Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return _gate.RunAsync(orderId, () => CancelCoreAsync(orderId, cancellationToken), cancellationToken);
    }

    public Task<Order> RefundAsync(
        int orderId,
        string actorId,
        bool actorIsAdministrator,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        return _gate.RunAsync(orderId, () => RefundCoreAsync(orderId, actorId, actorIsAdministrator, idempotencyKey, amount, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentDomainException("'to' must be on or after 'from'.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentActivitySpec(), cancellationToken);

        var matches = new List<ReconciliationMatch>();
        var matchedPayPalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var order in orders)
        {
            var knownIds = CollectPayPalIds(order);
            foreach (var txn in paypalTransactions)
            {
                var reason = MatchReason(order, knownIds, txn);
                if (reason == null)
                {
                    continue;
                }

                matches.Add(new ReconciliationMatch(order.Id, txn.TransactionId, txn.InvoiceId, reason));
                matchedPayPalIds.Add(txn.TransactionId);
                matchedOrderIds.Add(order.Id);
            }
        }

        var paypalOnly = paypalTransactions
            .Where(t => !matchedPayPalIds.Contains(t.TransactionId))
            .ToList();

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && OrderTouchesRange(o, from, to))
            .Select(o => new ReconciliationEshopEntry(
                o.Id,
                o.Status.ToString(),
                o.PayPalOrderId,
                o.AuthorizationId,
                o.CaptureId,
                o.Refunds.Select(r => r.PayPalRefundId).ToList()))
            .ToList();

        return new ReconciliationReport(from, to, matches, paypalOnly, eshopOnly);
    }

    private async Task<Order> PayCoreAsync(
        int orderId,
        string buyerId,
        CardPaymentSource? card,
        int? paymentMethodId,
        CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new InvalidOrderStateException($"Order {orderId} is cancelled and cannot be paid.");
        }

        var hasCard = card != null && !string.IsNullOrWhiteSpace(card.Number);
        var hasVault = paymentMethodId.HasValue;
        if (hasCard == hasVault)
        {
            throw new PaymentDomainException("Provide either card details or a saved paymentMethodId, not both.");
        }

        SavedPaymentMethod? saved = null;
        if (hasVault)
        {
            saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId!.Value, buyerId), cancellationToken);
            if (saved == null)
            {
                throw new SavedPaymentMethodNotFoundException(paymentMethodId.Value);
            }
        }
        else
        {
            ValidateCard(card!);
        }

        var amount = order.Total();
        if (amount <= 0m)
        {
            throw new PaymentDomainException("Order total must be greater than zero.");
        }

        var invoiceId = order.PayPalInvoiceId ?? NewInvoiceId(order.Id);

        if (string.IsNullOrEmpty(order.PayPalOrderId))
        {
            var paypalOrderId = await _payPal.CreateAuthorizedOrderAsync(
                amount, invoiceId, order.Id.ToString(), $"eshop-create-{order.Id}-{invoiceId}", cancellationToken);
            order.AttachPayPalOrder(paypalOrderId, "CREATED", invoiceId);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        var requestId = $"eshop-authorize-{order.PayPalOrderId}";

        AuthorizedPaymentResult authorized;
        if (saved != null)
        {
            authorized = await _payPal.AuthorizeVaultedCardAsync(
                order.PayPalOrderId!,
                new VaultedCardPaymentSource(saved.PayPalPaymentTokenId),
                requestId,
                cancellationToken);
        }
        else
        {
            authorized = await _payPal.AuthorizeCardAsync(order.PayPalOrderId!, card!, requestId, cancellationToken);
        }

        if (authorized.Amount != amount)
        {
            throw new PayPalApiException(
                $"PayPal authorized {authorized.Amount} {authorized.Currency} but the order total is {amount} {_payPal.Currency}.");
        }

        order.MarkAuthorized(
            authorized.PayPalOrderId,
            authorized.PayPalOrderStatus,
            authorized.AuthorizationId,
            authorized.AuthorizationStatus,
            authorized.ExpirationTime,
            authorized.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<Order> FulfilCoreAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.AuthorizationId))
        {
            throw new InvalidOrderStateException($"Order {orderId} must have an authorization before it can be fulfilled.");
        }

        var capture = await CaptureOrRecoverAsync(order, cancellationToken);

        order.MarkFulfilled(
            capture.CaptureId,
            capture.CaptureStatus,
            capture.CapturedAmount,
            capture.PayPalFee,
            capture.NetAmount,
            capture.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<Order> CancelCoreAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new InvalidOrderStateException($"Order {orderId} has already been fulfilled. Issue a refund instead of cancelling.");
        }

        if (!string.IsNullOrEmpty(order.AuthorizationId) && order.Status == OrderStatus.Authorized)
        {
            await _payPal.VoidAuthorizationAsync(order.AuthorizationId, $"eshop-void-{order.AuthorizationId}", cancellationToken);
            order.MarkCancelled("VOIDED");
        }
        else
        {
            order.MarkCancelled(order.AuthorizationStatus);
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<Order> RefundCoreAsync(
        int orderId,
        string actorId,
        bool actorIsAdministrator,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentDomainException("A caller-supplied idempotencyKey is required for refunds.");
        }

        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!actorIsAdministrator)
        {
            EnsureOwner(order, actorId);
        }

        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return order;
        }

        if (string.IsNullOrEmpty(order.CaptureId) || order.CapturedAmount is null)
        {
            throw new InvalidOrderStateException($"Order {orderId} has no captured payment to refund.");
        }

        var remaining = order.RemainingRefundableAmount();
        var refundAmount = amount.HasValue
            ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
            : remaining;

        if (refundAmount <= 0m)
        {
            throw new InvalidOrderStateException($"Order {orderId} has no remaining refundable amount.");
        }

        if (refundAmount > remaining)
        {
            throw new InvalidOrderStateException(
                $"Requested refund {refundAmount} exceeds remaining refundable amount {remaining} for order {orderId}.");
        }

        var result = await _payPal.RefundCaptureAsync(
            order.CaptureId,
            refundAmount,
            idempotencyKey,
            cancellationToken);

        order.AddRefund(result.PayPalRefundId, result.Status, result.Amount, result.Currency, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    private async Task<CapturedPaymentResult> CaptureOrRecoverAsync(Order order, CancellationToken cancellationToken)
    {
        var authorizationId = order.AuthorizationId!;
        AuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The authorization for order {order.Id} could not be loaded from PayPal ({ex.Message}). Ask the shopper to pay again.");
        }

        if (string.Equals(details.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(details.Status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(details.CaptureId))
            {
                throw new AuthorizationCannotBeRenewedException(
                    $"PayPal authorization {authorizationId} is already {details.Status}, but the capture id was not returned. Look up the capture in PayPal before retrying fulfilment.");
            }

            var existing = await _payPal.GetCaptureAsync(details.CaptureId, cancellationToken);
            if (existing.CapturedAmount != order.Total())
            {
                throw new PayPalApiException(
                    $"PayPal captured {existing.CapturedAmount} {existing.Currency} for order {order.Id} but the order total is {order.Total()} {_payPal.Currency}.");
            }

            return existing;
        }

        if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(details.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationCannotBeRenewedException(
                $"PayPal authorization {authorizationId} is {details.Status} and cannot be captured. Ask the shopper to pay again.");
        }

        authorizationId = await EnsureFreshAuthorizationAsync(order, details, cancellationToken);
        return await _payPal.CaptureAuthorizationAsync(
            authorizationId,
            order.Total(),
            order.PayPalInvoiceId ?? NewInvoiceId(order.Id),
            $"eshop-capture-{authorizationId}",
            cancellationToken);
    }

    private async Task<string> EnsureFreshAuthorizationAsync(
        Order order,
        AuthorizationDetails details,
        CancellationToken cancellationToken)
    {
        var authorizationId = details.AuthorizationId;
        var now = DateTimeOffset.UtcNow;
        var stale = details.ExpirationTime.HasValue && details.ExpirationTime.Value <= now;
        var honorExpired = order.OriginalAuthorizedAt.HasValue &&
                           now - order.OriginalAuthorizedAt.Value > AuthorizationHonorPeriod;

        if (!stale && !honorExpired)
        {
            return authorizationId;
        }

        var originalTime = order.OriginalAuthorizedAt ?? details.CreateTime ?? now;
        if (now - originalTime > AuthorizationRenewalWindow)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The authorization for order {order.Id} is older than 29 days and PayPal will not renew it. Ask the shopper to pay again, then fulfil the new hold.");
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                authorizationId,
                order.Total(),
                $"eshop-reauth-{authorizationId}",
                cancellationToken);

            order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"PayPal could not renew the authorization for order {order.Id}: {ex.Message}. Ask the shopper to pay again.");
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new OrderNotFoundException(orderId);
        }

        return order;
    }

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentDomainException("The caller cannot act on another shopper's order.", HttpStatusCode.Forbidden);
        }
    }

    private static void ValidateCard(CardPaymentSource card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) ||
            string.IsNullOrWhiteSpace(card.SecurityCode) ||
            string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentDomainException("Card number, expiry, security code, and name are required.");
        }
    }

    private static string NewInvoiceId(int orderId) => $"ESHOP-{orderId}-{Guid.NewGuid():N}";

    private static HashSet<string> CollectPayPalIds(Order order)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(ids, order.PayPalOrderId);
        Add(ids, order.AuthorizationId);
        Add(ids, order.CaptureId);
        foreach (var refund in order.Refunds)
        {
            Add(ids, refund.PayPalRefundId);
        }

        return ids;

        static void Add(HashSet<string> set, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                set.Add(value);
            }
        }
    }

    private static string? MatchReason(Order order, HashSet<string> knownIds, PayPalReportedTransaction txn)
    {
        if (!string.IsNullOrEmpty(txn.InvoiceId) &&
            (string.Equals(txn.InvoiceId, order.PayPalInvoiceId, StringComparison.OrdinalIgnoreCase) ||
             txn.InvoiceId.StartsWith($"ESHOP-{order.Id}-", StringComparison.OrdinalIgnoreCase)))
        {
            return "invoice_id";
        }

        if (!string.IsNullOrEmpty(txn.TransactionId) && knownIds.Contains(txn.TransactionId))
        {
            return "transaction_id";
        }

        if (!string.IsNullOrEmpty(txn.ReferenceId) && knownIds.Contains(txn.ReferenceId))
        {
            return "paypal_reference_id";
        }

        return null;
    }

    private static bool OrderTouchesRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        if (order.OrderDate >= from && order.OrderDate <= to)
        {
            return true;
        }

        if (order.OriginalAuthorizedAt is { } authorized && authorized >= from && authorized <= to)
        {
            return true;
        }

        return order.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to);
    }
}
