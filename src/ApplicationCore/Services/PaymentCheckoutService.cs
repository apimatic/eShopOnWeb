using System;
using System.Collections.Generic;
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

public class PaymentCheckoutService : IPaymentCheckoutService
{
    private static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan AuthorizationLifetime = TimeSpan.FromDays(29);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalClient _payPal;

    public PaymentCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IUriComposer uriComposer,
        IPayPalClient payPal)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
    }

    public string Currency => _payPal.Currency;

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineInput> items,
        Address shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new OrderPaymentException(400, "An order must contain at least one item.");
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (grouped.Any(g => g.Quantity <= 0))
        {
            throw new OrderPaymentException(400, "Each order item must have a quantity greater than zero.");
        }

        var ids = grouped.Select(g => g.CatalogItemId).ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new OrderPaymentException(404, $"Catalog item {missing[0]} was not found.");
        }

        var orderItems = grouped.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shippingAddress, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentDetails? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new OrderPaymentException(409, "A cancelled order cannot be paid.");
        }

        if (card is null && paymentMethodId is null)
        {
            throw new OrderPaymentException(400, "Provide card details or a saved payment method id.");
        }

        if (card is not null && paymentMethodId is not null)
        {
            throw new OrderPaymentException(400, "Provide either card details or a saved payment method id, not both.");
        }

        ValidateCardIfPresent(card);

        string? vaultId = null;
        int? savedId = null;
        if (paymentMethodId is not null)
        {
            var buyer = await GetBuyerAsync(buyerId, cancellationToken)
                ?? throw new OrderPaymentException(404, "Saved card was not found.");
            var method = buyer.GetPaymentMethod(paymentMethodId.Value);
            vaultId = method.CardId;
            savedId = method.Id;
        }

        var currency = _payPal.Currency;
        var amount = order.Total();
        var payment = order.EnsurePayment(currency);
        payment.EnsureIdempotencyKey();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var result = await _payPal.AuthorizeOrderAsync(new PayPalAuthorizeRequest
        {
            Amount = amount,
            Currency = currency,
            InvoiceId = $"ESHOP-{payment.IdempotencyKey}",
            CustomId = order.Id.ToString(),
            Card = card,
            VaultId = vaultId
        }, $"pay-{payment.IdempotencyKey}", cancellationToken);

        order.RecordAuthorization(
            currency,
            result.PayPalOrderId,
            result.AuthorizationId,
            result.AuthorizationStatus,
            result.Expiration,
            result.AuthorizedAt,
            result.Last4 ?? Last4FromCard(card),
            result.Brand,
            savedId);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new OrderPaymentException(409, "A cancelled order cannot be fulfilled.");
        }

        if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId is null)
        {
            throw new OrderPaymentException(409, "Order must be authorized before it can be fulfilled.");
        }

        var currency = order.Payment.Currency ?? _payPal.Currency;
        var amount = order.Total();
        var authorizationId = await EnsureFreshAuthorizationAsync(order, amount, currency, cancellationToken);

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                currency,
                $"eshop-{order.Payment.IdempotencyKey ?? order.Id.ToString()}-capture",
                cancellationToken);
        }
        catch (PayPalGatewayException ex) when (ex.IsExpiredAuthorization())
        {
            authorizationId = await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                currency,
                $"eshop-{order.Payment.IdempotencyKey ?? order.Id.ToString()}-capture-retry",
                cancellationToken);
        }

        if (IsFailedCapture(capture.Status))
        {
            throw new OrderPaymentException(409,
                $"PayPal could not capture the authorization (status {capture.Status}).");
        }

        if (string.Equals(capture.Status, "PENDING", StringComparison.OrdinalIgnoreCase)
            && (capture.PayPalFee is null || capture.NetAmount is null))
        {
            try
            {
                capture = await _payPal.GetCaptureAsync(capture.CaptureId, cancellationToken);
            }
            catch (OrderPaymentException)
            {
                // Keep the original capture payload if the follow-up read fails.
            }
        }

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.GrossAmount,
            capture.PayPalFee,
            capture.NetAmount,
            capture.CapturedAt);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new OrderPaymentException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (!string.IsNullOrWhiteSpace(order.Payment?.AuthorizationId))
        {
            await _payPal.VoidAuthorizationAsync(
                order.Payment.AuthorizationId,
                $"eshop-{order.Payment.IdempotencyKey ?? order.Id.ToString()}-void",
                cancellationToken);
        }

        order.Cancel("VOIDED");
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderPaymentException(400, "A refund idempotency key is required.");
        }

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        if (order.Payment?.FindRefundByIdempotencyKey(idempotencyKey) is { } existing)
        {
            return existing;
        }

        if (order.Payment?.CaptureId is null)
        {
            throw new OrderPaymentException(409, "Order has no captured payment to refund.");
        }

        var currency = order.Payment.Currency ?? _payPal.Currency;
        var refundAmount = PayPalMoney.Round(amount ?? order.Payment.RefundableRemaining);
        if (refundAmount <= 0)
        {
            throw new OrderPaymentException(409, "There is no remaining captured amount to refund.");
        }

        if (refundAmount > order.Payment.RefundableRemaining)
        {
            throw new OrderPaymentException(409,
                $"Refund of {PayPalMoney.ToValue(refundAmount)} exceeds the remaining refundable amount {PayPalMoney.ToValue(order.Payment.RefundableRemaining)}.");
        }

        PayPalRefundResult result;
        try
        {
            result = await _payPal.RefundCaptureAsync(
                order.Payment.CaptureId,
                refundAmount,
                currency,
                idempotencyKey,
                cancellationToken);
        }
        catch (PayPalGatewayException ex) when (ex.StatusCode == 409)
        {
            var replay = order.Payment.FindRefundByIdempotencyKey(idempotencyKey);
            if (replay is not null)
            {
                return replay;
            }

            throw;
        }

        var refund = order.RecordRefund(result.RefundId, result.Status, PayPalMoney.Round(result.Amount), result.Currency, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public Task<Order> GetMyOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default) =>
        GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

    public async Task<PaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentDetails card,
        CancellationToken cancellationToken = default)
    {
        ValidateCardIfPresent(card);
        if (card is null)
        {
            throw new OrderPaymentException(400, "Card details are required.");
        }

        var buyer = await GetBuyerAsync(buyerId, cancellationToken);
        if (buyer is null)
        {
            buyer = new Buyer(buyerId);
            await _buyerRepository.AddAsync(buyer, cancellationToken);
        }

        var vaulted = await _payPal.CreatePaymentTokenAsync(
            buyer.PayPalCustomerId,
            card,
            $"vault-{buyer.PayPalCustomerId}-{Guid.NewGuid():N}",
            cancellationToken);

        var last4 = vaulted.Last4 ?? Last4FromCard(card) ?? "****";
        var method = buyer.AddPaymentMethod(vaulted.VaultId, last4, vaulted.Brand, vaulted.Expiry ?? card.Expiry, vaulted.Name ?? card.Name);
        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
        return method;
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var buyer = await GetBuyerAsync(buyerId, cancellationToken);
        return buyer?.PaymentMethods.ToList() ?? new List<PaymentMethod>();
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var buyer = await GetBuyerAsync(buyerId, cancellationToken)
            ?? throw new OrderPaymentException(404, "Saved card was not found.");

        var method = buyer.RemovePaymentMethod(paymentMethodId);
        if (!string.IsNullOrWhiteSpace(method.CardId))
        {
            await _payPal.DeletePaymentTokenAsync(method.CardId, cancellationToken);
        }

        await _buyerRepository.UpdateAsync(buyer, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new OrderPaymentException(400, "`to` must be on or after `from`.");
        }

        var paypalTxns = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        var matches = new List<ReconciliationMatch>();
        var paypalOnly = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTxns)
        {
            var order = FindMatchingOrder(orders, txn);
            if (order is null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matchedOrderIds.Add(order.Id);
            matches.Add(new ReconciliationMatch { OrderId = order.Id, PayPalTransaction = txn });
        }

        var eshopOnly = orders
            .Where(o => !matchedOrderIds.Contains(o.Id) && OrderTouchesRange(o, from, to))
            .Select(ToEshopEntry)
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matches = matches,
            PayPalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private async Task<string> EnsureFreshAuthorizationAsync(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var payment = order.Payment!;
        var authorizationId = payment.AuthorizationId!;

        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PayPalGatewayException ex) when (ex.StatusCode == 404)
        {
            throw CannotRenew(order, "PayPal no longer has this authorization. Ask the shopper to pay again.");
        }

        if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(details.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw CannotRenew(order, $"The authorization is {details.Status} and cannot be captured. Ask the shopper to pay again.");
        }

        if (string.Equals(details.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(details.Status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            return authorizationId;
        }

        if (RequiresRenewal(payment, details))
        {
            return await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
        }

        return details.AuthorizationId;
    }

    private async Task<string> RenewAuthorizationAsync(
        Order order,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        var payment = order.Payment!;
        var original = payment.OriginalAuthorizedAt ?? payment.AuthorizedAt;
        if (original.HasValue && DateTimeOffset.UtcNow - original.Value > AuthorizationLifetime)
        {
            throw CannotRenew(order,
                "The PayPal authorization is older than 29 days and can no longer be renewed. Ask the shopper to pay again.");
        }

        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                payment.AuthorizationId!,
                amount,
                currency,
                $"eshop-{payment.IdempotencyKey ?? order.Id.ToString()}-reauth-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                cancellationToken);

            order.RecordReauthorization(
                renewed.AuthorizationId,
                renewed.Status,
                renewed.Expiration,
                renewed.CreateTime ?? DateTimeOffset.UtcNow);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PayPalGatewayException ex) when (ex.CannotReauthorize() || ex.StatusCode is 422 or 400)
        {
            throw CannotRenew(order,
                "The PayPal authorization can no longer be renewed. Ask the shopper to pay again. " + ex.Message);
        }
    }

    private static bool RequiresRenewal(OrderPayment payment, PayPalAuthorizationDetails details)
    {
        var now = DateTimeOffset.UtcNow;
        if (details.Expiration.HasValue && details.Expiration.Value <= now)
        {
            return true;
        }

        var created = details.CreateTime ?? payment.AuthorizedAt;
        if (created.HasValue && now - created.Value >= AuthorizationHonorPeriod)
        {
            return true;
        }

        return false;
    }

    private static OrderPaymentException CannotRenew(Order order, string message) =>
        new(409, $"Order {order.Id} cannot be fulfilled: {message}");

    private static bool IsFailedCapture(string status) =>
        string.Equals(status, "DECLINED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase);

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!order.BelongsTo(buyerId))
        {
            throw new OrderPaymentException(404, "Order was not found.");
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderPaymentException(404, "Order was not found.");
        }

        return order;
    }

    private Task<Buyer?> GetBuyerAsync(string buyerId, CancellationToken cancellationToken) =>
        _buyerRepository.FirstOrDefaultAsync(new BuyerByIdentitySpecification(buyerId), cancellationToken);

    private static void ValidateCardIfPresent(CardPaymentDetails? card)
    {
        if (card is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry)
            || string.IsNullOrWhiteSpace(card.SecurityCode) || string.IsNullOrWhiteSpace(card.Name))
        {
            throw new OrderPaymentException(400, "Card number, expiry (YYYY-MM), security code and name are required.");
        }

        if (card.Expiry.Length != 7)
        {
            throw new OrderPaymentException(400, "Card expiry must be in YYYY-MM format.");
        }
    }

    private static string? Last4FromCard(CardPaymentDetails? card)
    {
        if (card is null)
        {
            return null;
        }

        var digits = new string(card.Number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            var payment = order.Payment;
            if (payment is null)
            {
                continue;
            }

            if (IdsEqual(txn.TransactionId, payment.PayPalOrderId)
                || IdsEqual(txn.TransactionId, payment.AuthorizationId)
                || IdsEqual(txn.TransactionId, payment.CaptureId)
                || payment.Refunds.Any(r => IdsEqual(txn.TransactionId, r.PayPalRefundId)))
            {
                return order;
            }

            if (IdsEqual(txn.ReferenceId, payment.PayPalOrderId)
                || IdsEqual(txn.ReferenceId, payment.AuthorizationId)
                || IdsEqual(txn.ReferenceId, payment.CaptureId))
            {
                return order;
            }

            if (!string.IsNullOrWhiteSpace(txn.InvoiceId)
                && !string.IsNullOrWhiteSpace(order.Payment?.IdempotencyKey)
                && txn.InvoiceId.Equals($"ESHOP-{order.Payment.IdempotencyKey}", StringComparison.OrdinalIgnoreCase))
            {
                return order;
            }

            if (!string.IsNullOrWhiteSpace(txn.InvoiceId)
                && (txn.InvoiceId.Equals($"ESHOP-{order.Id}", StringComparison.OrdinalIgnoreCase)
                    || txn.InvoiceId.StartsWith($"ESHOP-{order.Id}-", StringComparison.OrdinalIgnoreCase)))
            {
                return order;
            }

            if (!string.IsNullOrWhiteSpace(txn.CustomField)
                && txn.CustomField.Equals(order.Id.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return order;
            }
        }

        return null;
    }

    private static bool IdsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool OrderTouchesRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        var payment = order.Payment;
        var times = new List<DateTimeOffset> { order.OrderDate };
        if (payment?.AuthorizedAt is { } authorized) times.Add(authorized);
        if (payment?.CapturedAt is { } captured) times.Add(captured);
        times.AddRange(payment?.Refunds.Select(r => r.CreatedAt) ?? Enumerable.Empty<DateTimeOffset>());
        return times.Any(t => t >= from && t <= to);
    }

    private static ReconciliationEshopEntry ToEshopEntry(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.Status.ToString(),
        PayPalOrderId = order.Payment?.PayPalOrderId,
        AuthorizationId = order.Payment?.AuthorizationId,
        CaptureId = order.Payment?.CaptureId,
        RefundIds = order.Payment?.Refunds.Select(r => r.PayPalRefundId).ToList() ?? new List<string>()
    };
}
