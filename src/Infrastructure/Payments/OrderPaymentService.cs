using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Payments.PayPal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan AuthorizationHonorPeriod = TimeSpan.FromDays(3);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalOptions _options;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        IOptions<PayPalOptions> options,
        ILogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new PaymentException("An order must contain at least one catalog item.");
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (grouped.Any(i => i.Quantity <= 0))
        {
            throw new PaymentException("Each order line must have a quantity greater than zero.");
        }

        var catalogIds = grouped.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        foreach (var line in grouped)
        {
            if (!catalogById.ContainsKey(line.CatalogItemId))
            {
                throw new PaymentException($"Catalog item {line.CatalogItemId} was not found.");
            }
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

        var address = shippingAddress ?? new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        var order = new Order(buyerId, address, orderItems);
        return await _orderRepository.AddAsync(order, cancellationToken);
    }

    public async Task<Order> PayAsync(
        int orderId,
        string buyerId,
        PayOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureBuyer(order, buyerId);

        if (order.Status is OrderFulfillmentStatus.Authorized
            or OrderFulfillmentStatus.Fulfilled
            or OrderFulfillmentStatus.PartiallyRefunded
            or OrderFulfillmentStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderFulfillmentStatus.Cancelled)
        {
            throw new PaymentConflictException("A cancelled order cannot be paid.");
        }

        if (order.Status != OrderFulfillmentStatus.AwaitingPayment)
        {
            throw new PaymentConflictException($"Order {orderId} cannot be paid in status {order.Status}.");
        }

        var currency = RequireCurrency();
        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        if (amount <= 0)
        {
            throw new PaymentException("The order total must be greater than zero to take payment.");
        }

        PayPalOrderAuthorization authorization;
        if (command.PaymentMethodId is int paymentMethodId)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByBuyerAndIdSpec(buyerId, paymentMethodId), cancellationToken);
            if (saved is null)
            {
                throw new OrderNotFoundException("The saved card was not found or does not belong to the current shopper.");
            }

            authorization = await _payPal.AuthorizeVaultedCardPaymentAsync(new PayPalVaultAuthorizationRequest
            {
                RequestId = PayRequestId(order),
                InvoiceId = InvoiceId(order),
                CustomId = order.Payment.PaymentAttemptKey,
                Amount = amount,
                Currency = currency,
                VaultId = saved.PayPalVaultId
            }, cancellationToken);
        }
        else if (command.Card is not null)
        {
            authorization = await _payPal.AuthorizeCardPaymentAsync(new PayPalCardAuthorizationRequest
            {
                RequestId = PayRequestId(order),
                InvoiceId = InvoiceId(order),
                CustomId = order.Payment.PaymentAttemptKey,
                Amount = amount,
                Currency = currency,
                CardNumber = NormalizeCardNumber(command.Card.Number),
                Expiry = command.Card.Expiry,
                SecurityCode = command.Card.SecurityCode,
                CardholderName = command.Card.Name,
                BillingAddress = MapBillingAddress(command.Card.BillingAddress)
            }, cancellationToken);
        }
        else
        {
            throw new PaymentException("Provide card details or a saved paymentMethodId.");
        }

        var authorizedAmount = decimal.Round(authorization.Amount, 2, MidpointRounding.AwayFromZero);
        if (authorizedAmount != amount)
        {
            throw new PayPalGatewayException(
                $"PayPal authorized {authorizedAmount} {authorization.Currency} but the order total is {amount} {currency}.");
        }

        order.Payment.RecordPayPalOrder(authorization.PayPalOrderId, authorization.PayPalOrderStatus);
        order.Payment.RecordAuthorization(
            authorization.AuthorizationId,
            authorization.AuthorizationStatus,
            authorizedAmount,
            authorization.Currency,
            authorization.CreatedAt ?? DateTimeOffset.UtcNow,
            authorization.ExpiresAt);
        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status is OrderFulfillmentStatus.Fulfilled
            or OrderFulfillmentStatus.PartiallyRefunded
            or OrderFulfillmentStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderFulfillmentStatus.Cancelled)
        {
            throw new PaymentConflictException("A cancelled order cannot be fulfilled.");
        }

        if (order.Status != OrderFulfillmentStatus.Authorized || string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
        {
            throw new PaymentConflictException("An order can only be fulfilled after payment has been authorized.");
        }

        var currency = order.Payment.Currency ?? RequireCurrency();
        var amount = decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        var authorizationId = order.Payment.AuthorizationId;

        PayPalAuthorizationDetails liveAuthorization;
        try
        {
            liveAuthorization = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PayPalGatewayException ex) when (ex.HttpStatus == 404)
        {
            throw new AuthorizationCannotBeRenewedException(
                "PayPal no longer has this authorization. Ask the shopper to pay again; a new hold cannot be placed from this order.");
        }

        if (string.Equals(liveAuthorization.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(liveAuthorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationCannotBeRenewedException(
                $"The PayPal authorization is {liveAuthorization.Status} and cannot be captured or renewed. Ask the shopper to pay again.");
        }

        if (NeedsReauthorization(liveAuthorization, order.Payment))
        {
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(
                    authorizationId,
                    amount,
                    currency,
                    ReauthRequestId(order),
                    cancellationToken);
                order.Payment.RecordAuthorization(
                    renewed.AuthorizationId,
                    renewed.Status,
                    decimal.Round(renewed.Amount, 2, MidpointRounding.AwayFromZero),
                    renewed.Currency,
                    renewed.CreatedAt ?? DateTimeOffset.UtcNow,
                    renewed.ExpiresAt);
                authorizationId = renewed.AuthorizationId;
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }
            catch (PayPalGatewayException ex)
            {
                _logger.LogWarning(ex, "PayPal refused to reauthorize order {OrderId}", order.Id);
                throw new AuthorizationCannotBeRenewedException(
                    "The payment hold has gone stale and PayPal will not renew it. Capture is no longer possible on this authorization; ask the shopper to pay again.");
            }
        }

        PayPalCaptureDetails capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId,
                amount,
                currency,
                FulfilRequestId(order),
                cancellationToken);
        }
        catch (PayPalGatewayException ex) when (IsStaleAuthorization(ex))
        {
            PayPalAuthorizationDetails renewed;
            try
            {
                renewed = await _payPal.ReauthorizeAsync(
                    authorizationId,
                    amount,
                    currency,
                    ReauthRequestId(order),
                    cancellationToken);
            }
            catch (PayPalGatewayException renewEx)
            {
                _logger.LogWarning(renewEx, "PayPal refused to reauthorize order {OrderId} after a failed capture", order.Id);
                throw new AuthorizationCannotBeRenewedException(
                    "The payment hold expired and PayPal will not renew it. Fulfilment cannot take the money; ask the shopper to pay again.");
            }

            order.Payment.RecordAuthorization(
                renewed.AuthorizationId,
                renewed.Status,
                decimal.Round(renewed.Amount, 2, MidpointRounding.AwayFromZero),
                renewed.Currency,
                renewed.CreatedAt ?? DateTimeOffset.UtcNow,
                renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            capture = await _payPal.CaptureAuthorizationAsync(
                renewed.AuthorizationId,
                amount,
                currency,
                $"{FulfilRequestId(order)}-renewed",
                cancellationToken);
        }

        order.Payment.RecordCapture(
            capture.CaptureId,
            capture.Status,
            capture.CapturedAmount,
            capture.PaypalFee,
            capture.NetAmount,
            capture.Currency);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderFulfillmentStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderFulfillmentStatus.Fulfilled
            or OrderFulfillmentStatus.PartiallyRefunded
            or OrderFulfillmentStatus.Refunded)
        {
            throw new PaymentConflictException("A fulfilled order cannot be cancelled; issue a refund instead.");
        }

        if (order.Status == OrderFulfillmentStatus.Authorized &&
            !string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
        {
            try
            {
                await _payPal.VoidAuthorizationAsync(
                    order.Payment.AuthorizationId,
                    CancelRequestId(order),
                    cancellationToken);
            }
            catch (PayPalGatewayException ex) when (ex.HttpStatus is 404 or 422)
            {
                _logger.LogWarning(ex, "PayPal could not void authorization for order {OrderId}; marking cancelled locally.", order.Id);
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<OrderRefund> RefundAsync(
        int orderId,
        string callerBuyerId,
        bool callerIsAdministrator,
        RefundOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new PaymentException("A refund requires an idempotencyKey.");
        }

        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!callerIsAdministrator)
        {
            EnsureBuyer(order, callerBuyerId);
        }

        var existing = order.FindRefundByIdempotencyKey(command.IdempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (order.Status is not (OrderFulfillmentStatus.Fulfilled
            or OrderFulfillmentStatus.PartiallyRefunded))
        {
            throw new PaymentConflictException("A refund can only be issued after the order has been fulfilled.");
        }

        if (string.IsNullOrWhiteSpace(order.Payment.CaptureId))
        {
            throw new PaymentConflictException("This order has no captured payment to refund.");
        }

        var remaining = decimal.Round(order.RemainingRefundable(), 2, MidpointRounding.AwayFromZero);
        if (remaining <= 0)
        {
            throw new PaymentConflictException("This capture has already been refunded in full.");
        }

        var amount = command.Amount.HasValue
            ? decimal.Round(command.Amount.Value, 2, MidpointRounding.AwayFromZero)
            : remaining;

        if (amount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }

        if (amount > remaining)
        {
            throw new PaymentException($"Refund amount {amount} exceeds the remaining refundable amount {remaining}.");
        }

        var currency = order.Payment.Currency ?? RequireCurrency();
        var paypalRequestId = PayPalRefundRequestId(order, command.IdempotencyKey);
        var paypalRefund = await _payPal.RefundCaptureAsync(
            order.Payment.CaptureId,
            amount,
            currency,
            paypalRequestId,
            cancellationToken);

        var refund = new OrderRefund(
            paypalRefund.RefundId,
            paypalRefund.Status,
            decimal.Round(paypalRefund.Amount, 2, MidpointRounding.AwayFromZero),
            paypalRefund.Currency,
            command.IdempotencyKey);

        order.AddRefund(refund);
        if (order.Status == OrderFulfillmentStatus.Refunded)
        {
            order.Payment.UpdateCaptureStatus("REFUNDED");
        }
        else
        {
            order.Payment.UpdateCaptureStatus("PARTIALLY_REFUNDED");
        }

        await _orderRepository.UpdateAsync(order, cancellationToken);
        return refund;
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var paypalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);

        var paypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var txn in paypalTransactions)
        {
            paypalIds.Add(txn.TransactionId);
            if (!string.IsNullOrWhiteSpace(txn.ReferenceId))
            {
                paypalIds.Add(txn.ReferenceId);
            }
        }

        var localIds = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        void Index(string? id, Order order)
        {
            if (!string.IsNullOrWhiteSpace(id) && !localIds.ContainsKey(id))
            {
                localIds[id] = order;
            }
        }

        foreach (var order in orders)
        {
            Index(order.Payment.PayPalOrderId, order);
            Index(order.Payment.AuthorizationId, order);
            Index(order.Payment.CaptureId, order);
            foreach (var refund in order.Refunds)
            {
                Index(refund.PayPalRefundId, order);
            }

            Index(InvoiceId(order), order);
            Index(order.Payment.PaymentAttemptKey, order);
        }

        var matches = new List<ReconciliationMatch>();
        var paypalOnly = new List<ReconciliationPayPalOnly>();
        var matchedPayPal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in paypalTransactions)
        {
            Order? order = null;
            if (localIds.TryGetValue(txn.TransactionId, out var byTxn))
            {
                order = byTxn;
            }
            else if (!string.IsNullOrWhiteSpace(txn.ReferenceId) && localIds.TryGetValue(txn.ReferenceId, out var byRef))
            {
                order = byRef;
            }
            else if (!string.IsNullOrWhiteSpace(txn.InvoiceId) && localIds.TryGetValue(txn.InvoiceId, out var byInvoice))
            {
                order = byInvoice;
            }
            else if (!string.IsNullOrWhiteSpace(txn.CustomField) && localIds.TryGetValue(txn.CustomField, out var byCustom))
            {
                order = byCustom;
            }

            if (order is not null)
            {
                matchedPayPal.Add(txn.TransactionId);
                matches.Add(new ReconciliationMatch
                {
                    OrderId = order.Id,
                    PayPalTransactionId = txn.TransactionId,
                    PayPalReferenceId = txn.ReferenceId,
                    LocalPaymentId = order.Payment.CaptureId ?? order.Payment.AuthorizationId ?? order.Payment.PayPalOrderId,
                    Kind = "matched"
                });
            }
            else
            {
                paypalOnly.Add(new ReconciliationPayPalOnly
                {
                    TransactionId = txn.TransactionId,
                    ReferenceId = txn.ReferenceId,
                    EventCode = txn.EventCode,
                    Status = txn.Status,
                    Amount = txn.Amount,
                    Currency = txn.Currency
                });
            }
        }

        var eshopOnly = new List<ReconciliationEshopOnly>();
        foreach (var order in orders.Where(HasPaymentFingerprint))
        {
            var inRange = OrderTouchesRange(order, from, to);
            if (!inRange)
            {
                continue;
            }

            var ids = LocalPaymentIds(order).ToList();
            if (ids.Count == 0)
            {
                continue;
            }

            if (ids.Any(id => paypalIds.Contains(id)))
            {
                continue;
            }

            eshopOnly.Add(new ReconciliationEshopOnly
            {
                OrderId = order.Id,
                PayPalOrderId = order.Payment.PayPalOrderId,
                AuthorizationId = order.Payment.AuthorizationId,
                CaptureId = order.Payment.CaptureId,
                Status = order.Status.ToString()
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matches = matches,
            PayPalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private static void EnsureBuyer(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderNotFoundException("This order does not belong to the current shopper.");
        }
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_options.Currency))
        {
            throw new PaymentException("PayPal:Currency is not configured.");
        }

        return _options.Currency;
    }

    private static bool NeedsReauthorization(PayPalAuthorizationDetails live, OrderPayment payment)
    {
        if (string.Equals(live.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(live.Status, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (live.ExpiresAt is DateTimeOffset expires && expires <= now)
        {
            return true;
        }

        var created = live.CreatedAt ?? payment.AuthorizationCreatedAt;
        return created is DateTimeOffset createdAt && now - createdAt >= AuthorizationHonorPeriod;
    }

    private static bool IsStaleAuthorization(PayPalGatewayException exception)
    {
        var name = exception.PayPalErrorName ?? string.Empty;
        var message = exception.Message ?? string.Empty;
        return name.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPaymentFingerprint(Order order) =>
        !string.IsNullOrWhiteSpace(order.Payment.PayPalOrderId) ||
        !string.IsNullOrWhiteSpace(order.Payment.AuthorizationId) ||
        !string.IsNullOrWhiteSpace(order.Payment.CaptureId);

    private static IEnumerable<string> LocalPaymentIds(Order order)
    {
        if (!string.IsNullOrWhiteSpace(order.Payment.PayPalOrderId)) yield return order.Payment.PayPalOrderId;
        if (!string.IsNullOrWhiteSpace(order.Payment.AuthorizationId)) yield return order.Payment.AuthorizationId;
        if (!string.IsNullOrWhiteSpace(order.Payment.CaptureId)) yield return order.Payment.CaptureId;
        foreach (var refund in order.Refunds)
        {
            yield return refund.PayPalRefundId;
        }

        yield return InvoiceId(order);
        if (!string.IsNullOrWhiteSpace(order.Payment.PaymentAttemptKey))
        {
            yield return order.Payment.PaymentAttemptKey;
        }
    }

    private static bool OrderTouchesRange(Order order, DateTimeOffset from, DateTimeOffset to)
    {
        var timestamps = new List<DateTimeOffset> { order.OrderDate };
        if (order.Payment.AuthorizationCreatedAt is DateTimeOffset authorized)
        {
            timestamps.Add(authorized);
        }

        timestamps.AddRange(order.Refunds.Select(r => r.CreatedAt));
        return timestamps.Any(ts => ts >= from && ts <= to);
    }

    private static PayPalBillingAddress MapBillingAddress(BillingAddressDetails? address)
    {
        return new PayPalBillingAddress
        {
            AddressLine1 = string.IsNullOrWhiteSpace(address?.AddressLine1) ? "123 Main Street" : address!.AddressLine1,
            AddressLine2 = address?.AddressLine2,
            AdminArea2 = string.IsNullOrWhiteSpace(address?.AdminArea2) ? "San Jose" : address!.AdminArea2,
            AdminArea1 = string.IsNullOrWhiteSpace(address?.AdminArea1) ? "CA" : address!.AdminArea1,
            PostalCode = string.IsNullOrWhiteSpace(address?.PostalCode) ? "95131" : address!.PostalCode,
            CountryCode = string.IsNullOrWhiteSpace(address?.CountryCode) ? "US" : address!.CountryCode
        };
    }

    private static string NormalizeCardNumber(string number) =>
        new string(number.Where(char.IsDigit).ToArray());

    private static string PayRequestId(Order order) => $"eshop-pay-{order.Payment.PaymentAttemptKey}";
    private static string InvoiceId(Order order) => $"ESHOP-{order.Payment.PaymentAttemptKey}";
    private static string FulfilRequestId(Order order) => $"eshop-fulfil-{order.Payment.PaymentAttemptKey}";
    private static string CancelRequestId(Order order) => $"eshop-cancel-{order.Payment.PaymentAttemptKey}";
    private static string ReauthRequestId(Order order) => $"eshop-reauth-{order.Payment.PaymentAttemptKey}";

    private static string PayPalRefundRequestId(Order order, string idempotencyKey)
    {
        var combined = $"{order.Payment.PaymentAttemptKey}-{idempotencyKey}";
        return combined.Length <= 108 ? combined : combined[..108];
    }
}
