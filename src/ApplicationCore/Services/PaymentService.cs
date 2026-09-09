using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    // A default ship-to address for API-placed orders (the existing storefront checkout does the
    // same). Payment, not shipping, is the concern of this feature.
    private static readonly Address DefaultShipToAddress =
        new("123 Main St.", "Kent", "OH", "United States", "44240");

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalClient payPal,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    public async Task<Order> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines,
        CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var requested = (lines ?? Enumerable.Empty<OrderLineRequest>()).ToList();
        if (requested.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }
        if (requested.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least 1.");
        }

        var ids = requested.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = requested.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var payment = new Payment(order.Id, buyerId, RoundMoney(order.Total()), Currency);
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation("Placed order {0} for {1} awaiting payment ({2} {3}).",
            order.Id, buyerId, payment.Amount, Currency);
        return order;
    }

    public async Task<Payment> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument,
        CancellationToken ct = default)
    {
        var order = await LoadOwnOrderAsync(buyerId, orderId, ct);
        var payment = await LoadPaymentAsync(orderId, ct);

        // Idempotent in effect: a double-click on an order already holding funds returns the
        // existing hold instead of authorizing a second time.
        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            return payment;
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            throw new PaymentException($"Order {orderId} has already been paid and captured.");
        }
        if (payment.Status == PaymentStatus.Voided)
        {
            throw new PaymentException($"Order {orderId} was cancelled and can no longer be paid.");
        }

        ValidateInstrument(instrument);
        var amount = RoundMoney(order.Total());
        var invoiceId = payment.PayPalInvoiceId;
        // Request ids are derived from the payment's unique invoice id so they are globally unique
        // (PayPal's idempotency cache lives on the account, not the process) yet stable within a
        // run: concurrent double-clicks collapse to one authorization. A deliberate retry after a
        // prior failure gets a fresh id so it is allowed to proceed rather than replaying.
        var requestId = payment.Status == PaymentStatus.Failed
            ? $"{invoiceId}-auth-{Guid.NewGuid():N}"
            : $"{invoiceId}-auth";

        Interfaces.PayPal.PayPalAuthorizationResult result;
        try
        {
            if (instrument.SavedCardId is int savedCardId)
            {
                var vaultId = await ResolveVaultIdAsync(buyerId, savedCardId, ct);
                result = await _payPal.AuthorizeWithVaultAsync(amount, Currency, invoiceId, vaultId, requestId, ct);
            }
            else
            {
                result = await _payPal.AuthorizeWithCardAsync(amount, Currency, invoiceId, instrument.Card!, requestId, ct);
            }
        }
        catch (PayPalApiException ex)
        {
            payment.SetFailed(Describe(ex));
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentException($"Authorization was declined by PayPal: {ex.Message}");
        }

        if (result.RequiresPayerAction)
        {
            payment.SetFailed("PayPal requires the shopper to approve this payment in a browser.");
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentException(
                "PayPal returned a challenge requiring browser approval for this card; a card that authorizes without a challenge is required.");
        }
        if (result.AuthorizationId is null)
        {
            payment.SetFailed($"PayPal did not return an authorization (order status {result.OrderStatus}).");
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentException($"PayPal did not create an authorization for order {orderId}.");
        }

        payment.SetAuthorized(result.PayPalOrderId, result.AuthorizationId,
            result.AuthorizationStatus ?? "CREATED", result.CardBrand, result.CardLastFour);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Authorized order {0}: PayPal order {1}, authorization {2}.",
            orderId, result.PayPalOrderId, result.AuthorizationId);
        return payment;
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            return payment; // already captured — idempotent
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentException(
                $"Order {orderId} is not authorized (status {payment.Status}); it cannot be fulfilled.");
        }

        var authId = payment.AuthorizationId!;

        // A hold that has gone stale before fulfilment is renewed rather than failing the
        // fulfilment: if PayPal no longer reports it capturable, reauthorize first.
        var currentStatus = await _payPal.GetAuthorizationStatusAsync(authId, ct);
        if (currentStatus is not null &&
            !currentStatus.Equals("CREATED", StringComparison.OrdinalIgnoreCase) &&
            !currentStatus.Equals("CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            authId = await RenewAuthorizationOrThrowAsync(payment, currentStatus, ct);
        }
        else if (currentStatus is not null)
        {
            payment.SetAuthorizationStatus(currentStatus);
        }

        Interfaces.PayPal.PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(authId, $"{payment.PayPalInvoiceId}-capture", ct);
        }
        catch (PayPalApiException ex) when (IsRenewableAuthFailure(ex))
        {
            authId = await RenewAuthorizationOrThrowAsync(payment, ex.PayPalName ?? ex.Message, ct);
            capture = await _payPal.CaptureAuthorizationAsync(authId,
                $"{payment.PayPalInvoiceId}-capture-{Guid.NewGuid():N}", ct);
        }

        // The immediate capture response omits the fee/net breakdown; read the capture back to
        // record what PayPal reported: captured amount, PayPal fee and net proceeds.
        var settled = capture;
        try
        {
            settled = await _payPal.GetCaptureAsync(capture.CaptureId, ct);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Could not read capture {0} breakdown: {1}", capture.CaptureId, ex.Message);
        }

        payment.SetCaptured(capture.CaptureId, settled.Status, settled.Amount,
            settled.PayPalFee ?? capture.PayPalFee, settled.NetAmount ?? capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Fulfilled order {0}: captured {1} (fee {2}, net {3}) capture {4}.",
            orderId, payment.CapturedAmount, payment.PayPalFee, payment.NetAmount, capture.CaptureId);
        return payment;
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Voided)
        {
            return payment; // idempotent
        }
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            throw new PaymentException(
                $"Order {orderId} has already been captured; refund it instead of cancelling.");
        }

        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, $"{payment.PayPalInvoiceId}-void", ct);
            _logger.LogInformation("Voided authorization {0} for order {1}.", payment.AuthorizationId, orderId);
        }

        payment.SetVoided();
        await _paymentRepository.UpdateAsync(payment, ct);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        await LoadOwnOrderAsync(buyerId, orderId, ct); // enforces ownership
        var payment = await LoadPaymentAsync(orderId, ct);
        if (payment.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }

        if (payment.CaptureId is null || payment.Status is PaymentStatus.AwaitingPayment
            or PaymentStatus.Authorized or PaymentStatus.Voided or PaymentStatus.Failed)
        {
            throw new PaymentException($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotent: repeating a request under the same key returns the existing refund.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
        {
            throw new PaymentException($"Order {orderId} has already been fully refunded.");
        }

        decimal refundAmount;
        if (amount is decimal requested)
        {
            if (requested <= 0m)
            {
                throw new PaymentException("A refund amount must be greater than zero.");
            }
            if (RoundMoney(requested) > remaining)
            {
                throw new PaymentException(
                    $"Refund of {RoundMoney(requested)} exceeds the {remaining} still refundable on order {orderId}.");
            }
            refundAmount = RoundMoney(requested);
        }
        else
        {
            refundAmount = remaining; // full refund of what remains
        }

        // The caller's key is namespaced by the payment's unique invoice id for the PayPal
        // request id: the same key on the same capture stays idempotent at PayPal, while the app's
        // own key lookup above already prevents a second PayPal call for a repeat.
        var payPalRequestId = $"{payment.PayPalInvoiceId}-refund-{idempotencyKey}";
        var result = await _payPal.RefundCaptureAsync(payment.CaptureId, refundAmount, Currency, payPalRequestId, ct);

        var refund = new PaymentRefund(idempotencyKey, result.RefundId,
            result.Amount > 0m ? result.Amount : refundAmount, result.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Refunded {0} on order {1}: refund {2} ({3}). Status now {4}.",
            refund.Amount, orderId, result.RefundId, result.Status, payment.Status);
        return refund;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId,
        CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpec(buyerId), ct);
        var byOrder = payments.ToDictionary(p => p.OrderId);
        return orders
            .OrderByDescending(o => o.Id)
            .Select(o => new OrderWithPayment(o, byOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    // --- helpers ---

    private async Task<Order> LoadOwnOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        // A missing order and another shopper's order are surfaced identically so ownership
        // cannot be probed.
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        if (payment is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        return payment;
    }

    private async Task<string> ResolveVaultIdAsync(string buyerId, int savedCardId, CancellationToken ct)
    {
        var card = await _savedCardRepository.GetByIdAsync(savedCardId, ct);
        if (card is null || card.BuyerId != buyerId)
        {
            throw new PaymentMethodNotFoundException(savedCardId);
        }
        return card.PayPalVaultId;
    }

    private async Task<string> RenewAuthorizationOrThrowAsync(Payment payment, string reason, CancellationToken ct)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                Currency, $"{payment.PayPalInvoiceId}-reauth-{Guid.NewGuid():N}", ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Renewed stale authorization for order {0}: new authorization {1}.",
                payment.OrderId, reauth.AuthorizationId);
            return reauth.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            var message =
                $"The authorization for order {payment.OrderId} has expired (was {reason}) and could not be " +
                $"renewed ({ex.Message}). Ask the shopper to pay the order again before fulfilling it.";
            payment.SetFailed(message);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentException(message);
        }
    }

    private static void ValidateInstrument(PaymentInstrument instrument)
    {
        var hasCard = instrument.Card is not null;
        var hasSaved = instrument.SavedCardId.HasValue;
        if (hasCard == hasSaved)
        {
            throw new PaymentException(
                "Provide exactly one of card details or a saved card id to pay with.");
        }
        if (hasCard)
        {
            var card = instrument.Card!;
            if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Expiry))
            {
                throw new PaymentException("Card number and expiry are required for a card payment.");
            }
        }
    }

    // A capture can fail because the hold expired during the honor period; PayPal signals this
    // with an AUTHORIZATION_EXPIRED issue, which a reauthorization can recover from. Other
    // failures (already captured, exceeded capture count, declined) are not renewable.
    private static bool IsRenewableAuthFailure(PayPalApiException ex)
    {
        var name = ex.PayPalName?.ToUpperInvariant() ?? string.Empty;
        return name.Contains("EXPIRED");
    }

    private static string Describe(PayPalApiException ex) =>
        ex.PayPalName is null ? ex.Message : $"{ex.PayPalName}: {ex.Message}";

    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
