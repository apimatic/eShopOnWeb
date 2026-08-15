using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private const string AuthStatusHeld = "CREATED";
    private static readonly string[] CaptureAccepted = { "COMPLETED", "PENDING" };
    private static readonly string[] RefundAccepted = { "COMPLETED", "PENDING" };

    // PayPal remembers a PayPal-Request-Id globally for a long window, but the in-memory database
    // resets order ids to 1 on every restart. Without a per-run salt, run 2's "auth-order-1" would
    // return run 1's cached PayPal response. This process-unique token keeps idempotency keys stable
    // for retries WITHIN a run (so a double-click still dedupes) while never colliding across runs.
    private static readonly string RunToken = Guid.NewGuid().ToString("N").Substring(0, 12);
    private static string Rid(string logical) => $"{RunToken}-{logical}";

    private readonly IRepository<Order> _orders;
    private readonly IRepository<Payment> _payments;
    private readonly IReadRepository<CatalogItem> _catalogItems;
    private readonly IReadRepository<PaymentMethod> _paymentMethods;
    private readonly IPayPalGateway _paypal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orders,
        IRepository<Payment> payments,
        IReadRepository<CatalogItem> catalogItems,
        IReadRepository<PaymentMethod> paymentMethods,
        IPayPalGateway paypal,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orders = orders;
        _payments = payments;
        _catalogItems = catalogItems;
        _paymentMethods = paymentMethods;
        _paypal = paypal;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput? shippingAddress, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw PaymentApiException.BadRequest("An order must contain at least one line.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw PaymentApiException.BadRequest("Every order line quantity must be greater than zero.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw PaymentApiException.BadRequest($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri)
                ? "eCatalog-item-default.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shippingAddress is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State,
                shippingAddress.Country, shippingAddress.ZipCode);

        var order = new Order(buyerId, address, items);
        order = await _orders.AddAsync(order, ct);

        _logger.LogInformation("Order {0} placed by {1}, total {2} {3}", order.Id, buyerId, order.Total(), _settings.Currency);
        return order.Id;
    }

    public async Task<OrderPaymentView> AuthorizeAsync(string buyerId, int orderId, AuthorizeInstruction instruction,
        CancellationToken ct = default)
    {
        var order = await LoadOwnOrderAsync(buyerId, orderId, ct);
        var payment = await _payments.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);

        // Idempotent in effect: a repeat once the hold exists returns the current state, never a second hold.
        if (payment is not null && payment.Status is not (PaymentStatus.Pending or PaymentStatus.Failed))
        {
            return PaymentViewMapper.ToView(order, payment);
        }

        var amount = order.Total();
        var currency = _settings.Currency;
        var requestId = Rid($"auth-order-{orderId}");

        AuthorizationResult result;
        string? instrumentDescription;

        if (instruction.Card is not null && instruction.SavedPaymentMethodId is not null)
        {
            throw PaymentApiException.BadRequest("Provide either card details or a saved card id, not both.");
        }
        else if (instruction.Card is not null)
        {
            result = await _paypal.AuthorizeWithCardAsync(amount, currency, instruction.Card, requestId, ct);
            instrumentDescription = result.InstrumentDescription;
        }
        else if (instruction.SavedPaymentMethodId is int savedId)
        {
            var saved = await _paymentMethods.FirstOrDefaultAsync(
                new PaymentMethodByIdForBuyerSpecification(savedId, buyerId), ct);
            if (saved is null)
            {
                throw PaymentApiException.NotFound($"Saved card {savedId} was not found for this shopper.");
            }
            result = await _paypal.AuthorizeWithVaultedCardAsync(amount, currency, saved.VaultTokenId, requestId, ct);
            instrumentDescription = result.InstrumentDescription ?? saved.Display;
        }
        else
        {
            throw PaymentApiException.BadRequest("A payment requires either card details or a saved card id.");
        }

        if (!string.Equals(result.Status, AuthStatusHeld, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApiException(402,
                $"The card was not approved (authorization status: {result.Status}). No hold was placed.");
        }

        if (payment is null)
        {
            payment = new Payment(orderId, buyerId, amount, currency);
            payment.SetAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt, instrumentDescription);
            await _payments.AddAsync(payment, ct);
        }
        else
        {
            payment.SetAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt, instrumentDescription);
            await _payments.UpdateAsync(payment, ct);
        }

        order.MarkAuthorized();
        await _orders.UpdateAsync(order, ct);

        _logger.LogInformation("Order {0} authorized: paypalOrder={1} auth={2} amount={3} {4}",
            orderId, result.PayPalOrderId, result.AuthorizationId, amount, currency);

        return PaymentViewMapper.ToView(order, payment);
    }

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
                    ?? throw PaymentApiException.NotFound($"Order {orderId} was not found.");
        var payment = await _payments.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);

        if (order.Status == OrderStatus.Fulfilled && payment is { Status: PaymentStatus.Captured })
        {
            return PaymentViewMapper.ToView(order, payment); // idempotent
        }
        if (payment?.AuthorizationId is null || payment.Status != PaymentStatus.Authorized)
        {
            throw PaymentApiException.Conflict($"Order {orderId} has no authorized payment to capture.");
        }

        var capture = await CaptureWithRenewalAsync(order, payment, ct);

        if (!CaptureAccepted.Contains(capture.Status, StringComparer.OrdinalIgnoreCase))
        {
            throw new PaymentApiException(402, $"Capture was not completed (status: {capture.Status}).");
        }

        payment.SetCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await _payments.UpdateAsync(payment, ct);

        order.MarkFulfilled();
        await _orders.UpdateAsync(order, ct);

        _logger.LogInformation("Order {0} fulfilled: capture={1} gross={2} fee={3} net={4} {5}",
            orderId, capture.CaptureId, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount, capture.CurrencyCode);

        return PaymentViewMapper.ToView(order, payment);
    }

    private async Task<CaptureResult> CaptureWithRenewalAsync(Order order, Payment payment, CancellationToken ct)
    {
        // Renew proactively when the hold has passed its honor period, then capture.
        if (payment.AuthorizationExpiresAt is { } exp && exp <= DateTimeOffset.UtcNow)
        {
            await ReauthorizeAsync(order.Id, payment, ct);
        }

        try
        {
            return await _paypal.CaptureAsync(payment.AuthorizationId!, Rid($"capture-order-{order.Id}"), ct);
        }
        catch (PayPalException ex) when (LooksLikeExpiredAuthorization(ex))
        {
            _logger.LogWarning("Order {0} capture found a stale authorization ({1}); renewing and retrying.", order.Id, ex.Issue ?? ex.Message);
            await ReauthorizeAsync(order.Id, payment, ct);
            return await _paypal.CaptureAsync(payment.AuthorizationId!, Rid($"capture-order-{order.Id}-r"), ct);
        }
    }

    private async Task ReauthorizeAsync(int orderId, Payment payment, CancellationToken ct)
    {
        try
        {
            var reauth = await _paypal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode,
                Rid($"reauth-{payment.AuthorizationId}"), ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _payments.UpdateAsync(payment, ct);
            _logger.LogInformation("Order {0} authorization renewed: new auth={1}", orderId, reauth.AuthorizationId);
        }
        catch (PayPalException ex)
        {
            throw new PaymentApiException(409,
                $"The payment hold for order {orderId} has expired and can no longer be renewed ({ex.Message}). " +
                "A new authorization is required — ask the shopper to pay for the order again.", ex.Issue, ex);
        }
    }

    private static bool LooksLikeExpiredAuthorization(PayPalException ex)
    {
        var text = $"{ex.Issue} {ex.Message}";
        return text.IndexOf("EXPIR", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
                    ?? throw PaymentApiException.NotFound($"Order {orderId} was not found.");
        var payment = await _payments.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            return PaymentViewMapper.ToView(order, payment); // idempotent
        }

        order.MarkCancelled(); // guards: only before fulfilment

        if (payment is { Status: PaymentStatus.Authorized, AuthorizationId: not null })
        {
            await _paypal.VoidAsync(payment.AuthorizationId, Rid($"void-order-{orderId}"), ct);
            payment.MarkVoided();
            await _payments.UpdateAsync(payment, ct);
            _logger.LogInformation("Order {0} cancelled; hold {1} released.", orderId, payment.AuthorizationId);
        }
        else
        {
            _logger.LogInformation("Order {0} cancelled; no hold to release.", orderId);
        }

        await _orders.UpdateAsync(order, ct);
        return PaymentViewMapper.ToView(order, payment);
    }

    public async Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw PaymentApiException.BadRequest("A refund requires an idempotency key.");
        }

        var order = await LoadOwnOrderAsync(buyerId, orderId, ct);
        var payment = await _payments.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);

        if (payment?.CaptureId is null)
        {
            throw PaymentApiException.Conflict($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotent: the same key returns the refund it already produced, never a second refund.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            return new RefundOutcome(existing.Id, existing.RefundId, PaymentViewMapper.ToView(order, payment));
        }

        var resolved = amount ?? payment.RefundableRemaining;
        if (resolved <= 0m)
        {
            throw PaymentApiException.BadRequest("Refund amount must be greater than zero.");
        }
        if (resolved > payment.RefundableRemaining)
        {
            throw PaymentApiException.Conflict(
                $"Refund of {resolved:0.00} exceeds the refundable balance of {payment.RefundableRemaining:0.00} {payment.CurrencyCode}.");
        }

        var result = await _paypal.RefundAsync(payment.CaptureId, resolved, payment.CurrencyCode, Rid(idempotencyKey), ct);

        if (!RefundAccepted.Contains(result.Status, StringComparer.OrdinalIgnoreCase))
        {
            throw new PaymentApiException(402, $"Refund was not accepted (status: {result.Status}).");
        }

        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _payments.UpdateAsync(payment, ct);

        order.MarkRefundState(payment.RefundableRemaining <= 0m);
        await _orders.UpdateAsync(order, ct);

        _logger.LogInformation("Order {0} refunded {1} {2}: refund={3} remaining={4}",
            orderId, result.Amount, payment.CurrencyCode, result.RefundId, payment.RefundableRemaining);

        return new RefundOutcome(refund.Id, refund.RefundId, PaymentViewMapper.ToView(order, payment));
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _payments.ListAsync(new PaymentsByBuyerSpecification(buyerId), ct);
        var paymentByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => PaymentViewMapper.ToView(o, paymentByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<OrderPaymentView> GetOrderAsync(string buyerId, int orderId, CancellationToken ct = default)
    {
        var order = await LoadOwnOrderAsync(buyerId, orderId, ct);
        var payment = await _payments.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        return PaymentViewMapper.ToView(order, payment);
    }

    private async Task<Order> LoadOwnOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        // Not found and not-owned are answered identically so one shopper cannot probe another's orders.
        if (order is null || order.BuyerId != buyerId)
        {
            throw PaymentApiException.NotFound($"Order {orderId} was not found.");
        }
        return order;
    }
}
