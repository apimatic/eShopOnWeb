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
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // PayPal issue codes (from the spec error model) that mean the hold has gone stale and
    // must be renewed before the money can be captured.
    private static readonly string[] RenewableCaptureIssues =
    {
        "AUTHORIZATION_EXPIRED",
        "AUTH_CAPTURE_CURRENCY_MISMATCH_INVALID" // defensive; not expected on the happy path
    };

    // eShopOnWeb has no per-order shipping capture in this API surface; reuse the same default
    // the storefront checkout uses so the existing order model is satisfied.
    private static readonly Func<Address> DefaultShipTo =
        () => new Address("123 Main St.", "Kent", "OH", "United States", "44240");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalGateway _payPal;
    private readonly PayPalSettings _settings;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IReadRepository<CatalogItem> itemRepository,
        IReadRepository<SavedCard> savedCardRepository,
        IPayPalGateway payPal,
        PayPalSettings settings,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _settings = settings;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one line.");
        }

        // Merge duplicate catalog items and reject non-positive quantities.
        var merged = new Dictionary<int, int>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
            merged[line.CatalogItemId] = merged.TryGetValue(line.CatalogItemId, out var q) ? q + line.Quantity : line.Quantity;
        }

        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(merged.Keys.ToArray()), cancellationToken);
        var missing = merged.Keys.Where(id => catalogItems.All(c => c.Id != id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = merged.Select(kvp =>
        {
            var catalogItem = catalogItems.First(c => c.Id == kvp.Key);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, kvp.Value);
        }).ToList();

        var order = new Order(buyerId, DefaultShipTo(), items);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, order.Total(), _settings.Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Placed order {0} for buyer {1} totalling {2} {3}.", order.Id, buyerId, order.Total(), _settings.Currency);
        return order.Id;
    }

    public async Task<Payment> PayOrderAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a repeated pay never authorizes the shopper twice.
        if (payment.Status == PaymentStatus.Authorized)
        {
            _logger.LogInformation("Order {0} is already authorized; returning existing authorization.", orderId);
            return payment;
        }
        if (payment.Status != PaymentStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {orderId} can no longer be paid (status: {payment.Status}).");
        }

        var (card, vaultId) = await ResolveInstrumentAsync(buyerId, instruction, cancellationToken);

        // Reserve a unique invoice id and persist it before contacting PayPal so a retry reuses it.
        if (string.IsNullOrEmpty(payment.InvoiceId))
        {
            payment.PrepareInvoice($"ESHOP-{orderId}-{Guid.NewGuid():N}");
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        var request = new AuthorizeOrderRequest(
            Amount: payment.Amount,
            CurrencyCode: payment.CurrencyCode,
            InvoiceId: payment.InvoiceId!,
            CustomId: orderId.ToString(CultureInfo.InvariantCulture),
            IdempotencyKey: $"{payment.IdempotencyToken}-pay",
            Card: card,
            VaultId: vaultId);

        var result = await _payPal.AuthorizeOrderAsync(request, cancellationToken);

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status,
            result.ExpiresAt, result.CardBrand, result.CardLast4);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Authorized order {0}: paypalOrder {1}, authorization {2}, status {3}.",
            orderId, result.PayPalOrderId, result.AuthorizationId, result.Status);
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Captured
            || payment.Status == PaymentStatus.PartiallyRefunded
            || payment.Status == PaymentStatus.Refunded)
        {
            _logger.LogInformation("Order {0} is already fulfilled; returning existing capture.", orderId);
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {orderId} cannot be fulfilled because it is not authorized (status: {payment.Status}).");
        }

        var authorizationId = payment.AuthorizationId!;

        // Proactively renew if the hold is already past its expiry, then capture.
        if (payment.AuthorizationExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Authorization {0} for order {1} expired at {2}; attempting renewal before capture.",
                authorizationId, orderId, expires);
            authorizationId = await RenewAuthorizationAsync(payment, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(authorizationId, payment.Amount, payment.CurrencyCode,
                payment.InvoiceId!, $"{payment.IdempotencyToken}-capture", cancellationToken);
        }
        catch (PayPalApiException ex) when (IsRenewable(ex))
        {
            _logger.LogWarning("Capture of authorization {0} for order {1} failed as stale ({2}); attempting renewal.",
                authorizationId, orderId, ex.Message);
            var renewedAuthorizationId = await RenewAuthorizationAsync(payment, cancellationToken);
            capture = await _payPal.CaptureAuthorizationAsync(renewedAuthorizationId, payment.Amount, payment.CurrencyCode,
                payment.InvoiceId!, $"{payment.IdempotencyToken}-capture-renewed", cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Captured order {0}: capture {1}, gross {2}, fee {3}, net {4} {5}.",
            orderId, capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount, capture.CurrencyCode);
        return payment;
    }

    public async Task<Payment> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Voided)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {orderId} cannot be cancelled because it is not awaiting fulfilment (status: {payment.Status}).");
        }

        await _payPal.VoidAuthorizationAsync(payment.AuthorizationId!, $"{payment.IdempotencyToken}-void", cancellationToken);

        payment.MarkVoided();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Cancelled order {0}: voided authorization {1}, held funds released.", orderId, payment.AuthorizationId);
        return payment;
    }

    public async Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, cancellationToken);

        // Repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation("Refund for order {0} under key {1} already exists ({2}); returning it.",
                orderId, idempotencyKey, existing.PayPalRefundId);
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableRemaining();
        payment.EnsureRefundable(refundAmount); // throws before any money moves if invalid

        // Namespace the caller's key with the payment token so the PayPal-Request-Id is globally
        // unique (two different orders can legitimately reuse the same caller key), while local
        // de-duplication above still keys on the caller's key within this payment.
        var payPalRequestId = $"{payment.IdempotencyToken}-refund-{idempotencyKey}";
        var result = await _payPal.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.CurrencyCode,
            payment.InvoiceId!, payPalRequestId, cancellationToken);

        var refund = payment.AddRefund(idempotencyKey, result.RefundId, result.Amount, result.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Refunded {0} {1} on order {2}: refund {3}, payment status now {4}.",
            result.Amount, result.CurrencyCode, orderId, result.RefundId, payment.Status);
        return refund;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderWithPayment(o, paymentsByOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    // ----- helpers -----

    private async Task<string> RenewAuthorizationAsync(Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode,
                $"{payment.IdempotencyToken}-reauth", cancellationToken);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation("Renewed authorization for order {0}: new authorization {1}.", payment.OrderId, reauth.AuthorizationId);
            return reauth.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                $"The authorization for order {payment.OrderId} has expired and can no longer be renewed. " +
                "Ask the shopper to pay for the order again.", ex);
        }
    }

    private static bool IsRenewable(PayPalApiException ex) => RenewableCaptureIssues.Any(ex.HasIssue);

    private async Task<(CardDetails? card, string? vaultId)> ResolveInstrumentAsync(string buyerId, PaymentInstruction instruction, CancellationToken cancellationToken)
    {
        if (instruction is null || (instruction.Card is null && instruction.SavedCardId is null))
        {
            throw new PaymentException("A payment must supply either card details or a saved card id.");
        }
        if (instruction.Card is not null && instruction.SavedCardId is not null)
        {
            throw new PaymentException("Supply either card details or a saved card id, not both.");
        }

        if (instruction.SavedCardId is int savedCardId)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdForBuyerSpecification(savedCardId, buyerId), cancellationToken);
            if (saved is null)
            {
                throw new PaymentException($"Saved card {savedCardId} was not found.");
            }
            return (null, saved.PayPalVaultId);
        }

        return (instruction.Card, null);
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        return payment ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<Payment> LoadOwnedPaymentAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal that the order exists for another shopper.
            throw new OrderNotFoundException(orderId);
        }
        return payment;
    }
}
