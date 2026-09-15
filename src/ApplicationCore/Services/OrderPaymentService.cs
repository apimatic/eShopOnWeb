using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Coordinates the order/payment lifecycle over the existing order model, the payment aggregate and the
/// PayPal boundary. Enforces caller ownership, once-only effect (idempotency) and the invariant that a
/// refund can never exceed what was captured.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentService _payPal;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalPaymentService payPal)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines,
        Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new OrderValidationException("An order must contain at least one line.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new OrderValidationException("Every order line must have a quantity of at least one.");
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), ct);

        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new OrderValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var payment = new Payment(order.Id, buyerId, _payPal.Currency, order.Total());
        await _paymentRepository.AddAsync(payment, ct);

        return order.Id;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, CardPaymentDetails? card,
        int? savedPaymentMethodId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var payment = await GetOwnedPaymentAsync(buyerId, orderId, ct);

        // Idempotent in effect: a repeat while already authorized (or captured) never doubles the hold.
        if (payment.Status == PaymentStatus.Authorized || payment.Status == PaymentStatus.Captured
            || payment.Status == PaymentStatus.PartiallyRefunded || payment.Status == PaymentStatus.Refunded)
        {
            return payment;
        }

        // Reserve/reuse the idempotency key; throws if the payment is not awaiting payment.
        var requestId = payment.BeginAuthorization();

        AuthorizationResult result;
        int? usedMethodId = null;

        if (savedPaymentMethodId.HasValue)
        {
            var savedMethod = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdSpecification(savedPaymentMethodId.Value, buyerId), ct)
                ?? throw new EntityNotFoundException(
                    $"Saved card {savedPaymentMethodId.Value} was not found for this shopper.");

            result = await _payPal.AuthorizeWithVaultedCardAsync(payment.Amount, payment.CurrencyCode,
                savedMethod.VaultId, savedMethod.PayPalCustomerId, requestId, ct);
            usedMethodId = savedMethod.Id;
        }
        else if (card is not null)
        {
            result = await _payPal.AuthorizeWithCardAsync(payment.Amount, payment.CurrencyCode,
                card, requestId, ct);
        }
        else
        {
            throw new OrderValidationException(
                "A payment must supply either card details or the id of a saved card.");
        }

        payment.SetAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status,
            result.ExpiresAt, usedMethodId);
        await _paymentRepository.UpdateAsync(payment, ct);
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await GetPaymentAsync(orderId, ct);

        // Idempotent: fulfilling an already-captured order returns the existing result unchanged.
        if (payment.Status == PaymentStatus.Captured || payment.Status == PaymentStatus.PartiallyRefunded
            || payment.Status == PaymentStatus.Refunded)
        {
            return payment;
        }

        // Reserve/reuse the capture idempotency key; throws if not in an authorized state.
        var captureRequestId = payment.BeginCapture();
        var authorizationId = payment.AuthorizationId!;

        // Proactively renew a hold that has passed its honor window before trying to capture.
        if (payment.IsAuthorizationStale(DateTimeOffset.UtcNow))
        {
            authorizationId = await RenewAuthorizationOrFailAsync(payment, ct);
        }

        CaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(authorizationId, captureRequestId, ct);
        }
        catch (PayPalProviderException ex) when (ex is not PayerActionRequiredException && ex.StatusCode == 422)
        {
            // A capture rejection can mean the hold went stale between our check and the call. Renew once
            // and retry; if the hold can no longer be renewed, surface that in operator terms.
            authorizationId = await RenewAuthorizationOrFailAsync(payment, ct, ex);
            capture = await _payPal.CaptureAsync(authorizationId, captureRequestId, ct);
        }

        payment.SetCaptured(capture.CaptureId, capture.Status, capture.GrossAmount,
            capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        return payment;
    }

    private async Task<string> RenewAuthorizationOrFailAsync(Payment payment, CancellationToken ct,
        PayPalProviderException? captureFailure = null)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                payment.CurrencyCode, ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            return reauth.AuthorizationId;
        }
        catch (PayPalProviderException reEx)
        {
            var detail = captureFailure is not null ? $" (capture reported: {captureFailure.Message})" : string.Empty;
            throw new PaymentConflictException(
                $"Order {payment.OrderId} cannot be fulfilled: the payment hold has expired and could not " +
                $"be renewed — {reEx.Message}.{detail} Ask the shopper to pay again to place a fresh hold.");
        }
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await GetPaymentAsync(orderId, ct);

        // Idempotent: cancelling an already-cancelled order is a no-op.
        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment;
        }

        // Only a held (authorized, not yet captured) order can be cancelled. This throws a clear,
        // actionable conflict if the money has already moved.
        if (payment.Status != PaymentStatus.Authorized)
        {
            payment.MarkCancelled();
        }

        // Release the hold at PayPal, then record the cancellation.
        await _payPal.VoidAsync(payment.AuthorizationId!, ct);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await GetOwnedPaymentAsync(buyerId, orderId, ct);

        // Idempotent replay: the same key returns the refund already recorded, never a second refund.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        // Validate state + amount before contacting PayPal so an invalid refund never reaches the provider.
        payment.EnsureCanRefund(amount);

        var result = await _payPal.RefundAsync(payment.CaptureId!, amount, payment.CurrencyCode, idempotencyKey, ct);

        var refund = payment.AddRefund(idempotencyKey, amount);
        refund.SetProviderResult(result.RefundId, result.Status);
        payment.RecalculateRefundState();
        await _paymentRepository.UpdateAsync(payment, ct);
        return refund;
    }

    private async Task<Payment> GetPaymentAsync(int orderId, CancellationToken ct)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct)
            ?? throw new EntityNotFoundException($"No payment was found for order {orderId}.");
    }

    private async Task<Payment> GetOwnedPaymentAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal another shopper's order exists — treat as not found.
            throw new EntityNotFoundException($"No payment was found for order {orderId}.");
        }
        return payment;
    }
}
