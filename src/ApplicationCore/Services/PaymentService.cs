using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the order/payment lifecycle: authorize at checkout, capture at
/// fulfilment, void on cancel, refund after fulfilment, and card vaulting.
/// All provider communication goes through <see cref="IPaymentGateway"/>.
/// </summary>
public class PaymentService : IPaymentService
{
    private static readonly Address DefaultShipToAddress = new("Not provided", "Not provided", "Not provided", "US", "00000");

    // PayPal-Request-Id keys must be unique per logical operation across process runs too
    // (PayPal refuses a key it has seen under a different payment), so deterministic keys
    // are namespaced by a per-run component. Within a run they stay stable, which is what
    // makes a retried/double-submitted operation reach PayPal at most once.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PayPalOptions _payPalOptions;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        PayPalOptions payPalOptions,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _payPalOptions = payPalOptions;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<(int CatalogItemId, int Quantity)> items, Address? shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0 || items.Any(i => i.Quantity <= 0))
        {
            throw new InvalidOperationException("An order requires at least one item with a positive quantity.");
        }

        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray()), ct);

        var missing = items.Select(i => i.CatalogItemId).Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = items.Select(i =>
        {
            var catalogItem = catalogItems.First(c => c.Id == i.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, i.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        return await _orderRepository.AddAsync(order, ct);
    }

    public async Task<Order?> PayWithCardAsync(string buyerId, int orderId, CardDetails card, CancellationToken ct = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, ct);
        if (order is null) return null;

        return await AuthorizeOrderAsync(order, ct =>
            _paymentGateway.AuthorizeCardAsync(card, order.Total(), Currency, ReferenceId(order), AuthorizeKey(order), ct), ct);
    }

    public async Task<Order?> PayWithSavedCardAsync(string buyerId, int orderId, int paymentMethodId, CancellationToken ct = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, ct);
        if (order is null) return null;

        var savedCard = await _paymentMethodRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(paymentMethodId), ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new PaymentGatewayException(PaymentGatewayErrorKind.NotFound, $"Saved payment method {paymentMethodId} was not found.");
        }

        return await AuthorizeOrderAsync(order, ct =>
            _paymentGateway.AuthorizeVaultedCardAsync(savedCard.VaultTokenId, order.Total(), Currency, ReferenceId(order), AuthorizeKey(order), ct), ct);
    }

    private async Task<Order> AuthorizeOrderAsync(Order order, Func<CancellationToken, Task<PaymentAuthorizationResult>> authorize, CancellationToken ct)
    {
        // Idempotent in effect: paying an already-authorized order returns its current state.
        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            return order;
        }

        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new InvalidOperationException($"Order {order.Id} is {order.Status} and cannot be paid.");
        }

        var authorization = await authorize(ct);
        order.MarkPaymentAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt, Currency);
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order?> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithRefundsSpecification(orderId), ct);
        if (order is null) return null;

        // Idempotent in effect: re-fulfilling returns the captured state.
        if (order.Status == OrderStatus.Fulfilled)
        {
            return order;
        }

        if (order.Status != OrderStatus.PaymentAuthorized || order.AuthorizationId is null)
        {
            throw new InvalidOperationException($"Order {order.Id} is {order.Status}; only an order with an authorized payment can be fulfilled.");
        }

        var authorization = await _paymentGateway.GetAuthorizationAsync(order.AuthorizationId, ct);
        if (authorization.Status is not ("CREATED" or "PENDING"))
        {
            // The hold has gone stale (or was voided externally): renew it, then capture the renewal.
            _logger.LogInformation("Authorization {AuthorizationId} for order {OrderId} is {Status}; attempting to reauthorize.", order.AuthorizationId, order.Id, authorization.Status);
            try
            {
                var renewed = await _paymentGateway.ReauthorizeAsync(order.AuthorizationId, order.Total(), Currency, $"eshop-{RunId}-order-{order.Id}-reauthorize", ct);
                order.MarkAuthorizationRenewed(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            }
            catch (PaymentGatewayException ex) when (ex.Kind is PaymentGatewayErrorKind.Validation or PaymentGatewayErrorKind.NotFound or PaymentGatewayErrorKind.Conflict)
            {
                throw new PaymentGatewayException(PaymentGatewayErrorKind.Conflict,
                    $"The PayPal authorization for order {order.Id} has expired and can no longer be renewed. Cancel this order and ask the shopper to place and pay for a new one.",
                    ex.ProviderStatusCode, ex);
            }
        }

        var capture = await _paymentGateway.CaptureAsync(order.AuthorizationId, $"eshop-{RunId}-order-{order.Id}-capture", ct);
        order.MarkFulfilled(capture.CaptureId, capture.GrossAmount, capture.Fee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<Order?> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithRefundsSpecification(orderId), ct);
        if (order is null) return null;

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status == OrderStatus.PaymentAuthorized && order.AuthorizationId is not null)
        {
            // Releasing the hold means the shopper's money never moves.
            await _paymentGateway.VoidAuthorizationAsync(order.AuthorizationId, $"eshop-{RunId}-order-{order.Id}-void", ct);
            order.MarkCancelled("VOIDED");
        }
        else
        {
            order.MarkCancelled();
        }

        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<(Order? Order, OrderRefund? Refund)> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnedOrderAsync(buyerId, orderId, ct);
        if (order is null) return (null, null);

        // Idempotent replay: the same key returns the original refund without calling the provider again.
        var existing = order.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return (order, existing);
        }

        if (order.Status != OrderStatus.Fulfilled || order.CaptureId is null)
        {
            throw new InvalidOperationException($"Order {order.Id} is {order.Status}; only a fulfilled order can be refunded.");
        }

        var refundAmount = amount ?? order.RefundableAmount();
        if (refundAmount <= 0 || refundAmount > order.RefundableAmount())
        {
            throw new InvalidOperationException($"Refund amount must be positive and no more than the remaining refundable amount ({order.RefundableAmount()}).");
        }

        var result = await _paymentGateway.RefundCaptureAsync(order.CaptureId, refundAmount, order.Currency ?? Currency, idempotencyKey, ct);
        var refund = order.AddRefund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        await _orderRepository.UpdateAsync(order, ct);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        return await _orderRepository.ListAsync(new OrdersByBuyerSpecification(buyerId), ct);
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var vaulted = await _paymentGateway.VaultCardAsync(card, buyerId, $"eshop-vault-{Guid.NewGuid():N}", ct);
        var savedCard = new SavedPaymentMethod(buyerId, vaulted.VaultTokenId, vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.CardholderName);
        return await _paymentMethodRepository.AddAsync(savedCard, ct);
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken ct = default)
    {
        return await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);
    }

    public async Task<bool> DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct = default)
    {
        var savedCard = await _paymentMethodRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(paymentMethodId), ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            return false;
        }

        try
        {
            await _paymentGateway.DeleteVaultedCardAsync(savedCard.VaultTokenId, ct);
        }
        catch (PaymentGatewayException ex) when (ex.Kind == PaymentGatewayErrorKind.NotFound)
        {
            // Already gone at the provider; removing the local record is still correct.
            _logger.LogInformation("Vault token for saved payment method {PaymentMethodId} was already gone at the provider.", paymentMethodId);
        }

        await _paymentMethodRepository.DeleteAsync(savedCard, ct);
        return true;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var transactions = await _paymentGateway.SearchTransactionsAsync(from, to, ct);
        var orders = await _orderRepository.ListAsync(new AllOrdersWithRefundsSpecification(), ct);

        var entries = new List<ReconciliationEntry>();
        var reportedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transaction in transactions)
        {
            if (transaction.TransactionId is not null) reportedIds.Add(transaction.TransactionId);
            if (transaction.ReferenceId is not null) reportedIds.Add(transaction.ReferenceId);

            var order = orders.FirstOrDefault(o =>
                (transaction.ReferenceId is not null && o.PayPalOrderId == transaction.ReferenceId) ||
                o.AuthorizationId == transaction.TransactionId ||
                o.CaptureId == transaction.TransactionId ||
                o.Refunds.Any(r => r.PayPalRefundId == transaction.TransactionId));

            entries.Add(new ReconciliationEntry(
                transaction.TransactionId,
                transaction.ReferenceId,
                transaction.Status,
                transaction.Amount,
                transaction.Currency,
                transaction.Fee,
                transaction.Time,
                order?.Id,
                order is null ? ReconciliationMatchStatus.MissingInEshop : ReconciliationMatchStatus.Matched));
        }

        // The reverse direction: eShop orders paid inside the range that PayPal's report does not know about.
        foreach (var order in orders.Where(o => o.PayPalOrderId is not null && o.OrderDate >= from && o.OrderDate <= to))
        {
            var providerIds = new[] { order.PayPalOrderId, order.AuthorizationId, order.CaptureId }
                .Concat(order.Refunds.Select(r => r.PayPalRefundId))
                .Where(id => id is not null);

            if (!providerIds.Any(id => reportedIds.Contains(id!)))
            {
                entries.Add(new ReconciliationEntry(
                    null,
                    order.PayPalOrderId,
                    order.Status.ToString(),
                    order.CapturedAmount ?? order.Total(),
                    order.Currency,
                    order.PayPalFee,
                    order.OrderDate,
                    order.Id,
                    ReconciliationMatchStatus.MissingInPayPal));
            }
        }

        return new ReconciliationReport(from, to, entries);
    }

    private async Task<Order?> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithRefundsSpecification(orderId), ct);
        return order is null || order.BuyerId != buyerId ? null : order;
    }

    private string Currency => _payPalOptions.Currency;

    private static string ReferenceId(Order order) => $"eshop-{RunId}-order-{order.Id}";

    // Deterministic per order: a retried/double-submitted authorize reaches PayPal under the
    // same PayPal-Request-Id, so the hold is placed at most once.
    private static string AuthorizeKey(Order order) => $"eshop-{RunId}-order-{order.Id}-authorize";
}
