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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentCurrencyProvider _currency;
    private readonly IAppLogger<PaymentService> _logger;

    // Placeholder shipping address: the API order flow carries no address, and payment does not need one.
    private static readonly Func<Address> DefaultShipTo = () =>
        new Address("Digital order - no shipping", "N/A", "N/A", "US", "00000");

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalPaymentGateway gateway,
        IUriComposer uriComposer,
        IPaymentCurrencyProvider currency,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _currency = currency;
        _logger = logger;
    }

    private string CurrencyCode => _currency.CurrencyCode;

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines == null || lines.Count == 0)
        {
            throw new PaymentConflictException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentConflictException("Every order line must have a quantity of at least 1.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentNotFoundException($"Catalog item {line.CatalogItemId} was not found.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShipTo(), orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var payment = new Payment(order.Id, buyerId, CurrencyCode, order.Total());
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation($"Placed order {order.Id} for buyer {buyerId}, total {order.Total()} {CurrencyCode}.");
        return order.Id;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);
        EnsureOwner(payment, buyerId);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId != null)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.PendingPayment)
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be paid because its payment is '{payment.Status}'.");
        }

        var (instrument, savedCardId) = await ResolveInstrumentAsync(buyerId, instruction, ct);

        var command = new AuthorizeCommand(
            new Money(CurrencyCode, payment.Amount),
            InvoiceId: payment.Reference,   // globally unique; the account requires a unique invoice id
            CustomId: orderId.ToString(),   // ties the PayPal transaction back to the eShop order for reconciliation
            Description: $"eShopOnWeb order {orderId}",
            Instrument: instrument,
            IdempotencyKey: $"auth-{payment.Reference}");

        var result = await _gateway.AuthorizeAsync(command, ct);
        payment.SetAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt, savedCardId);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation(
            $"Authorized order {orderId}: PayPal order {result.PayPalOrderId}, authorization {result.AuthorizationId} ({result.Status}).");
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Fulfilled && payment.CaptureId != null)
        {
            return payment; // already captured; idempotent
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId == null)
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be fulfilled because its payment is '{payment.Status}'. " +
                "Only an authorized order can be fulfilled.");
        }

        var amount = new Money(CurrencyCode, payment.Amount);

        // A hold that has gone stale must be renewed rather than failing fulfilment outright.
        if (IsAuthorizationStale(payment))
        {
            await RenewAuthorizationAsync(payment, amount, orderId, ct);
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, amount, $"capture-{payment.Reference}", ct);
        }
        catch (PayPalApiException ex) when (LooksLikeStaleAuthorization(ex))
        {
            _logger.LogWarning($"Capture of order {orderId} failed as stale ({ex.PayPalErrorName}); renewing the authorization.");
            await RenewAuthorizationAsync(payment, amount, orderId, ct);
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, amount, $"capture-{payment.Reference}-renewed", ct);
        }

        payment.SetCaptured(capture.CaptureId, capture.Status, capture.GrossAmount.Value,
            capture.PayPalFee?.Value, capture.NetAmount?.Value);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation(
            $"Fulfilled order {orderId}: captured {capture.GrossAmount.Value} {capture.GrossAmount.CurrencyCode}, " +
            $"fee {capture.PayPalFee?.Value}, net {capture.NetAmount?.Value} (capture {capture.CaptureId}).");
        return payment;
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment; // idempotent
        }
        if (payment.Status == PaymentStatus.PendingPayment)
        {
            throw new PaymentConflictException(
                $"Order {orderId} has no held funds to release (it was never paid).");
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId == null)
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be cancelled because its payment is '{payment.Status}'. " +
                "Cancel is only valid before fulfilment; use a refund afterwards.");
        }

        await _gateway.VoidAsync(payment.AuthorizationId, ct);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Cancelled order {orderId}: released the hold on authorization {payment.AuthorizationId}.");
        return payment;
    }

    public async Task<RefundOutcome> RefundAsync(int orderId, string requesterBuyerId, bool isAdmin, decimal? amount,
        string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadPaymentAsync(orderId, ct);
        if (!isAdmin)
        {
            EnsureOwner(payment, requesterBuyerId);
        }

        // Repeating a request under the same key must not refund twice.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing != null)
        {
            return new RefundOutcome(payment, existing);
        }

        if (payment.CaptureId == null ||
            (payment.Status != PaymentStatus.Fulfilled &&
             payment.Status != PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be refunded because its payment is '{payment.Status}'. " +
                "Only a fulfilled (captured) order can be refunded.");
        }

        var refundable = payment.RefundableAmount();
        var requested = amount ?? refundable;
        if (requested <= 0m)
        {
            throw new PaymentConflictException($"Order {orderId} has nothing left to refund.");
        }
        // A partly-refunded order must never become refundable beyond what was captured.
        if (requested > refundable)
        {
            throw new PaymentConflictException(
                $"Refund of {requested} {CurrencyCode} exceeds the refundable amount of {refundable} {CurrencyCode} for order {orderId}.");
        }

        // Scope the PayPal-Request-Id to this payment so the same caller key used on a different
        // capture (or in a different run) never collides, while a repeat under the same key is
        // already short-circuited above by the stored refund.
        var requestId = $"refund-{payment.Reference}-{idempotencyKey}";
        var result = await _gateway.RefundAsync(payment.CaptureId, new Money(CurrencyCode, requested), requestId, ct);

        var refundAmount = result.Amount.Value > 0m ? result.Amount.Value : requested;
        var refund = new PaymentRefund(idempotencyKey, result.RefundId, refundAmount,
            result.Amount.CurrencyCode ?? CurrencyCode, result.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation(
            $"Refunded {refundAmount} {CurrencyCode} on order {orderId} (refund {result.RefundId}, status {result.Status}).");
        return new RefundOutcome(payment, refund);
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpec(buyerId), ct);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.Id)
            .Select(o => new OrderWithPayment(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    // ---- helpers ----

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        return payment ?? throw new PaymentNotFoundException($"No payment exists for order {orderId}.");
    }

    private static void EnsureOwner(Payment payment, string buyerId)
    {
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Don't reveal existence of another shopper's order.
            throw new PaymentNotFoundException($"No payment exists for order {payment.OrderId}.");
        }
    }

    private async Task<(PaymentInstrument Instrument, int? SavedCardId)> ResolveInstrumentAsync(
        string buyerId, PayInstruction instruction, CancellationToken ct)
    {
        if (instruction.SavedPaymentMethodId.HasValue)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdForBuyerSpec(instruction.SavedPaymentMethodId.Value, buyerId), ct)
                ?? throw new PaymentNotFoundException(
                    $"Saved card {instruction.SavedPaymentMethodId.Value} was not found for this shopper.");
            return (new PaymentInstrument(null, saved.VaultId), saved.Id);
        }

        if (instruction.Card == null)
        {
            throw new PaymentConflictException(
                "A payment must supply either card details or the id of a saved card.");
        }
        return (new PaymentInstrument(instruction.Card, null), null);
    }

    private static bool IsAuthorizationStale(Payment payment)
        => payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow;

    private static bool LooksLikeStaleAuthorization(PayPalApiException ex)
    {
        var name = ex.PayPalErrorName ?? string.Empty;
        var message = ex.Message ?? string.Empty;
        return name.Contains("EXPIR", StringComparison.OrdinalIgnoreCase)
            || name.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase)
            || message.Contains("expired", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RenewAuthorizationAsync(Payment payment, Money amount, int orderId, CancellationToken ct)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, amount, $"reauth-{payment.Reference}", ct);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation($"Renewed the authorization for order {orderId}: {renewed.AuthorizationId} ({renewed.Status}).");
        }
        catch (PayPalApiException ex)
        {
            // A hold that can no longer be renewed must say so in terms an operator can act on.
            throw new PaymentUnprocessableException(
                $"The authorization for order {orderId} has gone stale and can no longer be renewed " +
                $"(PayPal: {ex.PayPalErrorName ?? "error"} — {ex.Message}). Ask the shopper to pay for the order again.");
        }
    }
}
