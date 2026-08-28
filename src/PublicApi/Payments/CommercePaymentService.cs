using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class CommercePaymentService
{
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;
    private readonly OrderOperationLock _orderLock;

    public CommercePaymentService(
        CatalogContext db,
        IPayPalClient payPal,
        IOptions<PayPalOptions> options,
        OrderOperationLock orderLock)
    {
        _db = db;
        _payPal = payPal;
        _options = options.Value;
        _orderLock = orderLock;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(
        string buyerId,
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            throw BadRequest("ORDER_ITEMS_REQUIRED", "At least one catalog item is required.");
        }

        var groupedItems = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new { CatalogItemId = x.Key, Quantity = x.Sum(i => i.Quantity) })
            .ToList();
        if (groupedItems.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0 || x.Quantity > 1000))
        {
            throw BadRequest("INVALID_ORDER_ITEM", "Catalog item IDs and quantities must be positive; quantity cannot exceed 1000.");
        }

        var ids = groupedItems.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var missingIds = ids.Where(x => !catalogItems.ContainsKey(x)).ToList();
        if (missingIds.Count > 0)
        {
            throw BadRequest("CATALOG_ITEM_NOT_FOUND", $"Catalog item(s) not found: {string.Join(", ", missingIds)}.");
        }

        var orderItems = groupedItems.Select(x =>
        {
            var catalogItem = catalogItems[x.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                Money(catalogItem.Price),
                x.Quantity);
        }).ToList();

        var address = request.ShipToAddress;
        var order = new Order(
            buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        return new CreateOrderResponse
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = Money(order.Total()),
            Currency = Currency
        };
    }

    public async Task<OrderPaymentResponse> PayAsync(
        int orderId,
        string buyerId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var gate = await _orderLock.AcquireAsync(orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        EnsureOwned(order, buyerId);

        if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
        {
            throw Conflict("ORDER_CANCELLED", "A cancelled order cannot be paid.");
        }

        if (order.PaymentStatus is OrderPaymentStatus.Authorized or OrderPaymentStatus.Captured or
            OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded or OrderPaymentStatus.RefundPending)
        {
            return MapOrder(order);
        }

        if ((request.Card == null) == !request.PaymentMethodId.HasValue)
        {
            throw BadRequest("PAYMENT_SOURCE_REQUIRED", "Supply exactly one of card or paymentMethodId.");
        }

        string? vaultId = null;
        PayPalCardDetails? card = null;
        if (request.PaymentMethodId.HasValue)
        {
            var saved = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
                x => x.Id == request.PaymentMethodId.Value && x.BuyerId == buyerId && x.DeletedAt == null,
                cancellationToken);
            if (saved == null)
            {
                throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved card does not exist, is removed, or belongs to another shopper.");
            }
            vaultId = saved.PayPalTokenId;
        }
        else
        {
            card = ConvertCard(request.Card!);
        }

        var total = Money(order.Total());
        var payment = order.GetOrCreatePayment(Currency);
        payment.MarkAttemptStarted();
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var authorization = await _payPal.AuthorizeOrderAsync(
                order.Id, payment.IntegrationId, payment.InvoiceId, total, Currency, card, vaultId, cancellationToken);

            if (authorization.Amount != total)
            {
                await _payPal.VoidAsync(
                    authorization.AuthorizationId,
                    $"eshop-{payment.IntegrationId}-amount-mismatch-void",
                    cancellationToken);
                payment.MarkFailed(authorization.PayPalOrderId, authorization.PayPalOrderStatus);
                order.MarkPaymentFailed();
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentApiException(502, "PAYPAL_AMOUNT_MISMATCH", "PayPal authorized an unexpected amount; the hold was released.");
            }

            payment.MarkAuthorized(
                authorization.PayPalOrderId,
                authorization.PayPalOrderStatus,
                authorization.AuthorizationId,
                authorization.AuthorizationStatus,
                authorization.Amount,
                authorization.CreatedAt,
                authorization.ExpiresAt,
                authorization.CardBrand,
                authorization.CardLastDigits);
            order.MarkPaymentAuthorized();
            await _db.SaveChangesAsync(cancellationToken);
            return MapOrder(order);
        }
        catch (PayPalApiException)
        {
            payment.MarkFailed(payment.PayPalOrderId, "FAILED");
            order.MarkPaymentFailed();
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<OrderPaymentResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var gate = await _orderLock.AcquireAsync(orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled) return MapOrder(order);
        if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
        {
            throw Conflict("ORDER_CANCELLED", "A cancelled order cannot be fulfilled.");
        }

        var payment = order.Payment;
        if (payment?.AuthorizationId == null || order.PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw Conflict("ORDER_NOT_AUTHORIZED", "The order must have an active payment authorization before fulfilment.");
        }

        PayPalCapture capture;
        if (payment.CaptureId != null)
        {
            capture = await _payPal.GetCaptureAsync(payment.CaptureId, cancellationToken);
        }
        else
        {
            await RenewAuthorizationWhenStaleAsync(order, payment, cancellationToken);
            try
            {
                capture = await _payPal.CaptureAsync(
                    payment.AuthorizationId!,
                    payment.Amount,
                    payment.Currency,
                    $"eshop-{payment.IntegrationId}-capture-{payment.AuthorizationId}",
                    cancellationToken);
            }
            catch (PayPalApiException ex) when (IsExpiredAuthorization(ex))
            {
                await RenewAuthorizationAfterCaptureFailureAsync(order, payment, cancellationToken);
                capture = await _payPal.CaptureAsync(
                    payment.AuthorizationId!,
                    payment.Amount,
                    payment.Currency,
                    $"eshop-{payment.IntegrationId}-capture-{payment.AuthorizationId}",
                    cancellationToken);
            }
        }

        payment.MarkCaptured(capture.Id, capture.Status, capture.Amount, capture.Fee, capture.NetAmount, capture.CreatedAt);
        if (!capture.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            await _db.SaveChangesAsync(cancellationToken);
            throw Conflict("CAPTURE_NOT_COMPLETED", $"PayPal capture {capture.Id} is {capture.Status}; retry fulfilment after it completes.");
        }

        if (capture.Amount != payment.Amount)
        {
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentApiException(502, "CAPTURE_AMOUNT_MISMATCH", "PayPal captured an amount different from the order total; investigate before fulfilment.");
        }

        order.MarkFulfilled();
        await _db.SaveChangesAsync(cancellationToken);
        return MapOrder(order);
    }

    public async Task<OrderPaymentResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var gate = await _orderLock.AcquireAsync(orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled) return MapOrder(order);
        if (order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled || order.Payment?.CaptureId != null)
        {
            throw Conflict("ORDER_ALREADY_CAPTURED", "A fulfilled or captured order must be refunded, not cancelled.");
        }

        if (order.Payment?.AuthorizationId != null &&
            !string.Equals(order.Payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            await _payPal.VoidAsync(
                order.Payment.AuthorizationId,
                $"eshop-{order.Payment.IntegrationId}-void-{order.Payment.AuthorizationId}",
                cancellationToken);
            order.Payment.MarkVoided("VOIDED");
        }

        order.MarkCancelled();
        await _db.SaveChangesAsync(cancellationToken);
        return MapOrder(order);
    }

    public async Task<RefundResponse> RefundAsync(
        int orderId,
        string buyerId,
        RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var gate = await _orderLock.AcquireAsync(orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        EnsureOwned(order, buyerId);

        var payment = order.Payment;
        if (order.FulfillmentStatus != OrderFulfillmentStatus.Fulfilled ||
            payment?.CaptureId == null ||
            payment.CapturedAmount == null ||
            !string.Equals(payment.CaptureStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict("ORDER_NOT_CAPTURED", "Only a completed captured payment can be refunded.");
        }

        var key = request.IdempotencyKey.Trim();
        if (key.Length == 0 || key.Length > 128)
        {
            throw BadRequest("INVALID_IDEMPOTENCY_KEY", "IdempotencyKey must contain 1 to 128 characters.");
        }

        var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == key);
        if (existing != null)
        {
            if (request.Amount.HasValue && Money(request.Amount.Value) != existing.Amount)
            {
                throw Conflict("IDEMPOTENCY_KEY_REUSED", "This idempotency key was already used with a different refund amount.");
            }
            if (existing.PayPalRefundId != null)
            {
                return MapRefund(order, payment, existing);
            }
        }

        var existingCommittedAmount = existing != null && existing.Status != "FAILED" ? existing.Amount : 0m;
        var remaining = Money(payment.CapturedAmount.Value - payment.RefundAmountCommitted + existingCommittedAmount);
        var amount = existing?.Amount ?? Money(request.Amount ?? remaining);
        if (amount <= 0 || amount > remaining)
        {
            throw Conflict("REFUND_AMOUNT_EXCEEDS_CAPTURE", $"Refund amount must be positive and cannot exceed {remaining:F2} {payment.Currency}.");
        }

        var refund = existing ?? payment.AddRefund(key, RefundRequestId(payment.IntegrationId, key), amount);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var payPalRefund = await _payPal.RefundAsync(
                payment.CaptureId,
                amount,
                payment.Currency,
                refund.PayPalRequestId,
                cancellationToken);
            refund.MarkProcessed(payPalRefund.Id, payPalRefund.Status, payPalRefund.Amount);
            var committed = payment.RefundAmountCommitted;
            var pending = payment.Refunds.Any(x =>
                x.Status != "FAILED" && !x.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase));
            order.MarkRefunded(committed >= payment.CapturedAmount.Value, pending);
            await _db.SaveChangesAsync(cancellationToken);
            return MapRefund(order, payment, refund);
        }
        catch (PayPalApiException ex) when ((int)ex.StatusCode < 500)
        {
            refund.MarkFailed();
            await _db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<MyOrderResponse>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .Include(x => x.Payment)!.ThenInclude(x => x!.Refunds)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(MapMyOrder).ToList();
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(
        string buyerId,
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var priorMethod = await _db.SavedPaymentMethods
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.BuyerId == buyerId, cancellationToken);
        var saved = await _payPal.SaveCardAsync(
            MerchantCustomerId(buyerId),
            priorMethod?.PayPalCustomerId,
            ConvertCard(request.Card),
            cancellationToken);
        var method = new SavedPaymentMethod(
            buyerId,
            saved.TokenId,
            saved.CustomerId,
            saved.Brand,
            saved.LastDigits,
            saved.Expiry,
            saved.Name);
        _db.SavedPaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return MapPaymentMethod(method);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var methods = await _db.SavedPaymentMethods
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return methods.Select(MapPaymentMethod).ToList();
    }

    public async Task DeletePaymentMethodAsync(
        int paymentMethodId,
        string buyerId,
        CancellationToken cancellationToken)
    {
        var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
            x => x.Id == paymentMethodId && x.BuyerId == buyerId,
            cancellationToken);
        if (method == null)
        {
            throw NotFound("PAYMENT_METHOD_NOT_FOUND", "The saved card does not exist or belongs to another shopper.");
        }
        if (!method.IsActive) return;

        try
        {
            await _payPal.DeletePaymentTokenAsync(method.PayPalTokenId, cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The desired state already exists in PayPal; still revoke it locally.
        }

        method.Delete();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw BadRequest("INVALID_DATE_RANGE", "from must be earlier than to.");
        }
        if (to - from > TimeSpan.FromDays(365 * 3 + 1) || from < DateTimeOffset.UtcNow.AddYears(-3))
        {
            throw BadRequest("DATE_RANGE_TOO_OLD", "PayPal transaction reporting supports the previous three years.");
        }

        var payPalTransactions = await _payPal.GetTransactionsAsync(from, to, cancellationToken);
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(x => x.Payment != null &&
                ((x.Payment.AuthorizationCreatedAt >= from && x.Payment.AuthorizationCreatedAt <= to) ||
                 (x.Payment.CapturedAt >= from && x.Payment.CapturedAt <= to) ||
                 x.Payment.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to)))
            .Include(x => x.Payment)!.ThenInclude(x => x!.Refunds)
            .ToListAsync(cancellationToken);

        var entries = new List<ReconciliationEntryResponse>();
        foreach (var transaction in payPalTransactions)
        {
            var order = orders.FirstOrDefault(x => TransactionMatchesOrder(transaction, x));
            entries.Add(new ReconciliationEntryResponse
            {
                ReconciliationStatus = order == null ? "MissingInEshop" : "Matched",
                Source = "PayPal",
                OrderId = order?.Id,
                PayPalTransactionId = transaction.Id,
                PayPalReferenceId = transaction.ReferenceId,
                InvoiceId = transaction.InvoiceId,
                TransactionType = transaction.EventCode,
                TransactionStatus = transaction.Status,
                TransactionDate = transaction.InitiatedAt,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Fee = transaction.Fee
            });
        }

        foreach (var order in orders)
        {
            foreach (var local in LocalTransactions(order, from, to))
            {
                if (payPalTransactions.Any(x => TransactionMatchesLocal(x, local.Id))) continue;
                entries.Add(new ReconciliationEntryResponse
                {
                    ReconciliationStatus = "MissingInPayPal",
                    Source = "eShop",
                    OrderId = order.Id,
                    PayPalTransactionId = local.Id,
                InvoiceId = order.Payment!.InvoiceId,
                    TransactionType = local.Type,
                    TransactionStatus = local.Status,
                    TransactionDate = local.Date,
                    Amount = local.Amount,
                    Currency = order.Payment!.Currency
                });
            }
        }

        return new ReconciliationResponse
        {
            From = from,
            To = to,
            Entries = entries.OrderBy(x => x.TransactionDate).ThenBy(x => x.PayPalTransactionId).ToList()
        };
    }

    private async Task RenewAuthorizationWhenStaleAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        var created = payment.AuthorizationCreatedAt ?? payment.CreatedAt;
        if (DateTimeOffset.UtcNow < created.AddDays(3)) return;
        if (payment.ReauthorizationCount > 0)
        {
            if (DateTimeOffset.UtcNow >= created.AddDays(29))
            {
                throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED", "The payment authorization is stale and has already been renewed once. Ask the shopper to pay again.");
            }
            return;
        }
        if (DateTimeOffset.UtcNow >= created.AddDays(29))
        {
            throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED", "The payment authorization is older than PayPal's 29-day renewal window. Ask the shopper to pay again.");
        }

        await ReauthorizeAsync(order, payment, cancellationToken);
    }

    private async Task RenewAuthorizationAfterCaptureFailureAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        var created = payment.AuthorizationCreatedAt ?? payment.CreatedAt;
        if (payment.ReauthorizationCount > 0 || DateTimeOffset.UtcNow < created.AddDays(3) || DateTimeOffset.UtcNow >= created.AddDays(29))
        {
            throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED", "PayPal reports that the authorization is no longer capturable and it cannot be renewed. Ask the shopper to pay again.");
        }
        await ReauthorizeAsync(order, payment, cancellationToken);
    }

    private async Task ReauthorizeAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var originalId = payment.AuthorizationId!;
            var renewed = await _payPal.ReauthorizeAsync(
                originalId,
                payment.Amount,
                payment.Currency,
                $"eshop-{payment.IntegrationId}-reauthorize-{originalId}",
                cancellationToken);
            payment.MarkReauthorized(
                renewed.AuthorizationId,
                renewed.AuthorizationStatus,
                renewed.Amount,
                renewed.CreatedAt,
                renewed.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            var reference = string.IsNullOrWhiteSpace(ex.DebugId) ? string.Empty : $" PayPal debug ID: {ex.DebugId}.";
            throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED", $"PayPal could not renew this authorization. Ask the shopper to pay again.{reference}");
        }
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(x => x.OrderItems)
            .Include(x => x.Payment)!.ThenInclude(x => x!.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return order ?? throw NotFound("ORDER_NOT_FOUND", "The order does not exist.");
    }

    private static void EnsureOwned(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Deliberately indistinguishable from a missing order to avoid disclosing another shopper's data.
            throw NotFound("ORDER_NOT_FOUND", "The order does not exist.");
        }
    }

    private static OrderPaymentResponse MapOrder(Order order)
    {
        var payment = order.Payment;
        return new OrderPaymentResponse
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            FulfillmentStatus = order.FulfillmentStatus.ToString(),
            Total = Money(order.Total()),
            Currency = payment?.Currency ?? string.Empty,
            PayPalOrderId = payment?.PayPalOrderId,
            AuthorizationId = payment?.AuthorizationId,
            AuthorizationStatus = payment?.AuthorizationStatus,
            AuthorizationExpiresAt = payment?.AuthorizationExpiresAt,
            CaptureId = payment?.CaptureId,
            CaptureStatus = payment?.CaptureStatus,
            CapturedAmount = payment?.CapturedAmount,
            PayPalFee = payment?.PayPalFee,
            NetAmount = payment?.NetAmount,
            RefundedAmount = payment?.RefundAmountCommitted ?? 0m,
            CardBrand = payment?.CardBrand,
            CardLastDigits = payment?.CardLastDigits,
            Refunds = payment?.Refunds.Select(MapRefundSummary).ToList() ?? new List<RefundSummaryResponse>()
        };
    }

    private static MyOrderResponse MapMyOrder(Order order)
    {
        var payment = MapOrder(order);
        return new MyOrderResponse
        {
            OrderId = payment.OrderId,
            PaymentStatus = payment.PaymentStatus,
            FulfillmentStatus = payment.FulfillmentStatus,
            Total = payment.Total,
            Currency = payment.Currency,
            PayPalOrderId = payment.PayPalOrderId,
            AuthorizationId = payment.AuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureId = payment.CaptureId,
            CaptureStatus = payment.CaptureStatus,
            CapturedAmount = payment.CapturedAmount,
            PayPalFee = payment.PayPalFee,
            NetAmount = payment.NetAmount,
            RefundedAmount = payment.RefundedAmount,
            CardBrand = payment.CardBrand,
            CardLastDigits = payment.CardLastDigits,
            Refunds = payment.Refunds,
            OrderDate = order.OrderDate,
            Items = order.OrderItems.Select(x => new MyOrderItemResponse
            {
                CatalogItemId = x.ItemOrdered.CatalogItemId,
                ProductName = x.ItemOrdered.ProductName,
                UnitPrice = x.UnitPrice,
                Quantity = x.Units
            }).ToList()
        };
    }

    private static RefundResponse MapRefund(Order order, Payment payment, PaymentRefund refund) => new()
    {
        RefundId = refund.PayPalRefundId!,
        OrderId = order.Id,
        Status = refund.Status,
        Amount = refund.Amount,
        Currency = payment.Currency,
        RemainingRefundableAmount = Money((payment.CapturedAmount ?? 0m) - payment.RefundAmountCommitted)
    };

    private static RefundSummaryResponse MapRefundSummary(PaymentRefund refund) => new()
    {
        RefundId = refund.PayPalRefundId,
        Status = refund.Status,
        Amount = refund.Amount,
        CreatedAt = refund.CreatedAt
    };

    private static PaymentMethodResponse MapPaymentMethod(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        LastDigits = method.LastDigits,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };

    private static PayPalCardDetails ConvertCard(CardRequest card)
    {
        var number = new string(card.Number.Where(char.IsDigit).ToArray());
        if (number.Length is < 13 or > 19 || card.SecurityCode.Length is < 3 or > 4 ||
            string.IsNullOrWhiteSpace(card.Name) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.City) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.PostalCode) ||
            card.BillingAddress.CountryCode.Length != 2)
        {
            throw BadRequest("INVALID_CARD", "Card number or security code format is invalid.");
        }
        if (!DateTime.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry) ||
            expiry.AddMonths(1) <= DateTime.UtcNow.Date)
        {
            throw BadRequest("INVALID_CARD_EXPIRY", "Card expiry must be a future month in yyyy-MM format.");
        }

        var address = card.BillingAddress;
        return new PayPalCardDetails(
            number,
            card.Expiry,
            card.SecurityCode,
            card.Name,
            new PayPalAddress(
                address.AddressLine1,
                address.AddressLine2,
                address.City,
                address.State,
                address.PostalCode,
                address.CountryCode));
    }

    private static bool IsExpiredAuthorization(PayPalApiException ex) =>
        ex.Issue?.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase) == true &&
        (ex.Issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ||
         ex.Issue.Contains("HONOR_PERIOD", StringComparison.OrdinalIgnoreCase));

    private static bool TransactionMatchesOrder(PayPalTransaction transaction, Order order)
    {
        var payment = order.Payment!;
        return TransactionMatchesLocal(transaction, payment.PayPalOrderId) ||
               TransactionMatchesLocal(transaction, payment.AuthorizationId) ||
               TransactionMatchesLocal(transaction, payment.CaptureId) ||
               payment.Refunds.Any(x => TransactionMatchesLocal(transaction, x.PayPalRefundId)) ||
               transaction.InvoiceId == payment.InvoiceId;
    }

    private static bool TransactionMatchesLocal(PayPalTransaction transaction, string? localId) =>
        (!string.IsNullOrWhiteSpace(localId) &&
         (transaction.Id == localId || transaction.ReferenceId == localId));

    private static IEnumerable<(string Id, string Type, string Status, DateTimeOffset Date, decimal Amount)> LocalTransactions(
        Order order,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var payment = order.Payment!;
        if (payment.AuthorizationId != null && payment.AuthorizationCreatedAt >= from && payment.AuthorizationCreatedAt <= to)
        {
            yield return (payment.AuthorizationId, "AUTHORIZATION", payment.AuthorizationStatus ?? string.Empty,
                payment.AuthorizationCreatedAt.Value, payment.AuthorizedAmount ?? payment.Amount);
        }
        if (payment.CaptureId != null && payment.CapturedAt >= from && payment.CapturedAt <= to)
        {
            yield return (payment.CaptureId, "CAPTURE", payment.CaptureStatus ?? string.Empty,
                payment.CapturedAt.Value, payment.CapturedAmount ?? payment.Amount);
        }
        foreach (var refund in payment.Refunds.Where(x => x.PayPalRefundId != null && x.CreatedAt >= from && x.CreatedAt <= to))
        {
            yield return (refund.PayPalRefundId!, "REFUND", refund.Status, refund.CreatedAt, refund.Amount);
        }
    }

    private string Currency => _options.Currency.ToUpperInvariant();
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string MerchantCustomerId(string buyerId) => "eshop_" + Hash(buyerId)[..32];
    private static string RefundRequestId(string integrationId, string key) => $"eshop-refund-{integrationId}-{Hash(key)[..32]}";
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static PaymentApiException BadRequest(string code, string message) => new(400, code, message);
    private static PaymentApiException NotFound(string code, string message) => new(404, code, message);
    private static PaymentApiException Conflict(string code, string message) => new(409, code, message);
}
