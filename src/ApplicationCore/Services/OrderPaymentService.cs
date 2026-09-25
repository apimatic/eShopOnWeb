using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates order placement and the money-movement flows on top of the existing Order/OrderItem model.
/// PayPal contact is delegated to <see cref="IPayPalPaymentGateway"/>; this class owns the state machine,
/// shopper-ownership checks and the local idempotency claims that stop a double-click authorizing or
/// capturing twice.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IRepository<IdempotencyClaim> _claimRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IPaymentConfiguration _paymentConfiguration;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IRepository<IdempotencyClaim> claimRepository,
        IReadRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IPayPalPaymentGateway gateway,
        IPaymentConfiguration paymentConfiguration,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _claimRepository = claimRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _paymentConfiguration = paymentConfiguration;
        _logger = logger;
    }

    private string Currency => _paymentConfiguration.Currency;

    // ---- Place order -------------------------------------------------------------------------

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw PaymentException.BadRequest("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw PaymentException.BadRequest("Every item quantity must be greater than zero.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
            throw PaymentException.BadRequest($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var invoiceId = $"ESHOP-{order.Id}-{Guid.NewGuid():N}";
        var payment = new OrderPayment(order.Id, buyerId, order.Total(), Currency, invoiceId);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Placed order {0} for {1}: total {2} {3}, awaiting payment.",
            order.Id, buyerId, order.Total(), Currency);
        return order.Id;
    }

    // ---- Pay (authorize) ---------------------------------------------------------------------

    public async Task<OrderPayment> PayAsync(string buyerId, int orderId, PayInstruction instruction,
        CancellationToken cancellationToken)
    {
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Authorized)
            return payment; // idempotent: already authorized, do not authorize again

        if (payment.Status != PaymentStatus.AwaitingPayment && payment.Status != PaymentStatus.AuthorizationFailed)
            throw PaymentException.Conflict($"Order {orderId} cannot be paid in its current state ({payment.Status}).");

        // Resolve the instrument: a saved card (must be the caller's) or one-off card details.
        string? vaultId = null;
        CardDetails? card = null;
        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpecification(buyerId, savedId), cancellationToken)
                ?? throw PaymentException.NotFound($"Saved card {savedId} was not found for this shopper.");
            vaultId = saved.PayPalVaultId;
        }
        else if (instruction.Card is not null)
        {
            card = instruction.Card;
        }
        else
        {
            throw PaymentException.BadRequest("Provide either card details or a saved paymentMethodId.");
        }

        var claimKey = $"auth:{orderId}";
        if (!await TryClaimAsync(claimKey, cancellationToken))
        {
            // A concurrent authorize is in flight or completed — reload and settle.
            payment = await LoadOwnedPaymentAsync(buyerId, orderId, cancellationToken);
            if (payment.Status == PaymentStatus.Authorized) return payment;
            throw PaymentException.Conflict($"An authorization for order {orderId} is already in progress.");
        }

        try
        {
            var request = new PayPalAuthorizeRequest
            {
                Amount = payment.Amount,
                Currency = payment.Currency,
                InvoiceId = payment.InvoiceId,
                CustomId = orderId.ToString(),
                RequestId = $"auth-{orderId}-{Guid.NewGuid():N}",
                Description = $"eShopOnWeb order {orderId}",
                Card = card,
                VaultId = vaultId
            };

            var result = await _gateway.AuthorizeAsync(request, cancellationToken);

            if (result.RequiresApproval || string.IsNullOrEmpty(result.AuthorizationId))
            {
                await ReleaseClaimAsync(claimKey, cancellationToken);
                var message = result.ApprovalMessage
                    ?? "PayPal requires the shopper to approve this payment in a browser, which this API does not support.";
                payment.MarkAuthorizationFailed(message);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PayPalGatewayException(message, 402, "PAYER_ACTION_REQUIRED");
            }

            payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId!, result.Status,
                result.ExpiresAt, result.CardBrand, result.CardLastDigits);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            _logger.LogInformation("Authorized order {0}: paypalOrderId {1}, authorizationId {2}.",
                orderId, result.PayPalOrderId, result.AuthorizationId);
            return payment;
        }
        catch (PayPalGatewayException ex)
        {
            await ReleaseClaimAsync(claimKey, cancellationToken);
            if (!ex.OutcomeUnknown)
            {
                payment.MarkAuthorizationFailed(ex.Issue ?? ex.Message);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
            throw;
        }
        catch
        {
            await ReleaseClaimAsync(claimKey, cancellationToken);
            throw;
        }
    }

    // ---- Fulfil (capture) --------------------------------------------------------------------

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Fulfilled)
            return payment; // idempotent

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
            throw PaymentException.Conflict($"Order {orderId} is not awaiting fulfilment (state: {payment.Status}).");

        var claimKey = $"capture:{orderId}";
        if (!await TryClaimAsync(claimKey, cancellationToken))
        {
            payment = await LoadPaymentAsync(orderId, cancellationToken);
            if (payment.Status == PaymentStatus.Fulfilled) return payment;
            throw PaymentException.Conflict($"A fulfilment for order {orderId} is already in progress.");
        }

        try
        {
            PayPalCaptureResult result;
            try
            {
                result = await _gateway.CaptureAsync(payment.AuthorizationId!, payment.Amount, payment.Currency,
                    $"capture-{orderId}-{Guid.NewGuid():N}", cancellationToken);
            }
            catch (PayPalGatewayException ex) when (ex.IsStaleAuthorization)
            {
                _logger.LogWarning("Authorization {0} for order {1} is stale; renewing before capture.",
                    payment.AuthorizationId!, orderId);
                result = await RenewAndCaptureAsync(payment, orderId, cancellationToken);
            }

            payment.MarkFulfilled(result.CaptureId, result.Status, result.CapturedAmount,
                result.PayPalFee, result.NetAmount);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            _logger.LogInformation("Fulfilled order {0}: captureId {1}, captured {2} {3}, fee {4}, net {5}.",
                orderId, result.CaptureId, result.CapturedAmount, result.Currency, result.PayPalFee, result.NetAmount);
            return payment;
        }
        catch
        {
            await ReleaseClaimAsync(claimKey, cancellationToken);
            throw;
        }
    }

    private async Task<PayPalCaptureResult> RenewAndCaptureAsync(OrderPayment payment, int orderId,
        CancellationToken cancellationToken)
    {
        PayPalAuthorizationResult reauth;
        try
        {
            reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.Currency,
                $"reauth-{orderId}-{Guid.NewGuid():N}", cancellationToken);
        }
        catch (PayPalGatewayException ex)
        {
            // The authorization has expired and cannot be renewed — say so in operator terms.
            throw new PayPalGatewayException(
                $"The authorization for order {orderId} has expired and could not be renewed" +
                (ex.Issue is null ? "" : $" ({ex.Issue})") +
                ". Re-collect payment from the shopper (place a new order or re-authorize).",
                ex.StatusCode, ex.Issue, ex.DebugId, ex.OutcomeUnknown, ex);
        }

        if (string.IsNullOrEmpty(reauth.AuthorizationId))
            throw new PayPalGatewayException(
                $"The authorization for order {orderId} has expired and could not be renewed. Re-collect payment from the shopper.",
                reauth.RequiresApproval ? 402 : 422, "AUTHORIZATION_EXPIRED");

        payment.UpdateAuthorization(reauth.AuthorizationId!, reauth.Status, reauth.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        return await _gateway.CaptureAsync(reauth.AuthorizationId!, payment.Amount, payment.Currency,
            $"capture-{orderId}-{Guid.NewGuid():N}", cancellationToken);
    }

    // ---- Cancel (void) -----------------------------------------------------------------------

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Canceled)
            return payment; // idempotent

        if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
            throw PaymentException.Conflict(
                $"Order {orderId} has been fulfilled; funds already moved. Use a refund instead of cancel.");

        if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrEmpty(payment.AuthorizationId))
        {
            var claimKey = $"cancel:{orderId}";
            if (!await TryClaimAsync(claimKey, cancellationToken))
            {
                payment = await LoadPaymentAsync(orderId, cancellationToken);
                if (payment.Status == PaymentStatus.Canceled) return payment;
                throw PaymentException.Conflict($"A cancellation for order {orderId} is already in progress.");
            }

            try
            {
                await _gateway.VoidAsync(payment.AuthorizationId!, cancellationToken);
            }
            catch
            {
                await ReleaseClaimAsync(claimKey, cancellationToken);
                throw;
            }
        }

        payment.MarkCanceled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Cancelled order {0}; held funds released.", orderId);
        return payment;
    }

    // ---- Refund ------------------------------------------------------------------------------

    public async Task<OrderRefund> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, cancellationToken);

        if (payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded)
            || string.IsNullOrEmpty(payment.CaptureId))
            throw PaymentException.Conflict($"Order {orderId} has no captured payment to refund (state: {payment.Status}).");

        // Idempotent replay: the same caller key already produced a refund → return it, do not refund again.
        if (payment.TryGetRefundByIdempotencyKey(idempotencyKey, out var existing) && existing is not null)
            return existing;

        var refundable = payment.RefundableRemaining;
        if (refundable <= 0m)
            throw PaymentException.Conflict($"Order {orderId} has been fully refunded; nothing remains to refund.");

        decimal amountToRefund;
        if (amount is decimal requested)
        {
            if (requested <= 0m)
                throw PaymentException.BadRequest("Refund amount must be greater than zero.");
            if (requested > refundable)
                throw PaymentException.Conflict(
                    $"Refund of {requested} exceeds the {refundable} still refundable on order {orderId}.");
            amountToRefund = requested;
        }
        else
        {
            amountToRefund = refundable; // full refund of the remaining balance
        }

        var claimKey = $"refund:{orderId}:{idempotencyKey}";
        if (!await TryClaimAsync(claimKey, cancellationToken))
        {
            // Concurrent duplicate under the same key — reload and return the refund if it landed.
            payment = await LoadOwnedPaymentAsync(buyerId, orderId, cancellationToken);
            if (payment.TryGetRefundByIdempotencyKey(idempotencyKey, out var raced) && raced is not null)
                return raced;
            throw PaymentException.Conflict($"A refund under key '{idempotencyKey}' is already in progress.");
        }

        try
        {
            // PayPal-Request-Id must be stable for this logical refund (so a transport resend dedups at PayPal)
            // yet unique across runs — the per-run-unique invoice id plus the caller key gives both. A legitimate
            // repeat under the same caller key is stopped by the pre-lookup/claim above before it reaches PayPal.
            var payPalRequestId = $"{payment.InvoiceId}:{idempotencyKey}";
            var result = await _gateway.RefundAsync(payment.CaptureId!, amountToRefund, payment.Currency,
                payPalRequestId, cancellationToken);

            var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status ?? "PENDING", idempotencyKey);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            _logger.LogInformation("Refunded order {0}: refundId {1}, amount {2} {3}, status now {4}.",
                orderId, result.RefundId, result.Amount, result.Currency, payment.Status);
            return refund;
        }
        catch
        {
            await ReleaseClaimAsync(claimKey, cancellationToken);
            throw;
        }
    }

    // ---- Queries -----------------------------------------------------------------------------

    public async Task<IReadOnlyList<OrderPayment>> GetMyPaymentsAsync(string buyerId, CancellationToken cancellationToken)
    {
        var list = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), cancellationToken);
        return list;
    }

    public Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken cancellationToken) =>
        _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), cancellationToken);

    // ---- Reconciliation ----------------------------------------------------------------------

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
            throw PaymentException.BadRequest("'to' must not be earlier than 'from'.");

        var search = await _gateway.SearchTransactionsAsync(from, to, cancellationToken);
        var captured = await _paymentRepository.ListAsync(new CapturedOrderPaymentsSpecification(), cancellationToken);

        var matched = new List<ReconciliationMatch>();
        var eShopOnly = new List<ReconciliationOrder>();
        var matchedTransactions = new HashSet<PayPalTransaction>(ReferenceEqualityComparer.Instance);

        foreach (var payment in captured)
        {
            var orderIdText = payment.OrderId.ToString();
            var tx = search.Transactions.FirstOrDefault(t =>
                (!string.IsNullOrEmpty(t.InvoiceId) && t.InvoiceId == payment.InvoiceId)
                || (!string.IsNullOrEmpty(t.CustomField) && t.CustomField == orderIdText)
                || (!string.IsNullOrEmpty(t.TransactionId) && t.TransactionId == payment.CaptureId));

            if (tx is not null)
            {
                matchedTransactions.Add(tx);
                matched.Add(new ReconciliationMatch
                {
                    OrderId = payment.OrderId,
                    InvoiceId = payment.InvoiceId,
                    CaptureId = payment.CaptureId,
                    PayPalTransactionId = tx.TransactionId,
                    EShopAmount = payment.CapturedAmount,
                    PayPalAmount = tx.Amount,
                    AmountsAgree = payment.CapturedAmount.HasValue && tx.Amount.HasValue
                        && decimal.Round(payment.CapturedAmount.Value, 2) == decimal.Round(tx.Amount.Value, 2),
                    PayPalStatus = tx.Status
                });
            }
            else
            {
                eShopOnly.Add(new ReconciliationOrder
                {
                    OrderId = payment.OrderId,
                    InvoiceId = payment.InvoiceId,
                    CaptureId = payment.CaptureId,
                    CapturedAmount = payment.CapturedAmount
                });
            }
        }

        var payPalOnly = search.Transactions.Where(t => !matchedTransactions.Contains(t)).ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PayPalOnly = payPalOnly,
            EShopOnly = eShopOnly,
            PayPalPagesRetrieved = search.PagesRetrieved,
            WindowsQueried = search.WindowsQueried
        };
    }

    // ---- Saved cards -------------------------------------------------------------------------

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        // Reuse the shopper's existing PayPal customer id so all their cards vault under one customer.
        var existing = await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        var payPalCustomerId = existing.Select(e => e.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        var result = await _gateway.VaultCardAsync(new PayPalVaultCardRequest
        {
            Card = card,
            PayPalCustomerId = payPalCustomerId,
            MerchantCustomerId = buyerId,
            RequestId = Guid.NewGuid().ToString("N")
        }, cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, result.VaultId, result.PayPalCustomerId ?? payPalCustomerId,
            result.Brand, result.LastDigits, result.Expiry ?? card.Expiry, result.CardHolderName ?? card.CardHolderName);
        await _savedCardRepository.AddAsync(saved, cancellationToken);

        _logger.LogInformation("Saved card for {0}: vaultId {1}, {2} ****{3}.",
            buyerId, result.VaultId, result.Brand, result.LastDigits);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken)
    {
        var list = await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return list;
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdSpecification(buyerId, paymentMethodId), cancellationToken)
            ?? throw PaymentException.NotFound($"Saved card {paymentMethodId} was not found for this shopper.");

        await _gateway.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        await _savedCardRepository.DeleteAsync(saved, cancellationToken);
        _logger.LogInformation("Deleted saved card {0} ({1}) for {2}.", paymentMethodId, saved.PayPalVaultId, buyerId);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken) =>
        await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), cancellationToken)
        ?? throw PaymentException.NotFound($"Order {orderId} was not found.");

    private async Task<OrderPayment> LoadOwnedPaymentAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
            throw PaymentException.NotFound($"Order {orderId} was not found."); // do not reveal another shopper's order
        return payment;
    }

    /// <summary>
    /// Insert a claim row keyed by <paramref name="key"/>. Returns false when the store refuses a duplicate —
    /// the concurrency guard that stops a second caller before it reaches PayPal.
    /// </summary>
    private async Task<bool> TryClaimAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await _claimRepository.AddAsync(new IdempotencyClaim(key), cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Idempotency claim '{0}' was refused ({1}); treating as duplicate.", key, ex.GetType().Name);
            return false;
        }
    }

    private async Task ReleaseClaimAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var claim = await _claimRepository.GetByIdAsync(key, cancellationToken);
            if (claim is not null)
                await _claimRepository.DeleteAsync(claim, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to release idempotency claim '{0}' ({1}).", key, ex.GetType().Name);
        }
    }
}
