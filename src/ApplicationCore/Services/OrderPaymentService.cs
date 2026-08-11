using System;
using System.Collections.Concurrent;
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
/// Orchestrates the pay-for-an-order flow against PayPal, keeping the eShop <see cref="Order"/> aggregate
/// untouched and holding money-movement state in the sibling <see cref="OrderPayment"/> aggregate.
/// Payment operations are serialised per order and made idempotent so a double-click never charges twice.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    // Process-wide per-order gate. Serialises pay/fulfil/cancel/refund for a given order so idempotency
    // checks and PayPal calls cannot race a concurrent duplicate request (e.g. a double-click).
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _orderLocks = new();

    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly ISavedCardService _savedCardService;
    private readonly IPayPalGateway _payPal;
    private readonly IPaymentConfiguration _configuration;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IReadRepository<CatalogItem> catalogRepository,
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        ISavedCardService savedCardService,
        IPayPalGateway payPal,
        IPaymentConfiguration configuration,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _catalogRepository = catalogRepository;
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardService = savedCardService;
        _payPal = payPal;
        _configuration = configuration;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one line item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentValidationException("Every order line must have a quantity of at least 1.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var total = order.Total();
        if (total <= 0m)
        {
            throw new PaymentValidationException("The order total must be greater than zero.");
        }

        var invoiceId = BuildInvoiceId(order.Id);
        var requestId = Guid.NewGuid().ToString("N");
        var payment = new OrderPayment(order.Id, buyerId, _configuration.Currency, total, invoiceId, requestId);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation($"Placed order {order.Id} for {buyerId}: total {total} {_configuration.Currency}, awaiting payment.");
        return order.Id;
    }

    public async Task<OrderPayment> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(instrument, nameof(instrument));

        var gate = await AcquireAsync(orderId, cancellationToken);
        try
        {
            var payment = await LoadOwnedAsync(buyerId, orderId, cancellationToken);

            switch (payment.Status)
            {
                case PaymentStatus.Authorized:
                    // Idempotent: a hold already exists — never place a second one.
                    _logger.LogInformation($"Order {orderId} is already authorized ({payment.AuthorizationId}); returning existing hold.");
                    return payment;
                case PaymentStatus.Captured:
                case PaymentStatus.PartiallyRefunded:
                case PaymentStatus.Refunded:
                    throw new PaymentOperationException($"Order {orderId} has already been captured and cannot be authorized again.");
                case PaymentStatus.Cancelled:
                    throw new PaymentOperationException($"Order {orderId} has been cancelled and can no longer be paid.");
            }

            var (card, vaultId) = await ResolveInstrumentAsync(buyerId, instrument, cancellationToken);

            var request = new AuthorizeOrderRequest
            {
                Amount = new PayPalMoney(payment.Amount, payment.CurrencyCode),
                InvoiceId = payment.InvoiceId,
                CustomId = orderId.ToString(),
                RequestId = NewRequestId(),
                Card = card,
                VaultId = vaultId,
                SoftDescriptor = "ESHOPONWEB"
            };

            AuthorizationResult result;
            try
            {
                result = await _payPal.AuthorizeOrderAsync(request, cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                payment.MarkAuthorizationFailed();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                _logger.LogWarning($"Authorization declined for order {orderId}: {ex.Message}");
                throw;
            }

            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Authorized order {orderId}: hold {result.AuthorizationId} for {result.Amount.Value} {result.Amount.CurrencyCode}.");
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = await AcquireAsync(orderId, cancellationToken);
        try
        {
            var payment = await LoadAsync(orderId, cancellationToken);

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                // Idempotent: money has already been taken.
                _logger.LogInformation($"Order {orderId} is already captured ({payment.CaptureId}); fulfilment is a no-op.");
                return payment;
            }
            if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
            {
                throw new PaymentOperationException(
                    $"Order {orderId} cannot be fulfilled because it is not authorized (state: {payment.Status}). Authorize the payment first.");
            }

            await EnsureAuthorizationIsFreshAsync(payment, cancellationToken);

            var amount = new PayPalMoney(payment.Amount, payment.CurrencyCode);
            var captureRequestId = NewRequestId();

            CaptureResult capture;
            try
            {
                capture = await _payPal.CaptureAuthorizationAsync(
                    payment.AuthorizationId!, amount, finalCapture: true, payment.InvoiceId, orderId.ToString(), captureRequestId, cancellationToken);
            }
            catch (PayPalApiException ex) when (IsExpiredAuthorization(ex))
            {
                // The hold went stale between our freshness check and the capture — renew and retry once.
                _logger.LogWarning($"Capture of order {orderId} reported an expired authorization; renewing and retrying.");
                await RenewAuthorizationAsync(payment, cancellationToken);
                capture = await _payPal.CaptureAuthorizationAsync(
                    payment.AuthorizationId!, amount, finalCapture: true, payment.InvoiceId, orderId.ToString(), captureRequestId, cancellationToken);
            }

            payment.MarkCaptured(
                capture.CaptureId,
                capture.Status,
                capture.Gross.Value,
                capture.PayPalFee?.Value,
                capture.NetAmount?.Value);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation(
                $"Captured order {orderId}: {capture.Gross.Value} {capture.Gross.CurrencyCode}, fee {capture.PayPalFee?.Value}, net {capture.NetAmount?.Value}.");
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = await AcquireAsync(orderId, cancellationToken);
        try
        {
            var payment = await LoadAsync(orderId, cancellationToken);

            if (payment.Status == PaymentStatus.Cancelled)
            {
                return payment; // idempotent
            }
            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                throw new PaymentOperationException(
                    $"Order {orderId} has already been captured and cannot be cancelled. Issue a refund instead.");
            }

            if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrEmpty(payment.AuthorizationId))
            {
                try
                {
                    await _payPal.VoidAuthorizationAsync(payment.AuthorizationId!, NewRequestId(), cancellationToken);
                }
                catch (PayPalApiException ex) when (IsAlreadyVoided(ex))
                {
                    _logger.LogInformation($"Authorization for order {orderId} was already voided at PayPal; treating cancel as complete.");
                }
            }

            payment.MarkCancelled();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Cancelled order {orderId}; any hold has been released.");
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        if (amount.HasValue && amount.Value <= 0m)
        {
            throw new PaymentValidationException("A refund amount, if given, must be greater than zero.");
        }

        var gate = await AcquireAsync(orderId, cancellationToken);
        try
        {
            var payment = await LoadOwnedAsync(buyerId, orderId, cancellationToken);

            if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            {
                throw new PaymentOperationException(
                    $"Order {orderId} cannot be refunded from its current state ({payment.Status}). Only a captured order can be refunded.");
            }
            if (string.IsNullOrEmpty(payment.CaptureId))
            {
                throw new PaymentOperationException($"Order {orderId} has no capture to refund.");
            }

            // Idempotency by caller-supplied key: a repeat under the same key returns the same refund.
            var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
            if (existing is not null && existing.CountsAgainstCapture)
            {
                _logger.LogInformation($"Refund for order {orderId} under key '{idempotencyKey}' already exists; returning it.");
                return existing;
            }

            var refundAmount = amount ?? payment.RefundableRemaining();
            if (refundAmount <= 0m)
            {
                throw new PaymentOperationException($"Order {orderId} has nothing left to refund.");
            }
            // A partly-refunded order must never become refundable beyond what was captured.
            if (refundAmount > payment.RefundableRemaining())
            {
                throw new PaymentOperationException(
                    $"Refund of {refundAmount} {payment.CurrencyCode} exceeds the refundable remaining balance of " +
                    $"{payment.RefundableRemaining()} {payment.CurrencyCode} on order {orderId}.");
            }

            PaymentRefund refund;
            if (existing is not null)
            {
                // A previous attempt under this key failed with no money moved — retry with the same row.
                refund = existing;
            }
            else
            {
                refund = new PaymentRefund(idempotencyKey, refundAmount);
                payment.AddRefund(refund); // guards against refunding beyond the captured amount
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }

            try
            {
                var result = await _payPal.RefundCaptureAsync(
                    payment.CaptureId!,
                    new PayPalMoney(refundAmount, payment.CurrencyCode),
                    payment.InvoiceId,
                    orderId.ToString(),
                    NewRequestId(),
                    cancellationToken);

                refund.SetResult(result.RefundId, result.Status, result.TotalRefunded?.Value);
                payment.RecalculateRefundStatus();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                _logger.LogInformation($"Refunded {refundAmount} {payment.CurrencyCode} on order {orderId} (refund {result.RefundId}).");
                return refund;
            }
            catch (PayPalApiException ex)
            {
                refund.MarkFailed();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                _logger.LogWarning($"Refund failed for order {orderId}: {ex.Message}");
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), cancellationToken);
        return payments;
    }

    public async Task<OrderPayment> GetOwnedPaymentAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var gate = await AcquireAsync(orderId, cancellationToken);
        try
        {
            return await LoadOwnedAsync(buyerId, orderId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    // --- helpers -------------------------------------------------------------

    private async Task<OrderPayment> LoadAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentEntityNotFoundException($"No order with id {orderId} was found.");
        }
        return payment;
    }

    private async Task<OrderPayment> LoadOwnedAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadAsync(orderId, cancellationToken);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal existence of another shopper's order.
            throw new ForbiddenPaymentAccessException($"Order {orderId} does not belong to the current user.");
        }
        return payment;
    }

    private async Task<(CardDetails? card, string? vaultId)> ResolveInstrumentAsync(string buyerId, PaymentInstrument instrument, CancellationToken cancellationToken)
    {
        var hasCard = instrument.Card is not null;
        var hasSaved = instrument.SavedPaymentMethodId.HasValue;
        if (hasCard == hasSaved)
        {
            throw new PaymentValidationException("Provide exactly one of: card details, or a saved payment method id.");
        }

        if (hasSaved)
        {
            var method = await _savedCardService.GetOwnedAsync(buyerId, instrument.SavedPaymentMethodId!.Value, cancellationToken);
            if (method is null)
            {
                throw new PaymentEntityNotFoundException($"Saved payment method {instrument.SavedPaymentMethodId} was not found for the current user.");
            }
            return (null, method.PayPalVaultId);
        }

        return (instrument.Card, null);
    }

    /// <summary>If the hold's expiry has passed (or is about to), renew it before capturing.</summary>
    private async Task EnsureAuthorizationIsFreshAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        if (!payment.AuthorizationExpiresAt.HasValue)
        {
            return;
        }
        // A small safety margin so a hold expiring within the minute is renewed rather than raced.
        if (DateTimeOffset.UtcNow < payment.AuthorizationExpiresAt.Value - TimeSpan.FromMinutes(1))
        {
            return;
        }
        _logger.LogInformation($"Authorization {payment.AuthorizationId} for order {payment.OrderId} has gone stale; renewing before capture.");
        await RenewAuthorizationAsync(payment, cancellationToken);
    }

    private async Task RenewAuthorizationAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(
                payment.AuthorizationId!, new PayPalMoney(payment.Amount, payment.CurrencyCode), NewRequestId(), cancellationToken);
            payment.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Renewed authorization for order {payment.OrderId}: new hold {renewed.AuthorizationId}.");
        }
        catch (PayPalApiException ex)
        {
            // Beyond PayPal's re-authorization window a hold can no longer be renewed — say so in operator terms.
            throw new PaymentOperationException(
                $"The authorization for order {payment.OrderId} has expired and can no longer be renewed ({ex.Message}). " +
                "Ask the shopper to place and pay for a new order.");
        }
    }

    private static bool IsExpiredAuthorization(PayPalApiException ex) =>
        ex.Issues.Any(i => i.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase));

    private static bool IsAlreadyVoided(PayPalApiException ex) =>
        ex.Issues.Any(i =>
            i.Contains("VOIDED", StringComparison.OrdinalIgnoreCase) ||
            i.Contains("ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase) ||
            i.Contains("INVALID_RESOURCE_ID", StringComparison.OrdinalIgnoreCase));

    // A fresh idempotency key per PayPal write attempt: unique so PayPal never rejects it as DUPLICATE_REQUEST_ID,
    // while our per-order lock and status/refund-key checks prevent any logical double charge or double refund.
    private static string NewRequestId() => Guid.NewGuid().ToString("N");

    private static string BuildInvoiceId(int orderId) =>
        // Unique per merchant across runs (the in-memory store restarts order ids from 1) so a capture never
        // collides with a previous run's invoice id on the shared sandbox account. Well within PayPal's 127 chars.
        $"ESHOP-{orderId}-{Guid.NewGuid():N}";

    private static async Task<SemaphoreSlim> AcquireAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return gate;
    }
}
