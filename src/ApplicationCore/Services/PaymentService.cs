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

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the pay-for-an-order flow over the existing Order/OrderItem model and the PayPal
/// gateway. Every payment action is idempotent in effect: a double-click never authorizes or
/// captures the shopper twice.
/// </summary>
public class PaymentService : IPaymentService
{
    private static readonly Address UnspecifiedAddress =
        new("N/A", "N/A", "N/A", "N/A", "00000");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Entities.SavedCardAggregate.SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Entities.SavedCardAggregate.SavedCard> savedCardRepository,
        IPayPalGateway gateway,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentException("An order must contain at least one item.", PaymentErrorKind.Validation);
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentException("Each order line must have a quantity of at least 1.", PaymentErrorKind.Validation);

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", PaymentErrorKind.Validation);

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? UnspecifiedAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _gateway.Currency);
        await _paymentRepository.AddAsync(payment, ct);

        return order;
    }

    public async Task<OrderPayment> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken ct = default)
    {
        var payment = await LoadOwnPaymentAsync(buyerId, orderId, ct);

        // Idempotent in effect: once a hold exists (or the order has moved past it), do not re-authorize.
        if (payment.Status != PaymentStatus.AwaitingPayment)
        {
            if (payment.Status == PaymentStatus.Authorized)
                return payment;
            throw new PaymentException(
                $"Order {orderId} is '{payment.Status}' and can no longer be authorized.", PaymentErrorKind.Conflict);
        }

        var resolved = await ResolveInstrumentAsync(buyerId, instrument, ct);

        var idempotencyKey = $"authorize-{payment.IdempotencyToken}";
        var result = await _gateway.AuthorizeAsync(payment.Amount, resolved.Instrument, idempotencyKey, ct);

        var descriptor = resolved.Descriptor ?? DescribeCard(result.CardBrand, result.CardLastFour);
        payment.SetAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, descriptor);
        await _paymentRepository.UpdateAsync(payment, ct);
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Captured ||
            payment.Status == PaymentStatus.PartiallyRefunded ||
            payment.Status == PaymentStatus.Refunded)
            return payment; // already fulfilled — idempotent

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentException(
                $"Order {orderId} is '{payment.Status}' and cannot be fulfilled; it must be authorized first.",
                PaymentErrorKind.Conflict);

        var idempotencyKey = $"capture-{payment.IdempotencyToken}";
        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId, idempotencyKey, ct);
        }
        catch (PaymentException ex) when (ex.Kind == PaymentErrorKind.BusinessRule)
        {
            // The hold may have gone stale. Renew it rather than failing fulfilment outright.
            var renewed = await RenewAuthorizationOrThrowAsync(payment, ex, ct);
            capture = await _gateway.CaptureAsync(renewed.AuthorizationId, idempotencyKey, ct);
        }

        payment.SetCaptured(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        return payment;
    }

    private async Task<ReauthorizationResult> RenewAuthorizationOrThrowAsync(OrderPayment payment, PaymentException captureError, CancellationToken ct)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, ct);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status);
            await _paymentRepository.UpdateAsync(payment, ct);
            return renewed;
        }
        catch (PaymentException reauthError)
        {
            // Could not renew — surface something an operator can act on, combining both PayPal messages.
            throw new PaymentException(
                $"Fulfilment failed: the authorization for order {payment.OrderId} could not be captured " +
                $"({captureError.Message}) and could no longer be renewed ({reauthError.Message}).",
                PaymentErrorKind.AuthorizationNotRenewable, reauthError);
        }
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
            return payment; // already released — idempotent

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentException(
                $"Order {orderId} is '{payment.Status}' and cannot be cancelled; only an authorized, unfulfilled order can be cancelled (a fulfilled order must be refunded).",
                PaymentErrorKind.Conflict);

        await _gateway.VoidAsync(payment.AuthorizationId, $"void-{payment.IdempotencyToken}", ct);
        payment.SetCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadOwnPaymentAsync(buyerId, orderId, ct);

        if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded)
            throw new PaymentException(
                $"Order {orderId} is '{payment.Status}' and cannot be refunded; only a captured order can be refunded.",
                PaymentErrorKind.Conflict);

        // Idempotent: a repeat under the same key returns the original refund, never a second one.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return existing;

        var remaining = payment.RefundableRemaining();
        var requested = amount ?? remaining;

        if (requested <= 0m)
            throw new PaymentException("Refund amount must be greater than zero.", PaymentErrorKind.Validation);
        if (requested > remaining)
            throw new PaymentException(
                $"Refund of {requested} exceeds the remaining refundable balance of {remaining} {payment.Currency}.",
                PaymentErrorKind.BusinessRule);

        var result = await _gateway.RefundAsync(payment.CaptureId!, requested, idempotencyKey, ct);
        var refund = payment.AddRefund(result.RefundId, requested, idempotencyKey, result.Status);
        await _paymentRepository.UpdateAsync(payment, ct);
        return refund;
    }

    public async Task<IReadOnlyList<OrderPayment>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), ct);
    }

    // --- helpers ---

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
            throw new PaymentException($"No payment exists for order {orderId}.", PaymentErrorKind.NotFound);
        return payment;
    }

    private async Task<OrderPayment> LoadOwnPaymentAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);
        // A shopper must never see or act on another shopper's order.
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentException($"Order {orderId} was not found for this shopper.", PaymentErrorKind.NotFound);
        return payment;
    }

    private async Task<(PaymentInstrument Instrument, string? Descriptor)> ResolveInstrumentAsync(
        string buyerId, PaymentInstrument instrument, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(instrument.VaultId))
        {
            // A vault id in the pay request names one of the shopper's saved cards by its application id.
            if (!int.TryParse(instrument.VaultId, out var savedCardId))
                throw new PaymentException("Saved card id must be a number.", PaymentErrorKind.Validation);

            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdForOwnerSpecification(savedCardId, buyerId), ct);
            if (savedCard is null)
                throw new PaymentException($"Saved card {savedCardId} was not found for this shopper.", PaymentErrorKind.NotFound);

            return (PaymentInstrument.FromVault(savedCard.PayPalVaultId), DescribeCard(savedCard.CardBrand, savedCard.LastFourDigits));
        }

        if (instrument.Card is not null)
        {
            var last4 = SafeLastFour(instrument.Card.Number);
            return (instrument, DescribeCard(null, last4));
        }

        throw new PaymentException("A payment must supply either card details or a saved card id.", PaymentErrorKind.Validation);
    }

    private static string? DescribeCard(string? brand, string? lastFour)
    {
        if (string.IsNullOrEmpty(lastFour))
            return string.IsNullOrEmpty(brand) ? null : brand;
        return $"{(string.IsNullOrEmpty(brand) ? "CARD" : brand)} ****{lastFour}";
    }

    private static string? SafeLastFour(string? number)
    {
        if (string.IsNullOrEmpty(number)) return null;
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : null;
    }
}
