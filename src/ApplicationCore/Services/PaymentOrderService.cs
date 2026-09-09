using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

public class PaymentOrderService : IPaymentOrderService
{
    // Serialize money-moving operations per order within the process, so a double-click (two concurrent
    // requests on the same order) can never authorize, capture or refund twice.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentGateway _payPal;

    public PaymentOrderService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IUriComposer uriComposer,
        IPayPalPaymentGateway payPal)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidPaymentSourceException("An order must contain at least one item.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidPaymentSourceException($"Quantity for catalog item {line.CatalogItemId} must be positive.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new InvalidPaymentSourceException($"Catalog item {line.CatalogItemId} does not exist.");

            // Amounts come from catalog prices, snapshotted onto the order line.
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(buyerId, address, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

            // Idempotent in effect: if the order is already authorized, a repeat pay returns the same result.
            if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
            {
                return order;
            }
            if (order.Status != OrderStatus.AwaitingPayment)
            {
                throw new InvalidOperationException($"Order {orderId} cannot be paid from status {order.Status}.");
            }

            var authRequest = new PayPalAuthorizationRequest
            {
                Amount = order.Total(),
                InvoiceId = $"eshop-{orderId}-{Guid.NewGuid():N}",
                CustomId = orderId.ToString(),
                Description = $"eShopOnWeb order {orderId}",
                IdempotencyKey = Guid.NewGuid().ToString("N")
            };

            if (paymentMethodId is not null)
            {
                authRequest.VaultId = await ResolveSavedCardVaultIdAsync(buyerId, paymentMethodId.Value, cancellationToken);
            }
            else if (card is not null)
            {
                authRequest.Card = card;
            }
            else
            {
                throw new InvalidPaymentSourceException("Provide either card details or a saved paymentMethodId to pay.");
            }

            var result = await _payPal.AuthorizeAsync(authRequest, cancellationToken);

            var payment = new Payment(result.PayPalOrderId, result.AuthorizationId, result.Status,
                _payPal.Currency, order.Total(), result.ExpiresAt);
            order.SetAuthorized(payment);
            await _orderRepository.UpdateAsync(order, cancellationToken);
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
            var order = await LoadOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Fulfilled)
            {
                return order; // Idempotent: already fulfilled and captured.
            }
            if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
            {
                throw new InvalidOperationException($"Order {orderId} cannot be fulfilled from status {order.Status}.");
            }

            var payment = order.Payment;

            // Renew a stale hold before capturing, rather than failing the fulfilment outright.
            await EnsureAuthorizationFreshAsync(payment, cancellationToken);

            PayPalCaptureResult capture;
            try
            {
                capture = await CaptureAsync(payment, cancellationToken);
            }
            catch (PayPalApiException ex) when (IsExpiredAuthorization(ex))
            {
                // The hold expired between our freshness check and the capture: renew and retry once.
                await ReauthorizeAsync(payment, cancellationToken);
                capture = await CaptureAsync(payment, cancellationToken);
            }

            payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            order.SetFulfilled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
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
            var order = await LoadOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order; // Idempotent.
            }
            if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
            {
                throw new InvalidOperationException($"Order {orderId} cannot be cancelled from status {order.Status}.");
            }

            await _payPal.VoidAsync(order.Payment.AuthorizationId, cancellationToken);
            order.Payment.Void();
            order.Cancel();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
            var payment = order.Payment
                ?? throw new InvalidOperationException($"Order {orderId} has no captured payment to refund.");

            // Idempotent: repeating a refund under the same key returns the original refund.
            var existing = payment.FindRefundByKey(idempotencyKey);
            if (existing is not null)
            {
                return existing;
            }

            if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded)
            {
                throw new InvalidOperationException($"Order {orderId} cannot be refunded from payment status {payment.Status}.");
            }

            var remaining = payment.RefundableRemaining();
            var refundAmount = amount ?? remaining;
            if (refundAmount <= 0m || refundAmount > remaining)
            {
                throw new InvalidOperationException(
                    $"Refund of {refundAmount} {payment.Currency} exceeds the remaining refundable amount of {remaining} {payment.Currency}.");
            }

            // The PayPal-Request-Id must be globally unique per merchant; namespace the caller's key with
            // the (unique) capture id so it never collides with an unrelated use of the same key string,
            // while a repeat of the same caller key against the same capture stays idempotent at PayPal.
            var payPalRequestId = $"refund-{payment.CaptureId}-{idempotencyKey}";
            var result = await _payPal.RefundAsync(payment.CaptureId!, amount, payPalRequestId, cancellationToken);
            var recordedAmount = result.Amount > 0m ? result.Amount : refundAmount;

            var refund = payment.AddRefund(result.RefundId, recordedAmount, result.Status, idempotencyKey);
            if (payment.RefundableRemaining() <= 0m)
            {
                order.MarkRefunded();
            }
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return refund;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    public async Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }
        return order;
    }

    // -----------------------------------------------------------------------------------------------

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken)
            ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not disclose that another shopper's order exists.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<string> ResolveSavedCardVaultIdAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var method = buyer?.FindPaymentMethod(paymentMethodId)
            ?? throw new PaymentMethodNotFoundException(paymentMethodId);
        return method.VaultId;
    }

    private Task<PayPalCaptureResult> CaptureAsync(Payment payment, CancellationToken cancellationToken)
    {
        // Deterministic per authorization id: retrying the same capture returns the same result;
        // a capture against a renewed authorization uses a fresh key.
        var idempotencyKey = $"eshop-capture-{payment.AuthorizationId}";
        return _payPal.CaptureAsync(payment.AuthorizationId, payment.AuthorizedAmount, finalCapture: true, idempotencyKey, cancellationToken);
    }

    private async Task EnsureAuthorizationFreshAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (payment.AuthorizationExpiresAt is { } expiry && DateTimeOffset.UtcNow >= expiry)
        {
            await ReauthorizeAsync(payment, cancellationToken);
        }
    }

    private async Task ReauthorizeAsync(Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId, payment.AuthorizedAmount, cancellationToken);
            payment.RecordReauthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationNotRenewableException(
                $"The payment hold for this order can no longer be renewed ({ex.Issue ?? "reauthorization failed"}). " +
                "A new authorization is required: ask the shopper to pay again before fulfilling. " +
                (ex.DebugId is null ? "" : $"PayPal debug id: {ex.DebugId}."));
        }
    }

    private static bool IsExpiredAuthorization(PayPalApiException ex) =>
        string.Equals(ex.Issue, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase);
}
