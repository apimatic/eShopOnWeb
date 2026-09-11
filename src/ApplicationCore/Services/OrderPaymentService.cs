using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the payment lifecycle of an order against the <see cref="IPaymentGateway"/> and the
/// order/payment repositories. Reuses the existing <see cref="Order"/>/<see cref="OrderItem"/>
/// model; all PayPal-owned state lives on the separate <see cref="Payment"/> aggregate.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    // No shipping address is collected over the API, so — like the existing Web checkout — a
    // placeholder is used. Payment, not shipping, is what this feature is about.
    private static readonly Func<Address> DefaultAddress =
        () => new Address("123 Main St.", "Kent", "OH", "United States", "44240");

    // A per-process id mixed into idempotency keys. The in-memory database resets entity ids to 1
    // on every restart, so keys built only from those ids would collide across runs and cause PayPal
    // to replay a previous run's cached response for the same PayPal-Request-Id. Mixing in a fresh
    // run id keeps keys unique across runs while staying stable within a run (so a double-click is
    // still idempotent).
    private static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 12);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly ICurrencyProvider _currencyProvider;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<PaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        ICurrencyProvider currencyProvider,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _currencyProvider = currencyProvider;
        _logger = logger;
    }

    private string Currency => _currencyProvider.CurrencyCode;

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one item.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (!catalogById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                catalogItem.PictureUri ?? "eCatalog-item-default.png");
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultAddress(), items);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, order.Total(), Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {order.Id} placed by {buyerId} for {order.Total():0.00} {Currency}.");
        return order;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment.Status == PaymentStatus.Authorized)
        {
            return payment;
        }

        if (payment.Status != PaymentStatus.AwaitingPayment)
        {
            throw new PaymentException(
                $"Order {orderId} cannot be paid because its payment status is {payment.Status}.");
        }

        var amount = new Money(payment.CurrencyCode, payment.Amount);
        if (string.IsNullOrEmpty(payment.InvoiceId))
        {
            // Deterministic per (run, order): lets PayPal's transaction records line up to this
            // order, stays unique across in-memory restarts, and is stable so concurrent double-clicks
            // compute the same value. Well under PayPal's 127-char limit.
            payment.AssignInvoiceId($"ESHOP-{RunId}-{orderId}");
        }
        var invoiceId = payment.InvoiceId!;
        var customId = orderId.ToString();
        var idempotencyKey = $"authorize-{invoiceId}";

        AuthorizeResult result;
        if (instruction.Card is not null)
        {
            result = await _paymentGateway.AuthorizeWithCardAsync(amount, instruction.Card, invoiceId,
                customId, idempotencyKey, cancellationToken);
        }
        else if (instruction.SavedPaymentMethodId is int methodId)
        {
            var method = await _paymentMethodRepository.GetByIdAsync(methodId, cancellationToken);
            if (method is null || method.BuyerId != buyerId)
            {
                throw new NotFoundException($"Saved card {methodId} was not found.");
            }

            result = await _paymentGateway.AuthorizeWithVaultTokenAsync(amount, method.VaultTokenId, invoiceId,
                customId, idempotencyKey, cancellationToken);
        }
        else
        {
            throw new ArgumentException("A payment must supply either card details or a saved card id.");
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus,
            result.ExpiresAt, result.CardBrand, result.CardLast4);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} authorized (PayPal auth {result.AuthorizationId}).");
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Captured
            || payment.Status == PaymentStatus.PartiallyRefunded
            || payment.Status == PaymentStatus.Refunded)
        {
            // Already fulfilled — idempotent no-op.
            return payment;
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new PaymentException(
                $"Order {orderId} cannot be fulfilled because its payment status is {payment.Status}.");
        }

        var amount = new Money(payment.CurrencyCode, payment.Amount);

        // If the hold has gone stale, renew it before capturing rather than failing outright.
        await EnsureAuthorizationFreshAsync(payment, amount, cancellationToken);

        CaptureResult capture;
        try
        {
            // The authorization id is PayPal-owned and unique per run, so it makes the capture
            // idempotency key both stable (safe to retry) and unique across runs.
            capture = await _paymentGateway.CaptureAsync(payment.AuthorizationId!, amount,
                $"capture-{payment.AuthorizationId}", cancellationToken);
        }
        catch (AuthorizationExpiredException)
        {
            // The authorization expired between our freshness check and the capture: renew and retry once.
            await ReauthorizeOrFailAsync(payment, amount, cancellationToken);
            capture = await _paymentGateway.CaptureAsync(payment.AuthorizationId!, amount,
                $"capture-{payment.AuthorizationId}", cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.Gross.Amount,
            capture.Fee?.Amount, capture.Net?.Amount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation(
            $"Order {orderId} fulfilled: captured {capture.Gross.Amount:0.00} {capture.Gross.CurrencyCode} " +
            $"(fee {capture.Fee?.Amount:0.00}, net {capture.Net?.Amount:0.00}).");
        return payment;
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment; // idempotent no-op
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new PaymentException(
                $"Order {orderId} cannot be cancelled because its payment status is {payment.Status}. " +
                "Only an authorized order that has not been fulfilled can be cancelled.");
        }

        await _paymentGateway.VoidAuthorizationAsync(payment.AuthorizationId!, cancellationToken);

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} cancelled; authorization {payment.AuthorizationId} voided.");
        return payment;
    }

    public async Task<(Refund Refund, Payment Payment)> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, cancellationToken);

        // Idempotent by caller key: repeating the same key returns the same refund, never a second one.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return (existing, payment);
        }

        if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException(
                $"Order {orderId} has not been fulfilled, so there is nothing to refund (status {payment.Status}).");
        }

        if (string.IsNullOrEmpty(payment.CaptureId))
        {
            throw new PaymentException($"Order {orderId} has no capture to refund.");
        }

        var refundAmount = amount ?? payment.RefundableRemaining;

        // Domain enforces that refunds never exceed what was captured.
        Refund refund;
        try
        {
            refund = payment.AddRefund(idempotencyKey, refundAmount);
        }
        catch (InvalidOperationException ex)
        {
            throw new PaymentException(ex.Message);
        }

        // The caller's key dedupes within our own data (above). For PayPal's PayPal-Request-Id we
        // namespace it by the PayPal-owned capture id so it is unique across runs yet stable per
        // (capture, caller key) — two distinct partial refunds use two distinct caller keys.
        var payPalRequestId = $"refund-{payment.CaptureId}-{idempotencyKey}";
        var result = await _paymentGateway.RefundAsync(payment.CaptureId!, new Money(payment.CurrencyCode, refundAmount),
            payment.InvoiceId ?? $"ESHOP-{orderId}", payPalRequestId, cancellationToken);

        refund.MarkCompleted(result.RefundId, result.Status);
        payment.ApplyRefundOutcome();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} refunded {refundAmount:0.00} {payment.CurrencyCode} " +
            $"(PayPal refund {result.RefundId}).");
        return (refund, payment);
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderWithPayment(o, paymentByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    // --- helpers ------------------------------------------------------------------------------

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private async Task<Payment> LoadOwnedPaymentAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment.BuyerId != buyerId)
        {
            // Report as not-found so a shopper cannot probe another shopper's orders.
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private async Task EnsureAuthorizationFreshAsync(Payment payment, Money amount, CancellationToken cancellationToken)
    {
        // Renew slightly ahead of the real expiry to avoid a race against capture.
        var buffer = TimeSpan.FromMinutes(5);
        if (payment.AuthorizationExpiresAt.HasValue &&
            payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow + buffer)
        {
            await ReauthorizeOrFailAsync(payment, amount, cancellationToken);
        }
    }

    private async Task ReauthorizeOrFailAsync(Payment payment, Money amount, CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _paymentGateway.ReauthorizeAsync(payment.AuthorizationId!, amount,
                $"reauth-{payment.Id}-{DateTimeOffset.UtcNow.Ticks}", cancellationToken);
            payment.UpdateAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Order {payment.OrderId} authorization renewed as {reauth.AuthorizationId}.");
        }
        catch (Exception ex) when (ex is not PaymentException)
        {
            throw new PaymentException(
                $"The payment hold for order {payment.OrderId} has expired and could not be renewed " +
                $"({ex.Message}). Ask the shopper to pay for this order again before fulfilling it.", ex);
        }
    }
}
