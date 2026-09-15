using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.PayPal;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentOperations
{
    private static readonly Regex CardNumberPattern = new("^[0-9]{13,19}$", RegexOptions.Compiled);
    private static readonly Regex SecurityCodePattern = new("^[0-9]{3,4}$", RegexOptions.Compiled);
    private static readonly Regex CountryCodePattern = new("^[A-Z]{2}$", RegexOptions.Compiled);
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly PaymentOperationLock _operationLock;
    private readonly IUriComposer _uriComposer;
    private readonly string _currency;

    public PaymentOperations(CatalogContext db, IPayPalClient payPal, PaymentOperationLock operationLock,
        IUriComposer uriComposer, IOptions<PayPalSettings> settings)
    {
        _db = db;
        _payPal = payPal;
        _operationLock = operationLock;
        _uriComposer = uriComposer;
        _currency = settings.Value.Currency.ToUpperInvariant();
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw BadRequest("EMPTY_ORDER", "At least one catalog item is required.");
        if (request.ShippingAddress is null)
            throw BadRequest("SHIPPING_ADDRESS_REQUIRED", "A shipping address is required.");
        ValidateShippingAddress(request.ShippingAddress);

        var quantities = request.Items
            .GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        if (quantities.Any(x => x.Key <= 0 || x.Value <= 0))
            throw BadRequest("INVALID_QUANTITY", "Catalog item IDs and quantities must be positive.");

        var catalogIds = quantities.Keys.ToArray();
        var catalogItems = await _db.CatalogItems.Where(x => catalogIds.Contains(x.Id)).ToListAsync(cancellationToken);
        var missingIds = catalogIds.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missingIds.Length > 0)
            throw new ApiProblemException(404, "CATALOG_ITEMS_NOT_FOUND",
                $"Catalog item(s) not found: {string.Join(", ", missingIds)}.");

        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, _uriComposer.ComposePicUri(item.PictureUri)),
            decimal.Round(item.Price, 2, MidpointRounding.AwayFromZero), quantities[item.Id])).ToList();
        var address = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return new CreateOrderResponse(order.Id, order.Status.ToString(), order.Total(), _currency);
    }

    public async Task<PayOrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var gate = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);

        if (order.Status == OrderStatus.Authorized || order.Payment?.Status == PaymentStatus.Authorized)
        {
            if (order.Status != OrderStatus.Authorized) order.MarkAuthorized();
            await _db.SaveChangesAsync(cancellationToken);
            return new PayOrderResponse(order.Id, order.Status.ToString(), MapPayment(order.Payment!));
        }
        if (order.Status != OrderStatus.AwaitingPayment)
            throw Conflict("ORDER_NOT_PAYABLE", $"Order {order.Id} cannot be paid while it is {order.Status}.");

        var payment = order.StartPayment(_currency);
        if (!string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            var existing = await _payPal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            RecordAuthorization(payment, existing, payment.PayPalOrderStatus ?? "COMPLETED");
            if (payment.Status == PaymentStatus.Authorized) order.MarkAuthorized();
            await _db.SaveChangesAsync(cancellationToken);
            if (payment.Status != PaymentStatus.Authorized)
                throw Conflict("AUTHORIZATION_NOT_READY",
                    $"PayPal reports authorization {existing.Id} as {existing.Status}; retry after it reaches CREATED.");
            return new PayOrderResponse(order.Id, order.Status.ToString(), MapPayment(payment));
        }

        var card = await ResolveCardAsync(buyerId, request, cancellationToken);
        if (string.IsNullOrWhiteSpace(payment.PayPalOrderId))
        {
            var payPalOrder = await _payPal.CreateOrderAsync(order.Id, payment.MerchantReference,
                order.Total(), _currency, cancellationToken);
            payment.SetPayPalOrder(payPalOrder.Id, payPalOrder.Status);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var authorizationOrder = await _payPal.AuthorizeOrderAsync(payment.PayPalOrderId!, card,
            $"{payment.MerchantReference}-authorize", cancellationToken);
        if (authorizationOrder.Status == "PAYER_ACTION_REQUIRED" ||
            authorizationOrder.Links.Any(x => x.Rel == "payer-action"))
            throw new PayPalPayerActionRequiredException();

        var authorization = authorizationOrder.PurchaseUnits
            .SelectMany(x => x.Payments?.Authorizations ?? new List<PayPalAuthorization>())
            .SingleOrDefault() ?? throw new PayPalApiException(HttpStatusCode.BadGateway, "INVALID_RESPONSE",
                "PayPal did not return the authorization created for this order.", null);
        RecordAuthorization(payment, authorization, authorizationOrder.Status);
        await _db.SaveChangesAsync(cancellationToken);
        if (payment.Status != PaymentStatus.Authorized)
            throw Conflict("AUTHORIZATION_NOT_READY",
                $"PayPal reports authorization {authorization.Id} as {authorization.Status}; the order remains awaiting payment.");
        order.MarkAuthorized();
        await _db.SaveChangesAsync(cancellationToken);
        return new PayOrderResponse(order.Id, order.Status.ToString(), MapPayment(payment));
    }

    public async Task<FulfilOrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var gate = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Fulfilled)
            return new FulfilOrderResponse(order.Id, order.Status.ToString(), MapPayment(order.Payment!));
        if (order.Status != OrderStatus.Authorized || order.Payment is null ||
            string.IsNullOrWhiteSpace(order.Payment.AuthorizationId))
            throw Conflict("ORDER_NOT_FULFILLABLE", $"Order {order.Id} is not in an authorized state.");

        var payment = order.Payment;
        if (!string.IsNullOrWhiteSpace(payment.CaptureId))
        {
            var existingCapture = await _payPal.GetCaptureAsync(payment.CaptureId, cancellationToken);
            RecordCapture(payment, existingCapture);
            await CompleteFulfilmentIfCapturedAsync(order, existingCapture, cancellationToken);
            return new FulfilOrderResponse(order.Id, order.Status.ToString(), MapPayment(payment));
        }

        var authorization = await _payPal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
        RecordAuthorization(payment, authorization, payment.PayPalOrderStatus ?? "COMPLETED");
        if (authorization.Status != "CREATED")
            throw Conflict("AUTHORIZATION_NOT_CAPTURABLE",
                $"PayPal reports authorization {authorization.Id} as {authorization.Status}. Collect a new payment or cancel the order.");

        if (IsStale(authorization))
            authorization = await ReauthorizeAsync(order, payment, authorization, cancellationToken);

        PayPalCapture capture;
        try
        {
            capture = await _payPal.CaptureAsync(authorization.Id, payment.MerchantReference,
                order.Total(), _currency,
                $"{payment.MerchantReference}-capture", cancellationToken);
        }
        catch (PayPalApiException ex) when (ex.IsAuthorizationStale && payment.ReauthorizationCount == 0)
        {
            authorization = await ReauthorizeAsync(order, payment, authorization, cancellationToken);
            capture = await _payPal.CaptureAsync(authorization.Id, payment.MerchantReference,
                order.Total(), _currency,
                $"{payment.MerchantReference}-capture", cancellationToken);
        }

        RecordCapture(payment, capture);
        await CompleteFulfilmentIfCapturedAsync(order, capture, cancellationToken);
        return new FulfilOrderResponse(order.Id, order.Status.ToString(), MapPayment(payment));
    }

    public async Task<CancelOrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var gate = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
            return new CancelOrderResponse(order.Id, order.Status.ToString(),
                order.Payment is null ? null : MapPayment(order.Payment));
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded ||
            order.Payment?.CaptureId is not null)
            throw Conflict("ORDER_ALREADY_CAPTURED", "A captured order cannot be cancelled; refund it instead.");

        if (order.Payment?.AuthorizationId is { Length: > 0 } authorizationId)
        {
            var authorization = await _payPal.GetAuthorizationAsync(authorizationId, cancellationToken);
            if (authorization.Status is "CAPTURED" or "PARTIALLY_CAPTURED")
                throw Conflict("ORDER_ALREADY_CAPTURED", "PayPal reports captured funds; refund the payment instead.");
            if (authorization.Status != "VOIDED")
                await _payPal.VoidAsync(authorizationId, $"{order.Payment.MerchantReference}-void", cancellationToken);
            order.Payment.MarkVoided("VOIDED");
        }
        order.MarkCancelled();
        await _db.SaveChangesAsync(cancellationToken);
        return new CancelOrderResponse(order.Id, order.Status.ToString(),
            order.Payment is null ? null : MapPayment(order.Payment));
    }

    public async Task<RefundOrderResponse> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var gate = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        ValidateIdempotencyKey(request.IdempotencyKey);
        if (request.Note?.Length > 255) throw BadRequest("INVALID_NOTE", "Refund note cannot exceed 255 characters.");
        var order = await GetOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);
        var payment = order.Payment;
        if (payment?.CaptureId is null)
            throw Conflict("ORDER_NOT_REFUNDABLE", "Only a fulfilled order with captured funds can be refunded.");

        var existing = payment.FindRefund(request.IdempotencyKey);
        if (existing is not null)
        {
            if (request.Amount.HasValue && request.Amount.Value != existing.Amount)
                throw Conflict("IDEMPOTENCY_KEY_REUSED",
                    "This idempotency key was already used with a different refund amount.");
            if (!string.IsNullOrWhiteSpace(existing.PayPalRefundId))
            {
                var current = await _payPal.GetRefundAsync(existing.PayPalRefundId, cancellationToken);
                payment.CompleteRefund(existing, current.Id, current.Status, ReadMoney(current.Amount, _currency),
                    current.UpdateTime ?? current.CreateTime);
                ApplyRefundOrderState(order);
                await _db.SaveChangesAsync(cancellationToken);
                return new RefundOrderResponse(existing.Id, order.Id, order.Status.ToString(), MapPayment(payment));
            }
        }

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            throw Conflict("ORDER_NOT_REFUNDABLE", "Only a fulfilled order with captured funds can be refunded.");

        var amount = existing?.Amount ?? request.Amount ?? payment.RefundableAmount;
        var refund = existing ?? payment.StartRefund(request.IdempotencyKey, amount);
        if (existing is null) await _db.SaveChangesAsync(cancellationToken);

        var payPalRefund = await _payPal.RefundAsync(payment.CaptureId, payment.MerchantReference,
            refund.Amount, _currency,
            request.IdempotencyKey, request.Note, cancellationToken);
        payment.CompleteRefund(refund, payPalRefund.Id, payPalRefund.Status,
            ReadMoney(payPalRefund.Amount, _currency), payPalRefund.UpdateTime ?? payPalRefund.CreateTime);
        ApplyRefundOrderState(order);
        await _db.SaveChangesAsync(cancellationToken);
        return new RefundOrderResponse(refund.Id, order.Id, order.Status.ToString(), MapPayment(payment));
    }

    public async Task<SavePaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        ValidateCard(request.Card);
        using var gate = await _operationLock.AcquireAsync($"payment-methods:{buyerId}", cancellationToken);
        var customerId = await _db.SavedPaymentMethods
            .Where(x => x.BuyerId == buyerId)
            .Select(x => x.PayPalCustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        var token = await _payPal.CreatePaymentTokenAsync(buyerId, customerId, MapCard(request.Card!),
            $"eshop-vault-{Guid.NewGuid():N}", cancellationToken);
        var card = token.PaymentSource.Card;
        if (card is null || string.IsNullOrWhiteSpace(token.Id) || string.IsNullOrWhiteSpace(token.Customer.Id) ||
            string.IsNullOrWhiteSpace(card.Brand) || string.IsNullOrWhiteSpace(card.LastDigits))
            throw new PayPalApiException(HttpStatusCode.BadGateway, "INVALID_RESPONSE",
                "PayPal did not return a recognizable saved card.", null);
        var method = new SavedPaymentMethod(buyerId, token.Id, token.Customer.Id, card.Brand,
            card.LastDigits, card.Expiry);
        _db.SavedPaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return new SavePaymentMethodResponse(method.Id, method.Brand, method.LastDigits, method.Expiry);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken) => await _db.SavedPaymentMethods
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PaymentMethodResponse(x.Id, x.Brand, x.LastDigits, x.Expiry, x.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        using var gate = await _operationLock.AcquireAsync($"payment-method:{paymentMethodId}", cancellationToken);
        var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId,
            cancellationToken);
        if (method is null || method.BuyerId != buyerId || method.IsDeleted)
            throw new ApiProblemException(404, "PAYMENT_METHOD_NOT_FOUND", "Payment method not found.");
        await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken);
        method.Delete();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
            .Include(x => x.Payment!).ThenInclude(x => x.Refunds)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(MapOrder).ToList();
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw BadRequest("INVALID_DATE_RANGE", "'from' must be earlier than 'to'.");
        var payPalTransactions = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        var allOrders = await _db.Orders.AsNoTracking()
            .Include(x => x.Payment!).ThenInclude(x => x.Refunds)
            .ToListAsync(cancellationToken);
        var orders = allOrders.Where(x => IsInRange(x, from, to)).ToList();
        var entries = new List<ReconciliationEntryResponse>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var detail in payPalTransactions)
        {
            var info = detail.TransactionInfo;
            var order = allOrders.FirstOrDefault(x => Matches(x, info));
            if (order is not null) matchedOrderIds.Add(order.Id);
            entries.Add(new ReconciliationEntryResponse(order is null ? "PayPalOnly" : "Matched",
                order?.Id, order?.Status.ToString(), info.TransactionId, info.PayPalReferenceId,
                info.TransactionEventCode, info.TransactionStatus,
                ParseOptionalMoney(info.TransactionAmount), info.TransactionAmount?.CurrencyCode,
                ParseOptionalMoney(info.FeeAmount), info.TransactionInitiationDate));
        }

        entries.AddRange(orders.Where(x => !matchedOrderIds.Contains(x.Id)).Select(x =>
            new ReconciliationEntryResponse("EShopOnly", x.Id, x.Status.ToString(), null,
                x.Payment?.PayPalOrderId, null, x.Payment?.Status.ToString(), x.Payment?.Amount,
                x.Payment?.Currency ?? _currency, x.Payment?.PayPalFee,
                x.Payment?.CapturedAt ?? x.Payment?.AuthorizationCreatedAt ?? x.OrderDate)));
        return new ReconciliationResponse(from, to, entries
            .OrderBy(x => x.TransactionTime)
            .ThenBy(x => x.OrderId)
            .ToList());
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
            .Include(x => x.Payment!).ThenInclude(x => x.Refunds)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return order ?? throw new ApiProblemException(404, "ORDER_NOT_FOUND", "Order not found.");
    }

    private async Task<PayPalCard> ResolveCardAsync(string buyerId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PaymentMethodId.HasValue == (request.Card is not null))
            throw BadRequest("PAYMENT_SOURCE_REQUIRED",
                "Provide exactly one of paymentMethodId or card.");
        if (request.Card is not null)
        {
            ValidateCard(request.Card);
            return MapCard(request.Card);
        }

        var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
            x.Id == request.PaymentMethodId && x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
        if (method is null)
            throw new ApiProblemException(404, "PAYMENT_METHOD_NOT_FOUND", "Payment method not found.");
        return new PayPalCard
        {
            VaultId = method.PayPalPaymentTokenId,
            StoredCredential = new PayPalStoredCredential("CUSTOMER", "ONE_TIME", "SUBSEQUENT")
        };
    }

    private async Task<PayPalAuthorization> ReauthorizeAsync(Order order, Payment payment,
        PayPalAuthorization authorization, CancellationToken cancellationToken)
    {
        if (payment.ReauthorizationCount > 0)
            throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                "This authorization has already been renewed and is stale. Cancel the order and ask the shopper to place and pay for a new order.");
        if (authorization.ExpirationTime <= DateTimeOffset.UtcNow)
            throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                "The authorization has expired and PayPal can no longer renew it. Cancel the order and ask the shopper to place and pay for a new order.");
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(authorization.Id, order.Total(), _currency,
                $"{payment.MerchantReference}-reauthorize", cancellationToken);
            RecordAuthorization(payment, renewed, payment.PayPalOrderStatus ?? "COMPLETED", true);
            await _db.SaveChangesAsync(cancellationToken);
            if (renewed.Status != "CREATED")
                throw Conflict("AUTHORIZATION_RENEWAL_NOT_READY",
                    $"PayPal reports renewed authorization {renewed.Id} as {renewed.Status}. Do not fulfil until it is CREATED.");
            return renewed;
        }
        catch (PayPalApiException ex)
        {
            var reference = string.IsNullOrWhiteSpace(ex.DebugId) ? string.Empty : $" PayPal debug ID: {ex.DebugId}.";
            throw Conflict("AUTHORIZATION_CANNOT_BE_RENEWED",
                $"PayPal could not renew the authorization. Cancel the order and ask the shopper to place and pay for a new order.{reference}");
        }
    }

    private async Task CompleteFulfilmentIfCapturedAsync(Order order, PayPalCapture capture,
        CancellationToken cancellationToken)
    {
        if (capture.Status == "COMPLETED")
        {
            order.MarkFulfilled();
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }
        await _db.SaveChangesAsync(cancellationToken);
        throw Conflict("CAPTURE_NOT_COMPLETED",
            $"PayPal reports capture {capture.Id} as {capture.Status}. Retry fulfilment after the capture reaches COMPLETED.");
    }

    private void RecordAuthorization(Payment payment, PayPalAuthorization authorization,
        string orderStatus, bool isReauthorization = false)
    {
        var amount = ReadMoney(authorization.Amount, _currency);
        payment.Authorize(authorization.Id, authorization.Status, amount, authorization.CreateTime,
            authorization.ExpirationTime, orderStatus, isReauthorization);
    }

    private void RecordCapture(Payment payment, PayPalCapture capture)
    {
        var amount = ReadMoney(capture.Amount, _currency);
        payment.RecordCapture(capture.Id, capture.Status, amount,
            ParseOptionalMoney(capture.SellerReceivableBreakdown?.PayPalFee),
            ParseOptionalMoney(capture.SellerReceivableBreakdown?.NetAmount),
            capture.UpdateTime ?? capture.CreateTime);
    }

    private static bool IsStale(PayPalAuthorization authorization) =>
        authorization.CreateTime.HasValue && authorization.CreateTime.Value.AddDays(3) <= DateTimeOffset.UtcNow;

    private static decimal ReadMoney(PayPalMoney money, string expectedCurrency)
    {
        if (!string.Equals(money.CurrencyCode, expectedCurrency, StringComparison.OrdinalIgnoreCase) ||
            !decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            throw new PayPalApiException(HttpStatusCode.BadGateway, "INVALID_MONEY_RESPONSE",
                "PayPal returned an unexpected amount or currency.", null);
        return amount;
    }

    private static decimal? ParseOptionalMoney(PayPalMoney? money) => money is not null &&
        decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    private static void ApplyRefundOrderState(Order order)
    {
        if (order.Payment?.Status is PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            order.ApplyRefundState();
    }

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new ApiProblemException(404, "ORDER_NOT_FOUND", "Order not found.");
    }

    private static void ValidateShippingAddress(ShippingAddressRequest address)
    {
        if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
            throw BadRequest("INVALID_SHIPPING_ADDRESS", "Street, city, country and zipCode are required.");
    }

    private static void ValidateCard(CardRequest? card)
    {
        if (card is null || string.IsNullOrWhiteSpace(card.Name) || !CardNumberPattern.IsMatch(card.Number ?? string.Empty) ||
            !SecurityCodePattern.IsMatch(card.SecurityCode ?? string.Empty) || card.BillingAddress is null)
            throw BadRequest("INVALID_CARD", "Card name, number, expiry, security code and billing address are required.");
        if (!DateTime.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry.AddMonths(1) <= DateTime.UtcNow.Date)
            throw BadRequest("INVALID_CARD_EXPIRY", "Card expiry must be a future date in YYYY-MM format.");
        if (!CountryCodePattern.IsMatch(card.BillingAddress.CountryCode?.ToUpperInvariant() ?? string.Empty))
            throw BadRequest("INVALID_COUNTRY_CODE", "Billing countryCode must be a two-letter country code.");
    }

    private static void ValidateIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 108 || key.Any(char.IsControl))
            throw BadRequest("INVALID_IDEMPOTENCY_KEY", "idempotencyKey must contain 1 to 108 characters.");
    }

    private static PayPalCard MapCard(CardRequest card) => new()
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = new PayPalBillingAddress
        {
            AddressLine1 = card.BillingAddress.AddressLine1,
            AddressLine2 = card.BillingAddress.AddressLine2,
            City = card.BillingAddress.City,
            State = card.BillingAddress.State,
            PostalCode = card.BillingAddress.PostalCode,
            CountryCode = card.BillingAddress.CountryCode.ToUpperInvariant()
        }
    };

    private OrderResponse MapOrder(Order order) => new(order.Id, order.OrderDate, order.Status.ToString(),
        order.Total(), order.Payment?.Currency ?? _currency,
        order.OrderItems.Select(x => new OrderItemResponse(x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
        order.Payment is null ? null : MapPayment(order.Payment));

    private static PaymentResponse MapPayment(Payment payment) => new(payment.Status.ToString(),
        payment.PayPalOrderId, payment.AuthorizationId, payment.AuthorizationStatus,
        payment.AuthorizationExpiresAt, payment.CaptureId, payment.CaptureStatus, payment.Amount,
        payment.CapturedAmount, payment.PayPalFee, payment.NetAmount, payment.RefundedAmount,
        payment.RefundableAmount, payment.Refunds.OrderBy(x => x.RequestedAt)
            .Select(x => new RefundResponse(x.Id, x.Status, x.Amount, x.Currency,
                x.RequestedAt, x.CompletedAt)).ToList());

    private static bool IsInRange(Order order, DateTimeOffset from, DateTimeOffset to) =>
        InRange(order.OrderDate, from, to) ||
        InRange(order.Payment?.AuthorizationCreatedAt, from, to) ||
        InRange(order.Payment?.CapturedAt, from, to) ||
        (order.Payment?.Refunds.Any(x => InRange(x.RequestedAt, from, to) || InRange(x.CompletedAt, from, to)) ?? false);

    private static bool InRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to) =>
        value.HasValue && value.Value >= from && value.Value <= to;

    private static bool Matches(Order order, PayPalTransactionInfo info)
    {
        var payment = order.Payment;
        if (payment is null) return false;
        return info.InvoiceId == payment.MerchantReference || info.CustomField == payment.MerchantReference ||
               info.TransactionId == payment.PayPalOrderId || info.TransactionId == payment.AuthorizationId ||
               info.TransactionId == payment.CaptureId || info.PayPalReferenceId == payment.PayPalOrderId ||
               info.PayPalReferenceId == payment.AuthorizationId || info.PayPalReferenceId == payment.CaptureId ||
               payment.Refunds.Any(x => x.PayPalRefundId == info.TransactionId || x.PayPalRefundId == info.PayPalReferenceId);
    }

    private static ApiProblemException BadRequest(string code, string message) => new(400, code, message);
    private static ApiProblemException Conflict(string code, string message) => new(409, code, message);
}
