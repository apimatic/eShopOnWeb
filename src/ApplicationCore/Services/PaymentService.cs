using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalClient _payPal;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    // Serializes money operations per order so a genuine double-click cannot race into a second
    // authorization or capture. Within a single host this is sufficient; PayPal-Request-Id gives
    // the second line of defence on the PayPal side.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IUriComposer uriComposer,
        IPayPalClient payPal,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidPaymentOperationException("An order must contain at least one line item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new InvalidPaymentOperationException("Every order line must have a quantity greater than zero.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentEntityNotFoundException($"Catalog item {line.CatalogItemId} was not found.");
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            // Amounts come from catalog prices.
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, order.Total(), Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Placed order {0} for buyer {1} awaiting payment of {2} {3}.", order.Id, buyerId, order.Total(), Currency);
        return order.Id;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request, nameof(request));
        ValidatePayRequest(request);

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var payment = await LoadPaymentForBuyerAsync(orderId, buyerId, cancellationToken);

            // Idempotent in effect: if the hold (or more) already exists, do not authorize again.
            if (payment.IsAuthorized || payment.Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return payment;
            }
            if (payment.Status == PaymentStatus.Cancelled)
            {
                throw new InvalidPaymentOperationException($"Order {orderId} was cancelled and can no longer be paid.");
            }

            PayPalAuthorizationResult result;
            string cardDescriptor;
            int? savedCardId = null;

            if (request.SavedPaymentMethodId is int savedId)
            {
                var savedCard = await _savedCardRepository.GetByIdAsync(savedId, cancellationToken)
                    ?? throw new PaymentEntityNotFoundException($"Saved card {savedId} was not found.");
                if (savedCard.BuyerId != buyerId)
                {
                    throw new ForbiddenPaymentException("The saved card does not belong to the caller.");
                }
                savedCardId = savedCard.Id;
                cardDescriptor = savedCard.Descriptor;
                result = await _payPal.AuthorizeWithVaultedCardAsync(
                    payment.Amount, payment.InvoiceId, orderId.ToString(), savedCard.VaultId, payment.AuthorizationRequestId, cancellationToken);
            }
            else
            {
                result = await _payPal.AuthorizeWithCardAsync(
                    payment.Amount, payment.InvoiceId, orderId.ToString(), request.Card!, payment.AuthorizationRequestId, cancellationToken);
                cardDescriptor = DescribeCard(result.CardBrand, result.CardLast4);
            }

            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, cardDescriptor, savedCardId);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation("Authorized order {0}: PayPal order {1}, authorization {2}.", orderId, result.PayPalOrderId, result.AuthorizationId);
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var payment = await LoadPaymentAsync(orderId, cancellationToken);

            if (payment.IsCaptured)
            {
                return payment; // idempotent: money already taken
            }
            if (!payment.IsAuthorized)
            {
                throw new InvalidPaymentOperationException($"Order {orderId} has no authorized payment to fulfil.");
            }

            var capture = await CaptureWithRenewalAsync(payment, cancellationToken);
            payment.MarkFulfilled(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation("Fulfilled order {0}: captured {1} {2}, fee {3}, net {4}.",
                orderId, capture.GrossAmount, capture.CurrencyCode, capture.PayPalFee, capture.NetAmount);
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Captures, renewing a stale authorization rather than failing the fulfilment outright.</summary>
    private async Task<PayPalCaptureResult> CaptureWithRenewalAsync(Payment payment, CancellationToken cancellationToken)
    {
        var authId = payment.AuthorizationId!;

        // Proactively renew if PayPal already reports the hold as expired.
        try
        {
            var status = await _payPal.GetAuthorizationStatusAsync(authId, cancellationToken);
            if (IsExpiredStatus(status))
            {
                authId = await RenewAuthorizationAsync(payment, authId, cancellationToken);
            }
            else if (IsDeadStatus(status))
            {
                throw new InvalidPaymentOperationException(
                    $"The authorization for order {payment.OrderId} is {status} and cannot be captured. A new payment is required.");
            }
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Could not read authorization status for order {0}: {1}. Proceeding to capture.", payment.OrderId, ex.Message);
        }

        try
        {
            return await _payPal.CaptureAuthorizationAsync(authId, payment.Amount, payment.CaptureRequestId, cancellationToken);
        }
        catch (PayPalApiException ex) when (IsExpiredError(ex))
        {
            // The hold went stale between the status check and the capture: renew and retry once.
            authId = await RenewAuthorizationAsync(payment, authId, cancellationToken);
            return await _payPal.CaptureAuthorizationAsync(authId, payment.Amount, payment.CaptureRequestId, cancellationToken);
        }
    }

    private async Task<string> RenewAuthorizationAsync(Payment payment, string authId, CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(authId, payment.Amount, cancellationToken);
            payment.RenewAuthorization(reauth.AuthorizationId);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation("Renewed stale authorization for order {0}: new authorization {1}.", payment.OrderId, reauth.AuthorizationId);
            return reauth.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            var message = $"The payment hold for order {payment.OrderId} has expired and could not be renewed " +
                          $"({ex.Message}). Ask the shopper to pay again to fulfil this order.";
            payment.SetOperatorMessage(message);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new AuthorizationNotRenewableException(message);
        }
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var payment = await LoadPaymentAsync(orderId, cancellationToken);

            if (payment.Status == PaymentStatus.Cancelled)
            {
                return payment; // idempotent
            }
            if (payment.IsCaptured)
            {
                throw new InvalidPaymentOperationException(
                    $"Order {orderId} has already been fulfilled; use a refund to return the money.");
            }

            if (payment.IsAuthorized)
            {
                try
                {
                    await _payPal.VoidAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
                }
                catch (PayPalApiException ex) when (IsAlreadyVoidedError(ex))
                {
                    _logger.LogWarning("Authorization for order {0} was already voided at PayPal.", orderId);
                }
            }

            payment.MarkCancelled();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation("Cancelled order {0}; any held funds were released.", orderId);
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Refund> RefundAsync(string buyerId, int orderId, string idempotencyKey, decimal? amount, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        if (amount is <= 0m)
        {
            throw new InvalidPaymentOperationException("Refund amount must be greater than zero.");
        }

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var payment = await LoadPaymentForBuyerAsync(orderId, buyerId, cancellationToken);

            if (!payment.IsCaptured)
            {
                throw new InvalidPaymentOperationException($"Order {orderId} has not been fulfilled, so there is nothing to refund.");
            }

            Refund refund;
            try
            {
                refund = payment.AddRefund(idempotencyKey, amount);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidPaymentOperationException(ex.Message);
            }

            // A repeat under the same key that already completed must not refund twice.
            if (refund.Status == RefundStatus.Completed)
            {
                return refund;
            }

            // Persist the pending refund so its id is stable across retries.
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            try
            {
                var result = await _payPal.RefundCaptureAsync(
                    payment.CaptureId!, amount, payment.InvoiceId, orderId.ToString(),
                    DeterministicRequestId(idempotencyKey), cancellationToken);
                payment.ApplyRefundResult(refund, result.RefundId);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                _logger.LogInformation("Refunded {0} {1} on order {2}: PayPal refund {3}.", refund.Amount, payment.CurrencyCode, orderId, result.RefundId);
                return refund;
            }
            catch (PayPalApiException)
            {
                refund.MarkFailed();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        var views = new List<OrderPaymentView>();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            paymentsByOrder.TryGetValue(order.Id, out var payment);
            views.Add(BuildOrderView(order, payment));
        }
        return views;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidPaymentOperationException("'to' must not be earlier than 'from'.");
        }

        var transactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsInDateRangeSpecification(from, to), cancellationToken);

        var byInvoice = payments
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => g.First());

        // Every PayPal id we know about -> the eShop order that owns it.
        var byPayPalId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in payments)
        {
            AddId(byPayPalId, p.PayPalOrderId, p.OrderId);
            AddId(byPayPalId, p.AuthorizationId, p.OrderId);
            AddId(byPayPalId, p.CaptureId, p.OrderId);
            foreach (var r in p.Refunds)
            {
                AddId(byPayPalId, r.PayPalRefundId, p.OrderId);
            }
        }

        var matchedOrderIds = new HashSet<int>();
        var entries = new List<ReconciliationEntry>(transactions.Count);
        foreach (var t in transactions)
        {
            int? matchedOrderId = null;

            // Match on the globally-unique invoice id first, then on any PayPal id we recorded.
            // (custom_field carries the order id for readability but is not globally unique, so it
            // is surfaced in the report rather than used as a match key.)
            if (!string.IsNullOrEmpty(t.InvoiceId) && byInvoice.TryGetValue(t.InvoiceId!, out var byInv))
            {
                matchedOrderId = byInv.OrderId;
            }
            else if (byPayPalId.TryGetValue(t.TransactionId, out var byId))
            {
                matchedOrderId = byId;
            }

            if (matchedOrderId is int m)
            {
                matchedOrderIds.Add(m);
            }

            entries.Add(new ReconciliationEntry(
                t.TransactionId, t.Status, t.Amount, t.CurrencyCode, t.FeeAmount,
                t.InvoiceId, t.CustomField, t.EventCode, t.Date, matchedOrderId));
        }

        // eShop payments that moved money (or hold) yet PayPal did not report in this range.
        var inEshopNotInPayPal = payments
            .Where(p => p.IsAuthorized && !matchedOrderIds.Contains(p.OrderId))
            .Select(p => new EshopUnmatchedPayment(
                p.OrderId, p.InvoiceId, p.Status.ToString(), p.AuthorizationId, p.CaptureId, p.Amount, p.CurrencyCode))
            .ToList();

        return new ReconciliationReport(
            from, to,
            PayPalTransactionCount: transactions.Count,
            EshopPaymentCount: payments.Count,
            MatchedCount: matchedOrderIds.Count,
            Transactions: entries,
            InEshopNotInPayPal: inEshopNotInPayPal);
    }

    // --- helpers ---

    private static void AddId(IDictionary<string, int> map, string? id, int orderId)
    {
        if (!string.IsNullOrEmpty(id))
        {
            map[id!] = orderId;
        }
    }

    private static void ValidatePayRequest(PayOrderRequest request)
    {
        var hasCard = request.Card is not null;
        var hasSaved = request.SavedPaymentMethodId is not null;
        if (hasCard == hasSaved)
        {
            throw new InvalidPaymentOperationException("Provide either one-off card details or a saved card id, but not both.");
        }
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new PaymentEntityNotFoundException($"No payment was found for order {orderId}.");
    }

    private async Task<Payment> LoadPaymentForBuyerAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment.BuyerId != buyerId)
        {
            // Do not reveal existence of another shopper's order.
            throw new PaymentEntityNotFoundException($"No payment was found for order {orderId}.");
        }
        return payment;
    }

    private OrderPaymentView BuildOrderView(Order order, Payment? payment)
    {
        var lines = order.OrderItems
            .Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();

        var refunds = payment?.Refunds
            .Select(r => new RefundView(r.Id, r.PayPalRefundId, r.Amount, r.Status.ToString(), r.CreatedDate))
            .ToList() ?? new List<RefundView>();

        return new OrderPaymentView(
            order.Id,
            order.OrderDate,
            order.Total(),
            payment?.CurrencyCode ?? Currency,
            (payment?.Status ?? PaymentStatus.AwaitingPayment).ToString(),
            payment?.PayPalOrderId,
            payment?.AuthorizationId,
            payment?.CaptureId,
            payment?.CapturedGross,
            payment?.PayPalFee,
            payment?.NetAmount,
            payment?.CardDescriptor,
            payment?.OperatorMessage,
            lines,
            refunds);
    }

    private static string DescribeCard(string? brand, string? last4)
    {
        var b = string.IsNullOrWhiteSpace(brand) ? "CARD" : brand!;
        var l = string.IsNullOrWhiteSpace(last4) ? "????" : last4!;
        return $"{b} ****{l}";
    }

    /// <summary>Turns a caller idempotency key into a stable GUID for the PayPal-Request-Id header.</summary>
    private static string DeterministicRequestId(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16).ToArray()).ToString("N");
    }

    private static bool IsExpiredStatus(string status) =>
        status.Equals("EXPIRED", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeadStatus(string status) =>
        status.Equals("VOIDED", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("DENIED", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpiredError(PayPalApiException ex) =>
        (ex.Name?.IndexOf("EXPIRED", StringComparison.OrdinalIgnoreCase) >= 0) ||
        (ex.Message?.IndexOf("EXPIRED", StringComparison.OrdinalIgnoreCase) >= 0) ||
        (ex.Message?.IndexOf("AUTH_CAPTURE", StringComparison.OrdinalIgnoreCase) >= 0);

    private static bool IsAlreadyVoidedError(PayPalApiException ex) =>
        (ex.Name?.IndexOf("VOID", StringComparison.OrdinalIgnoreCase) >= 0) ||
        (ex.Message?.IndexOf("PREVIOUSLY_VOIDED", StringComparison.OrdinalIgnoreCase) >= 0) ||
        (ex.Message?.IndexOf("ORDER_ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase) >= 0) ||
        (ex.Message?.IndexOf("AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase) >= 0);
}
