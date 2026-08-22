using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class CheckoutService : ICheckoutService
{
    public const int AuthorizationHonorPeriodDays = 3;
    public const int AuthorizationLifetimeDays = 29;

    private static readonly Address DefaultShipToAddress = new("123 Main St", "Redmond", "WA", "US", "98052");

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentSettings _paymentSettings;
    private readonly IAppLogger<CheckoutService> _logger;

    public CheckoutService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPayPalGateway payPalGateway,
        IUriComposer uriComposer,
        IPaymentSettings paymentSettings,
        IAppLogger<CheckoutService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _payPalGateway = payPalGateway;
        _uriComposer = uriComposer;
        _paymentSettings = paymentSettings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineRequest> items,
        Address? shipToAddress,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        if (items is null || items.Count == 0)
        {
            throw new CheckoutException(400, "An order must contain at least one catalog item.");
        }

        var grouped = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new OrderLineRequest(g.Key, g.Sum(x => x.Quantity)))
            .ToList();

        if (grouped.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new CheckoutException(400, "Each line must have a catalog item id and a quantity greater than zero.");
        }

        var catalogIds = grouped.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = catalogIds.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new CheckoutException(400, $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = grouped.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var snapshot = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(snapshot, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, order.Total(), RequireCurrency());
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return order;
    }

    public async Task<OrderPayment> PayAsync(
        string buyerId,
        int orderId,
        CardPaymentRequest? card,
        int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        var payment = await GetRequiredPaymentAsync(orderId, cancellationToken);
        payment.EnsureOwnedBy(buyerId);

        if (payment.Status is OrderPaymentStatus.Authorized or OrderPaymentStatus.Fulfilled
            or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            return payment;
        }

        if (payment.Status == OrderPaymentStatus.Cancelled)
        {
            throw new CheckoutException(409, "A cancelled order cannot be paid.");
        }

        var hasCard = card is not null;
        var hasSaved = paymentMethodId.HasValue;
        if (hasCard == hasSaved)
        {
            throw new CheckoutException(400, "Provide either card details or a saved paymentMethodId, not both.");
        }

        string? vaultId = null;
        PayPalCardDetails? paypalCard = null;
        if (hasSaved)
        {
            var saved = await GetOwnedSavedCardAsync(buyerId, paymentMethodId!.Value, cancellationToken);
            vaultId = saved.PayPalVaultId;
        }
        else
        {
            paypalCard = ToPayPalCard(card!);
        }

        var order = await GetRequiredOrderAsync(orderId, cancellationToken);
        var expectedTotal = PayPalMoney.Round(order.Total(), payment.Currency);
        if (expectedTotal != payment.Amount)
        {
            throw new CheckoutException(409, "The stored payment amount no longer matches the order total.");
        }

        var customId = $"order:{order.Id}";
        var invoiceId = $"ESHOP-{order.Id}-{Guid.NewGuid():N}"[..24];
        var idempotencyKey = payment.BeginPayAttempt();

        try
        {
            var authorization = await _payPalGateway.AuthorizeAsync(new PayPalAuthorizeRequest
            {
                Amount = payment.Amount,
                Currency = payment.Currency,
                CustomId = customId,
                InvoiceId = invoiceId,
                IdempotencyKey = idempotencyKey,
                Items = order.OrderItems.Select(i => new PayPalOrderItem
                {
                    Name = Truncate(i.ItemOrdered.ProductName, 127),
                    Quantity = i.Units,
                    UnitPrice = i.UnitPrice
                }).ToList(),
                Card = paypalCard,
                VaultId = vaultId
            }, cancellationToken);

            if (!string.Equals(PayPalMoney.Format(authorization.Amount, payment.Currency),
                    PayPalMoney.Format(payment.Amount, payment.Currency), StringComparison.Ordinal))
            {
                throw new CheckoutException(502,
                    $"PayPal held {authorization.Amount} {authorization.Currency} but the order total is {payment.Amount} {payment.Currency}.");
            }

            payment.AttachPayPalOrder(authorization.PayPalOrderId, customId, invoiceId);
            payment.RecordAuthorization(
                authorization.AuthorizationId,
                authorization.Status,
                authorization.ExpirationTime,
                authorization.CreateTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return payment;
        }
        catch (PayPalPayerActionRequiredException)
        {
            throw;
        }
        catch (PayPalApiException ex)
        {
            throw MapPayPalException(ex, "Authorization failed.");
        }
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetRequiredPaymentAsync(orderId, cancellationToken);

        if (payment.Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            return payment;
        }

        if (payment.Status == OrderPaymentStatus.Cancelled)
        {
            throw new CheckoutException(409, "A cancelled order cannot be fulfilled.");
        }

        if (payment.Status != OrderPaymentStatus.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
        {
            throw new CheckoutException(409, "Fulfilment requires an authorized payment. The shopper must pay first.");
        }

        try
        {
            await EnsureCapturableAuthorizationAsync(payment, cancellationToken);

            var capture = await _payPalGateway.CaptureAsync(
                payment.AuthorizationId!,
                payment.Amount,
                payment.Currency,
                payment.InvoiceId ?? $"ESHOP-{payment.OrderId}",
                $"eshop-capture-{payment.OrderId}",
                cancellationToken);

            payment.RecordCapture(
                capture.CaptureId,
                capture.Status,
                capture.CapturedAmount,
                capture.PaypalFee,
                capture.NetProceeds);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return payment;
        }
        catch (PayPalApiException ex)
        {
            throw MapPayPalException(ex, "Capture failed.");
        }
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var payment = await GetRequiredPaymentAsync(orderId, cancellationToken);

        if (payment.Status == OrderPaymentStatus.Cancelled)
        {
            return payment;
        }

        if (payment.Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            throw new CheckoutException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (payment.Status == OrderPaymentStatus.Authorized && !string.IsNullOrEmpty(payment.AuthorizationId))
        {
            try
            {
                await _payPalGateway.VoidAuthorizationAsync(
                    payment.AuthorizationId,
                    $"eshop-void-{payment.OrderId}",
                    cancellationToken);
            }
            catch (PayPalApiException ex) when (ex.StatusCode == 422 &&
                                                string.Equals(ex.Issue, "AUTHORIZATION_ALREADY_VOIDED", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Authorization {AuthorizationId} was already voided.", payment.AuthorizationId);
            }
            catch (PayPalApiException ex)
            {
                throw MapPayPalException(ex, "Releasing the payment hold failed.");
            }
        }

        payment.RecordCancellation();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(
        string buyerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new CheckoutException(400, "A refund idempotency key is required.");
        }

        var payment = await GetRequiredPaymentAsync(orderId, cancellationToken);
        payment.EnsureOwnedBy(buyerId);

        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey.Trim());
        if (existing is not null)
        {
            return existing;
        }

        payment.EnsureCanRefund();
        var refundAmount = amount.HasValue
            ? PayPalMoney.Round(amount.Value, payment.Currency)
            : payment.RemainingRefundable;

        if (refundAmount > payment.RemainingRefundable)
        {
            throw new CheckoutException(400,
                $"Refund of {refundAmount} {payment.Currency} exceeds the remaining refundable amount of {payment.RemainingRefundable} {payment.Currency}.");
        }

        try
        {
            var result = await _payPalGateway.RefundAsync(
                payment.CaptureId!,
                amount.HasValue ? refundAmount : null,
                payment.Currency,
                idempotencyKey.Trim(),
                cancellationToken);

            var refund = payment.RecordRefund(result.RefundId, result.Amount, result.Status, idempotencyKey.Trim());
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return refund;
        }
        catch (PayPalApiException ex)
        {
            throw MapPayPalException(ex, "Refund failed.");
        }
    }

    public async Task<IReadOnlyList<ShopperOrder>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(
            new OrderPaymentsByOrderIdsSpec(orders.Select(o => o.Id)), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order => new ShopperOrder
            {
                Order = order,
                Payment = paymentsByOrder.GetValueOrDefault(order.Id)
            })
            .ToList();
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(
        string buyerId,
        CardPaymentRequest card,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        var existing = await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        var paypalCustomerId = existing.Select(c => c.PayPalCustomerId).FirstOrDefault(id => !string.IsNullOrEmpty(id));

        try
        {
            var vaulted = await _payPalGateway.VaultCardAsync(new PayPalVaultCardRequest
            {
                Card = ToPayPalCard(card),
                MerchantCustomerId = ToMerchantCustomerId(buyerId),
                PayPalCustomerId = paypalCustomerId,
                IdempotencyKey = $"eshop-vault-{Guid.NewGuid():N}"
            }, cancellationToken);

            var saved = new SavedPaymentMethod(
                buyerId,
                vaulted.VaultId,
                vaulted.LastDigits,
                vaulted.Brand,
                vaulted.Expiry,
                vaulted.CardholderName,
                vaulted.PayPalCustomerId ?? paypalCustomerId);

            await _savedCardRepository.AddAsync(saved, cancellationToken);
            return saved;
        }
        catch (PayPalPayerActionRequiredException)
        {
            throw;
        }
        catch (PayPalApiException ex)
        {
            throw MapPayPalException(ex, "Saving the card failed.");
        }
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        return await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        var saved = await GetOwnedSavedCardAsync(buyerId, paymentMethodId, cancellationToken);

        try
        {
            await _payPalGateway.DeleteVaultedCardAsync(saved.PayPalVaultId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogInformation("PayPal vault token {TokenId} was already removed.", saved.Id);
        }
        catch (PayPalApiException ex)
        {
            throw MapPayPalException(ex, "Removing the saved card from PayPal failed.");
        }

        saved.MarkDeleted();
        await _savedCardRepository.UpdateAsync(saved, cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new CheckoutException(400, "The reconciliation 'to' timestamp must be on or after 'from'.");
        }

        var paypalTransactions = await _payPalGateway.ListAllTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsWithPayPalIdsSpec(), cancellationToken);

        var matched = new List<ReconciliationRow>();
        var matchedPaymentIds = new HashSet<int>();
        var matchedTransactionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in paypalTransactions)
        {
            var payment = payments.FirstOrDefault(p => Matches(p, tx));
            if (payment is null)
            {
                continue;
            }

            matched.Add(new ReconciliationRow { Payment = payment, Transaction = tx });
            matchedPaymentIds.Add(payment.Id);
            if (!string.IsNullOrEmpty(tx.TransactionId))
            {
                matchedTransactionIds.Add(tx.TransactionId);
            }
        }

        var paypalOnly = paypalTransactions
            .Where(tx => !matchedTransactionIds.Contains(tx.TransactionId))
            .ToList();

        var eshopOnly = payments
            .Where(p => !matchedPaymentIds.Contains(p.Id) && IsInRange(p, from, to))
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PayPalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    public async Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
    }

    private async Task EnsureCapturableAuthorizationAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        PayPalAuthorizationResult authorization;
        try
        {
            authorization = await _payPalGateway.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            throw new CheckoutException(409,
                "PayPal no longer has this authorization. It cannot be renewed. Ask the shopper to pay again.");
        }

        payment.UpdateAuthorization(
            authorization.AuthorizationId,
            authorization.Status,
            authorization.ExpirationTime,
            authorization.CreateTime);

        if (authorization.Status is "VOIDED" or "DENIED")
        {
            throw new CheckoutException(409,
                $"Authorization {authorization.AuthorizationId} is {authorization.Status} and cannot be captured. Ask the shopper to pay again.");
        }

        if (authorization.Status is "CAPTURED" or "PARTIALLY_CAPTURED")
        {
            return;
        }

        var created = authorization.CreateTime ?? payment.AuthorizationCreated ?? DateTimeOffset.UtcNow;
        var honorExpired = DateTimeOffset.UtcNow >= created.AddDays(AuthorizationHonorPeriodDays);
        var expired = authorization.ExpirationTime.HasValue && authorization.ExpirationTime.Value <= DateTimeOffset.UtcNow;
        var pastRenewalWindow = DateTimeOffset.UtcNow >= created.AddDays(AuthorizationLifetimeDays);

        if (!honorExpired && !expired)
        {
            return;
        }

        if (pastRenewalWindow)
        {
            throw new CheckoutException(409,
                "This authorization is older than 29 days and can no longer be renewed. Ask the shopper to place and pay a new order.");
        }

        try
        {
            var reauthorized = await _payPalGateway.ReauthorizeAsync(
                authorization.AuthorizationId,
                payment.Amount,
                payment.Currency,
                $"eshop-reauth-{payment.OrderId}",
                cancellationToken);

            payment.UpdateAuthorization(
                reauthorized.AuthorizationId,
                reauthorized.Status,
                reauthorized.ExpirationTime,
                reauthorized.CreateTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new CheckoutException(409,
                "This authorization has gone stale and PayPal would not renew it. Ask the shopper to pay again. PayPal said: " +
                ex.Message);
        }
    }

    private async Task<Order> GetRequiredOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new CheckoutException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<OrderPayment> GetRequiredPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new CheckoutException(404, $"Order {orderId} was not found.");
        }

        return payment;
    }

    private async Task<SavedPaymentMethod> GetOwnedSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await _savedCardRepository.FirstOrDefaultAsync(new SavedPaymentMethodByIdSpec(paymentMethodId), cancellationToken);
        if (saved is null)
        {
            throw new CheckoutException(404, "Payment method was not found.");
        }

        saved.EnsureOwnedBy(buyerId);
        return saved;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_paymentSettings.Currency))
        {
            throw new CheckoutException(500, "PayPal:Currency is not configured.");
        }

        return _paymentSettings.Currency.Trim().ToUpperInvariant();
    }

    private static void EnsureBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new CheckoutException(401, "The caller is not authenticated.");
        }
    }

    private static PayPalCardDetails ToPayPalCard(CardPaymentRequest card)
    {
        var number = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (number.Length is < 13 or > 19)
        {
            throw new CheckoutException(400, "A valid card number is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Expiry))
        {
            throw new CheckoutException(400, "Card expiry (YYYY-MM) is required.");
        }

        return new PayPalCardDetails
        {
            Number = number,
            Expiry = card.Expiry.Trim(),
            SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode.Trim(),
            Name = card.Name,
            BillingAddress = card.BillingAddress
        };
    }

    private static string ToMerchantCustomerId(string buyerId)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId))).ToLowerInvariant();
        return hash[..32];
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max];
    }

    private static CheckoutException MapPayPalException(PayPalApiException ex, string fallback)
    {
        var status = ex.StatusCode switch
        {
            400 or 422 => 400,
            401 or 403 => 502,
            404 => 404,
            409 => 409,
            _ => 502
        };

        var suffix = string.IsNullOrEmpty(ex.DebugId) ? string.Empty : $" (PayPal debug id {ex.DebugId})";
        return new CheckoutException(status, $"{fallback} {ex.Message}{suffix}".Trim());
    }

    public static bool Matches(OrderPayment payment, PayPalReportedTransaction transaction)
    {
        if (!string.IsNullOrEmpty(transaction.TransactionId) && IdsEqual(payment, transaction.TransactionId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(transaction.ReferenceId) && IdsEqual(payment, transaction.ReferenceId))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(transaction.CustomField) &&
            !string.IsNullOrEmpty(payment.CustomId) &&
            string.Equals(transaction.CustomField, payment.CustomId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(transaction.InvoiceId) &&
            !string.IsNullOrEmpty(payment.InvoiceId) &&
            string.Equals(transaction.InvoiceId, payment.InvoiceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IdsEqual(OrderPayment payment, string id)
    {
        return string.Equals(payment.PayPalOrderId, id, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(payment.AuthorizationId, id, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(payment.CaptureId, id, StringComparison.OrdinalIgnoreCase) ||
               payment.Refunds.Any(r => string.Equals(r.PayPalRefundId, id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInRange(OrderPayment payment, DateTimeOffset from, DateTimeOffset to)
    {
        var stamp = payment.AuthorizationCreated
                    ?? payment.Refunds.Select(r => (DateTimeOffset?)r.CreatedAt).Max()
                    ?? DateTimeOffset.MinValue;
        return stamp >= from && stamp <= to;
    }
}
