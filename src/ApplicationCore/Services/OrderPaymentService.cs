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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, Address? shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new InvalidPaymentOperationException("An order must contain at least one item.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidPaymentOperationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new InvalidPaymentOperationException($"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "N/A");
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, ct);

        // Unique per merchant account across runs (PayPal enforces invoice_id uniqueness), and persisted so
        // reconciliation can line the two sides up on it.
        var invoiceId = $"ESHOP-{order.Id.ToString(CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}";
        var payment = new OrderPayment(order.Id, buyerId, _gateway.Currency, order.Total(), invoiceId);
        try
        {
            await _paymentRepository.AddAsync(payment, ct);
        }
        catch (Exception ex) when (PersistenceErrors.IsUniqueViolation(ex))
        {
            // A payment row already exists for this order (unique index on OrderId) — nothing to do.
            _logger.LogWarning("Order payment for order {OrderId} already existed.", order.Id);
        }

        _logger.LogInformation("Placed order {OrderId} awaiting payment ({Amount} {Currency}).", order.Id, order.Total(), _gateway.Currency);
        return order.Id;
    }

    public async Task<OrderPayment> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

        if (payment.Status == PaymentStatus.Authorized)
        {
            return payment; // idempotent — already held
        }

        if (payment.Status != PaymentStatus.AwaitingPayment)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} cannot be paid from state '{payment.Status}'.");
        }

        // Resolve the payment source: a saved card (vault id) or one-off card details.
        CardDetails? oneOffCard = null;
        string? vaultId = null;
        string methodDescription;

        if (savedPaymentMethodId.HasValue)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpecification(savedPaymentMethodId.Value), ct);
            if (saved is null || saved.BuyerId != buyerId)
            {
                throw new PaymentMethodNotFoundException(savedPaymentMethodId.Value);
            }

            vaultId = saved.VaultId;
            methodDescription = DescribeSaved(saved);
        }
        else if (card is not null)
        {
            oneOffCard = card;
            methodDescription = DescribeCard(card);
        }
        else
        {
            throw new InvalidPaymentOperationException("Provide card details or a saved paymentMethodId to pay.");
        }

        var reference = Reference(payment);

        // Create the PayPal order first (idempotent), persisting its id before authorizing so an
        // interrupted call can resume without stranding a PayPal-side order.
        if (string.IsNullOrEmpty(payment.PayPalOrderId))
        {
            var payPalOrderId = await _gateway.CreateOrderAsync(
                $"{reference}-create",
                Money(payment.Amount),
                payment.InvoiceId,
                payment.OrderId.ToString(CultureInfo.InvariantCulture),
                $"eShopOnWeb order {payment.OrderId}",
                ct);
            payment.RecordPayPalOrder(payPalOrderId);
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        var auth = await _gateway.AuthorizeOrderAsync($"{reference}-auth", payment.PayPalOrderId!, oneOffCard, vaultId, ct);

        if (!IsAuthorizationHeld(auth.Status))
        {
            var reason = $"PayPal did not place a hold (authorization status: {auth.Status}).";
            payment.RecordFailure(reason);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new InvalidPaymentOperationException(reason);
        }

        payment.RecordAuthorization(auth.AuthorizationId, auth.Status, methodDescription);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Authorized order {OrderId}: PayPal auth {Auth} status {Status}.", orderId, auth.AuthorizationId, auth.Status);
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Fulfilled)
        {
            return payment; // idempotent — already captured
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new InvalidPaymentOperationException($"Order {orderId} cannot be fulfilled from state '{payment.Status}'.");
        }

        var reference = Reference(payment);
        var money = Money(payment.Amount);

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync($"{reference}-capture", payment.AuthorizationId!, money, ct);
        }
        catch (PayPalGatewayException ex) when (ex.IsAuthorizationExpired)
        {
            _logger.LogWarning("Authorization {Auth} for order {OrderId} is stale; renewing before capture.", payment.AuthorizationId, orderId);

            ReauthorizeResult reauth;
            try
            {
                reauth = await _gateway.ReauthorizeAsync($"{reference}-reauth", payment.AuthorizationId!, money, ct);
            }
            catch (PayPalGatewayException reauthEx)
            {
                var reason = "The payment hold has expired and can no longer be renewed. Ask the shopper to place and pay for the order again.";
                payment.RecordFailure(reason);
                await _paymentRepository.UpdateAsync(payment, ct);
                throw new InvalidPaymentOperationException(reason, reauthEx);
            }

            payment.RecordReauthorization(reauth.AuthorizationId, reauth.Status);
            await _paymentRepository.UpdateAsync(payment, ct);

            capture = await _gateway.CaptureAsync($"{reference}-capture-renewed", reauth.AuthorizationId, money, ct);
        }

        if (!IsCaptureCompleted(capture.Status))
        {
            var reason = $"Capture did not complete (status: {capture.Status}).";
            payment.RecordFailure(reason);
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new InvalidPaymentOperationException(reason);
        }

        payment.RecordCapture(
            capture.CaptureId,
            capture.Status,
            ParseAmount(capture.GrossAmount?.Value) ?? payment.Amount,
            ParseAmount(capture.PaypalFee?.Value),
            ParseAmount(capture.NetAmount?.Value));
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Fulfilled order {OrderId}: captured {Capture}, net {Net}.", orderId, capture.CaptureId, capture.NetAmount?.Value);
        return payment;
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return payment; // idempotent — already voided
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new InvalidPaymentOperationException($"Order {orderId} cannot be cancelled from state '{payment.Status}'.");
        }

        await _gateway.VoidAsync($"{Reference(payment)}-void", payment.AuthorizationId!, ct);
        payment.RecordVoided();
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation("Cancelled order {OrderId}: released hold on authorization {Auth}.", orderId, payment.AuthorizationId);
        return payment;
    }

    public async Task<RefundRecord> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var payment = await LoadOwnedPaymentAsync(orderId, buyerId, ct);

        if (string.IsNullOrEmpty(payment.CaptureId) ||
            (payment.Status != PaymentStatus.Fulfilled && payment.Status != PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidPaymentOperationException($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotent: a refund already recorded under this key is returned as-is.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var remaining = payment.RefundableRemaining();
        var refundAmount = amount ?? remaining;

        if (refundAmount <= 0m)
        {
            throw new InvalidPaymentOperationException("Refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw new InvalidPaymentOperationException(
                $"Refund of {Format(refundAmount)} exceeds the refundable remaining amount of {Format(remaining)}.");
        }

        var result = await _gateway.RefundAsync(
            idempotencyKey,
            payment.CaptureId!,
            Money(refundAmount),
            payment.OrderId.ToString(CultureInfo.InvariantCulture),
            ct);

        var record = new RefundRecord(idempotencyKey, result.RefundId, refundAmount, result.Status);
        payment.AddRefund(record);

        try
        {
            await _paymentRepository.UpdateAsync(payment, ct);
        }
        catch (Exception ex) when (PersistenceErrors.IsUniqueViolation(ex))
        {
            // A concurrent request under the same idempotency key won the race; return its record.
            var reloaded = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
            var winner = reloaded?.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }

        _logger.LogInformation("Refunded {Amount} on order {OrderId}: PayPal refund {Refund}.", Format(refundAmount), orderId, result.RefundId);
        return record;
    }

    public async Task<IReadOnlyList<OrderPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), ct);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
        {
            throw new InvalidPaymentOperationException("'to' must be on or after 'from'.");
        }

        // Cap pages defensively so an unexpected provider paging loop cannot run unbounded.
        const int MaxPages = 200;
        var search = await _gateway.SearchTransactionsAsync(from, to, MaxPages, ct);

        // Local orders whose money moved in the window (captured or refunded), keyed by invoice id.
        var localPayments = await _paymentRepository.ListAsync(
            new OrderPaymentsInWindowSpecification(from, to), ct);
        var localByInvoice = localPayments.ToDictionary(p => p.InvoiceId, StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationLine>();
        var onlyInPayPal = new List<ReconciliationLine>();
        var seenInvoices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in search.Transactions)
        {
            var invoice = tx.InvoiceId ?? tx.CustomField;
            OrderPayment? local = null;
            if (!string.IsNullOrEmpty(invoice))
            {
                localByInvoice.TryGetValue(invoice!, out local);
                if (tx.InvoiceId is not null)
                {
                    seenInvoices.Add(tx.InvoiceId);
                }
            }

            var line = new ReconciliationLine(
                local?.OrderId,
                tx.InvoiceId,
                tx.TransactionId,
                tx.Status,
                tx.Value,
                tx.CurrencyCode,
                tx.InitiationDate);

            if (local is not null)
            {
                matched.Add(line);
            }
            else
            {
                onlyInPayPal.Add(line);
            }
        }

        var onlyInEShop = localPayments
            .Where(p => !seenInvoices.Contains(p.InvoiceId))
            .Select(p => new ReconciliationLine(
                p.OrderId,
                p.InvoiceId,
                p.CaptureId,
                p.Status.ToString(),
                Format(p.CapturedAmount ?? p.Amount),
                p.CurrencyCode,
                p.CreatedAt.ToString("o", CultureInfo.InvariantCulture)))
            .ToList();

        return new ReconciliationReport(
            from,
            to,
            search.Transactions.Count,
            search.PagesScanned,
            search.Truncated,
            matched,
            onlyInPayPal,
            onlyInEShop);
    }

    // ---- helpers ----

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct)
            ?? throw new OrderPaymentNotFoundException(orderId);
    }

    private async Task<OrderPayment> LoadOwnedPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (payment is null || payment.BuyerId != buyerId)
        {
            // Treat another shopper's order as not found so existence is not leaked.
            throw new OrderPaymentNotFoundException(orderId);
        }

        return payment;
    }

    private GatewayMoney Money(decimal amount) => new(_gateway.Currency, Format(amount));

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    // The idempotency reference (sent as PayPal-Request-Id) is derived from the run-unique invoice id, so
    // it is stable across retries within a run yet never collides with a prior run's cached PayPal response.
    private static string Reference(OrderPayment payment) => payment.InvoiceId;

    private static bool IsAuthorizationHeld(string status) =>
        string.Equals(status, "CREATED", StringComparison.OrdinalIgnoreCase);

    private static bool IsCaptureCompleted(string status) =>
        string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase);

    private static string DescribeSaved(SavedPaymentMethod saved) =>
        $"{saved.Brand ?? "Card"} ****{saved.Last4 ?? "----"}";

    private static string DescribeCard(CardDetails card)
    {
        var number = card.Number ?? string.Empty;
        var last4 = number.Length >= 4 ? number[^4..] : "----";
        return $"Card ****{last4}";
    }
}
