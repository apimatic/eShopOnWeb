using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalClient payPal,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyCollection<OrderLineRequest> lines,
        ShippingAddressRequest? shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one line item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentValidationException("Every order line must have a quantity of at least 1.");
        }

        // Collapse duplicate catalog ids into a single line each.
        var quantities = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);

        var missing = quantities.Keys.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentValidationException(
                $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        // Amounts come from catalog prices — never trust client-supplied prices.
        var items = quantities.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var address = shippingAddress is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State,
                shippingAddress.Country, shippingAddress.ZipCode);

        var order = new Order(buyerId, address, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, _settings.Currency, order.Total());
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Placed order {0} for {1}: total {2} {3}.",
            order.Id, buyerId, order.Total(), _settings.Currency);

        return order;
    }

    public async Task<Payment> AuthorizeAsync(
        string buyerId,
        int orderId,
        PayPalCardDetails? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        if (card is null && savedPaymentMethodId is null)
        {
            throw new PaymentValidationException(
                "Provide card details or the id of a saved card to pay with.");
        }
        if (card is not null && savedPaymentMethodId is not null)
        {
            throw new PaymentValidationException(
                "Provide either card details or a saved card, not both.");
        }

        var payment = await GetOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: an already-authorized order returns its existing hold.
        if (payment.Status == PaymentStatus.Authorized)
        {
            _logger.LogInformation("Order {0} already authorized ({1}); returning existing hold.",
                orderId, payment.AuthorizationId);
            return payment;
        }
        if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.Cancelled
            or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be paid because it is already '{payment.Status}'.");
        }

        // The idempotency seed is unique per payment (even across in-memory restarts that reuse
        // order id 1), so PayPal never replays a stale cached response from an earlier run, while
        // concurrent double-clicks in the same attempt still share one request id and de-duplicate.
        var requestId = $"auth-order-{orderId}-{payment.CreatedAt.UtcTicks}-{payment.AuthorizeAttempts}";
        string? instrumentDescription;
        string? vaultId = null;

        try
        {
            PayPalAuthorizationResult result;
            if (savedPaymentMethodId is not null)
            {
                var saved = await _savedCardRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdSpecification(savedPaymentMethodId.Value, buyerId), cancellationToken);
                if (saved is null)
                {
                    throw new NotFoundException($"Saved card {savedPaymentMethodId} was not found.");
                }
                vaultId = saved.VaultId;
                result = await _payPal.AuthorizeWithVaultAsync(
                    payment.Amount, payment.CurrencyCode, saved.VaultId, payment.InvoiceReference, requestId, cancellationToken);
                instrumentDescription = saved.Description;
            }
            else
            {
                result = await _payPal.AuthorizeWithCardAsync(
                    payment.Amount, payment.CurrencyCode, card!, payment.InvoiceReference, requestId, cancellationToken);
                instrumentDescription = DescribeCard(result);
            }

            payment.MarkAuthorized(
                result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus,
                result.ExpiresAt, instrumentDescription, vaultId);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            _logger.LogInformation("Authorized order {0}: hold {1} status {2}.",
                orderId, result.AuthorizationId, result.AuthorizationStatus);
            return payment;
        }
        catch (Exception ex) when (ex is PayPalApiException or PayPalChallengeRequiredException)
        {
            payment.MarkAuthorizationFailed();
            payment.IncrementAuthorizeAttempt();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw;
        }
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        // Idempotent in effect: an already-captured order is returned unchanged.
        if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be fulfilled because its payment is '{payment.Status}'.");
        }

        await EnsureAuthorizationCapturableAsync(payment, cancellationToken);

        var requestId = $"capture-order-{orderId}-{payment.CreatedAt.UtcTicks}";
        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, payment.InvoiceReference, requestId, cancellationToken);
        }
        catch (PayPalApiException ex) when (IsStaleAuthorizationIssue(ex.Issue))
        {
            // The hold expired between our check and the capture — renew and capture once more.
            await RenewAuthorizationAsync(payment, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(
                payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, payment.InvoiceReference, requestId, cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Fulfilled order {0}: captured {1} {2} (fee {3}, net {4}) as {5}.",
            orderId, capture.GrossAmount, capture.CurrencyCode, capture.PayPalFee, capture.NetAmount, capture.CaptureId);
        return payment;
    }

    public async Task<Payment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        // Idempotent in effect.
        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be cancelled because its payment is '{payment.Status}'. " +
                "Only an order awaiting fulfilment can be cancelled; fulfilled orders are refunded instead.");
        }

        await _payPal.VoidAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Cancelled order {0}: released hold {1}.", orderId, payment.AuthorizationId);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(
        int orderId,
        string requesterBuyerId,
        bool requesterIsAdmin,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("A refund requires an idempotency key.");
        }
        if (amount is not null && amount.Value <= 0m)
        {
            throw new PaymentValidationException("A refund amount, when supplied, must be greater than zero.");
        }

        var payment = await GetPaymentAsync(orderId, cancellationToken);

        // Owner-or-operator scope: a shopper may refund only their own order.
        if (!requesterIsAdmin && payment.BuyerId != requesterBuyerId)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }

        if (payment.CaptureId is null ||
            payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be refunded because its payment is '{payment.Status}'. " +
                "Only a fulfilled (captured) order can be refunded.");
        }

        // Idempotent under the caller's key: a repeat returns the same refund, never a second one.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation("Refund for order {0} under key {1} already exists ({2}); returning it.",
                orderId, idempotencyKey, existing.PayPalRefundId);
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableRemaining;

        // A partly-refunded order never becomes refundable beyond what was captured.
        if (refundAmount > payment.RefundableRemaining)
        {
            throw new PaymentConflictException(
                $"Refund of {refundAmount:0.00} {payment.CurrencyCode} exceeds the refundable remaining of " +
                $"{payment.RefundableRemaining:0.00} {payment.CurrencyCode} on order {orderId}.");
        }

        // Stage the refund (the entity re-checks the invariant as a safety net).
        var refund = payment.AddRefund(idempotencyKey, refundAmount);

        var result = await _payPal.RefundCaptureAsync(
            payment.CaptureId!, refundAmount, payment.CurrencyCode, payment.InvoiceReference, idempotencyKey, cancellationToken);

        refund.SetResult(result.RefundId, result.Status);
        payment.RecalculateAfterRefund();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Refunded order {0}: {1} {2} as {3} (payment now {4}).",
            orderId, refundAmount, payment.CurrencyCode, result.RefundId, payment.Status);
        return refund;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(
        string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(
            new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.Id)
            .Select(o => new OrderWithPayment(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    // ---- helpers ----

    private async Task<Payment> GetPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderSpecification(orderId), cancellationToken);
        if (payment is null)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private async Task<Payment> GetOwnedPaymentAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var payment = await GetPaymentAsync(orderId, cancellationToken);
        if (payment.BuyerId != buyerId)
        {
            // Do not reveal another shopper's order.
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    /// <summary>
    /// Ensures the hold can be captured. If it has gone stale before fulfilment, it is renewed
    /// (reauthorized) rather than failing the fulfilment; a hold that can no longer be renewed
    /// surfaces an operator-actionable error.
    /// </summary>
    private async Task EnsureAuthorizationCapturableAsync(Payment payment, CancellationToken cancellationToken)
    {
        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPal.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Could not read authorization {0} for order {1}: {2}",
                payment.AuthorizationId!, payment.OrderId, ex.Issue ?? ex.Message);
            return; // Fall through to capture; the capture path also renews on a stale-hold error.
        }

        var isStale = string.Equals(details.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase)
            || (details.ExpiresAt.HasValue && details.ExpiresAt.Value <= DateTimeOffset.UtcNow);

        if (isStale)
        {
            _logger.LogWarning("Authorization {0} for order {1} is stale (status {2}); renewing.",
                payment.AuthorizationId!, payment.OrderId, details.Status);
            await RenewAuthorizationAsync(payment, cancellationToken);
        }
    }

    private async Task RenewAuthorizationAsync(Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(
                payment.AuthorizationId!, payment.Amount, payment.CurrencyCode,
                $"reauth-order-{payment.OrderId}-{payment.CreatedAt.UtcTicks}", cancellationToken);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation("Renewed hold for order {0}: new authorization {1}.",
                payment.OrderId, reauth.AuthorizationId);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentConflictException(
                $"The payment hold on order {payment.OrderId} has expired and can no longer be renewed " +
                $"({ex.Issue ?? "REAUTHORIZATION_FAILED"}). Ask the shopper to pay for the order again before fulfilling it.");
        }
    }

    private static bool IsStaleAuthorizationIssue(string? issue) =>
        issue is not null && issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);

    private static string DescribeCard(PayPalAuthorizationResult result)
    {
        var brand = string.IsNullOrWhiteSpace(result.CardBrand) ? "Card" : result.CardBrand!;
        var last = string.IsNullOrWhiteSpace(result.CardLastFour) ? "" : $" ****{result.CardLastFour}";
        return $"{brand}{last}";
    }
}
