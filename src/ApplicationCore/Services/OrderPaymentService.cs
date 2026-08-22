using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan ReauthorizationWindow = TimeSpan.FromDays(29);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
    }

    public async Task<OrderPaymentResult> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<PlaceOrderItem> items,
        PlaceOrderAddress? shipTo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new CheckoutException(401, "A signed-in shopper is required to place an order.");
        }

        if (items == null || items.Count == 0)
        {
            throw new CheckoutException(400, "An order must contain at least one catalog item.");
        }

        var quantities = new Dictionary<int, int>();
        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new CheckoutException(400, "Item quantities must be greater than zero.");
            }

            quantities[item.CatalogItemId] = quantities.GetValueOrDefault(item.CatalogItemId) + item.Quantity;
        }

        var catalogItems = await _catalogRepository.ListAsync(
            new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);

        foreach (var catalogItemId in quantities.Keys)
        {
            if (catalogItems.All(c => c.Id != catalogItemId))
            {
                throw new CheckoutException(400, $"Catalog item {catalogItemId} was not found.");
            }
        }

        var address = ToAddress(shipTo);
        var orderItems = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, address, orderItems, _payPal.Currency);
        await _orderRepository.AddAsync(order, cancellationToken);
        return OrderPaymentMapper.ToResult(order);
    }

    public async Task<OrderPaymentResult> PayAsync(
        int orderId,
        string buyerId,
        int? paymentMethodId,
        CardPaymentDetails? card,
        CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);

            if (order.Status == OrderStatus.Authorized)
            {
                return OrderPaymentMapper.ToResult(order);
            }

            if (order.Status != OrderStatus.AwaitingPayment)
            {
                throw new CheckoutException(409, $"Order {orderId} cannot be paid while it is {order.Status}.");
            }

            order.AssignCurrency(_payPal.Currency);
            var amount = order.Total();
            if (amount <= 0)
            {
                throw new CheckoutException(400, "An order must have a positive total to be paid.");
            }

            var idempotencyKey = $"pay-{order.Id}-{Guid.NewGuid():N}";
            PaymentHold hold;
            if (paymentMethodId.HasValue)
            {
                var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId.Value, buyerId), cancellationToken);
                if (method == null)
                {
                    throw new CheckoutException(404, "Saved card was not found.");
                }

                hold = await _payPal.AuthorizeVaultedCardPaymentAsync(
                    order.Id, amount, method.PayPalVaultId, idempotencyKey, cancellationToken);
            }
            else
            {
                if (card == null || string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
                {
                    throw new CheckoutException(400, "Provide card details or a saved paymentMethodId.");
                }

                hold = await _payPal.AuthorizeCardPaymentAsync(
                    order.Id, amount, card, idempotencyKey, cancellationToken);
            }

            order.RecordAuthorization(
                hold.PayPalOrderId,
                hold.AuthorizationId,
                hold.Status,
                hold.CreatedAt,
                hold.ExpiresAt);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return OrderPaymentMapper.ToResult(order);
        });
    }

    public async Task<OrderPaymentResult> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);

            if (order.IsCaptured)
            {
                return OrderPaymentMapper.ToResult(order);
            }

            if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.PayPalAuthorizationId))
            {
                throw new CheckoutException(409, $"Order {orderId} cannot be fulfilled until payment is authorized.");
            }

            var authorizationId = await EnsureFreshAuthorizationAsync(order, cancellationToken);
            PaymentCapture capture;
            var captureKey = $"fulfil-{authorizationId}";
            try
            {
                capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId, captureKey, cancellationToken);
            }
            catch (CheckoutException ex) when (ex.StatusCode is 422 or 400 or 409)
            {
                authorizationId = await RenewAuthorizationAsync(order, cancellationToken);
                capture = await _payPal.CaptureAuthorizationAsync(
                    authorizationId, $"fulfil-{authorizationId}", cancellationToken);
            }

            order.RecordCapture(
                capture.CaptureId,
                capture.Status,
                capture.CapturedAmount,
                capture.PaypalFee,
                capture.NetAmount);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return OrderPaymentMapper.ToResult(order);
        });
    }

    public async Task<OrderPaymentResult> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return OrderPaymentMapper.ToResult(order);
            }

            if (order.Status == OrderStatus.Authorized && !string.IsNullOrEmpty(order.PayPalAuthorizationId))
            {
                await _payPal.VoidAuthorizationAsync(
                    order.PayPalAuthorizationId, $"void-{order.PayPalAuthorizationId}", cancellationToken);
                order.RecordCancellation("VOIDED");
            }
            else
            {
                order.RecordCancellation();
            }

            await _orderRepository.UpdateAsync(order, cancellationToken);
            return OrderPaymentMapper.ToResult(order);
        });
    }

    public async Task<RefundResult> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CheckoutException(400, "A refund idempotency key is required.");
        }

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);

            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing != null)
            {
                return new RefundResult
                {
                    RefundId = existing.Id,
                    PayPalRefundId = existing.PayPalRefundId,
                    Amount = existing.Amount,
                    Status = existing.Status,
                    IdempotencyKey = existing.IdempotencyKey
                };
            }

            if (!order.IsCaptured || string.IsNullOrEmpty(order.PayPalCaptureId))
            {
                throw new CheckoutException(409, $"Order {orderId} cannot be refunded until it has been fulfilled.");
            }

            var remaining = order.RefundableAmount();
            var refundAmount = amount.HasValue ? decimal.Round(amount.Value, 2) : remaining;
            if (refundAmount <= 0)
            {
                throw new CheckoutException(400, "There is no remaining captured amount to refund.");
            }

            if (refundAmount > remaining)
            {
                throw new CheckoutException(409,
                    $"Refund of {refundAmount} exceeds the remaining refundable amount of {remaining}.");
            }

            var paypalRefund = await _payPal.RefundCaptureAsync(
                order.PayPalCaptureId,
                refundAmount,
                order.Currency ?? _payPal.Currency,
                $"{order.PayPalCaptureId}:{idempotencyKey}",
                cancellationToken);

            var refund = order.RecordRefund(
                paypalRefund.RefundId,
                paypalRefund.Amount > 0 ? paypalRefund.Amount : refundAmount,
                paypalRefund.Status,
                idempotencyKey);

            await _orderRepository.UpdateAsync(order, cancellationToken);

            return new RefundResult
            {
                RefundId = refund.Id,
                PayPalRefundId = refund.PayPalRefundId,
                Amount = refund.Amount,
                Status = refund.Status,
                IdempotencyKey = refund.IdempotencyKey
            };
        });
    }

    public async Task<IReadOnlyList<OrderPaymentResult>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(OrderPaymentMapper.ToResult)
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new CheckoutException(400, "The reconciliation 'to' date must be on or after 'from'.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var paidOrders = await _orderRepository.ListAsync(new OrdersWithPaymentsSpecification(), cancellationToken);
        var rangedOrders = await _orderRepository.ListAsync(new OrdersByDateRangeSpecification(from, to), cancellationToken);

        var eshopOrders = paidOrders
            .Concat(rangedOrders)
            .GroupBy(o => o.Id)
            .Select(g => g.First())
            .ToList();

        var rows = new List<ReconciliationRow>();
        var matchedPaypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var order = eshopOrders.FirstOrDefault(o => Matches(o, txn));
            if (order != null)
            {
                matchedPaypalIds.Add(txn.TransactionId);
                matchedOrderIds.Add(order.Id);
                rows.Add(new ReconciliationRow
                {
                    Match = "matched",
                    OrderId = order.Id,
                    OrderStatus = order.Status.ToString(),
                    PayPalTransactionId = txn.TransactionId,
                    PayPalReferenceId = txn.ReferenceId,
                    PayPalStatus = txn.Status,
                    PayPalAmount = txn.Amount,
                    PayPalFee = txn.FeeAmount,
                    Note = txn.EventCode
                });
            }
            else
            {
                rows.Add(new ReconciliationRow
                {
                    Match = "paypal-only",
                    PayPalTransactionId = txn.TransactionId,
                    PayPalReferenceId = txn.ReferenceId,
                    PayPalStatus = txn.Status,
                    PayPalAmount = txn.Amount,
                    PayPalFee = txn.FeeAmount,
                    Note = "PayPal recorded this transaction and eShop has no matching order."
                });
            }
        }

        foreach (var order in eshopOrders.Where(o => HasPaymentActivityInRange(o, from, to) && !matchedOrderIds.Contains(o.Id)))
        {
            rows.Add(new ReconciliationRow
            {
                Match = "eshop-only",
                OrderId = order.Id,
                OrderStatus = order.Status.ToString(),
                Note = "eShop recorded payment activity in this range that PayPal reporting did not return."
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactionCount = paypalTransactions.Count,
            EshopOrderCount = eshopOrders.Count,
            MatchedCount = rows.Count(r => r.Match == "matched"),
            PayPalOnlyCount = rows.Count(r => r.Match == "paypal-only"),
            EshopOnlyCount = rows.Count(r => r.Match == "eshop-only"),
            Rows = rows
        };
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var authorizationId = order.PayPalAuthorizationId!;
        PaymentAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (CheckoutException ex) when (ex.StatusCode == 404)
        {
            throw CannotRenew(order);
        }

        if (details.Status is "VOIDED" or "DENIED" or "CAPTURED")
        {
            throw new CheckoutException(409,
                $"Authorization {authorizationId} is {details.Status} and cannot be captured. The shopper must pay again.");
        }

        var createdAt = details.CreatedAt ?? order.AuthorizationCreatedAt ?? DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;
        var stale = now - createdAt > HonorPeriod;
        var expired = details.ExpiresAt.HasValue && details.ExpiresAt.Value <= now;

        if (!stale && !expired)
        {
            return authorizationId;
        }

        return await RenewAuthorizationAsync(order, cancellationToken);
    }

    private async Task<string> RenewAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var createdAt = order.AuthorizationCreatedAt ?? DateTimeOffset.UtcNow;
        if (DateTimeOffset.UtcNow - createdAt > ReauthorizationWindow)
        {
            throw CannotRenew(order);
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                order.PayPalAuthorizationId!,
                order.Total(),
                $"reauth-{order.PayPalAuthorizationId}",
                cancellationToken);

            order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.CreatedAt, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (CheckoutException)
        {
            throw CannotRenew(order);
        }
    }

    private static CheckoutException CannotRenew(Order order) =>
        new(409,
            $"The payment hold on order {order.Id} has expired and can no longer be renewed. Ask the shopper to pay again, then fulfil the new authorization.");

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new CheckoutException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new CheckoutException(404, $"Order {order.Id} was not found.");
        }
    }

    private static Address ToAddress(PlaceOrderAddress? shipTo)
    {
        if (shipTo == null)
        {
            return new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        }

        return new Address(
            string.IsNullOrWhiteSpace(shipTo.Street) ? "123 Main St." : shipTo.Street,
            string.IsNullOrWhiteSpace(shipTo.City) ? "Kent" : shipTo.City,
            string.IsNullOrWhiteSpace(shipTo.State) ? "OH" : shipTo.State,
            string.IsNullOrWhiteSpace(shipTo.Country) ? "United States" : shipTo.Country,
            string.IsNullOrWhiteSpace(shipTo.ZipCode) ? "44240" : shipTo.ZipCode);
    }

    private static bool Matches(Order order, PayPalReportedTransaction txn)
    {
        var ids = order.PayPalIdentifiers().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (!string.IsNullOrEmpty(txn.TransactionId) && ids.Contains(txn.TransactionId)) ||
               (!string.IsNullOrEmpty(txn.ReferenceId) && ids.Contains(txn.ReferenceId));
    }

    private static bool HasPaymentActivityInRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        if (order.AuthorizationCreatedAt >= from && order.AuthorizationCreatedAt <= to) return true;
        if (order.CapturedAt >= from && order.CapturedAt <= to) return true;
        if (order.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to)) return true;
        return order.Status != OrderStatus.AwaitingPayment && order.OrderDate >= from && order.OrderDate <= to;
    }

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
