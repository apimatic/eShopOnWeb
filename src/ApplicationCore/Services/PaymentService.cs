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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly ISavedCardService _savedCardService;
    private readonly IPayPalClient _payPalClient;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    // A per-process nonce so idempotency keys derived from (restartable) local order ids never collide with
    // a previous run's keys at PayPal. In-memory storage resets order ids on restart; PayPal remembers keys.
    private static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 8);

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        ISavedCardService savedCardService,
        IPayPalClient payPalClient,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardService = savedCardService;
        _payPalClient = payPalClient;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least one.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        // Amounts come from catalog prices, snapshotted onto the order — reusing the existing order model.
        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, order.Total(), _settings.CurrencyOrDefault());
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation($"Placed order {order.Id} for {payment.Amount} {payment.CurrencyCode}; awaiting payment.");
        return order.Id;
    }

    public async Task<Payment> AuthorizeOrderAsync(string buyerId, int orderId, PaymentInstrument instrument,
        CancellationToken cancellationToken = default)
    {
        var payment = await GetOwnedPaymentAsync(buyerId, orderId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
                _logger.LogInformation($"Order {orderId} is already authorized; returning existing hold.");
                return payment;
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                throw new PaymentException($"Order {orderId} has already been captured and cannot be authorized again.");
            case PaymentStatus.Voided:
                throw new PaymentException($"Order {orderId} was cancelled and can no longer be paid.");
        }

        // Resolve the funding instrument: a saved card (by id) or one-off raw card details.
        SavedCard? savedCard = null;
        if (instrument.IsSavedCard)
        {
            savedCard = await _savedCardService.GetOwnedCardAsync(buyerId, instrument.SavedCardId!.Value, cancellationToken);
            if (savedCard is null)
            {
                throw new PaymentResourceNotFoundException($"Saved card {instrument.SavedCardId} was not found.");
            }
        }
        else if (instrument.Card is null)
        {
            throw new PaymentException("Provide either card details or the id of a saved card to pay with.");
        }

        // Create the PayPal order once (idempotent), then authorize the exact order total.
        var payPalOrderId = payment.PayPalOrderId;
        if (string.IsNullOrEmpty(payPalOrderId))
        {
            payPalOrderId = await _payPalClient.CreateAuthorizationOrderAsync(
                payment.Amount, payment.CurrencyCode, orderId.ToString(),
                $"order-create-{RunId}-{orderId}", cancellationToken);
            payment.AttachPayPalOrder(payPalOrderId);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        // The PayPal order id is globally unique, so an auth key derived from it is stable within a run
        // (double-click safe) yet never collides with another run.
        var authResult = savedCard is not null
            ? await _payPalClient.AuthorizeWithVaultedCardAsync(payPalOrderId, savedCard.VaultId,
                $"order-auth-{payPalOrderId}", cancellationToken)
            : await _payPalClient.AuthorizeWithCardAsync(payPalOrderId, instrument.Card!,
                $"order-auth-{payPalOrderId}", cancellationToken);

        if (authResult.RequiresBuyerApproval)
        {
            throw new PaymentChallengeRequiredException();
        }

        var cardDescription = DescribeCard(authResult.CardBrand, authResult.CardLast4)
            ?? (savedCard is not null ? savedCard.Description : null);

        payment.MarkAuthorized(payPalOrderId, authResult.AuthorizationId, authResult.Status,
            authResult.ExpiresAt, cardDescription, savedCard?.Id);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Authorized order {orderId}: hold {authResult.AuthorizationId} for {payment.Amount} {payment.CurrencyCode}.");
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        switch (payment.Status)
        {
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                _logger.LogInformation($"Order {orderId} is already fulfilled; returning existing capture.");
                return payment; // idempotent: money is taken only once
            case PaymentStatus.PendingAuthorization:
                throw new PaymentException($"Order {orderId} has not been paid yet; authorize it before fulfilling.");
            case PaymentStatus.Voided:
                throw new PaymentException($"Order {orderId} was cancelled and cannot be fulfilled.");
            case PaymentStatus.Failed:
                throw new PaymentException($"Order {orderId} has a failed payment and cannot be fulfilled.");
        }

        var capture = await EnsureCapturedAsync(payment, cancellationToken);
        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Fulfilled order {orderId}: captured {capture.GrossAmount} {capture.CurrencyCode}, " +
            $"fee {capture.PayPalFee}, net {capture.NetAmount}.");
        return payment;
    }

    public async Task<Payment> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        switch (payment.Status)
        {
            case PaymentStatus.Voided:
                return payment; // idempotent
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                throw new PaymentException($"Order {orderId} has already been fulfilled; refund it instead of cancelling.");
        }

        // Release the hold if one was placed. Before authorization there is nothing to release at PayPal.
        if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrEmpty(payment.AuthorizationId))
        {
            await _payPalClient.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        }

        payment.MarkVoided();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation($"Cancelled order {orderId}; any held funds were released.");
        return payment;
    }

    public async Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await GetOwnedPaymentAsync(buyerId, orderId, cancellationToken);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            || string.IsNullOrEmpty(payment.CaptureId))
        {
            throw new PaymentException($"Order {orderId} has not been captured, so there is nothing to refund.");
        }

        // Repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Refund for order {orderId} under key '{idempotencyKey}' already exists; returning it.");
            return existing;
        }

        var refundable = payment.RefundableAmount();
        if (refundable <= 0m)
        {
            throw new PaymentException($"Order {orderId} is already fully refunded.");
        }
        if (amount.HasValue)
        {
            if (amount.Value <= 0m)
            {
                throw new PaymentException("A refund amount must be positive.");
            }
            // A partly-refunded order must never become refundable beyond what was captured.
            if (amount.Value > refundable)
            {
                throw new PaymentException(
                    $"Refund of {amount.Value:0.00} {payment.CurrencyCode} exceeds the refundable amount " +
                    $"{refundable:0.00} {payment.CurrencyCode} for order {orderId}.");
            }
        }

        // The caller's key is stored verbatim for app-level dedupe; PayPal's request id is namespaced by the
        // capture so a key reused across runs (against a different capture) can't map to an old refund.
        var payPalRequestId = $"{payment.CaptureId}-{idempotencyKey}";
        var result = await _payPalClient.RefundCaptureAsync(payment.CaptureId!, amount, payment.CurrencyCode,
            payPalRequestId, cancellationToken);

        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Refunded {result.Amount} {payment.CurrencyCode} on order {orderId} (refund {result.RefundId}).");
        return refund;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderWithPayment(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentException("The reconciliation 'to' date must not be before the 'from' date.");
        }

        // PayPal's own record over the whole range (chunked and fully paged by the client) ...
        var transactions = await _payPalClient.SearchTransactionsAsync(from, to, cancellationToken);
        // ... lined up against the eShop payments created in the same range.
        var payments = await _paymentRepository.ListAsync(new PaidPaymentsBetweenSpecification(from, to), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        var matched = new List<ReconciliationMatch>();
        var payPalOnly = new List<ReconciliationPayPalOnly>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            var reference = txn.InvoiceId ?? txn.CustomField;
            if (reference is not null && int.TryParse(reference, out var orderId) &&
                paymentsByOrder.TryGetValue(orderId, out var payment))
            {
                matchedOrderIds.Add(orderId);
                var amountsAgree = txn.Amount.HasValue &&
                    Math.Abs(Math.Abs(txn.Amount.Value) - payment.Amount) < 0.01m;
                matched.Add(new ReconciliationMatch(orderId, txn.TransactionId, txn.Amount, payment.Amount,
                    txn.Status, payment.Status.ToString(), amountsAgree));
            }
            else
            {
                payPalOnly.Add(new ReconciliationPayPalOnly(txn.TransactionId, txn.Amount, txn.CurrencyCode,
                    txn.InvoiceId, txn.Status));
            }
        }

        var eShopOnly = payments
            .Where(p => !matchedOrderIds.Contains(p.OrderId))
            .Select(p => new ReconciliationEShopOnly(p.OrderId, p.PayPalOrderId, p.CaptureId, p.Amount, p.Status.ToString()))
            .ToList();

        return new ReconciliationReport(from, to, transactions.Count, payments.Count, matched, payPalOnly, eShopOnly);
    }

    // --- helpers ---

    private async Task<Payment> GetPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private async Task<Payment> GetOwnedPaymentAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        // Not found and not-yours are indistinguishable so a shopper can never probe another's orders.
        if (payment is null || payment.BuyerId != buyerId)
        {
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private async Task<PayPalCaptureResult> EnsureCapturedAsync(Payment payment, CancellationToken cancellationToken)
    {
        var orderId = payment.OrderId;

        // Proactively renew a hold PayPal already considers expired, rather than failing the capture outright.
        if (payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            await RenewAuthorizationAsync(payment, cancellationToken);
        }

        try
        {
            return await _payPalClient.CaptureAuthorizationAsync(payment.AuthorizationId!, $"order-capture-{payment.AuthorizationId}", cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.IndicatesExpiredOrVoidedAuthorization)
        {
            _logger.LogWarning($"Capture of order {orderId} failed ({ex.IssueName}); renewing the authorization and retrying.");
            await RenewAuthorizationAsync(payment, cancellationToken);
            try
            {
                // AuthorizationId now points at the renewed hold, keeping the capture key unique.
                return await _payPalClient.CaptureAuthorizationAsync(payment.AuthorizationId!, $"order-capture-{payment.AuthorizationId}", cancellationToken);
            }
            catch (PayPalApiException retryEx)
            {
                throw new PaymentException(
                    $"Order {orderId} could not be captured even after renewing the authorization " +
                    $"(PayPal: {retryEx.IssueName ?? "capture failed"}, debug id {retryEx.DebugId ?? "n/a"}). " +
                    "The shopper must place and pay for a new order.");
            }
        }
    }

    private async Task RenewAuthorizationAsync(Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _payPalClient.ReauthorizeAsync(payment.AuthorizationId!, $"order-reauth-{payment.AuthorizationId}", cancellationToken);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            _logger.LogInformation($"Renewed authorization for order {payment.OrderId}: new hold {reauth.AuthorizationId}.");
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                $"The authorization for order {payment.OrderId} has expired and could not be renewed " +
                $"(PayPal: {ex.IssueName ?? "reauthorization failed"}, debug id {ex.DebugId ?? "n/a"}). " +
                "PayPal only allows renewing a hold within 30 days of the original authorization; " +
                "the shopper must place and pay for a new order.");
        }
    }

    private static string? DescribeCard(string? brand, string? last4)
    {
        if (string.IsNullOrEmpty(brand) && string.IsNullOrEmpty(last4))
        {
            return null;
        }
        return $"{brand ?? "CARD"} ****{last4 ?? "----"}";
    }
}
