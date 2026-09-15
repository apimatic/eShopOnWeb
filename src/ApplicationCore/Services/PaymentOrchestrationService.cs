using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentOrchestrationService : IPaymentOrchestrationService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentService _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentOrchestrationService> _logger;
    private readonly string _currency;

    public PaymentOrchestrationService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalPaymentService payPal,
        IUriComposer uriComposer,
        IOptions<PayPalSettings> settings,
        IAppLogger<PaymentOrchestrationService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
        _currency = string.IsNullOrWhiteSpace(settings.Value.Currency) ? "USD" : settings.Value.Currency!.Trim();
    }

    // ---------------------------------------------------------------- Place order

    public async Task<PaymentResult<PlacedOrderResult>> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineCommand> lines, ShippingAddressCommand? shipTo, CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
        {
            return PaymentResult<PlacedOrderResult>.Invalid("At least one order line is required.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            return PaymentResult<PlacedOrderResult>.Invalid("Every order line must have a quantity of at least 1.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            return PaymentResult<PlacedOrderResult>.Invalid($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri))
            {
                pictureUri = "eCatalog-item-default.png";
            }
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = BuildShippingAddress(shipTo);
        var order = new Order(buyerId, address, items);
        order = await _orderRepository.AddAsync(order, ct);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _currency);
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation("Placed order {0} for {1} totalling {2} {3}.", order.Id, buyerId, order.Total(), _currency);
        return PaymentResult<PlacedOrderResult>.Ok(
            new PlacedOrderResult(order.Id, payment.Status.ToString(), order.Total(), _currency));
    }

    // ---------------------------------------------------------------- Authorize (pay)

    public async Task<PaymentResult<OrderPaymentView>> AuthorizeAsync(string buyerId, int orderId, PayCommand command, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null || !OwnedBy(order.BuyerId, buyerId))
        {
            return PaymentResult<OrderPaymentView>.NotFound("Order not found.");
        }

        var payment = await GetOrCreatePaymentAsync(order, buyerId, ct);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return PaymentResult<OrderPaymentView>.Ok(ToView(payment));
        }
        if (payment.Status == PaymentStatus.Cancelled)
        {
            return PaymentResult<OrderPaymentView>.Conflict("This order was cancelled and can no longer be paid.");
        }

        var (card, cardError) = await BuildCardAsync(buyerId, command, ct);
        if (card is null)
        {
            return PaymentResult<OrderPaymentView>.Invalid(cardError!);
        }

        // Persist the stable idempotency key BEFORE the call, so a retry reuses it and PayPal de-duplicates.
        var requestId = payment.EnsureAuthorizationRequestId(() => Guid.NewGuid().ToString("N"));
        await _paymentRepository.UpdateAsync(payment, ct);

        try
        {
            var result = await _payPal.AuthorizeAsync(payment.Amount, card, requestId, ct);

            if (!string.IsNullOrEmpty(result.PayPalOrderId))
            {
                payment.SetPayPalOrderId(result.PayPalOrderId!);
            }

            if (result.RequiresApproval || !result.HasUsableAuthorization)
            {
                payment.MarkRequiresApproval();
                await _paymentRepository.UpdateAsync(payment, ct);
                return PaymentResult<OrderPaymentView>.RequiresApproval(
                    result.ApprovalDetail ?? "This card requires shopper approval in a browser; payment was stopped.");
            }

            payment.MarkAuthorized(result.AuthorizationId!, result.AuthorizationStatus ?? "CREATED", result.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Authorized order {0}: authorization {1}.", orderId, result.AuthorizationId);
            return PaymentResult<OrderPaymentView>.Ok(ToView(payment));
        }
        catch (PayPalException ex)
        {
            if (ex.IsTransient)
            {
                return PaymentResult<OrderPaymentView>.ProviderUnavailable(ex.Message);
            }
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, ct);
            return PaymentResult<OrderPaymentView>.Invalid(ex.Message);
        }
    }

    // ---------------------------------------------------------------- Fulfil (capture)

    public async Task<PaymentResult<OrderPaymentView>> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            return PaymentResult<OrderPaymentView>.NotFound("Order not found.");
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            // Already captured — idempotent.
            return PaymentResult<OrderPaymentView>.Ok(ToView(payment));
        }
        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            return PaymentResult<OrderPaymentView>.Conflict("The order is not authorized, so it cannot be fulfilled.");
        }

        var captureRequestId = payment.EnsureCaptureRequestId(() => Guid.NewGuid().ToString("N"));
        await _paymentRepository.UpdateAsync(payment, ct);

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(payment.AuthorizationId!, captureRequestId, ct);
        }
        catch (PayPalException ex) when (ex.IsTransient)
        {
            return PaymentResult<OrderPaymentView>.ProviderUnavailable(ex.Message);
        }
        catch (PayPalException)
        {
            // The capture failed on a business rule — most commonly a stale authorization. Renew it and retry
            // rather than failing the fulfilment outright.
            var renewed = await TryReauthorizeAsync(payment, ct);
            if (!renewed.IsSuccess)
            {
                return renewed;
            }

            try
            {
                capture = await _payPal.CaptureAsync(payment.AuthorizationId!, Guid.NewGuid().ToString("N"), ct);
            }
            catch (PayPalException ex) when (ex.IsTransient)
            {
                return PaymentResult<OrderPaymentView>.ProviderUnavailable(ex.Message);
            }
            catch (PayPalException ex)
            {
                return PaymentResult<OrderPaymentView>.Conflict(ex.Message);
            }
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Fulfilled order {0}: captured {1} {2} (fee {3}, net {4}).",
            orderId, capture.CapturedAmount, _currency, capture.PayPalFee, capture.NetAmount);
        return PaymentResult<OrderPaymentView>.Ok(ToView(payment));
    }

    private async Task<PaymentResult<OrderPaymentView>> TryReauthorizeAsync(OrderPayment payment, CancellationToken ct)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, Guid.NewGuid().ToString("N"), ct);
            if (!reauth.HasUsableAuthorization)
            {
                return PaymentResult<OrderPaymentView>.Conflict(
                    "The authorization can no longer be renewed. Ask the shopper to place and pay for a new order.");
            }
            payment.UpdateAuthorization(reauth.AuthorizationId!, reauth.AuthorizationStatus ?? "CREATED", reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation("Renewed authorization for order {0}: new authorization {1}.", payment.OrderId, reauth.AuthorizationId);
            return PaymentResult<OrderPaymentView>.Ok(ToView(payment));
        }
        catch (PayPalException ex) when (ex.IsTransient)
        {
            return PaymentResult<OrderPaymentView>.ProviderUnavailable(ex.Message);
        }
        catch (PayPalException)
        {
            return PaymentResult<OrderPaymentView>.Conflict(
                "The authorization can no longer be renewed. Ask the shopper to place and pay for a new order.");
        }
    }

    // ---------------------------------------------------------------- Cancel (void)

    public async Task<PaymentResult<OrderPaymentView>> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            return PaymentResult<OrderPaymentView>.NotFound("Order not found.");
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return PaymentResult<OrderPaymentView>.Ok(ToView(payment));
        }
        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            return PaymentResult<OrderPaymentView>.Conflict("Only an authorized order can be cancelled before fulfilment.");
        }

        try
        {
            await _payPal.VoidAsync(payment.AuthorizationId!, Guid.NewGuid().ToString("N"), ct);
        }
        catch (PayPalException ex) when (ex.IsTransient)
        {
            return PaymentResult<OrderPaymentView>.ProviderUnavailable(ex.Message);
        }
        catch (PayPalException ex)
        {
            return PaymentResult<OrderPaymentView>.Conflict(ex.Message);
        }

        payment.MarkCancelled("VOIDED");
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Cancelled order {0}: authorization {1} voided.", orderId, payment.AuthorizationId);
        return PaymentResult<OrderPaymentView>.Ok(ToView(payment));
    }

    // ---------------------------------------------------------------- Refund

    public async Task<PaymentResult<RefundResultView>> RefundAsync(string buyerId, bool isAdmin, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return PaymentResult<RefundResultView>.Invalid("An idempotency key is required for a refund.");
        }
        if (amount.HasValue && amount.Value <= 0m)
        {
            return PaymentResult<RefundResultView>.Invalid("Refund amount must be greater than zero.");
        }

        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || (!isAdmin && !OwnedBy(order.BuyerId, buyerId)))
        {
            return PaymentResult<RefundResultView>.NotFound("Order not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            return PaymentResult<RefundResultView>.NotFound("Order not found.");
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded)
            || string.IsNullOrEmpty(payment.CaptureId))
        {
            return PaymentResult<RefundResultView>.Conflict("The order has not been captured, so there is nothing to refund.");
        }

        // Idempotency: the same caller-supplied key never refunds twice.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return PaymentResult<RefundResultView>.Ok(new RefundResultView(
                existing.Id, existing.PayPalRefundId, existing.Amount, existing.Status,
                payment.TotalRefunded(), payment.Status.ToString()));
        }

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
        {
            return PaymentResult<RefundResultView>.Conflict("This capture has already been fully refunded.");
        }

        var refundAmount = amount ?? remaining;
        if (refundAmount > remaining)
        {
            return PaymentResult<RefundResultView>.Invalid(
                $"Refund of {refundAmount:0.00} exceeds the {remaining:0.00} {_currency} still available to refund.");
        }

        PayPalRefundResult refundResult;
        try
        {
            // The caller's key controls dedup; the request id sent to PayPal must also be globally unique, so
            // derive it deterministically from the (unique) capture id plus the caller's key. Same key → same
            // id (PayPal de-duplicates a crash-retry); distinct keys → distinct refunds.
            var payPalRequestId = DeterministicRequestId(payment.CaptureId!, idempotencyKey);
            refundResult = await _payPal.RefundAsync(payment.CaptureId!, refundAmount, payPalRequestId, ct);
        }
        catch (PayPalException ex) when (ex.IsTransient)
        {
            return PaymentResult<RefundResultView>.ProviderUnavailable(ex.Message);
        }
        catch (PayPalException ex)
        {
            return PaymentResult<RefundResultView>.Conflict(ex.Message);
        }

        var refund = new OrderRefund(idempotencyKey, refundAmount, _currency, refundResult.RefundId, refundResult.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Refunded {0} {1} on order {2} (refund {3}).", refundAmount, _currency, orderId, refundResult.RefundId);

        return PaymentResult<RefundResultView>.Ok(new RefundResultView(
            refund.Id, refund.PayPalRefundId, refund.Amount, refund.Status,
            payment.TotalRefunded(), payment.Status.ToString()));
    }

    // ---------------------------------------------------------------- My orders

    public async Task<PaymentResult<IReadOnlyList<OrderSummaryView>>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        if (orders.Count == 0)
        {
            return PaymentResult<IReadOnlyList<OrderSummaryView>>.Ok(Array.Empty<OrderSummaryView>());
        }

        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByOrderIdsSpecification(orderIds), ct);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        var summaries = orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order =>
            {
                paymentsByOrder.TryGetValue(order.Id, out var payment);
                var view = payment is null ? null : ToView(payment);
                var status = payment?.Status.ToString() ?? PaymentStatus.AwaitingPayment.ToString();
                return new OrderSummaryView(order.Id, order.OrderDate, order.Total(), status, view);
            })
            .ToList();

        return PaymentResult<IReadOnlyList<OrderSummaryView>>.Ok(summaries);
    }

    // ---------------------------------------------------------------- Reconciliation

    public async Task<PaymentResult<ReconciliationReport>> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
        {
            return PaymentResult<ReconciliationReport>.Invalid("'to' must not be earlier than 'from'.");
        }

        IReadOnlyList<PayPalTransactionRecord> transactions;
        try
        {
            transactions = await _payPal.SearchTransactionsAsync(from, to, ct);
        }
        catch (PayPalException ex) when (ex.IsTransient)
        {
            return PaymentResult<ReconciliationReport>.ProviderUnavailable(ex.Message);
        }
        catch (PayPalException ex)
        {
            return PaymentResult<ReconciliationReport>.Invalid(ex.Message);
        }

        var payments = await _paymentRepository.ListAsync(ct);
        // Map every PayPal id we hold (order/authorization/capture) back to the owning eShop payment.
        var byPayPalId = new Dictionary<string, OrderPayment>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            foreach (var id in new[] { payment.PayPalOrderId, payment.AuthorizationId, payment.CaptureId })
            {
                if (!string.IsNullOrEmpty(id))
                {
                    byPayPalId[id!] = payment;
                }
            }
        }

        var matched = new List<ReconciliationMatch>();
        var inPayPalNotEShop = new List<ReconciliationEntry>();
        var matchedPaymentIds = new HashSet<int>();
        var payPalTxnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in transactions)
        {
            if (!string.IsNullOrEmpty(txn.TransactionId))
            {
                payPalTxnIds.Add(txn.TransactionId!);
            }

            if (!string.IsNullOrEmpty(txn.TransactionId) && byPayPalId.TryGetValue(txn.TransactionId!, out var payment))
            {
                matched.Add(new ReconciliationMatch(txn.TransactionId!, payment.OrderId, txn.Amount, payment.Amount, txn.Status, payment.Status.ToString()));
                matchedPaymentIds.Add(payment.Id);
            }
            else
            {
                inPayPalNotEShop.Add(new ReconciliationEntry(txn.TransactionId, null, txn.Amount, txn.Status,
                    "PayPal has this transaction but no matching eShop order was found."));
            }
        }

        // eShop payments that reached PayPal (have a capture) but are not in PayPal's report for this range —
        // note the reporting lag, which can legitimately leave a just-created payment absent.
        var inEShopNotPayPal = payments
            .Where(p => !string.IsNullOrEmpty(p.CaptureId) && !matchedPaymentIds.Contains(p.Id)
                        && !ContainsAnyId(payPalTxnIds, p))
            .Select(p => new ReconciliationEntry(p.CaptureId, p.OrderId, p.CapturedAmount ?? p.Amount, p.CaptureStatus,
                "eShop captured this payment but PayPal's report for the range does not list it (may be reporting lag)."))
            .ToList();

        var report = new ReconciliationReport(from, to, transactions.Count, payments.Count, matched, inPayPalNotEShop, inEShopNotPayPal);
        return PaymentResult<ReconciliationReport>.Ok(report);
    }

    private static bool ContainsAnyId(HashSet<string> txnIds, OrderPayment payment) =>
        (payment.CaptureId is not null && txnIds.Contains(payment.CaptureId))
        || (payment.AuthorizationId is not null && txnIds.Contains(payment.AuthorizationId))
        || (payment.PayPalOrderId is not null && txnIds.Contains(payment.PayPalOrderId));

    // ---------------------------------------------------------------- Saved cards

    public async Task<PaymentResult<SavedCardView>> SaveCardAsync(string buyerId, CardCommand card, CancellationToken ct)
    {
        var validation = ValidateRawCard(card);
        if (validation is not null)
        {
            return PaymentResult<SavedCardView>.Invalid(validation);
        }

        var payPalCard = ToPayPalCard(card);
        PayPalVaultResult vault;
        try
        {
            vault = await _payPal.VaultCardAsync(payPalCard, Guid.NewGuid().ToString("N"), ct);
        }
        catch (PayPalException ex) when (ex.IsTransient)
        {
            return PaymentResult<SavedCardView>.ProviderUnavailable(ex.Message);
        }
        catch (PayPalException ex)
        {
            return PaymentResult<SavedCardView>.Invalid(ex.Message);
        }

        var method = new PaymentMethod(buyerId, vault.VaultId, vault.CardBrand ?? "CARD",
            vault.LastFourDigits, vault.Expiry ?? string.Empty, vault.CardholderName);
        method = await _paymentMethodRepository.AddAsync(method, ct);
        _logger.LogInformation("Saved card {0} ({1} ****{2}) for {3}.", method.Id, method.CardBrand, method.LastFourDigits, buyerId);

        return PaymentResult<SavedCardView>.Ok(ToCardView(method));
    }

    public async Task<PaymentResult<IReadOnlyList<SavedCardView>>> GetSavedCardsAsync(string buyerId, CancellationToken ct)
    {
        var methods = await _paymentMethodRepository.ListAsync(new PaymentMethodsByBuyerIdSpecification(buyerId), ct);
        IReadOnlyList<SavedCardView> views = methods
            .OrderByDescending(m => m.CreatedAt)
            .Select(ToCardView)
            .ToList();
        return PaymentResult<IReadOnlyList<SavedCardView>>.Ok(views);
    }

    public async Task<PaymentResult<bool>> DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var method = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, ct);
        if (method is null || !OwnedBy(method.BuyerId, buyerId))
        {
            return PaymentResult<bool>.NotFound("Saved card not found.");
        }

        try
        {
            await _payPal.DeleteVaultedCardAsync(method.VaultId, ct);
        }
        catch (PayPalException ex) when (ex.IsTransient)
        {
            // Do not remove it locally while PayPal is unreachable — the shopper can retry.
            return PaymentResult<bool>.ProviderUnavailable(ex.Message);
        }
        catch (PayPalException ex)
        {
            // A business error here (e.g. the token is already gone) still means it should leave the app.
            _logger.LogWarning("PayPal reported '{0}' deleting card {1}; removing it locally anyway.", ex.Message, paymentMethodId);
        }

        await _paymentMethodRepository.DeleteAsync(method, ct);
        _logger.LogInformation("Removed saved card {0} for {1}.", paymentMethodId, buyerId);
        return PaymentResult<bool>.Ok(true);
    }

    // ---------------------------------------------------------------- Helpers

    private async Task<OrderPayment> GetOrCreatePaymentAsync(Order order, string buyerId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(order.Id), ct);
        if (payment is not null)
        {
            return payment;
        }
        payment = new OrderPayment(order.Id, buyerId, order.Total(), _currency);
        return await _paymentRepository.AddAsync(payment, ct);
    }

    private async Task<(PayPalCard? card, string? error)> BuildCardAsync(string buyerId, PayCommand command, CancellationToken ct)
    {
        if (command.PaymentMethodId.HasValue)
        {
            var method = await _paymentMethodRepository.GetByIdAsync(command.PaymentMethodId.Value, ct);
            if (method is null || !OwnedBy(method.BuyerId, buyerId))
            {
                return (null, "The saved card was not found.");
            }
            return (new PayPalCard { VaultId = method.VaultId }, null);
        }

        if (command.Card is not null)
        {
            var validation = ValidateRawCard(command.Card);
            if (validation is not null)
            {
                return (null, validation);
            }
            return (ToPayPalCard(command.Card), null);
        }

        return (null, "Provide either a saved card id or card details to pay with.");
    }

    private static string? ValidateRawCard(CardCommand card)
    {
        if (string.IsNullOrWhiteSpace(card.Number))
        {
            return "Card number is required.";
        }
        if (string.IsNullOrWhiteSpace(card.Expiry))
        {
            return "Card expiry is required.";
        }
        if (string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            return "Card security code is required.";
        }
        return null;
    }

    private static PayPalCard ToPayPalCard(CardCommand card) => new()
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = card.BillingAddress is null
            ? null
            : new PayPalBillingAddress
            {
                AddressLine1 = card.BillingAddress.AddressLine1,
                AddressLine2 = card.BillingAddress.AddressLine2,
                AdminArea1 = card.BillingAddress.AdminArea1,
                AdminArea2 = card.BillingAddress.AdminArea2,
                PostalCode = card.BillingAddress.PostalCode,
                CountryCode = card.BillingAddress.CountryCode
            }
    };

    private static Address BuildShippingAddress(ShippingAddressCommand? shipTo)
    {
        return new Address(
            street: string.IsNullOrWhiteSpace(shipTo?.Street) ? "N/A" : shipTo!.Street!,
            city: string.IsNullOrWhiteSpace(shipTo?.City) ? "N/A" : shipTo!.City!,
            state: shipTo?.State ?? "N/A",
            country: string.IsNullOrWhiteSpace(shipTo?.Country) ? "N/A" : shipTo!.Country!,
            zipcode: string.IsNullOrWhiteSpace(shipTo?.ZipCode) ? "00000" : shipTo!.ZipCode!);
    }

    private static bool OwnedBy(string ownerId, string callerId) =>
        string.Equals(ownerId, callerId, StringComparison.Ordinal);

    /// <summary>
    /// Builds a stable, globally-unique idempotency id for PayPal from a scope (e.g. the capture id) and a
    /// caller-supplied key. Deterministic, so the same inputs always yield the same id.
    /// </summary>
    private static string DeterministicRequestId(string scope, string key)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes($"{scope}:{key}"));
        return new Guid(bytes).ToString();
    }

    private static OrderPaymentView ToView(OrderPayment p) => new(
        p.OrderId,
        p.Status.ToString(),
        p.Amount,
        p.Currency,
        p.PayPalOrderId,
        p.AuthorizationId,
        p.AuthorizationStatus,
        p.CaptureId,
        p.CaptureStatus,
        p.CapturedAmount,
        p.PayPalFee,
        p.NetAmount,
        p.TotalRefunded(),
        p.Refunds.Select(r => new RefundLineView(r.Id, r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt)).ToList());

    private static SavedCardView ToCardView(PaymentMethod m) =>
        new(m.Id, m.CardBrand, m.LastFourDigits, m.Expiry, m.CardholderName, m.CreatedAt);
}
