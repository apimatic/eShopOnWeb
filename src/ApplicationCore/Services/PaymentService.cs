using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private const string CustomIdPrefix = "eshop-order-";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IPayPalClient _payPal;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IRepository<CatalogItem> catalogRepository,
        IPayPalClient payPal,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _catalogRepository = catalogRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address shipToAddress)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items == null || items.Count == 0)
        {
            throw new OrderPaymentException("empty_items", "An order must contain at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new OrderPaymentException("invalid_quantity", "Item quantities must be greater than zero.");
        }

        var catalogItemsSpecification = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _catalogRepository.ListAsync(catalogItemsSpecification);

        foreach (var item in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
            if (catalogItem == null)
            {
                throw new OrderPaymentException("unknown_catalog_item",
                    $"Catalog item {item.CatalogItemId} does not exist.");
            }
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order);
        _logger.LogInformation($"Order {order.Id} placed by {buyerId}, total {order.Total():0.00} {_payPal.Currency}");
        return order;
    }

    public async Task<Payment> PayOrderAsync(Order order, PayPalCardPayment? card, int? savedPaymentMethodId)
    {
        Guard.Against.Null(order, nameof(order));

        if (order.Status == OrderStatus.PaymentAuthorized)
        {
            // Idempotent replay (e.g. double-click): the hold already exists.
            var existing = order.Payment
                ?? await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id));
            if (existing != null && !string.IsNullOrEmpty(existing.AuthorizationId))
            {
                return existing;
            }
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new OperationConflictException(
                $"Order {order.Id} is {order.Status} and cannot be paid. Only orders awaiting payment can be paid.");
        }

        var total = order.Total();
        if (total <= 0)
        {
            throw new OrderPaymentException("empty_order", "Cannot pay for an order with no items.");
        }

        string? vaultId = null;
        int? savedMethodId = null;
        if (savedPaymentMethodId.HasValue)
        {
            if (card != null)
            {
                throw new OrderPaymentException("two_payment_sources",
                    "Provide either a card or a saved payment method id, not both.");
            }
            var method = await _paymentMethodRepository.GetByIdAsync(savedPaymentMethodId.Value);
            if (method == null || method.BuyerId != order.BuyerId)
            {
                throw new EntityNotFoundException(
                    $"Saved payment method {savedPaymentMethodId.Value} was not found for this shopper.");
            }
            vaultId = method.PayPalPaymentTokenId;
            savedMethodId = method.Id;
        }
        else if (card == null)
        {
            throw new OrderPaymentException("missing_payment_source",
                "Provide card details or a savedPaymentMethodId to pay the order.");
        }

        var invoiceId = $"eshop-order-{order.Id}-{Guid.NewGuid().ToString("N").Substring(0, 12)}";
        var requestId = Guid.NewGuid();

        var (payPalOrderId, authorization) = await _payPal.AuthorizeAsync(
            total, invoiceId, $"{CustomIdPrefix}{order.Id}", card, vaultId, requestId);

        // The hold must equal the order total to the cent, in the configured currency.
        if (authorization.Amount == null ||
            !string.Equals(authorization.Amount.CurrencyCode, _payPal.Currency, StringComparison.OrdinalIgnoreCase) ||
            authorization.Amount.Value != decimal.Round(total, 2))
        {
            throw new PayPalApiException(
                $"PayPal held {authorization.Amount?.CurrencyCode} {authorization.Amount?.Formatted} " +
                $"but the order total is {_payPal.Currency} {total:0.00}. Refusing to accept a mismatched hold.");
        }

        var payment = new Payment(order.Id, order.BuyerId, _payPal.Currency, total);
        payment.RecordAuthorization(payPalOrderId, authorization.Id, authorization.Status,
            authorization.ExpirationTime, savedMethodId);
        await _paymentRepository.AddAsync(payment);

        order.AttachPayment(payment);
        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order);

        _logger.LogInformation($"Order {order.Id} authorized: PayPal order {payPalOrderId}, authorization {authorization.Id}, " +
            $"hold {authorization.Amount.Formatted} {authorization.Amount.CurrencyCode}, status {authorization.Status}, " +
            $"expires {authorization.ExpirationTime?.ToString("o")}");
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(Order order)
    {
        Guard.Against.Null(order, nameof(order));

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order.Payment ?? throw new OperationConflictException($"Order {order.Id} has no payment record.");
        }

        if (order.Status != OrderStatus.PaymentAuthorized)
        {
            throw new OperationConflictException(
                $"Order {order.Id} is {order.Status}. It must be payment-authorized before it can be fulfilled.");
        }

        var payment = order.Payment
            ?? await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id))
            ?? throw new OperationConflictException($"Order {order.Id} has no payment record.");

        if (!string.IsNullOrEmpty(payment.CaptureId))
        {
            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order);
            return payment;
        }

        var authorization = await _payPal.GetAuthorizationAsync(payment.AuthorizationId);
        payment.UpdateAuthorizationStatus(authorization.Status);

        PayPalCaptureInfo capture;
        if (IsCapturableStatus(authorization.Status))
        {
            capture = await CaptureAuthorizationAsync(payment, authorization.Id);
        }
        else if (string.Equals(authorization.Status, "CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            throw new OperationConflictException(
                $"Authorization {authorization.Id} for order {order.Id} was already captured on the PayPal side. " +
                $"No new capture was attempted; check order {order.Id} in PayPal before proceeding.");
        }
        else
        {
            // Stale hold (EXPIRED, VOIDED, DENIED, ...): try to renew it.
            _logger.LogInformation($"Authorization {payment.AuthorizationId} for order {order.Id} is {authorization.Status}; attempting renewal.");
            PayPalAuthorizationInfo renewed;
            try
            {
                renewed = await _payPal.ReauthorizeAsync(payment.AuthorizationId, Guid.NewGuid());
            }
            catch (PayPalApiException ex)
            {
                throw new OperationConflictException(
                    $"Order {order.Id} cannot be fulfilled: its PayPal authorization {payment.AuthorizationId} is {authorization.Status} " +
                    $"and could not be renewed (PayPal said: {ex.Message}). " +
                    "Cancel the order and ask the shopper to place a new payment.");
            }
            payment.RecordAuthorization(payment.PayPalOrderId, renewed.Id, renewed.Status,
                renewed.ExpirationTime, payment.SavedPaymentMethodId);
            await _paymentRepository.UpdateAsync(payment);
            capture = await CaptureAuthorizationAsync(payment, renewed.Id);
        }

        payment.RecordCapture(capture.Id, capture.Amount.Value,
            capture.PayPalFee?.Value ?? 0m, capture.NetAmount?.Value ?? capture.Amount.Value);
        await _paymentRepository.UpdateAsync(payment);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order);

        _logger.LogInformation($"Order {order.Id} fulfilled: capture {capture.Id} " +
            $"gross {capture.Amount.Formatted}, fee {(capture.PayPalFee?.Formatted ?? "n/a")}, net {(capture.NetAmount?.Formatted ?? "n/a")}");
        return payment;
    }

    private async Task<PayPalCaptureInfo> CaptureAuthorizationAsync(Payment payment, string authorizationId)
    {
        var captureInvoiceId = $"eshop-order-{payment.OrderId}-{Guid.NewGuid().ToString("N").Substring(0, 12)}-capture";
        try
        {
            return await _payPal.CaptureAsync(authorizationId, payment.AmountAuthorized,
                captureInvoiceId, Guid.NewGuid());
        }
        catch (PayPalApiException ex)
        {
            // A hold that went stale between the status check and the capture: renew and retry once.
            var auth = await _payPal.GetAuthorizationAsync(authorizationId);
            payment.UpdateAuthorizationStatus(auth.Status);
            if (IsCapturableStatus(auth.Status))
            {
                throw;
            }
            var renewed = await _payPal.ReauthorizeAsync(authorizationId, Guid.NewGuid());
            payment.RecordAuthorization(payment.PayPalOrderId, renewed.Id, renewed.Status,
                renewed.ExpirationTime, payment.SavedPaymentMethodId);
            return await _payPal.CaptureAsync(renewed.Id, payment.AmountAuthorized,
                captureInvoiceId, Guid.NewGuid());
        }
    }

    private static bool IsCapturableStatus(string status) =>
        status.Equals("CREATED", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("PENDING", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase);

    public async Task<Payment?> CancelOrderAsync(Order order)
    {
        Guard.Against.Null(order, nameof(order));

        if (order.Status == OrderStatus.Cancelled)
        {
            return order.Payment;
        }

        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new OperationConflictException(
                $"Order {order.Id} is already fulfilled and captured. Use a refund to return the money instead of cancelling.");
        }

        var payment = order.Payment
            ?? await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(order.Id));

        if (payment != null && !string.IsNullOrEmpty(payment.AuthorizationId) &&
            !string.Equals(payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(payment.AuthorizationStatus, "EXPIRED", StringComparison.OrdinalIgnoreCase))
        {
            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, Guid.NewGuid());
            payment.UpdateAuthorizationStatus("VOIDED");
            await _paymentRepository.UpdateAsync(payment);
            _logger.LogInformation($"Order {order.Id} cancelled: authorization {payment.AuthorizationId} voided, held funds released.");
        }
        else
        {
            _logger.LogInformation($"Order {order.Id} cancelled before any authorization existed; nothing to void.");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order);
        return payment;
    }

    public async Task<PaymentRefund> RefundOrderAsync(Order order, decimal? amount, string idempotencyKey)
    {
        Guard.Against.Null(order, nameof(order));
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new OrderPaymentException("missing_idempotency_key", "An idempotencyKey is required to refund an order.");
        }

        var payment = order.Payment
            ?? await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdWithRefundsSpec(order.Id))
            ?? throw new OperationConflictException($"Order {order.Id} has no payment to refund.");

        if (string.IsNullOrEmpty(payment.CaptureId))
        {
            throw new OperationConflictException(
                $"Order {order.Id} has no captured payment. Money can only be refunded after fulfilment captured the payment.");
        }

        // Idempotency in effect: repeating under the same key must not refund twice.
        if (payment.IsRefundKeyUsed(idempotencyKey))
        {
            var existing = payment.Refunds.First(r => r.IdempotencyKey == idempotencyKey);
            _logger.LogInformation($"Refund key {idempotencyKey} for order {order.Id} already applied (refund {existing.PayPalRefundId}); returning existing refund.");
            return existing;
        }

        var remaining = payment.CapturedAmount - payment.RefundedAmount;
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new OrderPaymentException("invalid_refund_amount", "Refund amount must be greater than zero.");
        }
        if (refundAmount > remaining)
        {
            throw new OperationConflictException(
                $"Refund of {refundAmount:0.00} {payment.Currency} exceeds what remains refundable. " +
                $"Captured {payment.CapturedAmount:0.00}, already refunded {payment.RefundedAmount:0.00}; " +
                $"at most {remaining:0.00} {payment.Currency} can still be refunded.");
        }

        // PayPal-Request-Id must be stable for retries of THIS refund but unique across captures.
        var requestId = DeterministicRequestId($"{idempotencyKey}|{payment.CaptureId}|{refundAmount:0.00}");
        var refundInvoiceId = $"eshop-order-{order.Id}-{Guid.NewGuid().ToString("N").Substring(0, 12)}-refund";
        var refund = await _payPal.RefundAsync(payment.CaptureId, refundAmount, refundInvoiceId, requestId);

        var record = payment.AddRefund(idempotencyKey, refund.Amount.Value, refund.Id, refund.Status);
        await _paymentRepository.UpdateAsync(payment);

        _logger.LogInformation($"Order {order.Id} refunded {refund.Amount.Formatted} {refund.Amount.CurrencyCode}: PayPal refund {refund.Id} ({refund.Status}).");
        return record;
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(string buyerId, PayPalCardPayment card)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));
        ValidateCard(card);

        // PayPal customer.id allows max 22 alphanumeric chars; derive a stable id from the buyer.
        var customerId = "eshop-" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(buyerId)))[..16];
        var token = await _payPal.CreatePaymentTokenAsync(card, customerId, Guid.NewGuid());

        var method = new SavedPaymentMethod(buyerId, token.Id, token.CustomerId,
            token.Brand ?? "CARD", token.LastDigits ?? "????", token.Expiry ?? card.Expiry, card.Name);
        await _paymentMethodRepository.AddAsync(method);

        _logger.LogInformation($"Shopper {buyerId} saved payment method {method.Id} (PayPal token {token.Id}, {method.Brand} ending {method.Last4}).");
        return method;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListPaymentMethodsAsync(string buyerId)
    {
        var methods = await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId));
        return methods;
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId)
    {
        var method = await _paymentMethodRepository.GetByIdAsync(paymentMethodId);
        if (method == null || method.BuyerId != buyerId)
        {
            throw new EntityNotFoundException(
                $"Saved payment method {paymentMethodId} was not found for this shopper.");
        }

        await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId);
        await _paymentMethodRepository.DeleteAsync(method);

        _logger.LogInformation($"Shopper {buyerId} deleted payment method {paymentMethodId} (PayPal token {method.PayPalPaymentTokenId}).");
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (from > to)
        {
            throw new OrderPaymentException("invalid_range", "The 'from' date must not be after the 'to' date.");
        }

        var report = new ReconciliationReport { From = from, To = to };

        var transactions = await _payPal.ListTransactionsAsync(from, to);

        var orderIds = new HashSet<int>();
        foreach (var tx in transactions)
        {
            var orderId = ExtractOrderId(tx.CustomId) ?? ExtractOrderId(tx.InvoiceId);
            if (orderId.HasValue) orderIds.Add(orderId.Value);
        }

        var ordersById = new Dictionary<int, Order>();
        foreach (var orderId in orderIds)
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order != null) ordersById[orderId] = order;
        }

        foreach (var tx in transactions)
        {
            var row = new ReconciliationRow
            {
                TransactionId = tx.TransactionId,
                TransactionType = tx.TransactionType,
                TransactionStatus = tx.TransactionStatus,
                TransactionDate = tx.TransactionInitiationDate,
                Amount = tx.Amount,
                Fee = tx.FeeAmount,
                Net = tx.NetAmount,
                InvoiceId = tx.InvoiceId,
                CustomId = tx.CustomId,
            };
            var orderId = ExtractOrderId(tx.CustomId) ?? ExtractOrderId(tx.InvoiceId);
            if (orderId.HasValue && ordersById.TryGetValue(orderId.Value, out var order))
            {
                row.OrderId = orderId;
                row.OrderStatus = order.Status.ToString();
                row.MatchState = ReconciliationMatchState.Matched;
            }
            else
            {
                row.MatchState = ReconciliationMatchState.PayPalOnly;
            }
            report.Rows.Add(row);
        }

        // Orders created inside the range that PayPal has no transaction for.
        var ordersInRange = await _orderRepository.ListAsync(new OrdersInRangeSpec(from, to));
        foreach (var order in ordersInRange)
        {
            if (report.Rows.Any(r => r.OrderId == order.Id)) continue;
            report.OrdersWithoutPayPalTransaction.Add(new ReconciliationOrderRow
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Currency = _payPal.Currency,
                OrderDate = order.OrderDate,
                PaymentStatus = order.Payment?.AuthorizationStatus
                    ?? (string.IsNullOrEmpty(order.Payment?.CaptureId) ? null : "CAPTURED"),
            });
        }

        return report;
    }

    private static int? ExtractOrderId(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(CustomIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }
        var rest = value.Substring(CustomIdPrefix.Length);
        var dash = rest.IndexOf('-');
        var digits = dash < 0 ? rest : rest.Substring(0, dash);
        return int.TryParse(digits, out var id) ? id : null;
    }

    private static Guid DeterministicRequestId(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(BitConverter.ToInt32(hash, 0), BitConverter.ToInt16(hash, 4),
            BitConverter.ToInt16(hash, 6), hash[8], hash[9], hash[10], hash[11], hash[12],
            hash[13], hash[14], hash[15]);
    }

    private static void ValidateCard(PayPalCardPayment card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || card.Number.Trim().Length < 12 ||
            !card.Number.Trim().All(char.IsDigit))
        {
            throw new OrderPaymentException("invalid_card", "Card number is required and must be 12-19 digits.");
        }
        if (string.IsNullOrWhiteSpace(card.Expiry) ||
            !System.Text.RegularExpressions.Regex.IsMatch(card.Expiry.Trim(), @"^\d{4}-(0[1-9]|1[0-2])$"))
        {
            throw new OrderPaymentException("invalid_card", "Card expiry must be in yyyy-MM format.");
        }
        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new OrderPaymentException("invalid_card", "Cardholder name is required.");
        }
        if (card.BillingAddress == null || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
        {
            throw new OrderPaymentException("invalid_card", "Card billing address with a country code is required.");
        }
    }
}
