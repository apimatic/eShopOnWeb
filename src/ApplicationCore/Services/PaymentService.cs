using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _gateway;

    // Serialize money-moving operations per order so a double-click never authorizes or captures
    // the shopper twice within a single running host.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IUriComposer uriComposer,
        IPayPalGateway gateway)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput? address, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one line.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException($"Quantity for catalog item {line.CatalogItemId} must be positive.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentValidationException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var shipTo = new Address(
            street: NullToDefault(address?.Street, "N/A"),
            city: NullToDefault(address?.City, "N/A"),
            state: address?.State ?? string.Empty,
            country: NullToDefault(address?.Country, "US"),
            zipcode: NullToDefault(address?.ZipCode, "00000"));

        var order = new Order(buyerId, shipTo, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order.Id;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, PayPalCardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await _orderRepository.FirstOrDefaultAsync(
                new OrderByIdWithItemsAndPaymentSpecification(orderId, buyerId), cancellationToken)
                ?? throw new OrderNotFoundException(orderId);

            // Idempotent in effect: an order already authorized or fulfilled is returned as-is.
            if (order.Status is OrderStatus.PaymentAuthorized or OrderStatus.Fulfilled)
            {
                return order;
            }
            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentStateException($"Order {orderId} was cancelled and can no longer be paid.");
            }

            // Resolve the funding source: one-off card, or one of the shopper's saved cards.
            string? vaultToken = null;
            string? savedBrand = null, savedLast4 = null;
            if (savedPaymentMethodId is int savedId)
            {
                var saved = await _savedCardRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdSpecification(savedId, buyerId), cancellationToken)
                    ?? throw new SavedPaymentMethodNotFoundException(savedId);
                vaultToken = saved.PayPalVaultTokenId;
                savedBrand = saved.CardBrand;
                savedLast4 = saved.CardLast4;
                card = null; // a saved card takes precedence and never carries raw details
            }
            else if (card is null)
            {
                throw new PaymentValidationException("Provide card details or a saved payment method id to pay.");
            }

            var total = order.Total();

            // Create (or reuse from a prior failed attempt) the pending payment, persisting the
            // idempotency keys before contacting PayPal so a retry reuses them.
            var payment = order.Payment;
            if (payment is null)
            {
                // Unique merchant reference carried into PayPal; used to reconcile later. Well within
                // PayPal's 127-char invoice_id limit.
                var invoiceId = $"ESHOP-{orderId}-{Guid.NewGuid():N}";
                payment = new Payment(
                    currency: _gateway.Currency,
                    authorizedAmount: total,
                    invoiceId: invoiceId,
                    authorizeRequestId: Guid.NewGuid().ToString("N"),
                    captureRequestId: Guid.NewGuid().ToString("N"));
                order.StartPayment(payment);
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }

            var request = new PayPalAuthorizationRequest(
                Amount: total,
                InvoiceId: payment.InvoiceId,
                RequestId: payment.AuthorizeRequestId,
                Card: card,
                VaultTokenId: vaultToken,
                Description: $"eShopOnWeb order {orderId}");

            var result = await _gateway.AuthorizeAsync(request, cancellationToken);

            payment.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
            payment.SetInstrumentDescription(result.CardBrand ?? savedBrand, result.CardLast4 ?? savedLast4);
            order.MarkAuthorized();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await _orderRepository.FirstOrDefaultAsync(
                new OrderByIdWithItemsAndPaymentSpecification(orderId), cancellationToken)
                ?? throw new OrderNotFoundException(orderId);

            if (order.Status == OrderStatus.Fulfilled)
            {
                return order; // already captured — idempotent
            }
            if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
            {
                throw new PaymentStateException(
                    $"Order {orderId} is '{order.Status}' and cannot be fulfilled; it must be authorized first.");
            }

            var payment = order.Payment;
            var authId = payment.AuthorizationId!;

            // Proactively renew a hold that has gone (or is about to go) stale before capturing.
            if (payment.AuthorizationExpiresAt is DateTimeOffset expiresAt &&
                expiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
            {
                authId = await RenewAuthorizationAsync(order, payment, cancellationToken);
            }

            PayPalCaptureResult result;
            try
            {
                result = await _gateway.CaptureAsync(authId, payment.AuthorizedAmount, payment.InvoiceId,
                    payment.CaptureRequestId, cancellationToken);
            }
            catch (PayPalApiException ex) when (IsExpiredAuthorization(ex))
            {
                // The hold expired between our check and the capture — renew and retry once.
                authId = await RenewAuthorizationAsync(order, payment, cancellationToken);
                result = await _gateway.CaptureAsync(authId, payment.AuthorizedAmount, payment.InvoiceId,
                    payment.CaptureRequestId, cancellationToken);
            }

            payment.RecordCapture(result.CaptureId, result.Status, result.Amount, result.PayPalFee, result.NetAmount);
            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await _orderRepository.FirstOrDefaultAsync(
                new OrderByIdWithItemsAndPaymentSpecification(orderId), cancellationToken)
                ?? throw new OrderNotFoundException(orderId);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order; // idempotent
            }
            if (order.Status == OrderStatus.Fulfilled)
            {
                throw new PaymentStateException(
                    $"Order {orderId} has been fulfilled; cancel is no longer possible. Issue a refund instead.");
            }

            if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
            {
                var payment = order.Payment;
                try
                {
                    await _gateway.VoidAsync(payment.AuthorizationId!, Guid.NewGuid().ToString("N"), cancellationToken);
                }
                catch (PayPalApiException ex) when (IsAlreadyResolvedAuthorization(ex))
                {
                    // The hold is already gone (voided/expired) — the funds are not held, so proceed.
                }
                payment.RecordVoid();
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<(Order Order, string RefundId)> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("A refund idempotency key is required.");
        }

        return await WithOrderLock(orderId, async () =>
        {
            var order = await _orderRepository.FirstOrDefaultAsync(
                new OrderByIdWithItemsAndPaymentSpecification(orderId, buyerId), cancellationToken)
                ?? throw new OrderNotFoundException(orderId);

            var payment = order.Payment;
            if (payment is null ||
                (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded))
            {
                throw new PaymentStateException($"Order {orderId} has no captured payment to refund.");
            }

            // App-level idempotency: repeating a request under the same key never refunds twice.
            var existing = payment.FindRefundByKey(idempotencyKey);
            if (existing is not null)
            {
                return (order, existing.PayPalRefundId);
            }

            var remaining = payment.RefundableRemaining;
            if (amount is decimal requested)
            {
                if (requested <= 0m)
                {
                    throw new PaymentValidationException("Refund amount must be positive.");
                }
                if (requested > remaining)
                {
                    throw new PaymentValidationException(
                        $"Refund amount {requested:0.00} exceeds the refundable remaining balance {remaining:0.00}.");
                }
            }

            var amountToRefund = amount ?? remaining;
            // A full refund with no prior partials is expressed to PayPal as an empty amount.
            decimal? gatewayAmount = (amount is null && payment.TotalRefunded == 0m) ? null : amountToRefund;

            var result = await _gateway.RefundAsync(payment.CaptureId!, gatewayAmount, idempotencyKey, cancellationToken);
            var refundedAmount = result.Amount != 0m ? result.Amount : amountToRefund;

            payment.AddRefund(new PaymentRefund(idempotencyKey, result.RefundId, refundedAmount, result.Status));
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return (order, result.RefundId);
        });
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    // ------------------------------------------------------------------------------------------

    private async Task<string> RenewAuthorizationAsync(Order order, Payment payment, CancellationToken ct)
    {
        try
        {
            var result = await _gateway.ReauthorizeAsync(
                payment.AuthorizationId!, payment.AuthorizedAmount, Guid.NewGuid().ToString("N"), ct);
            payment.RecordReauthorization(result.AuthorizationId, result.Status, result.ExpiresAt);
            await _orderRepository.UpdateAsync(order, ct);
            return result.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentStateException(
                $"The authorization for order {order.Id} has expired and can no longer be renewed " +
                $"({ex.Issue ?? ex.Name ?? "reauthorization failed"}). A new payment must be collected from the " +
                $"shopper before this order can be fulfilled. PayPal reported: {ex.Message}");
        }
    }

    private static bool IsExpiredAuthorization(PayPalApiException ex) =>
        (ex.Issue?.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (ex.Message.Contains("expire", StringComparison.OrdinalIgnoreCase));

    private static bool IsAlreadyResolvedAuthorization(PayPalApiException ex) =>
        (ex.Issue?.Contains("VOIDED", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (ex.Issue?.Contains("ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (ex.Issue?.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (ex.Issue?.Contains("INVALID_RESOURCE_ID", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string NullToDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static async Task<T> WithOrderLock<T>(int orderId, Func<Task<T>> action)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
