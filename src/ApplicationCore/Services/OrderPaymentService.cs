using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalClient payPal,
        IUriComposer uriComposer,
        PayPalSettings settings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogRepository = catalogRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _currency = settings.Currency;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentException("Every order line must have a quantity of at least one.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentException($"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        // Amounts come from catalog prices; the hold PayPal places equals this total to the cent.
        var payment = new Payment(order.Id, buyerId, order.Total(), _currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return order.Id;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument,
        CancellationToken cancellationToken = default)
    {
        await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken); // enforce ownership
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes the shopper twice.
        if (payment.Status == PaymentStatus.Authorized)
            return payment;
        if (payment.Status != PaymentStatus.AwaitingPayment)
            throw new PaymentException($"Order {orderId} cannot be paid because its payment is {payment.Status}.");

        var hasCard = instrument?.Card is not null;
        var hasSaved = instrument?.SavedPaymentMethodId is not null;
        if (hasCard == hasSaved)
            throw new PaymentException("Provide either card details or a saved card id, but not both.");

        // A stable, globally-unique key so retries and double-clicks collapse to one authorization at PayPal too.
        var idempotencyKey = $"authorize-{payment.IdempotencyToken}";

        AuthorizationResult result;
        if (hasCard)
        {
            result = await _payPal.AuthorizeWithCardAsync(payment.Amount, payment.Currency, instrument!.Card!,
                idempotencyKey, cancellationToken);
        }
        else
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(instrument!.SavedPaymentMethodId!.Value, cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
                throw new KeyNotFoundException($"Saved card {instrument.SavedPaymentMethodId} was not found.");

            result = await _payPal.AuthorizeWithVaultedCardAsync(payment.Amount, payment.Currency, savedCard.VaultId,
                idempotencyKey, cancellationToken);
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<FulfilmentResult> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // Idempotent: fulfilling an already-captured order returns what was captured, without capturing again.
        if (payment.Status == PaymentStatus.Captured)
        {
            return new FulfilmentResult(payment.CaptureId!, payment.CapturedAmount!.Value,
                payment.PayPalFee ?? 0m, payment.NetAmount ?? 0m, payment.Currency);
        }

        if (payment.Status != PaymentStatus.Authorized)
            throw new PaymentException($"Order {orderId} cannot be fulfilled because its payment is {payment.Status}.");

        var capture = await CaptureWithRenewalAsync(payment, cancellationToken);

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return new FulfilmentResult(capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount, capture.CurrencyCode);
    }

    /// <summary>
    /// Captures the authorization, renewing it first when the hold has gone stale before fulfilment.
    /// If PayPal will no longer renew it, reports the condition in terms an operator can act on.
    /// </summary>
    private async Task<CaptureResult> CaptureWithRenewalAsync(Payment payment, CancellationToken cancellationToken)
    {
        var authorizationId = payment.AuthorizationId!;
        try
        {
            return await _payPal.CaptureAuthorizationAsync(authorizationId, $"capture-{payment.IdempotencyToken}", cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            // A capture can fail because the authorization's honor period lapsed. Renew rather than fail.
            AuthorizationDetails details;
            try
            {
                details = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
            }
            catch (PayPalApiException)
            {
                throw; // can't even read the authorization — surface the original failure
            }

            if (!IsRenewable(details.Status))
            {
                // Not a staleness problem (e.g. the card was declined): surface PayPal's own reason.
                throw;
            }

            AuthorizationDetails renewed;
            try
            {
                renewed = await _payPal.ReauthorizeAuthorizationAsync(authorizationId, payment.Amount, payment.Currency, cancellationToken);
            }
            catch (PayPalApiException reauthEx)
            {
                throw new PaymentException(
                    $"The authorization for order {payment.OrderId} has expired and can no longer be renewed " +
                    $"({reauthEx.Issue ?? reauthEx.Message}). Ask the shopper to pay for the order again.", reauthEx);
            }

            payment.MarkReauthorized(renewed.AuthorizationId, renewed.Status);
            return await _payPal.CaptureAuthorizationAsync(renewed.AuthorizationId, $"capture-{payment.IdempotencyToken}-renewed", cancellationToken);
        }
    }

    private static bool IsRenewable(string authorizationStatus)
    {
        // A hold that has expired (or is past its honor period) can be reauthorized; a captured, voided,
        // or denied one cannot.
        var status = authorizationStatus?.ToUpperInvariant();
        return status is "EXPIRED" or "PENDING" or "CREATED";
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Voided)
            return payment; // idempotent

        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
                await _payPal.VoidAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
                payment.MarkVoided();
                break;
            case PaymentStatus.AwaitingPayment:
                // No hold was ever placed — nothing to release, but the order is cancelled.
                payment.MarkVoided();
                break;
            default:
                throw new PaymentException(
                    $"Order {orderId} cannot be cancelled because its payment is {payment.Status}; refund it instead.");
        }

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<int> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken); // enforce ownership
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        // Repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
            return existing.Id;

        if (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded)
            throw new PaymentException($"Order {orderId} cannot be refunded because its payment is {payment.Status}.");

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0m)
            throw new PaymentException("The refund amount must be greater than zero.");
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > payment.RefundableAmount)
            throw new PaymentException(
                $"Refund of {Format(refundAmount)} exceeds the refundable balance of {Format(payment.RefundableAmount)} {payment.Currency}.");

        // Call PayPal before booking the refund, so a failed call leaves no phantom refund behind.
        // Namespace the PayPal-Request-Id by this capture so the same caller key used against a
        // different order never collides at PayPal, while a repeat here is already short-circuited above.
        var payPalRequestId = $"refund-{payment.IdempotencyToken}-{idempotencyKey}";
        var result = await _payPal.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.Currency,
            payPalRequestId, cancellationToken);

        var refund = payment.AddRefund(idempotencyKey, refundAmount);
        refund.Confirm(result.RefundId, result.Status);
        payment.ApplyRefundOutcome();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return refund.Id;
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

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            throw new KeyNotFoundException($"Order {orderId} was not found.");
        return order;
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null)
            throw new KeyNotFoundException($"No payment exists for order {orderId}.");
        return payment;
    }

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
