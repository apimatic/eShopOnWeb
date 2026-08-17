using System;
using System.Collections.Concurrent;
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
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentSettings _paymentSettings;

    // Serialize money-moving operations per order within this host so a double-click can never
    // authorize or capture the shopper twice. (In-memory, single-host store — see task notes.)
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _orderLocks = new();

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalClient payPalClient,
        IUriComposer uriComposer,
        IPaymentSettings paymentSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPalClient = payPalClient;
        _uriComposer = uriComposer;
        _paymentSettings = paymentSettings;
    }

    private string Currency => _paymentSettings.Currency;

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address? shipToAddress)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentValidationException("Every order line must have a quantity of at least 1.");

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds));

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = lines.Select(line =>
        {
            // Amounts come from catalog prices, not from the caller.
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, address, orderItems);

        return await _orderRepository.AddAsync(order);
    }

    public async Task<Order> AuthorizeAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId)
    {
        if (card is null && savedPaymentMethodId is null)
            throw new PaymentValidationException("Provide either card details or a saved card id to pay with.");
        if (card is not null && savedPaymentMethodId is not null)
            throw new PaymentValidationException("Provide either card details or a saved card id, not both.");

        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var order = await LoadOwnedOrderAsync(orderId, buyerId);

            // Idempotent in effect: never authorize a shopper twice for the same order.
            if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            {
                if (order.PaymentStatus == OrderPaymentStatus.Authorized)
                    return order;
                throw new PaymentConflictException($"Order {orderId} cannot be paid because it is already {order.PaymentStatus}.");
            }

            var amount = order.Total();
            var reference = BuildReference(orderId);
            // Deterministic idempotency key per order → PayPal also de-dupes a genuine double-submit.
            var idempotencyKey = $"authorize-{reference}";

            PayPalAuthorizationResult auth;
            if (savedPaymentMethodId is not null)
            {
                var savedCard = await _paymentMethodRepository.GetByIdAsync(savedPaymentMethodId.Value);
                if (savedCard is null || savedCard.BuyerId != buyerId)
                    throw new PaymentValidationException("The specified saved card was not found.");

                auth = await _payPalClient.AuthorizeWithVaultTokenAsync(amount, Currency, savedCard.PayPalVaultId, reference, idempotencyKey);
            }
            else
            {
                auth = await _payPalClient.AuthorizeWithCardAsync(amount, Currency, card!, reference, idempotencyKey);
            }

            order.MarkAuthorized(new PayPalPayment(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, Currency, reference));
            await _orderRepository.UpdateAsync(order);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> FulfilAsync(int orderId)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var order = await LoadOrderAsync(orderId);

            if (order.PaymentStatus == OrderPaymentStatus.Captured
                || order.PaymentStatus == OrderPaymentStatus.PartiallyRefunded
                || order.PaymentStatus == OrderPaymentStatus.Refunded)
            {
                // Already captured — idempotent, nothing more to take.
                return order;
            }
            if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.Payment is null)
                throw new PaymentConflictException($"Order {orderId} cannot be fulfilled because it is {order.PaymentStatus}.");

            var amount = order.Total();
            var idempotencyKey = $"capture-{order.Payment.Reference}";

            var outcome = await _payPalClient.CaptureAuthorizationAsync(order.Payment.AuthorizationId, amount, Currency, idempotencyKey);

            if (outcome.AuthorizationWasRenewed && outcome.RenewedAuthorizationId is not null)
                order.Payment.RenewAuthorization(outcome.RenewedAuthorizationId, outcome.RenewedAuthorizationStatus ?? "CREATED");

            var cap = outcome.Capture;
            order.MarkCaptured(cap.CaptureId, cap.Status, cap.GrossAmount, cap.PayPalFee, cap.NetAmount);
            await _orderRepository.UpdateAsync(order);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> CancelAsync(int orderId)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var order = await LoadOrderAsync(orderId);

            if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
                return order; // idempotent

            if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.Payment is null)
                throw new PaymentConflictException(
                    $"Order {orderId} cannot be cancelled because it is {order.PaymentStatus}. Only an authorized, not-yet-fulfilled order can be cancelled.");

            var idempotencyKey = $"void-{order.Payment.Reference}";
            await _payPalClient.VoidAuthorizationAsync(order.Payment.AuthorizationId, idempotencyKey);

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<(Order Order, string RefundId)> RefundAsync(int orderId, string buyerId, bool isAdministrator, decimal? amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var order = await LoadOrderAsync(orderId);

            // Refund is allowed for the owning shopper or an administrator.
            if (!isAdministrator && order.BuyerId != buyerId)
                throw new PaymentNotFoundException($"Order {orderId} was not found.");

            if (order.Payment?.CaptureId is null
                || (order.PaymentStatus != OrderPaymentStatus.Captured
                    && order.PaymentStatus != OrderPaymentStatus.PartiallyRefunded))
            {
                throw new PaymentConflictException(
                    $"Order {orderId} cannot be refunded because it is {order.PaymentStatus}. Only a captured order can be refunded.");
            }

            // Idempotent by caller key: repeating a request under the same key returns the same refund.
            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
                return (order, existing.RefundId);

            var refundAmount = amount ?? order.RefundableRemaining();
            if (refundAmount <= 0m)
                throw new PaymentValidationException("Refund amount must be greater than zero.");
            if (refundAmount > order.RefundableRemaining())
                throw new PaymentValidationException(
                    $"Refund amount {refundAmount:F2} exceeds the remaining refundable balance {order.RefundableRemaining():F2}.");

            // The PayPal-Request-Id must be globally unique yet stable for retries of the *same*
            // logical refund. Scope the caller's key to this capture so two callers reusing a short
            // key (e.g. "k1") on different captures never collide, while a genuine retry of this
            // refund reuses the same id. App-level dedup above already covers repeats of this key.
            var payPalRequestId = $"refund-{order.Payment.CaptureId}-{idempotencyKey}";
            var result = await _payPalClient.RefundCaptureAsync(order.Payment.CaptureId, amount, Currency, payPalRequestId);

            order.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
            await _orderRepository.UpdateAsync(order);
            return (order, result.RefundId);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId)
    {
        var spec = new CustomerOrdersWithPaymentSpecification(buyerId);
        return await _orderRepository.ListAsync(spec);
    }

    private async Task<Order> LoadOrderAsync(int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsAndPaymentByIdSpec(orderId));
        if (order is null)
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId)
    {
        var order = await LoadOrderAsync(orderId);
        // A shopper only ever acts on their own order — hide others' existence behind a 404.
        if (order.BuyerId != buyerId)
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        return order;
    }

    // A stable, unique reference we stamp on the PayPal order (invoice_id) so reconciliation can
    // line PayPal's transactions up against this order. Includes a unique suffix so re-runs (the
    // in-memory store restarts order ids at 1) never collide with PayPal's invoice-id uniqueness.
    private static string BuildReference(int orderId) =>
        $"ESHOP-{orderId}-{Guid.NewGuid():N}".Substring(0, 20);
}
