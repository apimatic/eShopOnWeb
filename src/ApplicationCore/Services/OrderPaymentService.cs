using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalClient _payPal;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IUriComposer uriComposer,
        IPayPalClient payPal,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
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

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one item.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentValidationException("Every item quantity must be greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation("Placed order {0} for {1} awaiting payment.", order.Id, buyerId);
        return order;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        // Idempotent in effect: if the hold already exists, return it rather than authorizing again.
        if (payment is not null && payment.AuthorizationId is not null)
        {
            return payment;
        }

        if (order.Status is not (OrderStatus.AwaitingPayment or OrderStatus.Authorized))
        {
            throw new PaymentValidationException($"Order {orderId} cannot be paid from state '{order.Status}'.");
        }

        var amount = RoundMoney(order.Total());
        if (amount <= 0m)
        {
            throw new PaymentValidationException("Order total must be greater than zero.");
        }

        // Resolve the instrument and derive a stable idempotency key. A double-click with the same
        // instrument yields the same key (PayPal dedupes the hold); a retry with a different card
        // yields a different key, so a declined attempt can be retried with another card.
        CardDetails? card = instruction.Card;
        string? vaultId = null;
        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var saved = await _savedCardRepository.GetByIdAsync(savedId, cancellationToken);
            if (saved is null || saved.BuyerId != buyerId)
            {
                throw new NotFoundException($"Saved card {savedId} was not found.");
            }
            vaultId = saved.PayPalVaultId;
        }
        else if (card is null)
        {
            throw new PaymentValidationException("Provide either card details or a saved card id to pay.");
        }

        var fingerprint = InstrumentFingerprint(vaultId, card);
        var invoiceId = InvoiceIdFor(order);
        var authorizeRequestId = $"auth-{order.PaymentReference:N}-{fingerprint}";
        var captureRequestId = $"capture-{order.PaymentReference:N}";

        if (payment is null)
        {
            payment = new Payment(orderId, amount, Currency, authorizeRequestId, captureRequestId, invoiceId);
            payment = await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            payment.SetAuthorizeRequestId(authorizeRequestId);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        AuthorizationResult result = vaultId is not null
            ? await _payPal.AuthorizeWithVaultedCardAsync(amount, Currency, vaultId, invoiceId, authorizeRequestId, cancellationToken)
            : await _payPal.AuthorizeWithCardAsync(amount, Currency, card!, invoiceId, authorizeRequestId, cancellationToken);

        payment.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Authorized {0} {1} on order {2} (auth {3}).", amount, Currency, orderId, result.AuthorizationId);
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new PaymentValidationException($"Order {orderId} has no payment to capture.");

        // Idempotent: fulfilling an already-captured order returns the existing capture.
        if (payment.CaptureId is not null)
        {
            return payment;
        }

        if (payment.AuthorizationId is null || payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentValidationException($"Order {orderId} is not authorized and cannot be fulfilled.");
        }

        var authorizationId = payment.AuthorizationId;

        // An authorization that has gone stale before fulfilment is renewed rather than failing outright.
        var state = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
        if (IsStale(state.Status))
        {
            authorizationId = await RenewAuthorizationAsync(payment, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(authorizationId, payment.InvoiceId, payment.CaptureRequestId, cancellationToken);
        }
        catch (PayPalApiException ex) when (IsExpiredIssue(ex))
        {
            // Detected staleness only at capture time — renew once, then capture the fresh authorization.
            _logger.LogWarning("Capture of {0} failed as stale ({1}); renewing authorization.", authorizationId, ex.Issue);
            var renewed = await RenewAuthorizationAsync(payment, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(renewed, payment.InvoiceId, payment.CaptureRequestId, cancellationToken);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Fulfilled order {0}: captured {1} {2}, fee {3}, net {4}.",
            orderId, capture.GrossAmount, capture.Currency, capture.PayPalFee, capture.NetAmount);
        return payment;
    }

    /// <summary>Renews (reauthorizes) a stale hold, updating the payment. Throws an actionable error if it can no longer be renewed.</summary>
    private async Task<string> RenewAuthorizationAsync(Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.Currency, cancellationToken);
            payment.ReplaceAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation("Renewed authorization for order {0}: new auth {1}.", payment.OrderId, reauth.AuthorizationId);
            return reauth.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentOperationException(
                $"The authorization on order {payment.OrderId} has expired and can no longer be renewed " +
                $"({ex.Issue ?? "reauthorization not allowed"}). Ask the shopper to place and pay for a new order.",
                ex.Issue);
        }
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return payment!; // idempotent
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Refunded or OrderStatus.PartiallyRefunded)
        {
            throw new PaymentValidationException($"Order {orderId} was already fulfilled; refund it instead of cancelling.");
        }

        // Release any hold so no money ever moves.
        if (payment is not null && payment.AuthorizationId is not null && payment.Status == PaymentStatus.Authorized)
        {
            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            payment.RecordVoid();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Cancelled order {0}; any hold released.", orderId);
        return payment!;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("A refund idempotency key is required.");
        }

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken)
            ?? throw new PaymentValidationException($"Order {orderId} has no payment to refund.");

        if (payment.CaptureId is null)
        {
            throw new PaymentValidationException($"Order {orderId} has not been fulfilled, so there is nothing to refund.");
        }

        // Idempotent: the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refundAmount = amount.HasValue ? RoundMoney(amount.Value) : payment.RefundableRemaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        }

        // A partly-refunded order is never refundable beyond what was captured.
        if (refundAmount > payment.RefundableRemaining)
        {
            throw new PaymentValidationException(
                $"Refund of {refundAmount:0.00} exceeds the {payment.RefundableRemaining:0.00} {payment.Currency} still refundable on order {orderId}.");
        }

        var requestId = $"refund-{order.PaymentReference:N}-{idempotencyKey}";
        var result = await _payPal.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency, payment.InvoiceId, requestId, cancellationToken);

        var refund = new PaymentRefund(idempotencyKey, result.RefundId, result.Amount, result.Currency, result.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkRefunded(full: payment.RefundableRemaining <= 0m);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Refunded {0} {1} on order {2} (refund {3}).", refund.Amount, refund.Currency, orderId, refund.PayPalRefundId);
        return refund;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderWithPayment>();
        }

        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = await _paymentRepository.ListAsync(new PaymentsByOrderIdsSpecification(orderIds), cancellationToken);
        var byOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderWithPayment(o, byOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    public async Task<OrderWithPayment?> GetOrderForBuyerAsync(string buyerId, int orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return null;
        }
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        return new OrderWithPayment(order, payment);
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");
        if (order.BuyerId != buyerId)
        {
            // Do not reveal another shopper's order — treat as not found.
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    // A stable, unique reference recorded on the PayPal order and capture so reconciliation can line them up.
    private static string InvoiceIdFor(Order order) => $"ESHOP-{order.Id}-{order.PaymentReference:N}";

    private static decimal RoundMoney(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsStale(string? status) =>
        string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpiredIssue(PayPalApiException ex) =>
        ex.Issue is not null && (ex.Issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
                                 || ex.Issue.Contains("AUTHORIZATION_", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// One-way fingerprint of the payment instrument, computed transiently in memory and never stored or logged.
    /// It only namespaces the idempotency key so identical double-clicks dedupe while distinct instruments do not.
    /// </summary>
    private static string InstrumentFingerprint(string? vaultId, CardDetails? card)
    {
        var material = vaultId ?? (card is null ? Guid.NewGuid().ToString("N") : $"{card.Number}|{card.Expiry}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
    }
}
