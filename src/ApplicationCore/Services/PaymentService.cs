using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private static readonly TimeSpan HonorPeriod = TimeSpan.FromDays(3);
    private static readonly TimeSpan MaxReauthorizeAge = TimeSpan.FromDays(29);

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<Order> _orderReadRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IReadRepository<OrderPayment> _paymentReadRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<Order> orderReadRepository,
        IRepository<OrderPayment> paymentRepository,
        IReadRepository<OrderPayment> paymentReadRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalGateway payPal,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _orderReadRepository = orderReadRepository;
        _paymentRepository = paymentRepository;
        _paymentReadRepository = paymentReadRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<OrderPaymentResult> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentRequest? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetShopperOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await GetOrCreatePaymentAsync(order, cancellationToken);

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded
            && !string.IsNullOrEmpty(payment.AuthorizationId))
        {
            return ToResult(order, payment);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {orderId} is cancelled and cannot be paid.");
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentConflictException($"Order {orderId} is {order.Status} and cannot be authorized.");
        }

        if (card is not null && paymentMethodId is not null)
        {
            throw new InvalidPaymentRequestException("Send either card details or a saved paymentMethodId, not both.");
        }

        var amount = RoundMoney(order.Total());
        var items = ToPurchaseItems(order);
        var invoiceId = InvoiceId(order.Id);
        var customId = order.Id.ToString(CultureInfo.InvariantCulture);
        var requestId = RequestId("pay", order);

        PayPalAuthorizationResult authorization;
        if (paymentMethodId is not null)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId.Value, buyerId), cancellationToken);
            if (saved is null)
            {
                throw new ResourceNotFoundException($"Saved payment method {paymentMethodId} was not found.");
            }

            authorization = await _payPal.AuthorizeVaultedCardAsync(
                requestId, invoiceId, customId, amount, items, saved.PayPalPaymentTokenId, cancellationToken);
        }
        else
        {
            if (card is null)
            {
                throw new InvalidPaymentRequestException("Provide card details or a saved paymentMethodId to pay.");
            }

            authorization = await _payPal.AuthorizeCardAsync(
                requestId, invoiceId, customId, amount, items, ToPayPalCard(card, order.ShipToAddress), cancellationToken);
        }

        if (authorization.Amount != amount)
        {
            throw new PayPalGatewayException(
                $"PayPal authorized {authorization.Amount:0.00} {authorization.Currency} but the order total is {amount:0.00} {_payPal.Currency}.",
                502);
        }

        payment.RecordAuthorization(
            authorization.PayPalOrderId,
            authorization.PayPalOrderStatus,
            authorization.AuthorizationId,
            authorization.AuthorizationStatus,
            authorization.Amount,
            authorization.Expiration);
        order.MarkAuthorized();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return ToResult(order, payment);
    }

    public async Task<OrderPaymentResult> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await GetPaymentAsync(order.Id, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded
            && !string.IsNullOrEmpty(payment.CaptureId))
        {
            return ToResult(order, payment);
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {orderId} is cancelled and cannot be fulfilled.");
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new PaymentConflictException($"Order {orderId} must be authorized before it can be fulfilled.");
        }

        var authorizationId = await EnsureFreshAuthorizationAsync(order, payment, cancellationToken);
        var capture = await _payPal.CaptureAsync(
            RequestId("fulfil", order),
            authorizationId,
            InvoiceId(order.Id),
            cancellationToken);

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PaypalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return ToResult(order, payment);
    }

    public async Task<OrderPaymentResult> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await GetOrCreatePaymentAsync(order, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return ToResult(order, payment);
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentConflictException(
                $"Order {orderId} has already been fulfilled. Issue a refund instead of cancelling.");
        }

        if (!string.IsNullOrEmpty(payment.AuthorizationId))
        {
            try
            {
                await _payPal.VoidAuthorizationAsync(RequestId("cancel", order), payment.AuthorizationId, cancellationToken);
                payment.RecordVoid("VOIDED");
            }
            catch (PayPalGatewayException ex) when (IsAlreadyVoided(ex))
            {
                payment.RecordVoid("VOIDED");
            }
        }

        order.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return ToResult(order, payment);
    }

    public async Task<RefundResult> RefundAsync(
        string buyerId,
        int orderId,
        string idempotencyKey,
        decimal? amount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new InvalidPaymentRequestException("Refunds require an idempotencyKey.");
        }

        var order = await GetShopperOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await GetPaymentAsync(order.Id, cancellationToken);

        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return new RefundResult(existing.PayPalRefundId, order.Id, order.Status, existing.Amount, existing.Currency,
                payment.RemainingRefundable, existing.Status);
        }

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new PaymentConflictException($"Order {orderId} must be fulfilled before it can be refunded.");
        }

        if (string.IsNullOrEmpty(payment.CaptureId) || payment.CapturedAmount is null)
        {
            throw new PaymentConflictException($"Order {orderId} has no captured PayPal payment to refund.");
        }

        var refundAmount = amount is null ? payment.RemainingRefundable : RoundMoney(amount.Value);
        if (refundAmount <= 0)
        {
            throw new InvalidPaymentRequestException("Refund amount must be greater than zero.");
        }

        if (refundAmount > payment.RemainingRefundable)
        {
            throw new InvalidPaymentRequestException(
                $"Refund of {refundAmount:0.00} {payment.Currency} exceeds the remaining refundable amount of {payment.RemainingRefundable:0.00} {payment.Currency}.");
        }

        var paypalRefund = await _payPal.RefundAsync(
            idempotencyKey,
            payment.CaptureId,
            amount is null ? null : refundAmount,
            payment.Currency,
            cancellationToken);

        var recorded = payment.RecordRefund(paypalRefund.RefundId, paypalRefund.Status, paypalRefund.Amount, idempotencyKey);
        order.MarkRefunded(payment.RemainingRefundable == 0);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return new RefundResult(recorded.PayPalRefundId, order.Id, order.Status, recorded.Amount, recorded.Currency,
            payment.RemainingRefundable, recorded.Status);
    }

    public async Task<IReadOnlyList<ShopperOrderResult>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderReadRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentReadRepository.ListAsync(
            new OrderPaymentsByOrderIdsSpec(orders.Select(o => o.Id).ToArray()), cancellationToken);
        var paymentByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order =>
            {
                paymentByOrder.TryGetValue(order.Id, out var payment);
                return new ShopperOrderResult(
                    order.Id,
                    order.Status,
                    order.OrderDate,
                    RoundMoney(order.Total()),
                    payment?.Currency ?? _payPal.Currency,
                    ToLineResults(order),
                    payment is null ? null : ToPaymentState(payment));
            })
            .ToList();
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardPaymentRequest card, CancellationToken cancellationToken = default)
    {
        if (card is null)
        {
            throw new InvalidPaymentRequestException("Card details are required to save a payment method.");
        }

        var vaulted = await _payPal.VaultCardAsync(
            $"eshop-vault-{buyerId}-{Guid.NewGuid():N}",
            SanitizeCustomerId(buyerId),
            ToPayPalCard(card, billingFallback: null),
            cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            vaulted.PaymentTokenId,
            vaulted.CustomerId,
            vaulted.LastDigits,
            vaulted.Brand,
            vaulted.Expiry,
            vaulted.CardholderName);
        await _savedCardRepository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);
        if (saved is null)
        {
            throw new ResourceNotFoundException($"Saved payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _payPal.DeleteVaultedCardAsync(saved.PayPalPaymentTokenId, cancellationToken);
        }
        catch (PayPalGatewayException ex) when (ex.StatusCode == 404)
        {
            _logger.LogInformation($"PayPal payment token for payment method {paymentMethodId} was already removed.");
        }

        await _savedCardRepository.DeleteAsync(saved, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidPaymentRequestException("`to` must be on or after `from`.");
        }

        var paypalTxns = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderReadRepository.ListAsync(new OrdersInDateRangeSpec(from, to), cancellationToken);
        var payments = await _paymentReadRepository.ListAsync(new OrderPaymentsInDateRangeSpec(from, to), cancellationToken);

        var orderById = orders.ToDictionary(o => o.Id);
        foreach (var payment in payments)
        {
            if (!orderById.ContainsKey(payment.OrderId))
            {
                var extra = await _orderReadRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(payment.OrderId), cancellationToken);
                if (extra is not null)
                {
                    orderById[extra.Id] = extra;
                }
            }
        }

        var matched = new List<ReconciliationRow>();
        var paypalOnly = new List<ReconciliationRow>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in paypalTxns)
        {
            var (matchedOrderId, reason) = FindMatch(txn, payments, orderById);
            if (matchedOrderId is int orderId)
            {
                matchedOrderIds.Add(orderId);
                orderById.TryGetValue(orderId, out var full);
                var payment = payments.FirstOrDefault(p => p.OrderId == orderId);
                matched.Add(ToRow(txn, full, payment, reason));
            }
            else
            {
                paypalOnly.Add(ToRow(txn, null, null, "PayPal transaction has no matching eShop order"));
            }
        }

        var eshopOnly = new List<ReconciliationRow>();
        foreach (var payment in payments)
        {
            if (matchedOrderIds.Contains(payment.OrderId))
            {
                continue;
            }

            orderById.TryGetValue(payment.OrderId, out var order);
            if (order is null)
            {
                continue;
            }

            eshopOnly.Add(new ReconciliationRow(
                null, $"ESHOP-{order.Id}", order.Id.ToString(CultureInfo.InvariantCulture),
                order.Status.ToString(), RoundMoney(order.Total()), payment.Currency, order.OrderDate,
                order.Id, order.Status, payment.CaptureId, payment.AuthorizationId,
                "eShop order has no matching PayPal transaction in this range"));
        }

        foreach (var order in orders)
        {
            if (matchedOrderIds.Contains(order.Id) || payments.Any(p => p.OrderId == order.Id))
            {
                continue;
            }

            eshopOnly.Add(new ReconciliationRow(
                null, $"ESHOP-{order.Id}", order.Id.ToString(CultureInfo.InvariantCulture),
                order.Status.ToString(), RoundMoney(order.Total()), _payPal.Currency, order.OrderDate,
                order.Id, order.Status, null, null,
                "eShop order has no matching PayPal transaction in this range"));
        }

        return new ReconciliationReport(from, to, matched, paypalOnly, eshopOnly);
    }

    private async Task<string> EnsureFreshAuthorizationAsync(Order order, OrderPayment payment, CancellationToken cancellationToken)
    {
        var authorizationId = payment.AuthorizationId!;
        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PayPalGatewayException ex) when (ex.StatusCode == 404)
        {
            throw new AuthorizationNotRenewableException(
                "PayPal no longer has this authorization. The hold cannot be renewed. Ask the shopper to pay again, then retry fulfilment.");
        }

        if (string.Equals(details.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(details.CaptureId))
        {
            return authorizationId;
        }

        if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentConflictException($"The PayPal authorization for order {order.Id} was voided and cannot be captured.");
        }

        var created = details.CreateTime ?? payment.AuthorizedAt ?? order.OrderDate;
        var age = DateTimeOffset.UtcNow - created.ToUniversalTime();
        var expired = details.Expiration is not null && details.Expiration <= DateTimeOffset.UtcNow;

        if (age > MaxReauthorizeAge)
        {
            throw new AuthorizationNotRenewableException(
                "This PayPal authorization is older than 29 days and can no longer be renewed. Ask the shopper to pay again, then retry fulfilment.");
        }

        if (expired || age > HonorPeriod)
        {
            _logger.LogInformation($"Renewing stale PayPal authorization for order {order.Id}.");
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(
                    RequestId("reauth", order),
                    authorizationId,
                    payment.AuthorizedAmount ?? RoundMoney(order.Total()),
                    cancellationToken);
                payment.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.Expiration);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                return renewed.AuthorizationId;
            }
            catch (PayPalGatewayException ex) when (IsUnrenewableAuthorization(ex) || age > MaxReauthorizeAge)
            {
                throw new AuthorizationNotRenewableException(
                    "PayPal could not renew this authorization. The hold has expired permanently. Ask the shopper to pay again, then retry fulfilment.");
            }
        }

        return authorizationId;
    }

    private async Task<Order> GetShopperOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderReadRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<OrderPayment> GetPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentReadRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new ResourceNotFoundException($"No payment exists for order {orderId}.");
        }

        return payment;
    }

    private async Task<OrderPayment> GetOrCreatePaymentAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = await _paymentReadRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(order.Id), cancellationToken);
        if (payment is not null)
        {
            return payment;
        }

        payment = new OrderPayment(order.Id, _payPal.Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);
        return payment;
    }

    private static (int? OrderId, string Reason) FindMatch(
        PayPalReportedTransaction txn,
        IReadOnlyList<OrderPayment> payments,
        IReadOnlyDictionary<int, Order> orders)
    {
        foreach (var payment in payments)
        {
            if (IdsEqual(txn.TransactionId, payment.CaptureId) ||
                IdsEqual(txn.TransactionId, payment.AuthorizationId) ||
                IdsEqual(txn.TransactionId, payment.PayPalOrderId) ||
                IdsEqual(txn.ReferenceId, payment.CaptureId) ||
                IdsEqual(txn.ReferenceId, payment.AuthorizationId) ||
                payment.Refunds.Any(r => IdsEqual(txn.TransactionId, r.PayPalRefundId)))
            {
                return (payment.OrderId, "Matched on PayPal payment identifier");
            }
        }

        if (!string.IsNullOrEmpty(txn.InvoiceId) && txn.InvoiceId.StartsWith("ESHOP-", StringComparison.OrdinalIgnoreCase))
        {
            var remainder = txn.InvoiceId["ESHOP-".Length..];
            var separator = remainder.IndexOf('-');
            var idPart = separator >= 0 ? remainder[..separator] : remainder;
            if (int.TryParse(idPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var invoiceOrderId)
                && (orders.ContainsKey(invoiceOrderId) || payments.Any(p => p.OrderId == invoiceOrderId)))
            {
                return (invoiceOrderId, "Matched on invoice id");
            }
        }

        if (!string.IsNullOrEmpty(txn.CustomField)
            && int.TryParse(txn.CustomField, NumberStyles.Integer, CultureInfo.InvariantCulture, out var customOrderId)
            && (orders.ContainsKey(customOrderId) || payments.Any(p => p.OrderId == customOrderId)))
        {
            return (customOrderId, "Matched on custom id");
        }

        return (null, string.Empty);
    }

    private static bool IdsEqual(string? left, string? right) =>
        !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right)
        && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static ReconciliationRow ToRow(PayPalReportedTransaction txn, Order? order, OrderPayment? payment, string reason) =>
        new(txn.TransactionId, txn.InvoiceId, txn.CustomField, txn.Status, txn.Amount, txn.Currency, txn.Time,
            order?.Id, order?.Status, payment?.CaptureId, payment?.AuthorizationId, reason);

    private static bool IsAlreadyVoided(PayPalGatewayException ex) =>
        ex.StatusCode is 422 or 409
        && (Contains(ex, "VOIDED") || Contains(ex, "already voided") || Contains(ex, "AUTHORIZATION_VOIDED"));

    private static bool IsUnrenewableAuthorization(PayPalGatewayException ex) =>
        Contains(ex, "AUTHORIZATION_EXPIRED")
        || Contains(ex, "EXPIRED")
        || Contains(ex, "CANNOT_BE_REAUTHORIZED")
        || Contains(ex, "REAUTHORIZATION_NOT_ALLOWED");

    private static bool Contains(PayPalGatewayException ex, string text) =>
        (!string.IsNullOrEmpty(ex.Message) && ex.Message.Contains(text, StringComparison.OrdinalIgnoreCase))
        || (!string.IsNullOrEmpty(ex.PayPalName) && ex.PayPalName.Contains(text, StringComparison.OrdinalIgnoreCase));

    private OrderPaymentResult ToResult(Order order, OrderPayment payment) =>
        new(order.Id, order.Status, RoundMoney(order.Total()), payment.Currency, ToLineResults(order), ToPaymentState(payment));

    private static IReadOnlyList<OrderLineResult> ToLineResults(Order order) =>
        order.OrderItems.Select(i => new OrderLineResult(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList();

    private static PaymentStateResult ToPaymentState(OrderPayment payment) =>
        new(payment.PayPalOrderId, payment.PayPalOrderStatus, payment.AuthorizationId, payment.AuthorizationStatus,
            payment.AuthorizationExpiration, payment.AuthorizedAmount, payment.CaptureId, payment.CaptureStatus,
            payment.CapturedAmount, payment.PaypalFee, payment.NetAmount,
            payment.Refunds.Select(r => new RefundStateResult(r.PayPalRefundId, r.Status, r.Amount, r.Currency, r.IdempotencyKey)).ToList());

    private static IReadOnlyList<PayPalPurchaseItem> ToPurchaseItems(Order order) =>
        order.OrderItems.Select(i => new PayPalPurchaseItem(i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList();

    private static string InvoiceId(int orderId) => $"ESHOP-{orderId}-{Guid.NewGuid():N}";

    private static string RequestId(string operation, Order order) =>
        $"eshop-{operation}-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}";

    private static decimal RoundMoney(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string SanitizeCustomerId(string buyerId)
    {
        var sanitized = Regex.Replace(buyerId, @"[^A-Za-z0-9_-]", "_");
        return sanitized.Length <= 64 ? sanitized : sanitized[..64];
    }

    private static PayPalCardSource ToPayPalCard(CardPaymentRequest card, Address? billingFallback)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new InvalidPaymentRequestException("Card number and expiry are required.");
        }

        var number = new string(card.Number.Where(char.IsDigit).ToArray());
        if (number.Length is < 13 or > 19)
        {
            throw new InvalidPaymentRequestException("Card number is not a valid PAN.");
        }

        var address = card.BillingAddress;
        var paypalAddress = new PayPalBillingAddress(
            CountryCode: NormalizeCountry(address?.CountryCode ?? billingFallback?.Country),
            AddressLine1: address?.AddressLine1 ?? billingFallback?.Street,
            AddressLine2: address?.AddressLine2,
            AdminArea2: address?.AdminArea2 ?? billingFallback?.City,
            AdminArea1: address?.AdminArea1 ?? billingFallback?.State,
            PostalCode: address?.PostalCode ?? billingFallback?.ZipCode);

        return new PayPalCardSource(number, NormalizeExpiry(card.Expiry), card.SecurityCode, card.Name, paypalAddress);
    }

    private static string NormalizeCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return "US";
        }

        return country.Trim().Length >= 2 ? country.Trim()[..2].ToUpperInvariant() : "US";
    }

    private static string NormalizeExpiry(string expiry)
    {
        var trimmed = expiry.Trim();
        if (Regex.IsMatch(trimmed, @"^\d{4}-\d{2}$"))
        {
            return trimmed;
        }

        var slash = Regex.Match(trimmed, @"^(\d{1,2})\s*/\s*(\d{2}|\d{4})$");
        if (slash.Success)
        {
            var month = int.Parse(slash.Groups[1].Value, CultureInfo.InvariantCulture);
            var yearPart = slash.Groups[2].Value;
            var year = yearPart.Length == 2 ? 2000 + int.Parse(yearPart, CultureInfo.InvariantCulture)
                : int.Parse(yearPart, CultureInfo.InvariantCulture);
            return $"{year:D4}-{month:D2}";
        }

        throw new InvalidPaymentRequestException("Card expiry must be YYYY-MM or MM/YY.");
    }
}
