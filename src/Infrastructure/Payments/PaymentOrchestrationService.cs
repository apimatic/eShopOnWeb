using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Implements the payment flows. Persists the local <see cref="OrderPayment"/> record before any PayPal
/// money movement, gates every transition on the actual current state (so a repeat is a safe no-op), and
/// keeps all shopper actions scoped to the caller's own data.
/// </summary>
public class PaymentOrchestrationService : IPaymentOrchestrationService
{
    // A default shipping address — checkout address is out of scope for this API (mirrors the Web sample).
    private static readonly Address DefaultShipToAddress = new("123 Main St.", "Kent", "OH", "United States", "44240");
    private const string DefaultPictureUri = "eCatalog-item-default.png";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IPaymentConfiguration _config;
    private readonly ILogger<PaymentOrchestrationService> _logger;

    public PaymentOrchestrationService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IPayPalPaymentGateway gateway,
        IPaymentConfiguration config,
        ILogger<PaymentOrchestrationService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _catalogRepository = catalogRepository;
        _gateway = gateway;
        _config = config;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentOperationException(PaymentOperationErrorKind.Validation, "At least one order line is required.");

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                throw new PaymentOperationException(PaymentOperationErrorKind.Validation, $"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");

            var catalogItem = await _catalogRepository.GetByIdAsync(line.CatalogItemId, ct);
            if (catalogItem is null)
                throw new PaymentOperationException(PaymentOperationErrorKind.Validation, $"Catalog item {line.CatalogItemId} does not exist.");

            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? DefaultPictureUri : catalogItem.PictureUri;
            var snapshot = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            items.Add(new OrderItem(snapshot, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, DefaultShipToAddress, items);
        order = await _orderRepository.AddAsync(order, ct);

        var total = order.Total();
        if (total <= 0m)
            throw new PaymentOperationException(PaymentOperationErrorKind.Validation, "Order total must be greater than zero.");

        // Unique per order (in-memory ids restart each run), stamped onto the PayPal purchase unit for
        // reconciliation. Well under PayPal's 127-char invoice_id limit.
        var invoiceReference = $"ESHOP-{order.Id}-{Guid.NewGuid():N}";
        var payment = new OrderPayment(order.Id, buyerId, _config.CurrencyCode, total, invoiceReference);
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation("Placed order {OrderId} for {BuyerId}, total {Total} {Currency}", order.Id, buyerId, total, _config.CurrencyCode);
        return new PlaceOrderResult { OrderId = order.Id, Total = total, CurrencyCode = _config.CurrencyCode };
    }

