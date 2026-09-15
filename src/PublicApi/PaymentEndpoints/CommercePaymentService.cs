using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class CommercePaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly Regex ExpiryPattern = new("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
    private readonly CatalogContext _db;
    private readonly IPayPalGateway _gateway;
    private readonly PayPalOptions _options;
    private readonly TimeProvider _timeProvider;

    public CommercePaymentService(CatalogContext db, IPayPalGateway gateway,
        IOptions<PayPalOptions> options, TimeProvider timeProvider)
    {
        _db = db;
        _gateway = gateway;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(string buyerId,
        CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
            throw BadRequest("empty_order", "At least one catalog item is required.");
        if (request.ShippingAddress == null)
            throw BadRequest("shipping_address_required", "A shipping address is required.");
        ValidateShippingAddress(request.ShippingAddress);

        var requestedLines = request.Items
            .GroupBy(x => x.CatalogItemId)
            .Select(x => new { CatalogItemId = x.Key, Quantity = x.Sum(y => y.Quantity) })
            .ToList();
        if (requestedLines.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0 || x.Quantity > 1000))
            throw BadRequest("invalid_order_item", "Catalog item IDs and quantities must be positive; quantity cannot exceed 1000.");

        var ids = requestedLines.Select(x => x.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems.Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var missing = ids.Where(x => !catalogItems.ContainsKey(x)).ToList();
        if (missing.Count > 0)
            throw new CommerceException(404, "catalog_items_not_found",
                $"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var orderItems = requestedLines.Select(line =>
        {
            var item = catalogItems[line.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
                item.Price, line.Quantity);
        }).ToList();

        var address = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems);
        order.InitializePayment(GetCurrency());
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return new CreateOrderResponse(order.Id, ToDto(order));
    }

    public async Task<PayOrderResponse> PayAsync(string buyerId, int orderId,
        PayOrderRequest request, CancellationToken cancellationToken)
    {
        if ((request.Card == null) == !request.PaymentMethodId.HasValue)
            throw BadRequest("payment_source_required",
                "Supply exactly one of card or paymentMethodId.");

        CardPaymentSource? card = request.Card == null ? null : ToCardSource(request.Card);
        string? vaultId = null;
        if (request.PaymentMethodId.HasValue)
        {
            var method = await _db.PaymentMethods.SingleOrDefaultAsync(x =>
                x.Id == request.PaymentMethodId.Value && x.Buyer!.IdentityGuid == buyerId && x.DeletedAt == null,
                cancellationToken);
            if (method == null)
                throw new CommerceException(404, "payment_method_not_found",
                    "The saved payment method does not exist or does not belong to this shopper.");
            vaultId = method.PayPalVaultId;
        }

        return await WithOrderLock(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            if (order.FulfillmentStatus != OrderFulfillmentStatus.Unfulfilled)
                throw Conflict("order_not_payable", "A fulfilled or cancelled order cannot be paid.");
            var payment = order.Payment ?? throw Conflict("payment_not_initialized",
                "This order was not created through the payment-capable API.");

            if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
                or PaymentStatus.CapturePending or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
                return new PayOrderResponse(order.Id, ToDto(order));

            var total = EnsureCentAmount(order.Total());
            if (payment.PayPalOrderId == null)
            {
                var payPalOrder = await _gateway.CreateOrderAsync(payment.ExternalReference, total,
                    payment.Currency, cancellationToken);
                payment.RecordPayPalOrder(payPalOrder.Id, payPalOrder.Status, Now());
                await _db.SaveChangesAsync(cancellationToken);
            }

            PayPalAuthorizationResult authorization;
            try
            {
                authorization = await _gateway.AuthorizeOrderAsync(payment.ExternalReference, payment.PayPalOrderId!,
                    card, vaultId, cancellationToken);
            }
            catch (PayPalApiException ex) when (IsAlreadyAuthorized(ex))
            {
                var recoveredOrder = await _gateway.GetOrderAsync(payment.PayPalOrderId!, cancellationToken);
                authorization = recoveredOrder.Authorization
                    ?? throw Conflict("authorization_recovery_failed",
                        "PayPal says this order was already authorized but did not return the authorization. Use PayPal support with the stored PayPal order ID.");
            }

            ValidateMoney(authorization.Amount, authorization.Currency, total, payment.Currency,
                "authorization");
            payment.RecordAuthorization(authorization.Id, authorization.Status,
                authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt,
                authorization.OrderStatus, Now());
            await _db.SaveChangesAsync(cancellationToken);

            if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
                throw Conflict("authorization_not_held",
                    $"PayPal returned authorization status '{authorization.Status}'. Funds are not confirmed as held.");

            return new PayOrderResponse(order.Id, ToDto(order));
        });
    }

    public async Task<FulfilOrderResponse> FulfilAsync(int orderId,
        CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            var payment = order.Payment ?? throw Conflict("payment_not_initialized",
                "This order has no payment record and cannot be fulfilled through the payment API.");
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
                throw Conflict("order_cancelled", "A cancelled order cannot be fulfilled.");
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled)
                return new FulfilOrderResponse(order.Id, ToDto(order));
            if (payment.AuthorizationId == null)
                throw Conflict("payment_not_authorized", "Authorize the order before fulfilment.");

            var total = EnsureCentAmount(order.Total());
            PayPalCaptureResult? capture = null;

            if (payment.CaptureId != null)
            {
                capture = await _gateway.GetCaptureAsync(payment.CaptureId, cancellationToken);
            }
            else if (payment.PayPalOrderId != null)
            {
                var payPalOrder = await _gateway.GetOrderAsync(payment.PayPalOrderId, cancellationToken);
                capture = payPalOrder.Capture;
            }

            if (capture == null)
            {
                var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId,
                    cancellationToken);
                if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
                    throw Conflict("authorization_not_capturable",
                        $"PayPal authorization {authorization.Id} is '{authorization.Status}'. It cannot be captured; inspect the payment and obtain a new authorization if appropriate.");
                ValidateMoney(authorization.Amount, authorization.Currency, total, payment.Currency,
                    "authorization");
                payment.RecordAuthorization(authorization.Id, authorization.Status,
                    authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt,
                    payment.PayPalOrderStatus ?? string.Empty, Now());

                if (NeedsRenewal(payment, Now()))
                {
                    EnsureCanRenew(payment, Now());
                    authorization = await _gateway.ReauthorizeAsync(payment.ExternalReference, payment.AuthorizationId,
                        total, payment.Currency, cancellationToken);
                    ValidateMoney(authorization.Amount, authorization.Currency, total, payment.Currency,
                        "reauthorization");
                    if (!string.Equals(authorization.Status, "CREATED", StringComparison.OrdinalIgnoreCase))
                        throw Conflict("reauthorization_not_held",
                            $"PayPal returned reauthorization status '{authorization.Status}'. Do not fulfil; obtain a new authorization.");
                    payment.RecordAuthorization(authorization.Id, authorization.Status,
                        authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt,
                        payment.PayPalOrderStatus ?? string.Empty, Now(), renewed: true);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                capture = await _gateway.CaptureAsync(payment.ExternalReference, payment.AuthorizationId!, total,
                    payment.Currency, cancellationToken);
            }

            ValidateMoney(capture.Amount, capture.Currency, total, payment.Currency, "capture");
            payment.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee,
                capture.NetAmount, capture.CreatedAt, Now());
            if (string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                order.MarkFulfilled(Now());
            }
            await _db.SaveChangesAsync(cancellationToken);

            if (!string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(capture.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
                throw Conflict("capture_not_completed",
                    $"PayPal returned capture status '{capture.Status}'. The order was not marked fulfilled.");

            return new FulfilOrderResponse(order.Id, ToDto(order));
        });
    }

    public async Task<CancelOrderResponse> CancelAsync(int orderId,
        CancellationToken cancellationToken)
    {
        return await WithOrderLock(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            var payment = order.Payment ?? throw Conflict("payment_not_initialized",
                "This order has no payment record and cannot be cancelled through the payment API.");
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
                return new CancelOrderResponse(order.Id, ToDto(order));
            if (order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled || payment.CaptureId != null)
                throw Conflict("order_already_captured", "Captured orders must be refunded, not cancelled.");

            if (payment.AuthorizationId != null && payment.Status != PaymentStatus.Voided)
            {
                try
                {
                    await _gateway.VoidAsync(payment.ExternalReference, payment.AuthorizationId, cancellationToken);
                }
                catch (PayPalApiException)
                {
                    var current = await _gateway.GetAuthorizationAsync(payment.AuthorizationId,
                        cancellationToken);
                    if (!string.Equals(current.Status, "VOIDED", StringComparison.OrdinalIgnoreCase))
                        throw;
                }
                payment.RecordVoided("VOIDED", Now());
            }

            order.MarkCancelled(Now());
            await _db.SaveChangesAsync(cancellationToken);
            return new CancelOrderResponse(order.Id, ToDto(order));
        });
    }

    public async Task<RefundOrderResponse> RefundAsync(string buyerId, int orderId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        ValidateIdempotencyKey(request.IdempotencyKey);
        if (request.Note?.Length > 255)
            throw BadRequest("invalid_refund_note", "Refund notes cannot exceed 255 characters.");

        return await WithOrderLock(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            var payment = order.Payment ?? throw Conflict("payment_not_initialized",
                "This order has no payment record.");
            if (order.FulfillmentStatus != OrderFulfillmentStatus.Fulfilled ||
                payment.CaptureId == null || !payment.CapturedAmount.HasValue)
                throw Conflict("order_not_refundable", "Only a fulfilled, captured order can be refunded.");

            var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
            if (existing?.PayPalRefundId != null)
                return new RefundOrderResponse(existing.PayPalRefundId, order.Id, ToDto(order));
            if (existing != null && string.Equals(existing.Status, "FAILED", StringComparison.OrdinalIgnoreCase))
                throw Conflict("refund_key_already_failed",
                    "This idempotency key belongs to a failed refund. Use a new key for a new attempt.");

            var reserved = payment.Refunds
                .Where(x => x != existing &&
                    !string.Equals(x.Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.Amount);
            var remaining = payment.CapturedAmount.Value - reserved;
            var amount = existing?.Amount ?? request.Amount ?? remaining;
            amount = EnsureCentAmount(amount);
            if (amount <= 0 || amount > remaining)
                throw BadRequest("invalid_refund_amount",
                    $"Refund amount must be positive and cannot exceed the remaining captured amount of {remaining:0.00} {payment.Currency}.");

            var refund = existing ?? payment.StartRefund(request.IdempotencyKey, amount, Now());
            if (existing == null)
                await _db.SaveChangesAsync(cancellationToken);

            var result = await _gateway.RefundAsync(payment.ExternalReference, payment.CaptureId,
                PayPalRefundRequestId(order.Id, request.IdempotencyKey), amount,
                payment.Currency, request.Note, cancellationToken);
            ValidateMoney(result.Amount, result.Currency, amount, payment.Currency, "refund");
            refund.RecordResult(result.Id, result.Status, result.Amount, result.PayPalFee,
                result.NetAmount, Now());
            payment.RefreshRefundTotals(Now());
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                var currentCapture = await _gateway.GetCaptureAsync(payment.CaptureId,
                    cancellationToken);
                ValidateMoney(currentCapture.Amount, currentCapture.Currency,
                    payment.CapturedAmount.Value, payment.Currency, "capture refresh");
                payment.RecordCapture(currentCapture.Id, currentCapture.Status,
                    currentCapture.Amount, currentCapture.PayPalFee, currentCapture.NetAmount,
                    currentCapture.CreatedAt, Now());
                payment.RefreshRefundTotals(Now());
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (PayPalApiException)
            {
                // The refund is already durable and must be returned as successful. Its own
                // PayPal status is current; a later operation can refresh the capture status.
            }

            return new RefundOrderResponse(result.Id, order.Id, ToDto(order));
        });
    }

    public async Task<MyOrdersResponse> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems)
            .Include(x => x.Payment)!.ThenInclude(x => x!.Refunds)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return new MyOrdersResponse(orders.Select(ToDto).ToList());
    }

    public async Task<SavePaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        if (request.Card == null)
            throw BadRequest("card_required", "Card details are required.");
        var card = ToCardSource(request.Card);
        var result = await _gateway.SaveCardAsync(MerchantCustomerId(buyerId), card,
            cancellationToken);
        var buyer = await _db.Buyers.Include(x => x.PaymentMethods)
            .SingleOrDefaultAsync(x => x.IdentityGuid == buyerId, cancellationToken);
        if (buyer == null)
        {
            buyer = new Buyer(buyerId);
            _db.Buyers.Add(buyer);
        }
        var method = buyer.AddPaymentMethod(result.VaultId, result.Brand,
            result.LastDigits, result.Expiry, Now());
        await _db.SaveChangesAsync(cancellationToken);
        return new SavePaymentMethodResponse(method.Id, ToDto(method));
    }

    public async Task<PaymentMethodsResponse> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var methods = await _db.PaymentMethods.AsNoTracking()
            .Where(x => x.Buyer!.IdentityGuid == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return new PaymentMethodsResponse(methods.Select(ToDto).ToList());
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(x =>
            x.Id == paymentMethodId && x.Buyer!.IdentityGuid == buyerId && x.DeletedAt == null,
            cancellationToken);
        if (method == null)
            throw new CommerceException(404, "payment_method_not_found",
                "The saved payment method does not exist or does not belong to this shopper.");

        await _gateway.DeletePaymentTokenAsync(method.PayPalVaultId, cancellationToken);
        method.Delete(Now());
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to <= from)
            throw BadRequest("invalid_date_range", "The 'to' date must be later than 'from'.");

        var payPalTransactions = await _gateway.SearchTransactionsAsync(from, to,
            cancellationToken);
        var orders = await _db.Orders.AsNoTracking()
            .Include(x => x.Payment)!.ThenInclude(x => x!.Refunds)
            .Where(x => x.Payment != null &&
                (x.Payment.CreatedAt <= to || x.Payment.Refunds.Any(r => r.CreatedAt <= to)))
            .ToListAsync(cancellationToken);

        var idToOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            AddId(idToOrder, payment.PayPalOrderId, order.Id);
            AddId(idToOrder, payment.ExternalReference, order.Id);
            AddId(idToOrder, payment.AuthorizationId, order.Id);
            AddId(idToOrder, payment.CaptureId, order.Id);
            foreach (var refund in payment.Refunds)
                AddId(idToOrder, refund.PayPalRefundId, order.Id);
        }

        int? MatchOrder(PayPalTransactionResult transaction)
        {
            foreach (var id in new[] { transaction.TransactionId, transaction.ReferenceId })
                if (id != null && idToOrder.TryGetValue(id, out var orderId)) return orderId;
            foreach (var external in new[] { transaction.InvoiceId, transaction.CustomId })
                if (external != null && idToOrder.TryGetValue(external, out var orderId)) return orderId;
            return null;
        }

        var paypalDtos = payPalTransactions.Select(x =>
        {
            var orderId = MatchOrder(x);
            return new ReconciliationTransactionDto(x.TransactionId, x.ReferenceId, x.EventCode,
                x.Status, x.GrossAmount, x.FeeAmount, x.Currency, x.InitiatedAt, orderId,
                orderId.HasValue ? "Matched" : "PayPalOnly");
        }).ToList();

        var paypalIds = payPalTransactions.Select(x => x.TransactionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localDtos = new List<LocalTransactionDto>();
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            AddLocal(localDtos, paypalIds, order.Id, "Authorization", payment.AuthorizationId,
                payment.AuthorizationStatus, payment.AuthorizedAmount, payment.Currency,
                payment.AuthorizationCreatedAt, from, to);
            AddLocal(localDtos, paypalIds, order.Id, "Capture", payment.CaptureId,
                payment.CaptureStatus, payment.CapturedAmount, payment.Currency,
                payment.CapturedAt, from, to);
            foreach (var refund in payment.Refunds)
                AddLocal(localDtos, paypalIds, order.Id, "Refund", refund.PayPalRefundId,
                    refund.Status, refund.Amount, payment.Currency, refund.CreatedAt, from, to);
        }

        return new ReconciliationResponse(from, to, Now(), paypalDtos, localDtos);
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(x => x.OrderItems)
            .Include(x => x.Payment)!.ThenInclude(x => x!.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return order ?? throw new CommerceException(404, "order_not_found", "Order not found.");
    }

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new CommerceException(404, "order_not_found", "Order not found.");
    }

    private string GetCurrency()
    {
        var currency = _options.Currency.Trim().ToUpperInvariant();
        if (currency.Length != 3)
            throw new CommerceException(503, "payment_configuration_invalid",
                "PayPal:Currency must be configured as a three-letter currency code.");
        return currency;
    }

    private static CardPaymentSource ToCardSource(CardRequest request)
    {
        var number = new string(request.Number.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 300 ||
            number.Length is < 13 or > 19 || !number.All(char.IsDigit) ||
            !ExpiryPattern.IsMatch(request.Expiry) ||
            request.SecurityCode.Length is < 3 or > 4 || !request.SecurityCode.All(char.IsDigit) ||
            request.BillingAddress == null)
            throw BadRequest("invalid_card", "Card details are incomplete or invalid.");

        var address = request.BillingAddress;
        if (string.IsNullOrWhiteSpace(address.AddressLine1) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.PostalCode) ||
            address.CountryCode.Length != 2)
            throw BadRequest("invalid_billing_address", "A complete card billing address is required.");

        return new CardPaymentSource(request.Name, number, request.Expiry,
            request.SecurityCode, new CardBillingAddress(address.AddressLine1,
                address.AddressLine2, address.City, address.State, address.PostalCode,
                address.CountryCode));
    }

    private static void ValidateShippingAddress(ShippingAddressRequest address)
    {
        if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
            throw BadRequest("invalid_shipping_address", "Street, city, country and zipCode are required.");
    }

    private static decimal EnsureCentAmount(decimal amount)
    {
        if (decimal.Round(amount, 2, MidpointRounding.AwayFromZero) != amount)
            throw BadRequest("amount_precision_invalid", "Amounts must not have more than two decimal places.");
        return amount;
    }

    private static void ValidateMoney(decimal actualAmount, string actualCurrency,
        decimal expectedAmount, string expectedCurrency, string operation)
    {
        if (actualAmount != expectedAmount ||
            !string.Equals(actualCurrency, expectedCurrency, StringComparison.OrdinalIgnoreCase))
            throw new CommerceException(502, "paypal_amount_mismatch",
                $"PayPal's {operation} amount did not match the order amount. Do not continue; reconcile the PayPal transaction.");
    }

    private static bool NeedsRenewal(OrderPayment payment, DateTimeOffset now) =>
        payment.AuthorizationCreatedAt.HasValue && now >= payment.AuthorizationCreatedAt.Value.AddDays(3);

    private static void EnsureCanRenew(OrderPayment payment, DateTimeOffset now)
    {
        if (payment.AuthorizationRenewalCount > 0)
            throw Conflict("authorization_cannot_be_renewed",
                "This authorization has already been renewed once and its new three-day honor period has elapsed. Obtain a new authorization from the shopper before fulfilment.");
        var original = payment.OriginalAuthorizationCreatedAt ?? payment.AuthorizationCreatedAt;
        if (!original.HasValue || now >= original.Value.AddDays(29) ||
            payment.AuthorizationExpiresAt.HasValue && now >= payment.AuthorizationExpiresAt.Value)
            throw Conflict("authorization_cannot_be_renewed",
                "The authorization is outside PayPal's reauthorization window. Obtain a new authorization from the shopper before fulfilment.");
    }

    private static bool IsAlreadyAuthorized(PayPalApiException ex) =>
        string.Equals(ex.Issue, "ORDER_ALREADY_AUTHORIZED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ex.ErrorName, "ORDER_ALREADY_AUTHORIZED", StringComparison.OrdinalIgnoreCase);

    private static void ValidateIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 108)
            throw BadRequest("invalid_idempotency_key",
                "idempotencyKey is required and cannot exceed 108 characters.");
    }

    private static string PayPalRefundRequestId(int orderId, string callerKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(callerKey)))
            .ToLowerInvariant();
        return $"ESHOP-{orderId}-REFUND-{hash}";
    }

    private static string MerchantCustomerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private DateTimeOffset Now() => _timeProvider.GetUtcNow();

    private static OrderDto ToDto(Order order)
    {
        var payment = order.Payment;
        return new OrderDto(order.Id, order.OrderDate, order.Total(), payment?.Currency ?? string.Empty,
            order.FulfillmentStatus.ToString(), order.FulfilledAt, order.CancelledAt,
            payment == null ? null : new PaymentDto(payment.Status.ToString(), payment.Currency,
                payment.PayPalOrderId, payment.PayPalOrderStatus, payment.AuthorizationId,
                payment.AuthorizationStatus, payment.AuthorizedAmount, payment.AuthorizationExpiresAt,
                payment.CaptureId, payment.CaptureStatus, payment.CapturedAmount, payment.PayPalFee,
                payment.NetAmount, payment.RefundedAmount,
                payment.Refunds.Select(x => new RefundDto(x.PayPalRefundId, x.Status, x.Amount,
                    x.PayPalFee, x.NetAmount, x.CreatedAt)).ToList()),
            order.OrderItems.Select(x => new OrderItemDto(x.ItemOrdered.CatalogItemId,
                x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList());
    }

    private static PaymentMethodDto ToDto(PaymentMethod method) =>
        new(method.Id, method.Brand, method.LastDigits, method.Expiry, method.CreatedAt);

    private static void AddId(IDictionary<string, int> target, string? id, int orderId)
    {
        if (!string.IsNullOrWhiteSpace(id)) target[id] = orderId;
    }

    private static void AddLocal(ICollection<LocalTransactionDto> target,
        ISet<string> paypalIds, int orderId, string kind, string? id, string? status,
        decimal? amount, string currency, DateTimeOffset? occurredAt,
        DateTimeOffset from, DateTimeOffset to)
    {
        if (id == null || !amount.HasValue || !occurredAt.HasValue ||
            occurredAt < from || occurredAt > to) return;
        target.Add(new LocalTransactionDto(orderId, kind, id, status ?? "UNKNOWN", amount.Value,
            currency, occurredAt, paypalIds.Contains(id) ? "Matched" : "EShopOnly"));
    }

    private static CommerceException BadRequest(string code, string message) => new(400, code, message);
    private static CommerceException Conflict(string code, string message) => new(409, code, message);

    private static async Task<T> WithOrderLock<T>(int orderId, Func<Task<T>> action)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
