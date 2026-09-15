using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the additive payment lifecycle (place → authorize → fulfil/cancel → refund) over the
/// existing order model and the PayPal gateway. Each step is idempotent in effect: it inspects the
/// stored PayPal state before acting, so a double-click never authorizes or captures twice.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShipToAddress =
        new("123 Main St", "Redmond", "WA", "USA", "98052");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedCard> _savedCardReadRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalPaymentService _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedCard> savedCardReadRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalPaymentService payPal,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardReadRepository = savedCardReadRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        ShippingAddressInput? shipTo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines == null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }

        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least 1.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo == null
            ? DefaultShipToAddress
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);
        return order.Id;
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, PaymentInstrument instrument,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId, buyerId), cancellationToken);
        if (order == null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        // Idempotency: a second pay for an already-authorized order returns the existing hold.
        if (order.Status == OrderStatus.Authorized && order.Payment != null)
        {
            return order;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException(
                $"Order {orderId} is not awaiting payment (status: {order.Status}); it cannot be authorized.");
        }

        // Resolve the instrument: exactly one of saved card / one-off card.
        SavedCard? savedCard = null;
        PayPalCard? card = null;
        string? vaultId = null;
        var storeInVault = false;

        if (instrument.SavedCardId.HasValue)
        {
            savedCard = await _savedCardReadRepository.FirstOrDefaultAsync(
                new SavedCardByIdForBuyerSpecification(instrument.SavedCardId.Value, buyerId), cancellationToken);
            if (savedCard == null)
            {
                throw new EntityNotFoundException($"Saved card {instrument.SavedCardId} was not found.");
            }
            vaultId = savedCard.VaultId;
        }
        else if (instrument.Card != null)
        {
            card = ToPayPalCard(instrument.Card);
            storeInVault = instrument.SaveCard;
        }
        else
        {
            throw new PaymentException("A payment must supply either card details or a saved card id.");
        }

        var reconciliationId = $"ESHOP-{orderId}-{Guid.NewGuid():N}";
        var payment = new Payment(orderId, order.Total(), _payPal.Currency, reconciliationId);

        var request = new PayPalAuthorizeRequest
        {
            Amount = order.Total(),
            Currency = _payPal.Currency,
            ReconciliationId = reconciliationId,
            RequestId = NewRequestId(),
            Card = card,
            VaultId = vaultId,
            StoreInVault = storeInVault
        };

        var result = await _payPal.AuthorizeAsync(request, cancellationToken);

        if (result.RequiresApproval)
        {
            throw new PaymentException(
                "This card requires additional buyer approval in a browser (3-D Secure), which this " +
                "integration does not support. Ask the shopper to pay with a different card.");
        }

        if (string.IsNullOrEmpty(result.AuthorizationId))
        {
            throw new PayPalApiException(
                $"PayPal did not return an authorization for order {orderId} (order status: {result.OrderStatus}).");
        }

        payment.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.AuthorizationStatus ?? "CREATED",
            result.ExpiresAt, result.CardBrand ?? savedCard?.Brand, result.CardLast4 ?? savedCard?.Last4,
            savedCard?.Id);

        order.SetAuthorized(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        // If the shopper asked to save the one-off card, persist the vaulted card for later reuse.
        if (storeInVault && !string.IsNullOrEmpty(result.VaultId) && instrument.Card != null)
        {
            var toSave = new SavedCard(buyerId, result.VaultId!, result.VaultCustomerId,
                result.CardBrand ?? "CARD", result.CardLast4 ?? "0000",
                NormalizeExpiry(instrument.Card.Expiry), instrument.Card.Name);
            await _savedCardRepository.AddAsync(toSave, cancellationToken);
        }

        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        // Idempotency: fulfilling an already-fulfilled order is a no-op.
        if (order.Status == OrderStatus.Fulfilled)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || order.Payment == null)
        {
            throw new PaymentException(
                $"Order {orderId} is not authorized (status: {order.Status}); it cannot be fulfilled.");
        }

        var payment = order.Payment;
        if (payment.IsCaptured)
        {
            order.SetFulfilled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }

        var capture = await CaptureWithRenewalAsync(order, payment, cancellationToken);
        payment.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.Fee, capture.Net);
        order.SetFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        // Idempotency: cancelling an already-cancelled order is a no-op.
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status != OrderStatus.Authorized || order.Payment == null)
        {
            throw new PaymentException(
                $"Order {orderId} cannot be cancelled from status {order.Status}. Only an authorized, " +
                "not-yet-fulfilled order can be cancelled; a fulfilled order must be refunded instead.");
        }

        var payment = order.Payment;
        try
        {
            await _payPal.VoidAsync(payment.AuthorizationId!, cancellationToken);
        }
        catch (PayPalApiException ex) when (IsAlreadyCaptured(ex))
        {
            throw new PaymentException(
                $"Order {orderId} cannot be cancelled because the payment has already been captured. " +
                "Issue a refund instead.", ex);
        }

        payment.RecordVoid();
        order.SetCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId, buyerId), cancellationToken);
        if (order == null)
        {
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }

        var payment = order.Payment;
        if (payment == null || !payment.IsCaptured)
        {
            throw new PaymentException(
                $"Order {orderId} has no captured payment to refund (status: {order.Status}).");
        }

        // Idempotency: repeating a refund under the same key does not refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing != null)
        {
            return order;
        }

        var refundable = payment.RemainingRefundable();
        if (refundable <= 0m)
        {
            throw new PaymentException($"Order {orderId} has already been fully refunded.");
        }

        decimal amountToRefund;
        if (amount.HasValue)
        {
            if (amount.Value <= 0m)
            {
                throw new PaymentException("A refund amount must be greater than zero.");
            }
            if (amount.Value > refundable)
            {
                throw new PaymentException(
                    $"Refund amount {Money(amount.Value)} exceeds the refundable balance {Money(refundable)} " +
                    $"for order {orderId}.");
            }
            amountToRefund = amount.Value;
        }
        else
        {
            amountToRefund = refundable;
        }

        // App-level dedup (above) already guarantees the same key never refunds twice. The PayPal-Request-Id
        // is a second layer against a crash between PayPal succeeding and us saving: derive it deterministically
        // from capture + key so a retry replays, while two different captures never collide on PayPal's side.
        var payPalRequestId = DeterministicRequestId(payment.CaptureId!, idempotencyKey);
        var result = await _payPal.RefundAsync(payment.CaptureId!, amountToRefund, _payPal.Currency,
            payPalRequestId, cancellationToken);

        var refund = new PaymentRefund(idempotencyKey, result.RefundId, result.Amount, result.Currency, result.Status);
        payment.AddRefund(refund);
        order.SetRefundState();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    /// <summary>
    /// Capture the hold, renewing it first if it has gone stale. A hold that can no longer be renewed
    /// is reported in terms an operator can act on rather than failing the fulfilment opaquely.
    /// </summary>
    private async Task<PayPalCaptureResult> CaptureWithRenewalAsync(Order order, Payment payment,
        CancellationToken cancellationToken)
    {
        var info = await _payPal.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);

        if (string.Equals(info.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(
                $"Order {order.Id} cannot be fulfilled because its authorization was voided.");
        }

        if (IsStale(info))
        {
            await RenewAuthorizationAsync(order, payment, cancellationToken);
        }

        try
        {
            return await _payPal.CaptureAsync(payment.AuthorizationId!, NewRequestId(), cancellationToken);
        }
        catch (PayPalApiException ex) when (IndicatesStaleAuthorization(ex))
        {
            // The hold expired between the status read and the capture — renew once and retry.
            await RenewAuthorizationAsync(order, payment, cancellationToken);
            return await _payPal.CaptureAsync(payment.AuthorizationId!, NewRequestId(), cancellationToken);
        }
    }

    private async Task RenewAuthorizationAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        _logger.LogInformation($"Renewing stale authorization {payment.AuthorizationId} for order {order.Id}.");
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                payment.Currency, NewRequestId(), cancellationToken);
            if (string.IsNullOrEmpty(renewed.AuthorizationId))
            {
                throw new PayPalApiException("Reauthorization did not return a new authorization id.");
            }
            payment.RecordReauthorization(renewed.AuthorizationId!, renewed.AuthorizationStatus ?? "CREATED",
                renewed.ExpiresAt);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                $"The authorization for order {order.Id} has expired and can no longer be renewed " +
                "(PayPal allows renewal only within 29 days of the original hold). Ask the shopper to " +
                "place and pay for a new order.", ex);
        }
    }

    private static bool IsStale(PayPalAuthorizationInfo info) =>
        string.Equals(info.Status, "EXPIRED", StringComparison.OrdinalIgnoreCase);

    private static bool IndicatesStaleAuthorization(PayPalApiException ex)
    {
        if (ex.Issue == null)
        {
            return false;
        }
        return ex.Issue is "AUTHORIZATION_EXPIRED" or "INVALID_RESOURCE_ID"
            or "AUTH_CAPTURE_NOT_ALLOWED" or "AUTHORIZATION_VOIDED_OR_EXPIRED";
    }

    private static bool IsAlreadyCaptured(PayPalApiException ex) =>
        ex.Issue is "PREVIOUSLY_CAPTURED" or "AUTHORIZATION_ALREADY_CAPTURED";

    private static PayPalCard ToPayPalCard(CardInput card)
    {
        PayPalBillingAddress? billing = null;
        if (card.BillingAddress != null)
        {
            var b = card.BillingAddress;
            billing = new PayPalBillingAddress(b.Line1, b.Line2, b.City, b.State, b.PostalCode, b.CountryCode);
        }
        return new PayPalCard(card.Number, NormalizeExpiry(card.Expiry), card.SecurityCode, card.Name, billing);
    }

    /// <summary>Accept "YYYY-MM" or "MM/YY" / "MM/YYYY" and return PayPal's "YYYY-MM".</summary>
    private static string NormalizeExpiry(string expiry)
    {
        var value = (expiry ?? string.Empty).Trim();
        if (value.Length == 7 && value[4] == '-')
        {
            return value; // already YYYY-MM
        }
        if (value.Contains('/'))
        {
            var parts = value.Split('/');
            if (parts.Length == 2)
            {
                var month = parts[0].PadLeft(2, '0');
                var year = parts[1];
                if (year.Length == 2)
                {
                    year = "20" + year;
                }
                return $"{year}-{month}";
            }
        }
        return value;
    }

    private static string Money(decimal value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static string NewRequestId() => Guid.NewGuid().ToString("N");

    /// <summary>A stable, collision-free PayPal-Request-Id for a (capture, caller-key) pair.</summary>
    private static string DeterministicRequestId(string captureId, string idempotencyKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{captureId}|{idempotencyKey}");
        var hash = System.Security.Cryptography.MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant(); // 32 chars, within PayPal's limit
    }
}