    public async Task<PaymentView> PayAsync(string buyerId, int orderId, PayInput input, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payment = await GetOwnedPaymentAsync(orderId, buyerId, ct);

        // Idempotent: a repeat after the hold is already placed returns the existing state, no second authorize.
        if (payment.Status == PaymentStatus.Authorized)
            return ToView(payment);
        if (payment.Status != PaymentStatus.PendingPayment)
            throw new PaymentOperationException(PaymentOperationErrorKind.Conflict,
                $"Order {orderId} cannot be paid because its payment status is {payment.Status}.");

        // Resolve the funding source: exactly one of saved card or one-off card.
        var hasSaved = input.SavedPaymentMethodId is not null;
        var hasCard = input.Card is not null;
        if (hasSaved == hasCard)
            throw new PaymentOperationException(PaymentOperationErrorKind.Validation,
                "Provide either a saved paymentMethodId or one-off card details (exactly one).");

        string? vaultId = null;
        PayPalCardDetails? card = null;
        if (hasSaved)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(input.SavedPaymentMethodId!.Value, buyerId), ct);
            if (saved is null)
                throw new PaymentOperationException(PaymentOperationErrorKind.NotFound,
                    $"Saved payment method {input.SavedPaymentMethodId} was not found for this shopper.");
            vaultId = saved.PayPalVaultId;
        }
        else
        {
            card = MapCard(input.Card!);
        }

        var command = new PayPalAuthorizeCommand
        {
            Amount = payment.Amount,
            CurrencyCode = payment.CurrencyCode,
            InvoiceReference = payment.InvoiceReference,
            Description = $"eShopOnWeb order {orderId}",
            // Idempotency key stable per order but unique across runs (InvoiceReference carries a fresh GUID),
            // so it never collides with PayPal's 45-day-retained request-ids from an earlier in-memory run.
            IdempotencyKey = payment.InvoiceReference,
            Card = card,
            VaultId = vaultId,
        };

        var result = await _gateway.AuthorizeAsync(command, ct);
        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.AuthorizedAt, result.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {OrderId} authorized (hold placed) for {BuyerId}", orderId, buyerId);
        return ToView(payment);
    }

    public async Task<PaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await GetAnyPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Captured || payment.Status == PaymentStatus.PartiallyRefunded || payment.Status == PaymentStatus.Refunded)
            return ToView(payment); // already fulfilled — no second capture

        if (payment.Status != PaymentStatus.Authorized)
            throw new PaymentOperationException(PaymentOperationErrorKind.Conflict,
                $"Order {orderId} cannot be fulfilled because its payment status is {payment.Status}.");

        var authorizationId = payment.AuthorizationId
            ?? throw new PaymentOperationException(PaymentOperationErrorKind.Conflict, $"Order {orderId} has no authorization to capture.");

        // Proactively renew a hold that has already expired.
        if (payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            authorizationId = await RenewOrThrowAsync(payment, orderId, ct);
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, $"{payment.InvoiceReference}-cap", ct);
        }
        catch (PaymentGatewayException ex) when (IsStaleAuthorization(ex))
        {
            _logger.LogWarning("Capture for order {OrderId} hit a stale authorization; attempting renewal", orderId);
            authorizationId = await RenewOrThrowAsync(payment, orderId, ct);
            capture = await _gateway.CaptureAsync(authorizationId, $"{payment.InvoiceReference}-cap-renewed", ct);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.CapturedAt,
            capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {OrderId} fulfilled — captured {Gross}, fee {Fee}, net {Net}",
            orderId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        return ToView(payment);
    }

    public async Task<PaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await GetAnyPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
            return ToView(payment); // already released

        if (payment.Status != PaymentStatus.Authorized)
            throw new PaymentOperationException(PaymentOperationErrorKind.Conflict,
                $"Order {orderId} cannot be cancelled because its payment status is {payment.Status}. " +
                (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
                    ? "Use a refund to return captured funds."
                    : string.Empty));

        var authorizationId = payment.AuthorizationId
            ?? throw new PaymentOperationException(PaymentOperationErrorKind.Conflict, $"Order {orderId} has no authorization to void.");

        await _gateway.VoidAsync(authorizationId, $"{payment.InvoiceReference}-void", ct);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation("Order {OrderId} cancelled — held funds released", orderId);
        return ToView(payment);
    }

    public async Task<RefundResult> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PaymentOperationException(PaymentOperationErrorKind.Validation, "An idempotency key is required for a refund.");

        var payment = await GetOwnedPaymentAsync(orderId, buyerId, ct);

        // Idempotent replay: same key → the existing refund, no second PayPal refund.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return new RefundResult
            {
                RefundId = existing.PayPalRefundId ?? existing.Id.ToString(),
                Amount = existing.Amount,
                Status = existing.PayPalStatus,
                Payment = ToView(payment),
            };

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            throw new PaymentOperationException(PaymentOperationErrorKind.Conflict,
                $"Order {orderId} cannot be refunded because its payment status is {payment.Status}.");

        if (payment.CaptureId is null)
            throw new PaymentOperationException(PaymentOperationErrorKind.Conflict, $"Order {orderId} has no capture to refund.");

        var remaining = payment.RemainingRefundable;
        if (remaining <= 0m)
            throw new PaymentOperationException(PaymentOperationErrorKind.Conflict, $"Order {orderId} has nothing left to refund.");

        if (amount is { } requested)
        {
            if (requested <= 0m)
                throw new PaymentOperationException(PaymentOperationErrorKind.Validation, "Refund amount must be greater than zero.");
            if (requested > remaining)
                throw new PaymentOperationException(PaymentOperationErrorKind.Validation,
                    $"Refund amount {requested} exceeds the remaining refundable amount {remaining}.");
        }

        // Only send a null (full) refund to PayPal when nothing has been refunded yet; otherwise refund the
        // explicit amount so a partly-refunded capture never over-refunds.
        var isFullFresh = amount is null && payment.TotalRefunded == 0m;
        var amountToRefund = amount ?? remaining;
        decimal? payPalAmount = isFullFresh ? null : amountToRefund;

        // Compose the PayPal request-id so distinct captures never collide even if a caller reuses a key,
        // and it stays unique across in-memory runs (InvoiceReference carries a fresh GUID). Local dedup
        // remains keyed by the raw caller key per capture.
        var payPalRequestId = $"{payment.InvoiceReference}-refund-{idempotencyKey}";
        var refundResult = await _gateway.RefundAsync(payment.CaptureId, payPalAmount, payment.CurrencyCode, payPalRequestId, ct);

        var recordedAmount = refundResult.Amount ?? amountToRefund;
        payment.AddRefund(idempotencyKey, recordedAmount, refundResult.RefundId, refundResult.Status);

        try
        {
            await _paymentRepository.UpdateAsync(payment, ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent request under the same key won the unique (OrderPaymentId, IdempotencyKey) claim;
            // reload and return that refund rather than double-refunding.
            var reloaded = await GetOwnedPaymentAsync(orderId, buyerId, ct);
            var winner = reloaded.FindRefundByKey(idempotencyKey);
            if (winner is not null)
                return new RefundResult
                {
                    RefundId = winner.PayPalRefundId ?? winner.Id.ToString(),
                    Amount = winner.Amount,
                    Status = winner.PayPalStatus,
                    Payment = ToView(reloaded),
                };
            throw;
        }

        _logger.LogInformation("Order {OrderId} refunded {Amount} ({RefundId})", orderId, recordedAmount, refundResult.RefundId);
        return new RefundResult
        {
            RefundId = refundResult.RefundId,
            Amount = recordedAmount,
            Status = refundResult.Status,
            Payment = ToView(payment),
        };
    }

    public async Task<IReadOnlyList<PaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), ct);
        return payments.Select(ToView).ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            throw new PaymentOperationException(PaymentOperationErrorKind.Validation, "'to' must be on or after 'from'.");

        var sweep = await _gateway.SearchTransactionsAsync(from, to, ct);
        var localPayments = await _paymentRepository.ListAsync(new OrderPaymentsInPayPalWindowSpecification(from, to), ct);

        // Index eShop payments by the reference we stamped onto the PayPal purchase unit.
        var byReference = localPayments
            .GroupBy(p => p.InvoiceReference)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var lines = new List<ReconciliationLine>();
        var matchedReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var txn in sweep.Transactions)
        {
            var reference = !string.IsNullOrEmpty(txn.InvoiceId) ? txn.InvoiceId : txn.CustomField;
            OrderPayment? local = null;
            if (!string.IsNullOrEmpty(reference))
                byReference.TryGetValue(reference!, out local);

            if (local is not null)
            {
                matchedReferences.Add(local.InvoiceReference);
                lines.Add(new ReconciliationLine
                {
                    Match = ReconciliationMatch.Matched,
                    InvoiceReference = local.InvoiceReference,
                    OrderId = local.OrderId,
                    PayPalTransactionId = txn.TransactionId,
                    PayPalStatus = txn.Status,
                    PayPalAmount = txn.Amount,
                    EShopAmount = local.CapturedAmount > 0 ? local.CapturedAmount : local.Amount,
                    EShopStatus = local.Status.ToString(),
                    TransactionDate = txn.InitiationDate,
                });
            }
            else
            {
                lines.Add(new ReconciliationLine
                {
                    Match = ReconciliationMatch.PayPalOnly,
                    InvoiceReference = reference,
                    PayPalTransactionId = txn.TransactionId,
                    PayPalStatus = txn.Status,
                    PayPalAmount = txn.Amount,
                    TransactionDate = txn.InitiationDate,
                });
            }
        }

        // eShop payments PayPal's report does not show (common in sandbox due to reporting lag).
        foreach (var local in localPayments)
        {
            if (matchedReferences.Contains(local.InvoiceReference)) continue;
            lines.Add(new ReconciliationLine
            {
                Match = ReconciliationMatch.EShopOnly,
                InvoiceReference = local.InvoiceReference,
                OrderId = local.OrderId,
                EShopAmount = local.CapturedAmount > 0 ? local.CapturedAmount : local.Amount,
                EShopStatus = local.Status.ToString(),
                TransactionDate = local.CapturedAt ?? local.AuthorizedAt,
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Complete = sweep.Complete,
            WindowsScanned = sweep.WindowsScanned,
            PagesScanned = sweep.PagesScanned,
            MatchedCount = lines.Count(l => l.Match == ReconciliationMatch.Matched),
            PayPalOnlyCount = lines.Count(l => l.Match == ReconciliationMatch.PayPalOnly),
            EShopOnlyCount = lines.Count(l => l.Match == ReconciliationMatch.EShopOnly),
            Lines = lines,
        };
    }

    // --- helpers ---

    private async Task<string> RenewOrThrowAsync(OrderPayment payment, int orderId, CancellationToken ct)
    {
        try
        {
            var state = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, $"{payment.InvoiceReference}-reauth", ct);
            payment.RenewAuthorization(state.AuthorizationId, state.Status, state.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            return state.AuthorizationId;
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentGatewayException(
                $"The payment hold for order {orderId} has expired and could not be renewed. " +
                "Ask the shopper to place and pay for the order again.",
                ex, kind: PaymentGatewayFailureKind.AuthorizationUnrenewable, debugId: ex.DebugId);
        }
    }

    private static bool IsStaleAuthorization(PaymentGatewayException ex)
    {
        // PayPal reports a lapsed hold as issue AUTHORIZATION_EXPIRED (and related EXPIRED codes).
        var token = (ex.ErrorName ?? string.Empty).ToUpperInvariant();
        return token.Contains("EXPIRED");
    }

    private async Task<OrderPayment> GetOwnedPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId, buyerId), ct);
        return payment ?? throw new PaymentOperationException(PaymentOperationErrorKind.NotFound, $"Order {orderId} was not found for this shopper.");
    }

    private async Task<OrderPayment> GetAnyPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdAnyOwnerSpecification(orderId), ct);
        return payment ?? throw new PaymentOperationException(PaymentOperationErrorKind.NotFound, $"Order {orderId} was not found.");
    }

    private static PayPalCardDetails MapCard(CardInput input) => new()
    {
        Number = input.Number,
        Expiry = input.Expiry,
        SecurityCode = input.SecurityCode,
        CardholderName = input.CardholderName,
        BillingAddress = (input.BillingAddressLine1 ?? input.BillingCity ?? input.BillingPostalCode ?? input.BillingCountryCode) is null
            ? null
            : new PayPalBillingAddress
            {
                AddressLine1 = input.BillingAddressLine1,
                AdminArea2 = input.BillingCity,
                AdminArea1 = input.BillingState,
                PostalCode = input.BillingPostalCode,
                CountryCode = input.BillingCountryCode,
            },
    };

    private static PaymentView ToView(OrderPayment p) => new()
    {
        OrderId = p.OrderId,
        Status = p.Status.ToString(),
        Amount = p.Amount,
        CurrencyCode = p.CurrencyCode,
        OrderDate = p.CreatedAt,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedGross = p.CapturedGross,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        TotalRefunded = p.TotalRefunded,
        RemainingRefundable = p.RemainingRefundable,
        FailureReason = p.FailureReason,
        Refunds = p.Refunds.Select(r => new RefundView
        {
            RefundId = r.PayPalRefundId,
            Amount = r.Amount,
            Status = r.PayPalStatus,
            CreatedAt = r.CreatedAt,
        }).ToList(),
    };
}
