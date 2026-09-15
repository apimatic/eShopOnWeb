using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using ApplicationAddress = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;
using ApplicationOrder = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;
using SavedPaymentMethod = Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate.PaymentMethod;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalPaymentService
{
    private const string ChallengeMessage = "PayPal requires browser approval; this headless card flow cannot continue.";
    private readonly CatalogContext _db;
    private readonly PayPalServerSdk.PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    private readonly OrderOperationLocks _locks;

    public PayPalPaymentService(CatalogContext db, PayPalServerSdk.PayPalServerSdkClient client,
        IOptions<PayPalSettings> settings, OrderOperationLocks locks)
    {
        _db = db;
        _client = client;
        _settings = settings.Value;
        _locks = locks;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(x => x.Quantity <= 0))
            throw BadRequest("INVALID_ORDER", "At least one catalog item with a positive quantity is required.");
        if (request.Items.Select(x => x.CatalogItemId).Distinct().Count() != request.Items.Count)
            throw BadRequest("DUPLICATE_ITEM", "Each catalog item may appear only once.");

        var ids = request.Items.Select(x => x.CatalogItemId).ToArray();
        var catalog = await _db.CatalogItems.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        if (catalog.Count != ids.Length)
            throw BadRequest("CATALOG_ITEM_NOT_FOUND", "One or more catalog items do not exist.");

        var lines = request.Items.Select(x => new OrderItem(
            new CatalogItemOrdered(catalog[x.CatalogItemId].Id, catalog[x.CatalogItemId].Name,
                catalog[x.CatalogItemId].PictureUri),
            catalog[x.CatalogItemId].Price, x.Quantity)).ToList();
        var a = request.ShippingAddress ?? throw BadRequest("SHIPPING_ADDRESS_REQUIRED", "A shipping address is required.");
        var order = new ApplicationOrder(buyerId, new ApplicationAddress(a.Street, a.City, a.State, a.Country, a.ZipCode), lines);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);
        order.EnsurePaymentReference();
        await _db.SaveChangesAsync(ct);
        return new CreateOrderResponse(order.Id, order.Total(), _settings.Currency, order.PaymentState.ToString());
    }

    public async Task<SavePaymentMethodResponse> SavePaymentMethodAsync(string ownerId,
        SavePaymentMethodRequest request, CancellationToken ct)
    {
        var operation = Guid.NewGuid().ToString("N");
        SetupTokenResponse setup;
        try
        {
            setup = await Bounded(token => _client.Vault.CreateSetupToken(
                payPalRequestId: $"eshop-vault-setup-{operation}",
                body: new SetupTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = StableCustomerId(ownerId) },
                    PaymentSource = new SetupTokenRequestPaymentSource
                    {
                        Card = ToSetupCard(request.Card)
                    }
                }, ct: token), ct);
        }
        catch (SdkException<CreateSetupTokenError> ex) { throw Provider(ex.Error, "SAVE_CARD_REJECTED"); }

        var setupStatus = setup.Status?.Value;
        if (setupStatus == PaymentTokenStatus.PayerActionRequired.Value)
            throw new PaymentApiException(409, "PAYER_ACTION_REQUIRED", ChallengeMessage);
        if (string.IsNullOrWhiteSpace(setup.Id))
            throw ProviderResponse();

        PaymentTokenResponse tokenResponse;
        try
        {
            tokenResponse = await Bounded(token => _client.Vault.CreatePaymentToken(
                payPalRequestId: $"eshop-vault-token-{operation}",
                body: new PaymentTokenRequest
                {
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest
                        {
                            Id = setup.Id,
                            Type = VaultTokenRequestType.SetupToken
                        }
                    }
                }, ct: token), ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex) { throw Provider(ex.Error, "SAVE_CARD_REJECTED"); }

        var safeCard = tokenResponse.PaymentSource?.Card;
        if (string.IsNullOrWhiteSpace(tokenResponse.Id) || string.IsNullOrWhiteSpace(safeCard?.LastDigits))
            throw ProviderResponse();

        var method = new SavedPaymentMethod(ownerId, tokenResponse.Id, tokenResponse.Customer?.Id,
            safeCard.Brand?.Value, safeCard.LastDigits, safeCard.Expiry, "VAULTED");
        _db.PaymentMethods.Add(method);
        await _db.SaveChangesAsync(ct);
        return new SavePaymentMethodResponse(method.Id, method.Brand, method.Last4, method.Expiry, method.ProviderStatus);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> ListPaymentMethodsAsync(string ownerId, CancellationToken ct) =>
        await _db.PaymentMethods.AsNoTracking().Where(x => x.OwnerId == ownerId && x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new PaymentMethodResponse(x.Id, x.Brand, x.Last4, x.Expiry, x.ProviderStatus))
            .ToListAsync(ct);

    public async Task DeletePaymentMethodAsync(string ownerId, int paymentMethodId, CancellationToken ct)
    {
        var method = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId && x.OwnerId == ownerId, ct)
            ?? throw NotFound("PAYMENT_METHOD_NOT_FOUND", "Payment method not found.");
        if (!method.IsActive) return;
        try
        {
            await Bounded(async token =>
            {
                await _client.Vault.DeletePaymentToken(method.ProviderTokenId, ct: token);
                return true;
            }, ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex) { throw Provider(ex.Error, "DELETE_CARD_REJECTED"); }
        method.Deactivate();
        await _db.SaveChangesAsync(ct);
    }

    public async Task<PaymentResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request, CancellationToken ct)
    {
        var gate = _locks.For(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await LoadOrderAsync(orderId, ct);
            EnsureOwner(order, buyerId);
            var paymentReference = await EnsurePaymentReferenceAsync(order, ct);
            if (order.AuthorizationId is not null) return ToPayment(order);
            if (order.PaymentState is OrderPaymentState.Cancelled or OrderPaymentState.Fulfilled or
                OrderPaymentState.PartiallyRefunded or OrderPaymentState.Refunded)
                throw Conflict("ORDER_NOT_PAYABLE", "This order can no longer be authorized.");

            var hasCard = request.Card is not null;
            var hasSaved = request.PaymentMethodId.HasValue;
            if (hasCard == hasSaved)
                throw BadRequest("PAYMENT_SOURCE_REQUIRED", "Provide either card details or one saved payment method.");

            SavedPaymentMethod? saved = null;
            if (hasSaved)
            {
                saved = await _db.PaymentMethods.SingleOrDefaultAsync(x => x.Id == request.PaymentMethodId &&
                    x.OwnerId == buyerId && x.IsActive, ct)
                    ?? throw NotFound("PAYMENT_METHOD_NOT_FOUND", "Active payment method not found.");
            }

            if (order.PayPalOrderId is null)
            {
                PayPalServerSdk.Models.Order providerOrder;
                try
                {
                    providerOrder = await Bounded(token => _client.Orders.CreateOrder(
                        payPalMockResponse: null,
                        payPalRequestId: $"{paymentReference}-create",
                        payPalPartnerAttributionId: null,
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: new OrderRequest
                        {
                            Intent = CheckoutPaymentIntent.Authorize,
                            PurchaseUnits = new[]
                            {
                                new PurchaseUnitRequest
                                {
                                    InvoiceId = paymentReference,
                                    CustomId = order.Id.ToString(CultureInfo.InvariantCulture),
                                    Amount = MoneyForOrder(order)
                                }
                            }
                        }, prefer: "return=representation", ct: token), ct);
                }
                catch (SdkException<CreateOrderError> ex) { throw Provider(ex.Error, "AUTHORIZE_REJECTED"); }
                if (string.IsNullOrWhiteSpace(providerOrder.Id)) throw ProviderResponse();
                order.RecordPayPalOrder(providerOrder.Id, providerOrder.Status?.Value, _settings.Currency);
                await _db.SaveChangesAsync(ct);
            }

            var card = saved is null ? ToOrderCard(request.Card!) : new CardRequest
            {
                VaultId = saved.ProviderTokenId,
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Customer,
                    PaymentType = StoredPaymentSourcePaymentType.OneTime,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
            };

            OrderAuthorizeResponse authorized;
            try
            {
                authorized = await Bounded(token => _client.Orders.AuthorizeOrder(
                    id: order.PayPalOrderId!, payPalMockResponse: null,
                    payPalRequestId: $"{paymentReference}-authorize",
                    payPalClientMetadataId: null, payPalAuthAssertion: null,
                    body: new OrderAuthorizeRequest
                    {
                        PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = card }
                    }, prefer: "return=representation", ct: token), ct);
            }
            catch (SdkException<AuthorizeOrderError> ex) { throw Provider(ex.Error, "AUTHORIZE_REJECTED"); }

            if (authorized.Status?.Value == OrderStatus.PayerActionRequired.Value)
            {
                order.RecordPaymentActionRequired(ChallengeMessage);
                await _db.SaveChangesAsync(ct);
                throw new PaymentApiException(409, "PAYER_ACTION_REQUIRED", ChallengeMessage);
            }

            var auth = authorized.PurchaseUnits?.SelectMany(x => x.Payments?.Authorizations ??
                Array.Empty<AuthorizationWithAdditionalData>()).FirstOrDefault()
                ?? throw ProviderResponse();
            EnsureProviderAmount(auth.Amount, order.Total());
            if (string.IsNullOrWhiteSpace(auth.Id) || string.IsNullOrWhiteSpace(auth.Status?.Value))
                throw ProviderResponse();
            order.RecordAuthorization(auth.Id, auth.Status.Value,
                ParseDate(auth.CreateTime), ParseDate(auth.ExpirationTime));
            await _db.SaveChangesAsync(ct);
            return ToPayment(order);
        }
        finally { gate.Release(); }
    }

    public async Task<PaymentResponse> FulfilAsync(int orderId, CancellationToken ct)
    {
        var gate = _locks.For(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await LoadOrderAsync(orderId, ct);
            var paymentReference = await EnsurePaymentReferenceAsync(order, ct);
            if (order.PaymentState is OrderPaymentState.Fulfilled or OrderPaymentState.PartiallyRefunded or OrderPaymentState.Refunded)
                return ToPayment(order);
            if (order.AuthorizationId is null)
                throw Conflict("ORDER_NOT_AUTHORIZED", "The shopper must authorize this order before fulfilment.");

            if (order.CaptureId is not null)
            {
                CapturedPayment existing;
                try { existing = await Bounded(t => _client.Payments.GetCapturedPayment(order.CaptureId, null, ct: t), ct); }
                catch (SdkException<GetCapturedPaymentError> ex) { throw Provider(ex.Error, "CAPTURE_LOOKUP_FAILED"); }
                ApplyCapture(order, existing);
                await _db.SaveChangesAsync(ct);
                return ToPayment(order);
            }

            PaymentAuthorization authorization;
            try { authorization = await Bounded(t => _client.Payments.GetAuthorizedPayment(order.AuthorizationId, null, null, ct: t), ct); }
            catch (SdkException<GetAuthorizedPaymentError> ex) { throw Provider(ex.Error, "AUTHORIZATION_LOOKUP_FAILED"); }

            var originalCreated = order.AuthorizationCreatedAt ?? ParseDate(authorization.CreateTime) ?? order.OrderDate;
            if (DateTimeOffset.UtcNow >= originalCreated.AddDays(29))
                throw Conflict("AUTHORIZATION_EXPIRED", "The authorization is outside PayPal's renewal window; ask the shopper to authorize the order again.");

            if (DateTimeOffset.UtcNow >= originalCreated.AddDays(3))
            {
                try
                {
                    authorization = await Bounded(t => _client.Payments.ReauthorizePayment(
                        authorizationId: order.AuthorizationId,
                        payPalRequestId: $"{paymentReference}-reauthorize",
                        payPalAuthAssertion: null,
                        body: new ReauthorizeRequest { Amount = Money(order.Total()) },
                        prefer: "return=representation", ct: t), ct);
                }
                catch (SdkException<ReauthorizePaymentError> ex)
                {
                    throw Provider(ex.Error, "AUTHORIZATION_CANNOT_BE_RENEWED",
                        "PayPal could not renew the authorization; ask the shopper to authorize the order again.");
                }
                if (string.IsNullOrWhiteSpace(authorization.Id) || string.IsNullOrWhiteSpace(authorization.Status?.Value))
                    throw ProviderResponse();
                order.RecordAuthorization(authorization.Id, authorization.Status.Value,
                    ParseDate(authorization.CreateTime), ParseDate(authorization.ExpirationTime));
                await _db.SaveChangesAsync(ct);
            }

            CapturedPayment capture;
            try
            {
                capture = await Bounded(t => _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: order.AuthorizationId!, payPalMockResponse: null,
                    payPalRequestId: $"{paymentReference}-capture", payPalAuthAssertion: null,
                    body: new CaptureRequest
                    {
                        Amount = Money(order.Total()), FinalCapture = true,
                        InvoiceId = paymentReference
                    }, prefer: "return=representation", ct: t), ct);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw Provider(ex.Error, "CAPTURE_REJECTED"); }
            ApplyCapture(order, capture);
            await _db.SaveChangesAsync(ct);
            return ToPayment(order);
        }
        finally { gate.Release(); }
    }

    public async Task<PaymentResponse> CancelAsync(int orderId, CancellationToken ct)
    {
        var gate = _locks.For(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await LoadOrderAsync(orderId, ct);
            var paymentReference = await EnsurePaymentReferenceAsync(order, ct);
            if (order.PaymentState == OrderPaymentState.Cancelled) return ToPayment(order);
            if (order.CaptureId is not null)
                throw Conflict("ORDER_ALREADY_CAPTURED", "Captured orders must be refunded, not cancelled.");
            if (order.AuthorizationId is null)
                throw Conflict("ORDER_NOT_AUTHORIZED", "There is no authorization to release.");
            PaymentAuthorization result;
            try
            {
                result = await Bounded(t => _client.Payments.VoidPayment(order.AuthorizationId,
                    payPalMockResponse: null, payPalAuthAssertion: null,
                    payPalRequestId: $"{paymentReference}-void",
                    prefer: "return=representation", ct: t), ct);
            }
            catch (SdkException<VoidPaymentError> ex) { throw Provider(ex.Error, "VOID_REJECTED"); }
            order.RecordCancellation(result.Status?.Value ?? "VOIDED");
            await _db.SaveChangesAsync(ct);
            return ToPayment(order);
        }
        finally { gate.Release(); }
    }

    public async Task<RefundResponse> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            throw BadRequest("IDEMPOTENCY_KEY_REQUIRED", "A non-empty idempotency key of at most 128 characters is required.");
        var gate = _locks.For(orderId);
        await gate.WaitAsync(ct);
        try
        {
            var order = await LoadOrderAsync(orderId, ct);
            EnsureOwner(order, buyerId);
            var paymentReference = await EnsurePaymentReferenceAsync(order, ct);
            if (order.CaptureId is null || !order.CapturedAmount.HasValue)
                throw Conflict("ORDER_NOT_CAPTURED", "Only a captured order can be refunded.");
            var existing = order.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
            if (existing is not null && existing.PayPalRefundId is not null)
                return ToRefund(existing);

            var reserved = order.Refunds.Where(x => x != existing && x.Status is not "FAILED" and not "CANCELLED")
                .Sum(x => x.Amount);
            var remaining = order.CapturedAmount.Value - reserved;
            var amount = request.Amount ?? remaining;
            if (amount <= 0 || amount > remaining)
                throw Conflict("REFUND_EXCEEDS_CAPTURE", "The refund exceeds the unrefunded captured amount.");

            var refund = existing ?? order.AddRefund(request.IdempotencyKey,
                $"{paymentReference}-refund-{StableKey(request.IdempotencyKey)}", amount);
            if (existing is null) await _db.SaveChangesAsync(ct);
            var isFullRemaining = amount == remaining;
            Refund result;
            try
            {
                result = await Bounded(t => _client.Payments.RefundCapturedPayment(
                    captureId: order.CaptureId, payPalMockResponse: null,
                    payPalRequestId: refund.ProviderRequestId, payPalAuthAssertion: null,
                    body: isFullRemaining ? new RefundRequest() : new RefundRequest
                    {
                        Amount = Money(amount),
                        CustomId = refund.Id.ToString(CultureInfo.InvariantCulture),
                        InvoiceId = paymentReference
                    }, prefer: "return=representation", ct: t), ct);
            }
            catch (SdkException<RefundCapturedPaymentError> ex) { throw Provider(ex.Error, "REFUND_REJECTED"); }
            var actual = ParseMoney(result.Amount) ?? amount;
            refund.RecordProviderResult(result.Id ?? throw ProviderResponse(), result.Status?.Value ?? "PENDING", actual);
            if (result.Status?.Value == RefundStatus.Completed.Value) order.ApplyCompletedRefund(actual);
            await _db.SaveChangesAsync(ct);
            return ToRefund(refund);
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<OrderResponse>> MyOrdersAsync(string buyerId, CancellationToken ct)
    {
        var orders = await _db.Orders.AsNoTracking().Include(x => x.OrderItems).Include(x => x.Refunds)
            .Where(x => x.BuyerId == buyerId).OrderByDescending(x => x.OrderDate).ToListAsync(ct);
        return orders.Select(x => new OrderResponse(x.Id, x.OrderDate, x.Total(), ToPayment(x))).ToList();
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to <= from || to - from > TimeSpan.FromDays(31))
            throw BadRequest("INVALID_RANGE", "The range must be positive and no longer than 31 days.");
        var transactions = new List<TransactionInformation>();
        var page = 1;
        var totalPages = 1;
        do
        {
            SearchResponse response;
            try
            {
                response = await Bounded(t => _client.TransactionSearch.SearchTransactions(
                    startDate: FormatReportingTimestamp(from),
                    endDate: FormatReportingTimestamp(to),
                    transactionId: null, transactionType: null, transactionStatus: null,
                    transactionAmount: null, transactionCurrency: null, paymentInstrumentType: null,
                    storeId: null, terminalId: null, fields: "transaction_info",
                    balanceAffectingRecordsOnly: "N", pageSize: 100, page: page, ct: t), ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw ReportingProviderError(ex.Error, ex);
            }
            transactions.AddRange(response.TransactionDetails?.Select(x => x.TransactionInfo)
                .Where(x => x is not null).Cast<TransactionInformation>() ?? Array.Empty<TransactionInformation>());
            totalPages = Math.Max(1, response.TotalPages ?? 1);
            page++;
        } while (page <= totalPages);

        var locals = await _db.Orders.AsNoTracking().Include(x => x.Refunds)
            .Where(x => x.OrderDate <= to && (x.OrderDate >= from || x.PayPalOrderId != null)).ToListAsync(ct);
        var matchedOrders = new HashSet<int>();
        var rows = new List<ReconciliationRow>();
        foreach (var tx in transactions)
        {
            var local = locals.FirstOrDefault(o => Matches(o, tx));
            if (local is not null) matchedOrders.Add(local.Id);
            var amount = tx.TransactionAmount?.Value;
            var currency = tx.TransactionAmount?.CurrencyCode;
            rows.Add(new ReconciliationRow(local is null ? "PayPalOnly" : "Matched", tx.TransactionId,
                local?.Id, tx.TransactionStatus, local?.PaymentState.ToString(), amount, currency,
                false, local is null ? "No eShop order matched this PayPal transaction." : Difference(local, amount, currency)));
        }
        foreach (var local in locals.Where(x => x.PayPalOrderId is not null && !matchedOrders.Contains(x.Id)))
        {
            var recent = DateTimeOffset.UtcNow - local.OrderDate < TimeSpan.FromHours(3);
            rows.Add(new ReconciliationRow("EShopOnly", local.CaptureId ?? local.AuthorizationId ?? local.PayPalOrderId,
                local.Id, null, local.PaymentState.ToString(), local.Total().ToString("F2", CultureInfo.InvariantCulture),
                local.PaymentCurrency, recent, recent ? "Pending PayPal reporting latency." : "No PayPal transaction matched this eShop order."));
        }
        return new ReconciliationResponse(from, to, rows, totalPages);
    }

    private async Task<ApplicationOrder> LoadOrderAsync(int id, CancellationToken ct) =>
        await _db.Orders.Include(x => x.OrderItems).Include(x => x.Refunds).SingleOrDefaultAsync(x => x.Id == id, ct)
        ?? throw NotFound("ORDER_NOT_FOUND", "Order not found.");

    private async Task<string> EnsurePaymentReferenceAsync(ApplicationOrder order, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(order.PaymentReference)) return order.PaymentReference;
        var paymentReference = order.EnsurePaymentReference();
        await _db.SaveChangesAsync(ct);
        return paymentReference;
    }

    private static void EnsureOwner(ApplicationOrder order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw NotFound("ORDER_NOT_FOUND", "Order not found.");
    }

    private SetupTokenRequestCard ToSetupCard(CardRequestDto card) => new()
    {
        Name = card.Name, Number = card.Number, Expiry = card.Expiry, SecurityCode = card.SecurityCode,
        BillingAddress = ToProviderAddress(card.BillingAddress)
    };

    private CardRequest ToOrderCard(CardRequestDto card) => new()
    {
        Name = card.Name, Number = card.Number, Expiry = card.Expiry, SecurityCode = card.SecurityCode,
        BillingAddress = ToProviderAddress(card.BillingAddress)
    };

    private static PayPalServerSdk.Models.Address ToProviderAddress(AddressDto address) => new()
    {
        AddressLine1 = address.Street, AdminArea2 = address.City, AdminArea1 = address.State,
        PostalCode = address.ZipCode, CountryCode = address.Country
    };

    private AmountWithBreakdown MoneyForOrder(ApplicationOrder order) => new()
    {
        CurrencyCode = _settings.Currency,
        Value = order.Total().ToString("F2", CultureInfo.InvariantCulture)
    };

    private Money Money(decimal value) => new()
    {
        CurrencyCode = _settings.Currency,
        Value = value.ToString("F2", CultureInfo.InvariantCulture)
    };

    private void ApplyCapture(ApplicationOrder order, CapturedPayment capture)
    {
        if (string.IsNullOrWhiteSpace(capture.Id) || string.IsNullOrWhiteSpace(capture.Status?.Value))
            throw ProviderResponse();
        EnsureProviderAmount(capture.Amount, order.Total());
        var amount = ParseMoney(capture.Amount) ?? throw ProviderResponse();
        order.RecordCapture(capture.Id, capture.Status.Value, amount,
            ParseMoney(capture.SellerReceivableBreakdown?.PaypalFee),
            ParseMoney(capture.SellerReceivableBreakdown?.NetAmount));
    }

    private void EnsureProviderAmount(Money? money, decimal expected)
    {
        var actual = ParseMoney(money);
        if (!actual.HasValue || actual.Value != expected ||
            !string.Equals(money?.CurrencyCode, _settings.Currency, StringComparison.OrdinalIgnoreCase))
            throw new PaymentApiException(502, "PROVIDER_AMOUNT_MISMATCH",
                "PayPal returned an amount or currency that does not match the order total.");
    }

    private static decimal? ParseMoney(Money? money) =>
        decimal.TryParse(money?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;

    private static string FormatReportingTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        try { return await action(budget.Token); }
        catch (PaymentApiException) { throw; }
        catch (JsonException ex) { throw new PaymentApiException(502, "PAYPAL_RESPONSE_INVALID", "PayPal returned a response that could not be processed.", ex); }
        catch (HttpRequestException ex) { throw new PaymentApiException(503, "PAYPAL_UNAVAILABLE", "PayPal is temporarily unreachable.", ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new PaymentApiException(504, "PAYPAL_TIMEOUT", "PayPal did not respond before the request deadline.", ex); }
    }

    private static PaymentApiException Provider(CreateOrderError e, string code, string? fallback = null) =>
        e.TryGetError(out var x) ? Provider(x, code, fallback) : new PaymentApiException(502, code, fallback ?? "PayPal rejected the request.");
    private static PaymentApiException Provider(AuthorizeOrderError e, string code, string? fallback = null) =>
        e.TryGetError(out var x) ? Provider(x, code, fallback) : new PaymentApiException(502, code, fallback ?? "PayPal rejected the request.");
    private static PaymentApiException Provider(CreateSetupTokenError e, string code, string? fallback = null) =>
        e.TryGetError1(out var x) ? Provider(x, code, fallback) : new PaymentApiException(502, code, fallback ?? "PayPal rejected the request.");
    private static PaymentApiException Provider(CreatePaymentTokenError e, string code, string? fallback = null) =>
        e.TryGetError1(out var x) ? Provider(x, code, fallback) : new PaymentApiException(502, code, fallback ?? "PayPal rejected the request.");
    private static PaymentApiException Provider(DeletePaymentTokenError e, string code, string? fallback = null) =>
        e.TryGetError1(out var x) ? Provider(x, code, fallback) : new PaymentApiException(502, code, fallback ?? "PayPal rejected the request.");
    private static PaymentApiException Provider(GetAuthorizedPaymentError e, string code, string? fallback = null) =>
        e.TryGetError(out var x) ? Provider(x, code, fallback) : new PaymentApiException(502, code, fallback ?? "PayPal rejected the request.");
    private static PaymentApiException Provider(GetCapturedPaymentError e, string code, string? fallback = null) =>
        e.TryGetError(out var x) ? Provider(x, code, fallback) : new PaymentApiException(502, code, fallback ?? "PayPal rejected the request.");
    private static PaymentApiException Provider(ReauthorizePaymentError e, string code, string? fallback = null) =>
        e.TryGetError(out var x) ? Provider(x, code, fallback) : new PaymentApiException(422, code, fallback ?? "PayPal rejected the request.");
    private static PaymentApiException Provider(CaptureAuthorizedPaymentError e, string code, string? fallback = null) =>
        e.TryGetError(out var x) ? Provider(x, code, fallback) : new PaymentApiException(502, code, fallback ?? "PayPal rejected the request.");
    private static PaymentApiException Provider(VoidPaymentError e, string code, string? fallback = null) =>
        e.TryGetError(out var x) ? Provider(x, code, fallback) : new PaymentApiException(502, code, fallback ?? "PayPal rejected the request.");
    private static PaymentApiException Provider(RefundCapturedPaymentError e, string code, string? fallback = null) =>
        e.TryGetError(out var x) ? Provider(x, code, fallback) : new PaymentApiException(502, code, fallback ?? "PayPal rejected the request.");

    private static PaymentApiException Provider(Error error, string code, string? fallback)
    {
        var detail = error.Details?.FirstOrDefault();
        var message = fallback ?? detail?.Description ?? error.Message;
        return new PaymentApiException(422, code,
            $"{message} PayPal issue: {detail?.Issue ?? error.Name}; debug id: {error.DebugId}.");
    }

    private static PaymentApiException Provider(Error1 error, string code, string? fallback)
    {
        var detail = error.Details?.FirstOrDefault();
        var message = fallback ?? detail?.Description ?? error.Message;
        return new PaymentApiException(422, code,
            $"{message} PayPal issue: {detail?.Issue ?? error.Name}; debug id: {error.DebugId}.");
    }

    private static PaymentApiException ReportingProviderError(RawError error, Exception inner)
    {
        const string fallback = "PayPal transaction reporting rejected the request.";
        var bytes = error.ReadAsBytes();
        if (bytes.IsEmpty || bytes.Length > 16 * 1024)
            return new PaymentApiException((int)error.StatusCode, "RECONCILIATION_FAILED", fallback, inner);

        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var name = JsonString(root, "name");
            var message = JsonString(root, "message");
            var debugId = JsonString(root, "debug_id");
            string? issue = null;
            string? field = null;
            string? description = null;
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                details.GetArrayLength() > 0 && details[0].ValueKind == JsonValueKind.Object)
            {
                issue = JsonString(details[0], "issue");
                field = JsonString(details[0], "field");
                description = JsonString(details[0], "description");
            }

            var parts = new List<string> { fallback };
            if (CleanProviderText(description ?? message, 300) is { Length: > 0 } explanation)
                parts.Add(explanation);
            if (CleanProviderText(issue ?? name, 100) is { Length: > 0 } providerIssue)
                parts.Add($"PayPal issue: {providerIssue}.");
            if (CleanProviderText(field, 100) is { Length: > 0 } providerField)
                parts.Add($"Field: {providerField}.");
            if (CleanProviderText(debugId, 100) is { Length: > 0 } providerDebugId)
                parts.Add($"Debug id: {providerDebugId}.");
            return new PaymentApiException((int)error.StatusCode, "RECONCILIATION_FAILED",
                string.Join(' ', parts), inner);
        }
        catch (JsonException)
        {
            return new PaymentApiException((int)error.StatusCode, "RECONCILIATION_FAILED", fallback, inner);
        }
    }

    private static string? JsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? CleanProviderText(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var clean = new string(value.Where(character => !char.IsControl(character)).Take(maximumLength).ToArray()).Trim();
        return clean.Length == 0 ? null : clean;
    }

    private static PaymentApiException BadRequest(string code, string message) => new(400, code, message);
    private static PaymentApiException NotFound(string code, string message) => new(404, code, message);
    private static PaymentApiException Conflict(string code, string message) => new(409, code, message);
    private static PaymentApiException ProviderResponse() => new(502, "PAYPAL_RESPONSE_INVALID", "PayPal returned an incomplete response.");

    private static PaymentResponse ToPayment(ApplicationOrder o) => new(o.Id, o.PaymentState.ToString(), o.PayPalOrderId,
        o.AuthorizationId, o.AuthorizationStatus, o.CaptureId, o.CaptureStatus, o.CapturedAmount,
        o.PayPalFee, o.NetProceeds, o.RefundedAmount, o.PaymentCurrency, o.PaymentFailureCode, o.PaymentFailureMessage);
    private static RefundResponse ToRefund(OrderRefund r) => new(r.Id, r.PayPalRefundId, r.Amount, r.Status);

    private static string StableCustomerId(string value) => "eshop-" + StableKey(value);
    private static string StableKey(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();
    private static bool Matches(ApplicationOrder o, TransactionInformation tx) =>
        new[] { o.PayPalOrderId, o.AuthorizationId, o.CaptureId }.Any(x => x == tx.TransactionId || x == tx.PaypalReferenceId) ||
        o.Refunds.Any(r => r.PayPalRefundId == tx.TransactionId || r.PayPalRefundId == tx.PaypalReferenceId) ||
        tx.InvoiceId == o.PaymentReference || tx.CustomField == o.Id.ToString(CultureInfo.InvariantCulture);
    private string? Difference(ApplicationOrder o, string? amount, string? currency)
    {
        if (!string.IsNullOrWhiteSpace(currency) && !string.Equals(currency, o.PaymentCurrency, StringComparison.OrdinalIgnoreCase))
            return "Currency differs.";
        if (decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) &&
            value != o.Total() && value != o.CapturedAmount && !o.Refunds.Any(r => r.Amount == value))
            return "Amount differs.";
        return null;
    }
}
