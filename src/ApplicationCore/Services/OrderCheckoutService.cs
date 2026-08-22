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

public class OrderCheckoutService : IOrderCheckoutService
{
    public const string CurrencyOptionsName = "PayPal";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IPayPalSettings _payPalSettings;

    public OrderCheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalGateway payPalGateway,
        IPayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _uriComposer = uriComposer;
        _payPalGateway = payPalGateway;
        _payPalSettings = payPalSettings;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> lines,
        Address shippingAddress,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shippingAddress, nameof(shippingAddress));

        if (lines == null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one catalog item.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new PaymentNotFoundException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shippingAddress, orderItems);
        order.AwaitPayment(_payPalSettings.Currency);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        CardPaymentSource? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.PaymentStatus is OrderPaymentStatus.Authorized or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {orderId} is cancelled and cannot be paid.");
        }

        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new PaymentConflictException($"Order {orderId} is not awaiting payment.");
        }

        var hasCard = card != null && !string.IsNullOrWhiteSpace(card.Number);
        if (hasCard == savedPaymentMethodId.HasValue)
        {
            throw new PaymentException("Provide either card details or a saved payment method, not both.");
        }

        string? vaultId = null;
        if (savedPaymentMethodId.HasValue)
        {
            var saved = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(savedPaymentMethodId.Value, buyerId),
                cancellationToken);
            if (saved == null)
            {
                throw new PaymentNotFoundException(
                    $"Saved payment method {savedPaymentMethodId.Value} was not found for this shopper.");
            }

            vaultId = saved.PayPalPaymentTokenId;
        }
        else
        {
            ValidateCard(card!);
        }

        var currency = _payPalSettings.Currency;
        var amount = order.Total();
        var authorization = await _payPalGateway.AuthorizeOrderAsync(
            new CreateAuthorizedPaymentRequest
            {
                InvoiceId = UniqueInvoiceId(order.Id),
                CustomId = order.Id.ToString(),
                Currency = currency,
                Amount = amount,
                Description = $"eShopOnWeb order {order.Id}",
                Card = vaultId == null ? card : null,
                VaultId = vaultId
            },
            payPalRequestId: PayRequestId(order),
            cancellationToken);

        if (!PayPalMoney.EqualsToTheCent(authorization.Amount, amount, currency))
        {
            throw new PaymentException(
                $"PayPal authorized {authorization.Amount} {authorization.Currency} but the order total is {PayPalMoney.Format(amount, currency)} {currency}.");
        }

        order.RecordAuthorization(
            authorization.OrderId,
            authorization.OrderStatus,
            authorization.AuthorizationId,
            authorization.AuthorizationStatus,
            authorization.CreateTime,
            authorization.ExpirationTime,
            authorization.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            return order;
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {orderId} is cancelled and cannot be fulfilled.");
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized || string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
        {
            throw new PaymentConflictException($"Order {orderId} has no authorization to capture.");
        }

        var currency = order.Payment.Currency ?? _payPalSettings.Currency;
        var amount = order.Total();
        var authorizationId = await EnsureFreshAuthorizationAsync(order, currency, amount, cancellationToken);

        var capture = await _payPalGateway.CaptureAuthorizationAsync(
            authorizationId,
            currency,
            amount,
            UniqueInvoiceId(order.Id),
            payPalRequestId: $"eshop-fulfil-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}",
            cancellationToken);

        order.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetProceeds,
            capture.Currency);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return order;
        }

        if (order.PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException($"Order {orderId} has already been fulfilled. Issue a refund instead of cancelling.");
        }

        if (order.PaymentStatus == OrderPaymentStatus.Authorized
            && !string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
        {
            await _payPalGateway.VoidAuthorizationAsync(
                order.Payment.AuthorizationId,
                payPalRequestId: $"eshop-cancel-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}",
                cancellationToken);
        }

        order.VoidAuthorization("VOIDED");
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);
        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return (order, existing);
        }

        if (order.PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded)
            || string.IsNullOrWhiteSpace(order.Payment.CaptureId))
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
        }

        var remaining = order.RemainingRefundable();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }

        if (refundAmount - remaining > 0.0000001m)
        {
            throw new PaymentConflictException(
                $"Refund of {refundAmount} exceeds the remaining captured amount of {remaining} for order {orderId}.");
        }

        var currency = order.Payment.Currency ?? _payPalSettings.Currency;
        var paypalRefund = await _payPalGateway.RefundCaptureAsync(
            order.Payment.CaptureId,
            currency,
            amount.HasValue ? refundAmount : null,
            payPalRequestId: $"eshop-refund-{order.Id}-{idempotencyKey}",
            cancellationToken);

        var recordedAmount = paypalRefund.Amount > 0 ? paypalRefund.Amount : refundAmount;
        var refund = order.RecordRefund(
            paypalRefund.RefundId,
            paypalRefund.Status,
            recordedAmount,
            string.IsNullOrWhiteSpace(paypalRefund.Currency) ? currency : paypalRefund.Currency,
            idempotencyKey);

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("`to` must be on or after `from`.");
        }

        var paypalTransactions = await _payPalGateway.ListAllTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInDateRangeSpec(from, to), cancellationToken);

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<PayPalReportedTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTransactions)
        {
            var order = FindMatchingOrder(orders, txn);
            if (order == null)
            {
                paypalOnly.Add(txn);
                continue;
            }

            matchedOrderIds.Add(order.Id);

            matched.Add(new ReconciliationMatch
            {
                OrderId = order.Id,
                PayPalTransactionId = txn.TransactionId,
                MatchReason = DescribeMatch(order, txn)
            });
        }

        var eShopOnly = orders
            .Where(o => o.PaymentStatus != OrderPaymentStatus.None
                        && o.PaymentStatus != OrderPaymentStatus.AwaitingPayment
                        && !matchedOrderIds.Contains(o.Id)
                        && HasPayPalIdentifiers(o))
            .Select(o => new EShopUnmatchedPayment
            {
                OrderId = o.Id,
                PaymentStatus = o.PaymentStatus.ToString(),
                PayPalOrderId = o.Payment.PayPalOrderId,
                AuthorizationId = o.Payment.AuthorizationId,
                CaptureId = o.Payment.CaptureId
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PayPalOnly = paypalOnly,
            EShopOnly = eShopOnly
        };
    }

    private async Task<string> EnsureFreshAuthorizationAsync(
        Order order,
        string currency,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var authorizationId = order.Payment.AuthorizationId!;
        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPalGateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PaymentException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The PayPal authorization for order {order.Id} is no longer available. Ask the shopper to pay again so a new hold can be placed.",
                ex.DebugId);
        }

        order.RefreshAuthorization(details.AuthorizationId, details.Status, details.CreateTime, details.ExpirationTime);

        var now = DateTimeOffset.UtcNow;
        var stale = details.Status.Equals("PENDING", StringComparison.OrdinalIgnoreCase) is false
                    && (order.AuthorizationHasExpired(now) || order.AuthorizationHonorPeriodElapsed(now));

        if (!stale && details.Status.Equals("CREATED", StringComparison.OrdinalIgnoreCase))
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return details.AuthorizationId;
        }

        if (details.Status.Equals("VOIDED", StringComparison.OrdinalIgnoreCase)
            || details.Status.Equals("DENIED", StringComparison.OrdinalIgnoreCase)
            || details.Status.Equals("CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The PayPal authorization for order {order.Id} is {details.Status} and cannot be captured. Ask the shopper to pay again so a new hold can be placed.");
        }

        if (!stale)
        {
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return details.AuthorizationId;
        }

        try
        {
            var renewed = await _payPalGateway.ReauthorizeAsync(
                details.AuthorizationId,
                currency,
                amount,
                payPalRequestId: $"eshop-reauth-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}",
                cancellationToken);

            order.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.CreateTime, renewed.ExpirationTime);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The PayPal authorization for order {order.Id} has gone stale and cannot be renewed. Ask the shopper to pay again so a new hold can be placed. PayPal: {ex.Message}",
                ex.DebugId);
        }
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentForbiddenException("This order does not belong to the signed-in shopper.");
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private static void ValidateCard(CardPaymentSource card)
    {
        Guard.Against.NullOrEmpty(card.Number, nameof(card.Number));
        Guard.Against.NullOrEmpty(card.Expiry, nameof(card.Expiry));
        Guard.Against.NullOrEmpty(card.SecurityCode, nameof(card.SecurityCode));
        Guard.Against.NullOrEmpty(card.Name, nameof(card.Name));
        if (card.BillingAddress == null || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
        {
            throw new PaymentException("Card billing address with a country code is required.");
        }
    }

    private static string InvoiceId(int orderId) => $"ESHOP-{orderId}";

    private static string UniqueInvoiceId(int orderId) =>
        $"{InvoiceId(orderId)}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";

    private static string PayRequestId(Order order) =>
        $"eshop-pay-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}";

    private static bool HasPayPalIdentifiers(Order order)
    {
        return !string.IsNullOrWhiteSpace(order.Payment.PayPalOrderId)
               || !string.IsNullOrWhiteSpace(order.Payment.AuthorizationId)
               || !string.IsNullOrWhiteSpace(order.Payment.CaptureId)
               || order.Refunds.Count > 0;
    }

    private static Order? FindMatchingOrder(IReadOnlyList<Order> orders, PayPalReportedTransaction txn)
    {
        foreach (var order in orders)
        {
            if (Matches(order, txn))
            {
                return order;
            }
        }

        return null;
    }

    private static bool Matches(Order order, PayPalReportedTransaction txn)
    {
        var orderId = order.Id.ToString();
        var invoiceId = InvoiceId(order.Id);
        var ids = new[]
        {
            order.Payment.PayPalOrderId,
            order.Payment.AuthorizationId,
            order.Payment.CaptureId
        }.Concat(order.Refunds.Select(r => r.PayPalRefundId))
         .Where(id => !string.IsNullOrWhiteSpace(id))
         .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(txn.CustomField)
            && (string.Equals(txn.CustomField, orderId, StringComparison.OrdinalIgnoreCase)
                || txn.CustomField.Contains(orderId, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(txn.InvoiceId)
            && (string.Equals(txn.InvoiceId, invoiceId, StringComparison.OrdinalIgnoreCase)
                || txn.InvoiceId.StartsWith(invoiceId + "-", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(txn.TransactionId) && ids.Contains(txn.TransactionId))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(txn.ReferenceId) && ids.Contains(txn.ReferenceId))
        {
            return true;
        }

        return false;
    }

    private static string DescribeMatch(Order order, PayPalReportedTransaction txn)
    {
        if (!string.IsNullOrWhiteSpace(txn.InvoiceId) && txn.InvoiceId.Contains($"ESHOP-{order.Id}", StringComparison.OrdinalIgnoreCase))
        {
            return "invoice_id";
        }

        if (!string.IsNullOrWhiteSpace(txn.CustomField))
        {
            return "custom_field";
        }

        if (!string.IsNullOrWhiteSpace(txn.TransactionId))
        {
            return "transaction_id";
        }

        return "paypal_reference_id";
    }
}
