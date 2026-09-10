using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly ISavedCardService _savedCards;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IReadRepository<CatalogItem> catalogRepository,
        ISavedCardService savedCards,
        IPaymentGateway gateway,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _catalogRepository = catalogRepository;
        _savedCards = savedCards;
        _gateway = gateway;
        _uriComposer = uriComposer;
    }

    public async Task<PlacedOrder> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines,
        Address shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentValidationException("Every order line must have a quantity of at least 1.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var items = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, ct);

        var payment = new OrderPayment(order.Id, buyerId, _gateway.Currency, order.Total());
        payment.SetInvoiceReference($"eshop-{order.Id}-{Guid.NewGuid():N}".Substring(0, 40));
        await _paymentRepository.AddAsync(payment, ct);

        return new PlacedOrder(order.Id, payment.Amount, payment.Currency);
    }

    public async Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken ct)
    {
        var payment = await GetOwnedPaymentAsync(orderId, buyerId, ct);

        // Idempotent in effect: a double-click never authorizes twice.
        if (payment.IsAuthorized || payment.IsFulfilled)
            return await BuildViewAsync(payment, ct);
        if (payment.Status == PaymentStatus.Cancelled)
            throw new PaymentConflictException($"Order {orderId} has been cancelled and cannot be paid.");

        var hasCard = card is not null;
        var hasSaved = savedPaymentMethodId is not null;
        if (hasCard == hasSaved)
            throw new PaymentValidationException("Provide either one-off card details or a saved payment method id, not both or neither.");

        string? vaultId = null;
        if (hasSaved)
        {
            vaultId = await _savedCards.GetVaultIdForCallerAsync(buyerId, savedPaymentMethodId!.Value, ct)
                ?? throw new PaymentEntityNotFoundException($"Saved payment method {savedPaymentMethodId} was not found for this shopper.");
        }

        var invoiceReference = payment.InvoiceReference ?? $"eshop-{orderId}";

        try
        {
            var auth = await _gateway.AuthorizeAsync(payment.Amount, invoiceReference, card, vaultId,
                $"pay-{invoiceReference}", ct);
            payment.MarkAuthorized(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            return await BuildViewAsync(payment, ct);
        }
        catch (PaymentChallengeRequiredException)
        {
            throw; // stop-and-report; leave the order awaiting payment.
        }
        catch (PaymentGatewayException)
        {
            payment.MarkAuthorizationFailed();
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }
    }

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);

        if (payment.IsFulfilled)
            return await BuildViewAsync(payment, ct); // idempotent: already captured.
        if (!payment.IsAuthorized || payment.AuthorizationId is null)
            throw new PaymentConflictException($"Order {orderId} is not authorized, so it cannot be fulfilled.");

        var keyBase = payment.InvoiceReference ?? $"eshop-{orderId}";
        var invoiceReference = keyBase;
        var authorizationId = payment.AuthorizationId;
        var renewed = false;

        // Renew a stale hold rather than failing the fulfilment outright.
        if (IsStale(payment.AuthorizationExpiresAt))
        {
            authorizationId = await RenewOrThrowAsync(payment, keyBase, ct);
            renewed = true;
        }

        PaymentCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, invoiceReference, $"capture-{keyBase}", ct);
        }
        catch (PaymentGatewayException ex) when (!renewed && LooksLikeExpiredAuthorization(ex))
        {
            authorizationId = await RenewOrThrowAsync(payment, keyBase, ct);
            capture = await _gateway.CaptureAsync(authorizationId, invoiceReference, $"capture-{keyBase}", ct);
        }

        payment.MarkFulfilled(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        return await BuildViewAsync(payment, ct);
    }

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
            return await BuildViewAsync(payment, ct); // idempotent.
        if (payment.IsFulfilled)
            throw new PaymentConflictException($"Order {orderId} is already fulfilled; use a refund, not a cancel.");

        if (payment.IsAuthorized && payment.AuthorizationId is not null)
        {
            await _gateway.VoidAsync(payment.AuthorizationId, $"cancel-{payment.InvoiceReference ?? orderId.ToString()}", ct);
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);
        return await BuildViewAsync(payment, ct);
    }

    public async Task<(OrderPaymentView Payment, RefundView Refund)> RefundAsync(string buyerId, int orderId,
        decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await GetOwnedPaymentAsync(orderId, buyerId, ct);

        if (!payment.IsFulfilled || payment.CaptureId is null)
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");

        // Repeat under the same idempotency key => return the prior refund, never refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
            return (await BuildViewAsync(payment, ct), new RefundView(existing.PayPalRefundId, existing.Amount, existing.Status));

        var refundAmount = amount ?? payment.RefundableRemaining;
        if (refundAmount <= 0m)
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        if (refundAmount > payment.RefundableRemaining)
            throw new PaymentValidationException(
                $"Refund of {refundAmount.ToString(CultureInfo.InvariantCulture)} exceeds the refundable remaining " +
                $"{payment.RefundableRemaining.ToString(CultureInfo.InvariantCulture)} for order {orderId}.");

        // Send an explicit amount for a partial refund or when a prior refund exists; null (full) only on
        // the very first, whole-capture refund.
        var gatewayAmount = (amount is null && payment.RefundedAmount == 0m) ? (decimal?)null : refundAmount;

        var result = await _gateway.RefundAsync(payment.CaptureId, gatewayAmount, idempotencyKey, ct);

        var refund = new PaymentRefund(idempotencyKey, refundAmount, result.RefundId, result.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, ct);

        return (await BuildViewAsync(payment, ct), new RefundView(result.RefundId, refundAmount, result.Status));
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), ct);
        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), ct);
        var orderDates = orders.ToDictionary(o => o.Id, o => o.OrderDate);

        return payments
            .OrderByDescending(p => orderDates.TryGetValue(p.OrderId, out var d) ? d : DateTimeOffset.MinValue)
            .Select(p => ToView(p, orderDates.TryGetValue(p.OrderId, out var d) ? d : default))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            throw new PaymentValidationException("Reconciliation 'to' must be on or after 'from'.");

        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var payments = await _paymentRepository.ListAsync(ct);

        var paymentsByReference = payments
            .Where(p => !string.IsNullOrEmpty(p.InvoiceReference))
            .GroupBy(p => p.InvoiceReference!)
            .ToDictionary(g => g.Key, g => g.First());

        var entries = new List<ReconciliationEntry>();
        var matchedReferences = new HashSet<string>();

        foreach (var txn in transactions)
        {
            OrderPayment? matched = null;
            if (!string.IsNullOrEmpty(txn.InvoiceId) && paymentsByReference.TryGetValue(txn.InvoiceId!, out var p))
            {
                matched = p;
                matchedReferences.Add(txn.InvoiceId!);
            }

            entries.Add(new ReconciliationEntry
            {
                Match = matched is null ? ReconciliationMatch.PayPalOnly : ReconciliationMatch.Matched,
                InvoiceReference = txn.InvoiceId,
                PayPalTransactionId = txn.TransactionId,
                PayPalReferenceId = txn.PayPalReferenceId,
                PayPalAmount = txn.Amount,
                PayPalStatus = txn.Status,
                EShopOrderId = matched?.OrderId,
                EShopCapturedAmount = matched?.CapturedAmount,
                EShopStatus = matched?.Status.ToString(),
            });
        }

        // eShop payments that were captured but for which PayPal reports nothing in this range.
        foreach (var payment in payments.Where(p => p.CaptureId is not null
            && !string.IsNullOrEmpty(p.InvoiceReference)
            && !matchedReferences.Contains(p.InvoiceReference!)))
        {
            entries.Add(new ReconciliationEntry
            {
                Match = ReconciliationMatch.EShopOnly,
                InvoiceReference = payment.InvoiceReference,
                EShopOrderId = payment.OrderId,
                EShopCapturedAmount = payment.CapturedAmount,
                EShopStatus = payment.Status.ToString(),
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactionCount = transactions.Count,
            MatchedCount = entries.Count(e => e.Match == ReconciliationMatch.Matched),
            PayPalOnlyCount = entries.Count(e => e.Match == ReconciliationMatch.PayPalOnly),
            EShopOnlyCount = entries.Count(e => e.Match == ReconciliationMatch.EShopOnly),
            Entries = entries,
        };
    }

    // ---- Helpers -------------------------------------------------------------------------------------

    private async Task<OrderPayment> GetPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), ct);
        return payment ?? throw new PaymentEntityNotFoundException($"Order {orderId} was not found.");
    }

    private async Task<OrderPayment> GetOwnedPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await GetPaymentAsync(orderId, ct);
        // Not-found vs not-owned are indistinguishable to the caller, so one shopper cannot probe another's.
        if (payment.BuyerId != buyerId)
            throw new PaymentEntityNotFoundException($"Order {orderId} was not found.");
        return payment;
    }

    private async Task<string> RenewOrThrowAsync(OrderPayment payment, string keyBase, CancellationToken ct)
    {
        try
        {
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                $"reauth-{keyBase}", ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            return reauth.AuthorizationId;
        }
        catch (PaymentGatewayException ex)
        {
            throw new AuthorizationNotRenewableException(
                $"The payment hold for order {payment.OrderId} has expired and could not be renewed ({ex.Message}). " +
                "Ask the shopper to pay the order again to place a fresh authorization before fulfilling.", ex);
        }
    }

    private async Task<OrderPaymentView> BuildViewAsync(OrderPayment payment, CancellationToken ct)
    {
        var order = await _orderRepository.GetByIdAsync(payment.OrderId, ct);
        return ToView(payment, order?.OrderDate ?? default);
    }

    private static OrderPaymentView ToView(OrderPayment p, DateTimeOffset orderDate) => new()
    {
        OrderId = p.OrderId,
        OrderDate = orderDate,
        Status = p.Status.ToString(),
        Currency = p.Currency,
        Amount = p.Amount,
        PayPalOrderId = p.PayPalOrderId,
        AuthorizationId = p.AuthorizationId,
        AuthorizationStatus = p.AuthorizationStatus,
        AuthorizationExpiresAt = p.AuthorizationExpiresAt,
        CaptureId = p.CaptureId,
        CaptureStatus = p.CaptureStatus,
        CapturedAmount = p.CapturedAmount,
        PayPalFee = p.PayPalFee,
        NetAmount = p.NetAmount,
        RefundedAmount = p.RefundedAmount,
        RefundableRemaining = p.RefundableRemaining,
        Refunds = p.Refunds.Select(r => new RefundView(r.PayPalRefundId, r.Amount, r.Status)).ToList(),
    };

    private static bool IsStale(string? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(expiresAt)) return false;
        return DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry)
            && expiry <= DateTimeOffset.UtcNow.AddMinutes(1);
    }

    private static bool LooksLikeExpiredAuthorization(PaymentGatewayException ex)
    {
        if (ex.StatusCode is 422 or 404) return true;
        var text = $"{ex.ProviderErrorName} {ex.Message}";
        return text.IndexOf("EXPIR", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
