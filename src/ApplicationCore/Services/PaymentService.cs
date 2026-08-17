using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalClient _payPal;
    private readonly IKeyedLock _locks;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly ProcessInstance _instance;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalClient payPal,
        IKeyedLock locks,
        IUriComposer uriComposer,
        PayPalSettings settings,
        ProcessInstance instance,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _locks = locks;
        _uriComposer = uriComposer;
        _settings = settings;
        _instance = instance;
        _logger = logger;
    }

    private static string LockKey(int orderId) => $"order-payment:{orderId}";

    public async Task<PlacedOrder> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
            throw new PaymentException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentException("Every order line must have a quantity of at least 1.");

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), ct);
        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var snapshot = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(snapshot, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        // Amount comes from catalog prices; hold must equal the total to the cent.
        var total = Math.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        var payment = new Payment(order.Id, buyerId, _settings.Currency, total);
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation($"Order {order.Id} placed by {buyerId}; awaiting payment of {total} {_settings.Currency}.");
        return new PlacedOrder(order.Id, total, _settings.Currency);
    }

    public async Task<PaymentView> AuthorizeAsync(int orderId, string buyerId, PayInstruction instruction,
        CancellationToken ct = default)
    {
        using var _ = await _locks.AcquireAsync(LockKey(orderId), ct);

        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new EntityNotFoundException($"Order {orderId} was not found.");

        var payment = await LoadPaymentAsync(orderId, ct);

        // Idempotent in effect: a double-click never authorises twice.
        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
                return PaymentViewMapper.ToView(payment);
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                throw new PaymentException($"Order {orderId} has already been paid and captured.");
            case PaymentStatus.Cancelled:
                throw new PaymentException($"Order {orderId} was cancelled and can no longer be paid.");
        }

        var (card, vaultId) = await ResolveInstrumentAsync(buyerId, instruction, ct);

        var attempt = payment.NextAuthorizationAttempt();
        var requestId = $"eshop-auth-{_instance.Id}-{orderId}-{attempt}";
        var authRequest = new PayPalAuthorizeRequest(payment.Amount, payment.Currency,
            CustomId: PaymentCorrelation.OrderToken(orderId), RequestId: requestId, Card: card, VaultId: vaultId);

        try
        {
            var result = await _payPal.AuthorizeOrderAsync(authRequest, ct);
            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status,
                result.ExpiresAt, result.CardBrand, result.CardLast4);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation($"Order {orderId} authorized (PayPal auth {result.AuthorizationId}, status {result.Status}).");
            return PaymentViewMapper.ToView(payment);
        }
        catch (PaymentException ex)
        {
            payment.MarkFailed(ex.Message);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }
    }

    public async Task<PaymentView> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        using var _ = await _locks.AcquireAsync(LockKey(orderId), ct);

        var payment = await LoadPaymentAsync(orderId, ct);

        // Already captured (in any post-capture state) → fulfilment is done; return current state.
        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            return PaymentViewMapper.ToView(payment);

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentException(
                $"Order {orderId} is '{payment.Status}' and cannot be fulfilled; it must be authorized first.");

        // Renew a stale hold rather than failing the fulfilment outright.
        await EnsureCapturableAuthorizationAsync(orderId, payment, ct);

        var captureRequestId = $"eshop-capture-{_instance.Id}-{payment.AuthorizationId}";
        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId!, payment.Amount,
                payment.Currency, captureRequestId, ct);
        }
        catch (PaymentException ex) when (IsExpiredAuthorization(ex))
        {
            // Race: the hold expired between the status check and the capture — renew once and retry.
            await ReauthorizeAsync(orderId, payment, ct);
            var retryRequestId = $"eshop-capture-{_instance.Id}-{payment.AuthorizationId}";
            capture = await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId!, payment.Amount,
                payment.Currency, retryRequestId, ct);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation(
            $"Order {orderId} fulfilled; captured {capture.GrossAmount} {capture.Currency} " +
            $"(fee {capture.PayPalFee}, net {capture.NetAmount}), capture {capture.CaptureId}.");
        return PaymentViewMapper.ToView(payment);
    }

    public async Task<PaymentView> CancelAsync(int orderId, CancellationToken ct = default)
    {
        using var _ = await _locks.AcquireAsync(LockKey(orderId), ct);

        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
            return PaymentViewMapper.ToView(payment);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            throw new PaymentException(
                $"Order {orderId} has already been fulfilled; use a refund to return money.");

        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            try
            {
                await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, $"eshop-void-{_instance.Id}-{orderId}", ct);
            }
            catch (PaymentException ex)
            {
                // If PayPal already released the hold, treat cancellation as successful.
                _logger.LogWarning($"Voiding authorization for order {orderId} reported: {ex.Message}");
            }
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Order {orderId} cancelled; any held funds released.");
        return PaymentViewMapper.ToView(payment);
    }

    public async Task<(int RefundId, PaymentView Payment)> RefundAsync(int orderId, string callerBuyerId,
        bool callerIsAdmin, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        using var _ = await _locks.AcquireAsync(LockKey(orderId), ct);

        var payment = await LoadPaymentAsync(orderId, ct);

        // Shopper-scoped to the caller's own order; operators (admins) may act on any order.
        if (!callerIsAdmin && !string.Equals(payment.BuyerId, callerBuyerId, StringComparison.Ordinal))
            throw new EntityNotFoundException($"Order {orderId} was not found.");

        if (payment.CaptureId is null)
            throw new PaymentException($"Order {orderId} has no captured payment to refund.");

        // Idempotent: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return (existing.Id, PaymentViewMapper.ToView(payment));

        var remaining = payment.RemainingRefundable();
        decimal refundAmount;
        if (amount is null)
        {
            refundAmount = remaining;
            if (refundAmount <= 0m)
                throw new PaymentException($"Order {orderId} has nothing left to refund.");
        }
        else
        {
            refundAmount = Math.Round(amount.Value, 2, MidpointRounding.AwayFromZero);
            if (refundAmount <= 0m)
                throw new PaymentException("Refund amount must be greater than zero.");
            // Never refundable beyond what was captured.
            if (refundAmount > remaining)
                throw new PaymentException(
                    $"Refund amount {refundAmount} {payment.Currency} exceeds the {remaining} {payment.Currency} " +
                    "still refundable on this capture.");
        }

        var requestId = BuildRefundRequestId(_instance.Id, orderId, idempotencyKey);
        var result = await _payPal.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.Currency,
            requestId, noteToPayer, ct);

        var refund = new PaymentRefund(result.RefundId, result.Amount, result.Currency, result.Status,
            idempotencyKey, noteToPayer);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation(
            $"Order {orderId} refunded {result.Amount} {result.Currency} (PayPal refund {result.RefundId}); " +
            $"status now {payment.Status}.");
        return (refund.Id, PaymentViewMapper.ToView(payment));
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpec(buyerId), ct);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o =>
            {
                var items = o.OrderItems
                    .Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
                    .ToList();
                paymentsByOrder.TryGetValue(o.Id, out var payment);
                return new OrderPaymentView(o.Id, o.OrderDate, o.Total(), items,
                    payment is null ? null : PaymentViewMapper.ToView(payment));
            })
            .ToList();
    }

    // --- helpers ------------------------------------------------------------

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        if (payment is null)
            throw new EntityNotFoundException($"No payment exists for order {orderId}.");
        return payment;
    }

    private async Task<(PayPalCardDetails? card, string? vaultId)> ResolveInstrumentAsync(string buyerId,
        PayInstruction instruction, CancellationToken ct)
    {
        var hasCard = instruction.Card is not null;
        var hasSaved = instruction.SavedPaymentMethodId is not null;

        if (hasCard == hasSaved)
            throw new PaymentException("Provide exactly one of: card details or a savedPaymentMethodId.");

        if (hasSaved)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdForBuyerSpec(instruction.SavedPaymentMethodId!.Value, buyerId), ct);
            if (saved is null)
                throw new EntityNotFoundException($"Saved card {instruction.SavedPaymentMethodId} was not found.");
            return (null, saved.PayPalVaultId);
        }

        return (instruction.Card, null);
    }

    /// <summary>Ensures the hold is capturable, renewing a stale (expired) authorization when possible.</summary>
    private async Task EnsureCapturableAuthorizationAsync(int orderId, Payment payment, CancellationToken ct)
    {
        var status = await _payPal.GetAuthorizationStatusAsync(payment.AuthorizationId!, ct);
        switch (status?.ToUpperInvariant())
        {
            case "EXPIRED":
                await ReauthorizeAsync(orderId, payment, ct);
                break;
            case "VOIDED":
                throw new PaymentException(
                    $"Order {orderId}'s authorization was voided (the order was cancelled); it cannot be fulfilled.");
            case "CAPTURED":
                throw new PaymentException(
                    $"Order {orderId}'s authorization has already been captured at PayPal.");
            default:
                // CREATED / PENDING (or unknown) — proceed and let the capture call be the source of truth.
                break;
        }
    }

    private async Task ReauthorizeAsync(int orderId, Payment payment, CancellationToken ct)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                payment.Currency, $"eshop-reauth-{_instance.Id}-{payment.AuthorizationId}", ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation($"Order {orderId}'s stale hold was renewed as authorization {reauth.AuthorizationId}.");
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                $"Order {orderId}'s authorization has expired and could not be renewed ({ex.Message}). " +
                "Cancel the order and ask the shopper to place and pay for it again.", ex);
        }
    }

    private static bool IsExpiredAuthorization(PaymentException ex) =>
        ex.Message.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase) &&
        ex.Message.Contains("expire", StringComparison.OrdinalIgnoreCase);

    private static string BuildRefundRequestId(string instanceId, int orderId, string idempotencyKey)
    {
        var sb = new StringBuilder("eshop-refund-").Append(instanceId).Append('-').Append(orderId).Append('-');
        foreach (var ch in idempotencyKey)
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-');
        var id = sb.ToString();
        return id.Length <= 108 ? id : id[..108];
    }
}
