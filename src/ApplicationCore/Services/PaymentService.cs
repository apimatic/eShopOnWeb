using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // Process-scoped salt so invoice ids are globally unique per run (PayPal enforces invoice_id
    // uniqueness per merchant, and small integer ids collide with prior runs on a shared account).
    // The eShop order id is carried separately in custom_id for reconciliation.
    private static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 8);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IRepository<PaymentCustomer> _customerRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalGateway _gateway;
    private readonly PaymentSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IRepository<PaymentCustomer> customerRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IPayPalGateway gateway,
        PaymentSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _customerRepository = customerRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.CurrencyCode;

    // ------------------------------------------------------------------ Flow 1

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        Address shipToAddress)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new PaymentOperationException("An order must contain at least one item.");
        }
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentOperationException(
                    $"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds));

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentOperationException(
                    $"Catalog item {line.CatalogItemId} does not exist.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order);
        return order;
    }

    public async Task<OrderPaymentView> AuthorizeOrderAsync(string buyerId, int orderId,
        PayInstruction instruction)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId);
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentOperationException("This order has been cancelled and cannot be paid.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId));

        // Idempotent in effect: a hold or capture already exists — never authorize twice.
        if (payment is not null &&
            (payment.Status == PaymentStatus.Authorized ||
             payment.Status == PaymentStatus.Captured ||
             payment.Status == PaymentStatus.Refunded ||
             payment.Status == PaymentStatus.PartiallyRefunded))
        {
            return new OrderPaymentView(order, payment);
        }

        var amount = Math.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        if (amount <= 0m)
        {
            throw new PaymentOperationException("Order total must be greater than zero to authorize.");
        }

        var invoiceReference = $"eshop-{RunId}-{orderId}";
        var authorizeRequest = new GatewayAuthorizeRequest
        {
            Amount = amount,
            CurrencyCode = Currency,
            InvoiceId = invoiceReference,
            CustomId = orderId.ToString(CultureInfo.InvariantCulture),
            RequestId = $"eshop-auth-{RunId}-{orderId}",
            Card = instruction.Card,
            VaultId = await ResolveVaultIdAsync(buyerId, instruction)
        };

        if (authorizeRequest.Card is null && authorizeRequest.VaultId is null)
        {
            throw new PaymentOperationException(
                "Provide either card details or a saved card id to pay with.");
        }

        var result = await _gateway.AuthorizeAsync(authorizeRequest);

        if (payment is null)
        {
            payment = new Payment(orderId, buyerId, result.CurrencyCode, amount, invoiceReference);
            payment.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId,
                result.AuthorizationStatus, result.ExpiresAt);
            payment = await _paymentRepository.AddAsync(payment);
        }
        else
        {
            payment.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId,
                result.AuthorizationStatus, result.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment);
        }

        order.SetStatus(OrderStatus.Authorized);
        await _orderRepository.UpdateAsync(order);

        return new OrderPaymentView(order, payment);
    }

    public async Task<OrderPaymentView> FulfilOrderAsync(int orderId)
    {
        var order = await GetOrderAsync(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId))
            ?? throw new PaymentOperationException(
                "This order has no payment to capture; it must be paid before it can be fulfilled.");

        if (payment.Status == PaymentStatus.Voided || order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentOperationException("A cancelled order cannot be fulfilled.");
        }

        // Idempotent in effect: money already captured — never capture twice.
        if (payment.IsCaptured)
        {
            return new OrderPaymentView(order, payment);
        }

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
        {
            throw new PaymentOperationException(
                "This order is not in an authorized state and cannot be fulfilled.");
        }

        var amount = payment.Amount;

        // A stale hold has to be renewed rather than failing the fulfilment. Renew proactively if
        // it has expired, and reactively if the capture is rejected because the hold is no longer
        // capturable.
        if (IsAuthorizationStale(payment))
        {
            await ReauthorizeAsync(payment);
        }

        GatewayCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, amount, payment.CurrencyCode,
                $"eshop-capture-{RunId}-{orderId}");
        }
        catch (PayPalGatewayException ex) when (IsExpiredAuthorization(ex))
        {
            _logger.LogWarning(
                $"Capture of order {orderId} rejected as stale ({string.Join(",", ex.Issues)}); renewing the hold.");
            await ReauthorizeAsync(payment);
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, amount, payment.CurrencyCode,
                $"eshop-capture-{RunId}-{orderId}-renewed");
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.Amount,
            capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment);

        order.SetStatus(OrderStatus.Fulfilled);
        await _orderRepository.UpdateAsync(order);

        return new OrderPaymentView(order, payment);
    }

    public async Task<OrderPaymentView> CancelOrderAsync(int orderId)
    {
        var order = await GetOrderAsync(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId));

        if (payment is not null && payment.IsCaptured)
        {
            throw new PaymentOperationException(
                "This order has already been fulfilled; refund it instead of cancelling.");
        }

        // Idempotent in effect.
        if (order.Status == OrderStatus.Cancelled ||
            (payment is not null && payment.Status == PaymentStatus.Voided))
        {
            return new OrderPaymentView(order, payment);
        }

        if (payment is not null && payment.Status == PaymentStatus.Authorized &&
            payment.AuthorizationId is not null)
        {
            await _gateway.VoidAsync(payment.AuthorizationId);
            payment.RecordVoid();
            await _paymentRepository.UpdateAsync(payment);
        }

        order.SetStatus(OrderStatus.Cancelled);
        await _orderRepository.UpdateAsync(order);

        return new OrderPaymentView(order, payment);
    }

    public async Task<(PaymentRefund Refund, OrderPaymentView View)> RefundOrderAsync(int orderId,
        decimal? amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOrderAsync(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId))
            ?? throw new PaymentOperationException("This order has no captured payment to refund.");

        if (!payment.IsCaptured || payment.CaptureId is null)
        {
            throw new PaymentOperationException(
                "This order has not been fulfilled; there is nothing to refund yet.");
        }

        // Idempotent per key: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            return (existing, new OrderPaymentView(order, payment));
        }

        var refundAmount = amount ?? payment.RefundableRemaining;
        refundAmount = Math.Round(refundAmount, 2, MidpointRounding.AwayFromZero);

        // A partly-refunded order must never become refundable beyond what was captured.
        payment.EnsureRefundable(refundAmount);

        // The PayPal-Request-Id is scoped to this (run, order, caller-key) so the same caller key
        // repeated on this capture dedups at PayPal, but the caller's key namespace can never
        // collide with a refund on a different capture (PayPal keeps request ids for 45 days).
        var payPalRequestId = $"eshop-refund-{RunId}-{orderId}-{idempotencyKey}";
        var result = await _gateway.RefundAsync(payment.CaptureId, refundAmount, payment.CurrencyCode,
            orderId.ToString(CultureInfo.InvariantCulture), payPalRequestId);

        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment);

        order.SetStatus(payment.Status == PaymentStatus.Refunded
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded);
        await _orderRepository.UpdateAsync(order);

        return (refund, new OrderPaymentView(order, payment));
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId));
        if (orders.Count == 0)
        {
            return Array.Empty<OrderPaymentView>();
        }

        var payments = await _paymentRepository.ListAsync(
            new PaymentsByOrderIdsSpec(orders.Select(o => o.Id)));
        var byOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderPaymentView(o, byOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    // ------------------------------------------------------------------ Flow 2

    public async Task<SavedCard> SaveCardAsync(string buyerId, GatewayCardDetails card)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));

        var customerId = await GetOrCreateCustomerIdAsync(buyerId);
        var result = await _gateway.VaultCardAsync(customerId, card, Guid.NewGuid().ToString("N"));

        var savedCard = new SavedCard(buyerId, result.TokenId, result.Brand, result.LastDigits,
            result.Expiry, result.CardholderName);
        savedCard = await _savedCardRepository.AddAsync(savedCard);
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId));
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var card = await _savedCardRepository.GetByIdAsync(paymentMethodId);

        // A saved card belongs to the shopper who saved it: hide others' cards behind "not found".
        if (card is null || !string.Equals(card.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new SavedCardNotFoundException(paymentMethodId);
        }

        try
        {
            await _gateway.DeleteVaultedCardAsync(card.VaultTokenId);
        }
        catch (PayPalGatewayException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning($"Vault token for saved card {paymentMethodId} was already gone at PayPal.");
        }

        await _savedCardRepository.DeleteAsync(card);
    }

    // ------------------------------------------------------------------ Reconciliation

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to)
    {
        if (to < from)
        {
            throw new PaymentOperationException("'to' must be on or after 'from'.");
        }

        var transactions = await _gateway.SearchTransactionsAsync(from, to);
        var payments = await _paymentRepository.ListAsync(new CapturedPaymentsSpec());

        var lines = new List<ReconciliationLine>();
        var matchedOrderIds = new HashSet<int>();

        // Match on the globally-unique invoice reference this run stamped on each payment; the
        // custom_id (bare order id) is intentionally NOT used to match because small integers
        // collide with unrelated historical transactions on a shared sandbox account.
        var byInvoice = payments
            .Where(p => !string.IsNullOrEmpty(p.InvoiceReference))
            .GroupBy(p => p.InvoiceReference)
            .ToDictionary(g => g.Key, g => g.First());

        // PayPal's record → eShop order.
        foreach (var tx in transactions)
        {
            var reference = tx.InvoiceId ?? tx.CustomField;
            Payment? match = null;
            if (!string.IsNullOrEmpty(tx.InvoiceId))
            {
                byInvoice.TryGetValue(tx.InvoiceId!, out match);
            }

            if (match is not null)
            {
                matchedOrderIds.Add(match.OrderId);
                lines.Add(new ReconciliationLine
                {
                    Kind = "Matched",
                    InvoiceId = reference,
                    OrderId = match.OrderId,
                    EShopPaymentStatus = match.Status.ToString(),
                    EShopCapturedAmount = match.CapturedAmount,
                    EShopCaptureId = match.CaptureId,
                    PayPalTransactionId = tx.TransactionId,
                    PayPalStatus = tx.Status,
                    PayPalEventCode = tx.EventCode,
                    PayPalAmount = tx.Amount,
                    PayPalCurrency = tx.CurrencyCode,
                    PayPalDate = tx.InitiationDate
                });
            }
            else
            {
                lines.Add(new ReconciliationLine
                {
                    Kind = "PayPalOnly",
                    InvoiceId = reference,
                    PayPalTransactionId = tx.TransactionId,
                    PayPalStatus = tx.Status,
                    PayPalEventCode = tx.EventCode,
                    PayPalAmount = tx.Amount,
                    PayPalCurrency = tx.CurrencyCode,
                    PayPalDate = tx.InitiationDate
                });
            }
        }

        // eShop captured payments PayPal's report has no row for (over this range).
        foreach (var payment in payments.Where(p => !matchedOrderIds.Contains(p.OrderId)))
        {
            lines.Add(new ReconciliationLine
            {
                Kind = "EShopOnly",
                InvoiceId = payment.OrderId.ToString(CultureInfo.InvariantCulture),
                OrderId = payment.OrderId,
                EShopPaymentStatus = payment.Status.ToString(),
                EShopCapturedAmount = payment.CapturedAmount,
                EShopCaptureId = payment.CaptureId
            });
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            PayPalTransactionCount = transactions.Count,
            EShopPaymentCount = payments.Count,
            MatchedCount = lines.Count(l => l.Kind == "Matched"),
            PayPalOnlyCount = lines.Count(l => l.Kind == "PayPalOnly"),
            EShopOnlyCount = lines.Count(l => l.Kind == "EShopOnly"),
            Lines = lines
        };
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Order> GetOrderAsync(int orderId)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId))
            ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId)
    {
        var order = await GetOrderAsync(orderId);
        // One shopper must never see or act on another's order.
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<string?> ResolveVaultIdAsync(string buyerId, PayInstruction instruction)
    {
        if (instruction.SavedCardId is null)
        {
            return null;
        }

        var card = await _savedCardRepository.GetByIdAsync(instruction.SavedCardId.Value);
        if (card is null || !string.Equals(card.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new SavedCardNotFoundException(instruction.SavedCardId.Value);
        }
        return card.VaultTokenId;
    }

    private async Task<string> GetOrCreateCustomerIdAsync(string buyerId)
    {
        var existing = await _customerRepository.FirstOrDefaultAsync(new PaymentCustomerByBuyerSpec(buyerId));
        if (existing is not null)
        {
            return existing.PayPalCustomerId;
        }

        // PayPal customer id: max 22 chars, pattern ^[0-9a-zA-Z_-]+$.
        var customerId = "c" + Guid.NewGuid().ToString("N").Substring(0, 21);
        var customer = new PaymentCustomer(buyerId, customerId);
        await _customerRepository.AddAsync(customer);
        return customerId;
    }

    private bool IsAuthorizationStale(Payment payment)
    {
        // Renew if the hold has (nearly) expired. PayPal honours an authorization for ~3 days.
        return payment.AuthorizationExpiresAt is DateTimeOffset expiry &&
               expiry <= DateTimeOffset.UtcNow.AddMinutes(1);
    }

    private static bool IsExpiredAuthorization(PayPalGatewayException ex)
    {
        // error.issue is an open string in the spec; match the ones PayPal uses for a stale hold.
        return ex.Issues.Any(i =>
            i.IndexOf("EXPIRED", StringComparison.OrdinalIgnoreCase) >= 0 ||
            i.IndexOf("AUTHORIZATION_ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase) >= 0 ||
            i.IndexOf("INVALID_AUTHORIZATION_STATUS", StringComparison.OrdinalIgnoreCase) >= 0 ||
            i.IndexOf("AUTH_EXPIRED", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private async Task ReauthorizeAsync(Payment payment)
    {
        try
        {
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                payment.CurrencyCode, $"eshop-reauth-{payment.OrderId}-{DateTimeOffset.UtcNow.Ticks}");
            payment.RecordReauthorization(reauth.AuthorizationId, reauth.AuthorizationStatus, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment);
        }
        catch (PayPalGatewayException ex)
        {
            throw new PaymentOperationException(
                "The payment hold on this order has expired and could not be renewed " +
                $"({ex.Message}). Ask the shopper to place and pay for the order again.");
        }
    }
}
