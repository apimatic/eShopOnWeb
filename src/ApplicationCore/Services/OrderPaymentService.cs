using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentsClient _payPal;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<Buyer> buyerRepository,
        IUriComposer uriComposer,
        IPayPalPaymentsClient payPal)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _buyerRepository = buyerRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one catalog item.");
        }

        var quantities = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException("Each order line must have a quantity greater than zero.");
            }

            quantities[line.CatalogItemId] = quantities.TryGetValue(line.CatalogItemId, out var existing)
                ? existing + line.Quantity
                : line.Quantity;
        }

        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);
        var missing = quantities.Keys.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var address = shipToAddress ?? new Address("2211 N First Street", "San Jose", "CA", "US", "95131");
        var order = new Order(buyerId, address, items);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, CardPaymentInput? card, int? paymentMethodId, CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        if (card is null && paymentMethodId is null)
        {
            throw new PaymentException("Provide card details or a saved paymentMethodId.");
        }

        if (card is not null && paymentMethodId is not null)
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId, not both.");
        }

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);

            if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentConflictException("A cancelled order cannot be paid.");
            }

            if (card is not null && card.BillingAddress is null)
            {
                var ship = order.ShipToAddress;
                card = card with
                {
                    BillingAddress = new BillingAddressInput(
                        ship.Street,
                        null,
                        ship.City,
                        ship.State,
                        ship.ZipCode,
                        ship.Country)
                };
            }

            string? vaultId = null;
            if (paymentMethodId is not null)
            {
                vaultId = await ResolveVaultIdAsync(buyerId, paymentMethodId.Value, cancellationToken);
            }

            var amount = order.Total();
            if (string.IsNullOrEmpty(order.PaypalOrderId))
            {
                var paypalOrderId = await _payPal.CreateAuthorizeOrderAsync(order.Id, amount, InvoiceId(order), cancellationToken);
                order.RecordPaypalOrderId(paypalOrderId);
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }

            var authorization = await _payPal.AuthorizeOrderAsync(order.PaypalOrderId!, InvoiceId(order), card, vaultId, cancellationToken);
            if (authorization.AuthorizedAmount != amount)
            {
                throw new PaymentException($"PayPal authorized {authorization.AuthorizedAmount.ToString("0.00", CultureInfo.InvariantCulture)} but the order total is {amount.ToString("0.00", CultureInfo.InvariantCulture)}.");
            }

            order.RecordAuthorization(
                authorization.PaypalOrderId,
                authorization.AuthorizationId,
                authorization.Status,
                authorization.ExpirationTime,
                authorization.Currency);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentConflictException("A cancelled order cannot be fulfilled.");
            }

            if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(order.PaypalAuthorizationId))
            {
                throw new PaymentConflictException("The order has no authorization to capture. The shopper must pay first.");
            }

            var authorizationId = await EnsureCaptureReadyAuthorizationAsync(order, cancellationToken);
            PaypalCaptureResult capture;
            try
            {
                capture = await _payPal.CaptureAuthorizationAsync(authorizationId, InvoiceId(order), order.Total(), cancellationToken);
            }
            catch (PaymentException)
            {
                authorizationId = await ReauthorizeOrFailAsync(order,
                    "PayPal rejected the capture, likely because the authorization is stale.",
                    cancellationToken);
                capture = await _payPal.CaptureAuthorizationAsync(authorizationId, InvoiceId(order), order.Total(), cancellationToken);
            }
            order.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PaypalFee, capture.NetAmount, capture.Currency);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                throw new PaymentConflictException("A fulfilled order cannot be cancelled. Issue a refund instead.");
            }

            if (!string.IsNullOrEmpty(order.PaypalAuthorizationId))
            {
                await _payPal.VoidAuthorizationAsync(order.PaypalAuthorizationId, cancellationToken);
            }

            order.Cancel();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<Order> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund idempotency key is required.");
        }

        return await WithOrderLock(orderId, async () =>
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);

            var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
            {
                return order;
            }

            if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            {
                throw new PaymentConflictException("Only a fulfilled payment can be refunded.");
            }

            if (string.IsNullOrEmpty(order.PaypalCaptureId) || order.CapturedAmount is null)
            {
                throw new PaymentConflictException("The order has no captured payment to refund.");
            }

            var remaining = order.RemainingRefundable();
            var refundAmount = amount.HasValue
                ? decimal.Round(amount.Value, 2, MidpointRounding.AwayFromZero)
                : remaining;

            if (refundAmount <= 0)
            {
                throw new PaymentException("Refund amount must be greater than zero.");
            }

            if (refundAmount > remaining)
            {
                throw new PaymentException($"Refund of {refundAmount.ToString("0.00", CultureInfo.InvariantCulture)} exceeds the remaining captured amount of {remaining.ToString("0.00", CultureInfo.InvariantCulture)}.");
            }

            var result = await _payPal.RefundCaptureAsync(order.PaypalCaptureId, refundAmount, idempotencyKey, cancellationToken);
            order.RecordRefund(result.RefundId, result.Status, result.Amount, result.Currency, idempotencyKey);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentException("`from` must be earlier than or equal to `to`.");
        }

        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentActivitySpecification(from, to), cancellationToken);

        var eshopRecords = new List<EshopPaymentRecord>();
        foreach (var order in orders)
        {
            if (!string.IsNullOrEmpty(order.PaypalAuthorizationId))
            {
                eshopRecords.Add(new EshopPaymentRecord
                {
                    OrderId = order.Id,
                    Kind = "authorization",
                    PaypalId = order.PaypalAuthorizationId,
                    Status = order.PaypalAuthorizationStatus,
                    Amount = order.Total(),
                    OccurredAt = order.AuthorizedAt
                });
            }

            if (!string.IsNullOrEmpty(order.PaypalCaptureId))
            {
                eshopRecords.Add(new EshopPaymentRecord
                {
                    OrderId = order.Id,
                    Kind = "capture",
                    PaypalId = order.PaypalCaptureId,
                    Status = order.PaypalCaptureStatus,
                    Amount = order.CapturedAmount,
                    OccurredAt = order.CapturedAt
                });
            }

            foreach (var refund in order.Refunds)
            {
                var inRange = refund.CreatedAt >= from && refund.CreatedAt <= to;
                if (inRange || (order.OrderDate >= from && order.OrderDate <= to))
                {
                    eshopRecords.Add(new EshopPaymentRecord
                    {
                        OrderId = order.Id,
                        Kind = "refund",
                        PaypalId = refund.PaypalRefundId,
                        Status = refund.Status,
                        Amount = refund.Amount,
                        OccurredAt = refund.CreatedAt
                    });
                }
            }
        }

        var matches = new List<ReconciliationMatch>();
        var paypalOnly = new List<PaypalReportedTransaction>();
        var matchedEshop = new HashSet<EshopPaymentRecord>();

        foreach (var txn in paypalTransactions)
        {
            var match = FindMatch(txn, orders, eshopRecords);
            if (match is not null)
            {
                matches.Add(new ReconciliationMatch { Paypal = txn, Eshop = match });
                matchedEshop.Add(match);
            }
            else
            {
                paypalOnly.Add(txn);
            }
        }

        var eshopOnly = eshopRecords.Where(r => !matchedEshop.Contains(r)).ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            PaypalTransactions = paypalTransactions,
            Matches = matches,
            PaypalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private async Task<string> EnsureCaptureReadyAuthorizationAsync(Order order, CancellationToken cancellationToken)
    {
        var authorizationId = order.PaypalAuthorizationId!;
        PaypalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException)
        {
            return await ReauthorizeOrFailAsync(order, "PayPal no longer recognizes the authorization.", cancellationToken);
        }

        order.RecordReauthorization(details.AuthorizationId, details.Status, details.ExpirationTime);

        if (IsTerminalAuthorization(details.Status))
        {
            return await ReauthorizeOrFailAsync(order,
                $"PayPal authorization {details.AuthorizationId} is {details.Status} and cannot be captured.",
                cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var honorExpired = details.CreateTime.HasValue && now - details.CreateTime.Value > HonorPeriod;
        var authorizationExpired = details.ExpirationTime.HasValue && details.ExpirationTime.Value <= now;

        if (honorExpired || authorizationExpired)
        {
            return await ReauthorizeOrFailAsync(order,
                authorizationExpired
                    ? $"PayPal authorization {details.AuthorizationId} expired at {details.ExpirationTime:O}."
                    : $"PayPal authorization {details.AuthorizationId} is past its 3-day honor period.",
                cancellationToken);
        }

        return details.AuthorizationId;
    }

    private async Task<string> ReauthorizeOrFailAsync(Order order, string reason, CancellationToken cancellationToken)
    {
        var sourceId = order.PaypalOriginalAuthorizationId ?? order.PaypalAuthorizationId;
        if (string.IsNullOrEmpty(sourceId))
        {
            throw new PaymentConflictException($"{reason} Ask the shopper to authorize payment again before fulfilment.");
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(sourceId, order.Total(), cancellationToken);
            order.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new PaymentConflictException(
                $"{reason} PayPal could not renew the authorization ({ex.Message}). Ask the shopper to authorize payment again before fulfilment.");
        }
    }

    private static bool IsTerminalAuthorization(string status)
    {
        return status.Equals("VOIDED", StringComparison.OrdinalIgnoreCase)
               || status.Equals("DENIED", StringComparison.OrdinalIgnoreCase)
               || status.Equals("EXPIRED", StringComparison.OrdinalIgnoreCase)
               || status.Equals("CAPTURED", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> ResolveVaultIdAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);
        var method = buyer?.PaymentMethods.FirstOrDefault(m => m.Id == paymentMethodId);
        if (method is null || string.IsNullOrEmpty(method.CardId))
        {
            throw new PaymentNotFoundException($"Saved payment method {paymentMethodId} was not found.");
        }

        return method.CardId;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentForbiddenException("The order does not belong to the caller.");
        }
    }

    private static void EnsureBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentForbiddenException("The caller is not authenticated.");
        }
    }

    private static string InvoiceId(Order order) => $"ESHOP-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}";

    private static EshopPaymentRecord? FindMatch(
        PaypalReportedTransaction txn,
        IReadOnlyList<Order> orders,
        IReadOnlyList<EshopPaymentRecord> records)
    {
        var byId = records.FirstOrDefault(r =>
            string.Equals(r.PaypalId, txn.TransactionId, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(txn.ReferenceId) && string.Equals(r.PaypalId, txn.ReferenceId, StringComparison.OrdinalIgnoreCase)));
        if (byId is not null)
        {
            return byId;
        }

        if (!string.IsNullOrEmpty(txn.InvoiceId))
        {
            var order = orders.FirstOrDefault(o =>
                !string.IsNullOrEmpty(txn.InvoiceId) &&
                txn.InvoiceId.StartsWith($"ESHOP-{o.Id}-", StringComparison.OrdinalIgnoreCase));
            if (order is not null)
            {
                return records.FirstOrDefault(r => r.OrderId == order.Id);
            }
        }

        if (!string.IsNullOrEmpty(txn.CustomField) && int.TryParse(txn.CustomField, out var orderId))
        {
            return records.FirstOrDefault(r => r.OrderId == orderId);
        }

        return null;
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
