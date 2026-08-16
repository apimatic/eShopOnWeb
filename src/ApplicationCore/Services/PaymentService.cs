using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using static Microsoft.eShopWeb.ApplicationCore.Services.ServiceResults;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalGateway payPalGateway,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _savedCardRepository = savedCardRepository;
        _payPalGateway = payPalGateway;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.CurrencyCode;

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    // ---- Flow 1: place an order ----

    public async Task<Result<Order>> PlaceOrderAsync(
        string buyerId, IReadOnlyList<OrderLineRequest> lines, Address shipToAddress,
        CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return Invalid<Order>("At least one order line is required.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            return Invalid<Order>("Every order line must have a quantity greater than zero.");
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);

        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return Invalid<Order>($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order.InitializePayment(Currency);

        order = await _orderRepository.AddAsync(order, cancellationToken);

        // A globally-unique invoice id (the in-memory store restarts order ids each run, so the id
        // alone is not unique at PayPal). Fixed for the life of the order so retries stay idempotent.
        order.Payment!.AssignInvoiceId($"ESHOP-ORDER-{order.Id}-{Guid.NewGuid():N}");
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Placed order {0} for buyer {1}, total {2} {3}.", order.Id, buyerId, Money(order.Total()), Currency);

        return Result<Order>.Success(order);
    }

    // ---- Flow 1: authorize (hold the money) ----

    public async Task<Result<Order>> AuthorizeAsync(
        string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return Result<Order>.NotFound();
        }

        // Idempotent: a repeated pay on an already-authorized order returns the current state.
        if (order.IsAuthorized)
        {
            return Result<Order>.Success(order);
        }

        if (!order.IsAwaitingPayment)
        {
            return Invalid<Order>($"Order {orderId} is {order.Status} and cannot be authorized.");
        }

        // Exactly one payment instrument.
        if (card is null && savedPaymentMethodId is null)
        {
            return Invalid<Order>("Provide either card details or a saved payment method id.");
        }
        if (card is not null && savedPaymentMethodId is not null)
        {
            return Invalid<Order>("Provide either card details or a saved payment method id, not both.");
        }

        string? vaultId = null;
        string sourceDescription;

        if (savedPaymentMethodId is not null)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedPaymentMethodId.Value, cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                return Result<Order>.NotFound($"Saved payment method {savedPaymentMethodId} was not found.");
            }
            vaultId = savedCard.PayPalVaultId;
            sourceDescription = DescribeSavedCard(savedCard);
        }
        else
        {
            var validation = ValidateCard(card!);
            if (validation is not null)
            {
                return Invalid<Order>(validation);
            }
            sourceDescription = DescribeCard(card!);
        }

        var payment = order.Payment ?? order.InitializePayment(Currency);
        if (payment.InvoiceId is null)
        {
            payment.AssignInvoiceId($"ESHOP-ORDER-{order.Id}-{Guid.NewGuid():N}");
        }
        var invoiceId = payment.InvoiceId!;

        var command = new CreateAuthorizationCommand(
            ReferenceId: order.Id.ToString(CultureInfo.InvariantCulture),
            InvoiceId: invoiceId,
            Amount: Money(payment.Amount),
            CurrencyCode: Currency,
            Items: BuildLineItems(order),
            Card: card,
            VaultId: vaultId);

        var idempotencyKey = $"eshop-authorize-{invoiceId}";

        PayPalAuthorizationResult authorization;
        try
        {
            authorization = await _payPalGateway.AuthorizeOrderWithCardAsync(command, idempotencyKey, cancellationToken);
        }
        catch (PayPalChallengeRequiredException ex)
        {
            _logger.LogWarning("Order {0} authorization needs browser approval: {1}", order.Id, ex.Message);
            return Result<Order>.Error(
                "The card issuer requires the shopper to approve this payment in a browser (3-D Secure challenge). " +
                "This flow does not support a browser approval round-trip; ask the shopper to use a different card.");
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Order {0} authorization declined by PayPal: {1} (debug id {2}).", order.Id, ex.Message, ex.DebugId ?? "n/a");
            return Result<Order>.Error($"PayPal declined the authorization: {ex.Message}");
        }

        payment.SetAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status, sourceDescription);
        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Authorized order {0}: PayPal order {1}, authorization {2} ({3}).",
            order.Id, authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status);

        return Result<Order>.Success(order);
    }

    // ---- Flow 1: fulfil (capture the money) ----

    public async Task<Result<Order>> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return Result<Order>.NotFound();
        }

        if (order.IsFulfilled)
        {
            return Result<Order>.Success(order); // idempotent
        }

        if (!order.IsAuthorized || order.Payment?.AuthorizationId is null)
        {
            return Invalid<Order>($"Order {orderId} is {order.Status} and cannot be fulfilled.");
        }

        var payment = order.Payment;
        var amount = Money(payment.Amount);
        var captureKey = $"eshop-capture-{payment.InvoiceId ?? order.Id.ToString(CultureInfo.InvariantCulture)}";

        PayPalCaptureResult capture;
        try
        {
            capture = await CaptureWithRenewalAsync(order, payment, amount, captureKey, cancellationToken);
        }
        catch (AuthorizationUnrenewableException ex)
        {
            _logger.LogWarning("Order {0} cannot be fulfilled: {1}", order.Id, ex.Message);
            return Result<Order>.Error(ex.Message);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Order {0} capture failed: {1} (debug id {2}).", order.Id, ex.Message, ex.DebugId ?? "n/a");
            return Result<Order>.Error($"PayPal could not capture the payment: {ex.Message}");
        }

        payment.SetCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Fulfilled order {0}: captured {1} {2}, fee {3}, net {4} (capture {5}).",
            order.Id, Money(capture.GrossAmount), capture.CurrencyCode, Money(capture.PayPalFee), Money(capture.NetAmount), capture.CaptureId);

        return Result<Order>.Success(order);
    }

    /// <summary>
    /// Captures the authorization, renewing it first if PayPal reports it stale/expired. If the hold
    /// can no longer be renewed, throws with a message an operator can act on.
    /// </summary>
    private async Task<PayPalCaptureResult> CaptureWithRenewalAsync(
        Order order, Payment payment, string amount, string captureKey, CancellationToken cancellationToken)
    {
        var authorizationId = payment.AuthorizationId!;

        // Proactively check the hold; renew it if it has gone stale before fulfilment.
        PayPalAuthorizationDetails details;
        try
        {
            details = await _payPalGateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (PayPalApiException)
        {
            details = new PayPalAuthorizationDetails(authorizationId, "UNKNOWN", payment.Amount, payment.CurrencyCode);
        }

        if (IsExpired(details.Status))
        {
            authorizationId = await RenewAuthorizationAsync(order, payment, amount, cancellationToken);
        }
        else if (string.Equals(details.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthorizationUnrenewableException(
                $"The authorization for order {order.Id} was voided and cannot be captured. Collect a new payment from the customer.");
        }

        try
        {
            return await _payPalGateway.CaptureAuthorizationAsync(authorizationId, amount, payment.CurrencyCode, captureKey, cancellationToken);
        }
        catch (PayPalApiException ex) when (IsExpiredIssue(ex.IssueName))
        {
            // The honor period lapsed between our check and the capture — renew once and retry.
            _logger.LogInformation("Order {0} authorization went stale during capture; renewing.", order.Id);
            var renewedId = await RenewAuthorizationAsync(order, payment, amount, cancellationToken);
            return await _payPalGateway.CaptureAuthorizationAsync(renewedId, amount, payment.CurrencyCode, captureKey, cancellationToken);
        }
    }

    private async Task<string> RenewAuthorizationAsync(Order order, Payment payment, string amount, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPalGateway.ReauthorizeAsync(
                payment.AuthorizationId!, amount, payment.CurrencyCode,
                $"eshop-reauth-{payment.InvoiceId ?? order.Id.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status);
            _logger.LogInformation("Renewed authorization for order {0}: new authorization {1}.", order.Id, renewed.AuthorizationId);
            return renewed.AuthorizationId;
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationUnrenewableException(
                $"The authorization for order {order.Id} has expired and can no longer be renewed ({ex.Message}). " +
                "Collect a new payment from the customer before fulfilling.");
        }
    }

    // ---- Flow 1: cancel (release the hold) ----

    public async Task<Result<Order>> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            return Result<Order>.NotFound();
        }

        if (order.IsCancelled)
        {
            return Result<Order>.Success(order); // idempotent
        }

        if (order.IsFulfilled)
        {
            return Invalid<Order>($"Order {orderId} has been fulfilled; issue a refund instead of cancelling.");
        }

        // Release any hold at PayPal.
        if (order.IsAuthorized && order.Payment?.AuthorizationId is not null)
        {
            try
            {
                await _payPalGateway.VoidAuthorizationAsync(
                    order.Payment.AuthorizationId,
                    $"eshop-void-{order.Payment.InvoiceId ?? order.Id.ToString(CultureInfo.InvariantCulture)}", cancellationToken);
                order.Payment.SetVoided();
            }
            catch (PayPalApiException ex)
            {
                _logger.LogWarning("Order {0} void failed: {1} (debug id {2}).", order.Id, ex.Message, ex.DebugId ?? "n/a");
                return Result<Order>.Error($"PayPal could not release the held funds: {ex.Message}");
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation("Cancelled order {0}; any hold has been released.", order.Id);

        return Result<Order>.Success(order);
    }

    // ---- Flow 1: refund (return money after fulfilment) ----

    public async Task<Result<RefundOutcome>> RefundAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Invalid<RefundOutcome>("An idempotency key is required for refunds.");
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            return Result<RefundOutcome>.NotFound();
        }

        var payment = order.Payment;
        if (payment?.CaptureId is null)
        {
            return Invalid<RefundOutcome>($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotent: a repeat under the same key returns the original refund without refunding twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            return Result<RefundOutcome>.Success(new RefundOutcome(order, existing));
        }

        decimal amountToRefund;
        if (amount is not null)
        {
            if (amount <= 0m)
            {
                return Invalid<RefundOutcome>("Refund amount must be greater than zero.");
            }
            if (amount > payment.RefundableRemaining)
            {
                return Invalid<RefundOutcome>(
                    $"Refund of {Money(amount.Value)} exceeds the remaining refundable amount of {Money(payment.RefundableRemaining)}.");
            }
            amountToRefund = amount.Value;
        }
        else
        {
            amountToRefund = payment.RefundableRemaining;
            if (amountToRefund <= 0m)
            {
                return Invalid<RefundOutcome>("Nothing remains to refund on this order.");
            }
        }

        PayPalRefundResult refundResult;
        try
        {
            refundResult = await _payPalGateway.RefundCaptureAsync(
                payment.CaptureId, Money(amountToRefund), payment.CurrencyCode, idempotencyKey, noteToPayer, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            _logger.LogWarning("Order {0} refund failed: {1} (debug id {2}).", order.Id, ex.Message, ex.DebugId ?? "n/a");
            return Result<RefundOutcome>.Error($"PayPal could not process the refund: {ex.Message}");
        }

        var refund = payment.AddRefund(refundResult.RefundId, amountToRefund, refundResult.Status, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Refunded {0} {1} on order {2} (refund {3}); {4} remaining refundable.",
            Money(amountToRefund), payment.CurrencyCode, order.Id, refundResult.RefundId, Money(payment.RefundableRemaining));

        return Result<RefundOutcome>.Success(new RefundOutcome(order, refund));
    }

    // ---- Flow 1: list the caller's orders ----

    public async Task<Result<IReadOnlyList<Order>>> GetOrdersForBuyerAsync(
        string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpec(buyerId), cancellationToken);
        return Result<IReadOnlyList<Order>>.Success(orders);
    }

    // ---- helpers ----

    private IReadOnlyList<PayPalLineItem> BuildLineItems(Order order) =>
        order.OrderItems
            .Select(i => new PayPalLineItem(i.ItemOrdered.ProductName, Money(i.UnitPrice), i.Units))
            .ToList();

    private static string DescribeCard(CardDetails card)
    {
        var digits = new string(card.Number.Where(char.IsDigit).ToArray());
        var last4 = digits.Length >= 4 ? digits[^4..] : digits;
        return $"Card ending {last4}";
    }

    private static string DescribeSavedCard(SavedPaymentMethod card)
    {
        var brand = string.IsNullOrWhiteSpace(card.CardBrand) ? "Card" : card.CardBrand;
        return string.IsNullOrWhiteSpace(card.Last4) ? $"Saved {brand}" : $"Saved {brand} ending {card.Last4}";
    }

    private static string? ValidateCard(CardDetails card)
    {
        var digits = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 12 || digits.Length > 19)
        {
            return "Card number is not valid.";
        }
        if (string.IsNullOrWhiteSpace(card.Expiry) || !IsValidExpiry(card.Expiry))
        {
            return "Card expiry must be in the format YYYY-MM.";
        }
        return null;
    }

    private static bool IsValidExpiry(string expiry)
    {
        var parts = expiry.Split('-');
        return parts.Length == 2
            && int.TryParse(parts[0], out var year) && year is >= 2000 and <= 2100
            && int.TryParse(parts[1], out var month) && month is >= 1 and <= 12;
    }

    private static bool IsExpired(string? status) =>
        string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpiredIssue(string? issue) =>
        issue is not null && issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);

    private sealed class AuthorizationUnrenewableException : Exception
    {
        public AuthorizationUnrenewableException(string message) : base(message) { }
    }
}
