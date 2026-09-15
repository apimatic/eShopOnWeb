using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentProcessor : IPaymentProcessor
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SearchBudget = TimeSpan.FromSeconds(90);
    private const string PreferRepresentation = "return=representation";

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentProcessor> _logger;

    public PayPalPaymentProcessor(PayPalServerSdkClient client, ILogger<PayPalPaymentProcessor> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<AuthorizationResult> AuthorizeCardAsync(int orderId, decimal amount, string currency, CardPaymentInput card, string requestId, CancellationToken ct) =>
        AuthorizeAsync(orderId, amount, currency, BuildCardRequest(card), requestId, ct);

    public Task<AuthorizationResult> AuthorizeVaultedCardAsync(int orderId, decimal amount, string currency, string vaultId, string requestId, CancellationToken ct)
    {
        var card = new CardRequest
        {
            VaultId = vaultId,
            StoredCredential = new CardStoredCredential
            {
                PaymentInitiator = PaymentInitiator.Customer,
                PaymentType = StoredPaymentSourcePaymentType.Unscheduled,
                Usage = StoredPaymentSourceUsageType.Subsequent
            }
        };
        return AuthorizeAsync(orderId, amount, currency, card, requestId, ct);
    }

    public Task<AuthorizationResult> AuthorizeExistingPayPalOrderAsync(string paypalOrderId, string requestId, CancellationToken ct) =>
        Bounded(token => AuthorizeHoldAsync(paypalOrderId, requestId, token), ct);

    public Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken ct) =>
        Bounded(async token =>
        {
            try
            {
                var hold = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: token);
                return new AuthorizationDetails
                {
                    AuthorizationId = hold.Id ?? authorizationId,
                    Status = Wire(hold.Status),
                    ExpirationTime = ToTimestamp(hold.ExpirationTime),
                    Amount = ParseMoney(hold.Amount)
                };
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                throw ToPaymentException(ex.Error, "GetAuthorizedPayment");
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw TranslateBoundary(ex);
            }
        }, ct);

    public Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken ct) =>
        Bounded(async token =>
        {
            _logger.LogInformation("Reauthorizing PayPal hold {AuthorizationId}", authorizationId);
            try
            {
                var hold = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = MoneyOf(currency, amount)
                    },
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: token);

                if (string.IsNullOrEmpty(hold.Id))
                    throw new PaymentProcessingException("PayPal reauthorized the hold but returned no authorization id.", 502, operatorActionable: true);

                return new AuthorizationResult
                {
                    PayPalOrderId = authorizationId,
                    AuthorizationId = hold.Id,
                    AuthorizationStatus = Wire(hold.Status),
                    ExpirationTime = ToTimestamp(hold.ExpirationTime)
                };
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw ToPaymentException(ex.Error, "ReauthorizePayment", operatorActionable: true);
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw TranslateBoundary(ex, operatorActionable: true);
            }
        }, ct);

    public Task<CaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken ct) =>
        Bounded(async token =>
        {
            _logger.LogInformation("Capturing PayPal hold {AuthorizationId}", authorizationId);
            try
            {
                var captured = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest { FinalCapture = true },
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: token);

                if (string.IsNullOrEmpty(captured.Id) || captured.SellerReceivableBreakdown == null)
                    captured = await _client.Payments.GetCapturedPayment(captured.Id ?? string.Empty, payPalMockResponse: null, requestOptions: null, ct: token);

                return MapCapture(captured);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw ToPaymentException(ex.Error, "CaptureAuthorizedPayment");
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw TranslateBoundary(ex);
            }
        }, ct);

    public Task<CaptureDetails> GetCaptureAsync(string captureId, CancellationToken ct) =>
        Bounded(async token =>
        {
            try
            {
                var captured = await _client.Payments.GetCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    requestOptions: null,
                    ct: token);
                var mapped = MapCapture(captured);
                return new CaptureDetails
                {
                    CaptureId = mapped.CaptureId,
                    Status = mapped.CaptureStatus,
                    CapturedAmount = mapped.CapturedAmount,
                    PaypalFee = mapped.PaypalFee,
                    NetAmount = mapped.NetAmount
                };
            }
            catch (SdkException<GetCapturedPaymentError> ex)
            {
                throw ToPaymentException(ex.Error, "GetCapturedPayment");
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw TranslateBoundary(ex);
            }
        }, ct);

    public Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct) =>
        Bounded(async token =>
        {
            _logger.LogInformation("Voiding PayPal hold {AuthorizationId}", authorizationId);
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: requestId,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: token);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw ToPaymentException(ex.Error, "VoidPayment");
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw TranslateBoundary(ex);
            }

            return 0;
        }, ct);

    public Task<RefundResult> RefundAsync(string captureId, string? paypalOrderId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct) =>
        Bounded(async token =>
        {
            _logger.LogInformation("Refunding PayPal capture {CaptureId}", captureId);
            var payPalRequestId = $"eshop-refund-{captureId}-{idempotencyKey}";
            try
            {
                RefundRequest? body = amount.HasValue
                    ? new RefundRequest { Amount = MoneyOf(currency, amount.Value) }
                    : null;

                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: token);

                return await MapRefund(captureId, refund, amount, token);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                if (IsDuplicateRequest(ex.Error) && !string.IsNullOrEmpty(paypalOrderId))
                    return await RecoverRefund(captureId, paypalOrderId, amount, token);
                throw ToPaymentException(ex.Error, "RefundCapturedPayment");
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw TranslateBoundary(ex);
            }
        }, ct);

    public Task<VaultedCardResult> VaultCardAsync(string merchantCustomerId, string? paypalCustomerId, CardPaymentInput card, string requestId, CancellationToken ct) =>
        Bounded(async token =>
        {
            _logger.LogInformation("Vaulting a card for merchant customer {MerchantCustomerId}", merchantCustomerId);
            try
            {
                var customer = new Customer
                {
                    MerchantCustomerId = merchantCustomerId,
                    Id = paypalCustomerId
                };

                var created = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: requestId,
                    body: new PaymentTokenRequest
                    {
                        Customer = customer,
                        PaymentSource = new PaymentTokenRequestPaymentSource
                        {
                            Card = new PaymentTokenRequestCard
                            {
                                Name = card.Name,
                                Number = card.Number,
                                Expiry = card.Expiry,
                                SecurityCode = card.SecurityCode,
                                BillingAddress = MapAddress(card.BillingAddress)
                            }
                        }
                    },
                    requestOptions: null,
                    ct: token);

                if (string.IsNullOrEmpty(created.Id))
                    throw new PaymentProcessingException("PayPal vaulted the card but returned no payment token id.", 502);

                var cardEntity = created.PaymentSource?.Card;
                return new VaultedCardResult
                {
                    PaymentTokenId = created.Id,
                    PayPalCustomerId = created.Customer?.Id,
                    LastDigits = cardEntity?.LastDigits,
                    Brand = Wire(cardEntity?.Brand),
                    Expiry = cardEntity?.Expiry
                };
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw ToVaultException(ex.Error, "CreatePaymentToken");
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw TranslateBoundary(ex);
            }
        }, ct);

    public Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct) =>
        Bounded(async token =>
        {
            _logger.LogInformation("Deleting vaulted payment token {PaymentTokenId}", paymentTokenId);
            try
            {
                await _client.Vault.DeletePaymentToken(id: paymentTokenId, requestOptions: null, ct: token);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw ToVaultException(ex.Error, "DeletePaymentToken");
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw TranslateBoundary(ex);
            }

            return 0;
        }, ct);

    public Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Bounded(async token =>
        {
            var results = new List<ProviderTransaction>();
            foreach (var (windowStart, windowEnd) in Windows(from, to))
            {
                int page = 1;
                int? totalPages = null;
                do
                {
                    SearchResponse response;
                    try
                    {
                        response = await _client.TransactionSearch.SearchTransactions(
                            startDate: Rfc3339(windowStart),
                            endDate: Rfc3339(windowEnd),
                            transactionId: null,
                            transactionType: null,
                            transactionStatus: null,
                            transactionAmount: null,
                            transactionCurrency: null,
                            paymentInstrumentType: null,
                            storeId: null,
                            terminalId: null,
                            fields: "all",
                            balanceAffectingRecordsOnly: "Y",
                            pageSize: 100,
                            page: page,
                            requestOptions: null,
                            ct: token);
                    }
                    catch (SdkException<RawError> ex)
                    {
                        var body = SafeRaw(ex.Error);
                        throw new PaymentProcessingException(
                            $"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}): {body}",
                            (int)ex.Error.StatusCode);
                    }
                    catch (Exception ex) when (IsTransportOrParse(ex))
                    {
                        throw TranslateBoundary(ex);
                    }

                    if (response.TransactionDetails != null)
                    {
                        foreach (var detail in response.TransactionDetails)
                        {
                            var info = detail.TransactionInfo;
                            if (info == null)
                                continue;
                            results.Add(new ProviderTransaction
                            {
                                TransactionId = info.TransactionId,
                                PaypalReferenceId = info.PaypalReferenceId,
                                InvoiceId = info.InvoiceId,
                                CustomField = info.CustomField,
                                Status = info.TransactionStatus,
                                Amount = AmountValue(info.TransactionAmount),
                                Currency = AmountCurrency(info.TransactionAmount),
                                FeeAmount = AmountValue(info.FeeAmount),
                                InitiationDate = info.TransactionInitiationDate
                            });
                        }
                    }

                    totalPages = response.TotalPages;
                    page++;
                } while (totalPages.HasValue ? page <= totalPages.Value : false);
            }

            return (IReadOnlyList<ProviderTransaction>)results;
        }, ct, SearchBudget);

    private async Task<AuthorizationResult> AuthorizeAsync(int orderId, decimal amount, string currency, CardRequest card, string requestId, CancellationToken ct)
    {
        return await Bounded(async token =>
        {
            _logger.LogInformation("Creating PayPal authorization for eShop order {OrderId}", orderId);
            Order created;
            try
            {
                created = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: $"{requestId}-create",
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
                                Amount = new AmountWithBreakdown
                                {
                                    CurrencyCode = currency,
                                    Value = FormatAmount(amount)
                                },
                                CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                                InvoiceId = $"eshop-{orderId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                                Description = $"eShopOnWeb order {orderId}"
                            }
                        },
                        PaymentSource = new PaymentSource { Card = card }
                    },
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: token);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                throw ToPaymentException(ex.Error, "CreateOrder");
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw TranslateBoundary(ex);
            }

            EnsureNoBrowserChallenge(created.Status, created.Id);
            if (string.IsNullOrEmpty(created.Id))
                throw new PaymentProcessingException("PayPal created an order without an id.", 502);

            var existingHold = FirstAuthorization(created.PurchaseUnits);
            if (existingHold != null && !string.IsNullOrEmpty(existingHold.Id))
            {
                return new AuthorizationResult
                {
                    PayPalOrderId = created.Id,
                    PayPalOrderStatus = Wire(created.Status),
                    AuthorizationId = existingHold.Id,
                    AuthorizationStatus = Wire(existingHold.Status),
                    ExpirationTime = ToTimestamp(existingHold.ExpirationTime)
                };
            }

            return await AuthorizeHoldAsync(created.Id, requestId, token);
        }, ct);
    }

    private async Task<AuthorizationResult> AuthorizeHoldAsync(string paypalOrderId, string requestId, CancellationToken ct)
    {
        OrderAuthorizeResponse authorized;
        try
        {
            authorized = await _client.Orders.AuthorizeOrder(
                id: paypalOrderId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: PreferRepresentation,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (IsAlreadyAuthorized(ex.Error))
            {
                Order refreshed;
                try
                {
                    refreshed = await _client.Orders.GetOrder(
                        id: paypalOrderId,
                        fields: "payment_source",
                        payPalMockResponse: null,
                        payPalAuthAssertion: null,
                        requestOptions: null,
                        ct: ct);
                }
                catch (SdkException<GetOrderError> getEx)
                {
                    throw ToPaymentException(getEx.Error, "GetOrder");
                }

                var existing = FirstAuthorization(refreshed.PurchaseUnits);
                if (existing == null || string.IsNullOrEmpty(existing.Id))
                    throw ToPaymentException(ex.Error, "AuthorizeOrder");

                return new AuthorizationResult
                {
                    PayPalOrderId = paypalOrderId,
                    PayPalOrderStatus = Wire(refreshed.Status),
                    AuthorizationId = existing.Id,
                    AuthorizationStatus = Wire(existing.Status),
                    ExpirationTime = ToTimestamp(existing.ExpirationTime)
                };
            }

            throw ToPaymentException(ex.Error, "AuthorizeOrder");
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw TranslateBoundary(ex);
        }

        EnsureNoBrowserChallenge(authorized.Status, authorized.Id);
        var hold = FirstAuthorization(authorized.PurchaseUnits);
        if (hold == null || string.IsNullOrEmpty(hold.Id))
        {
            Order refreshed;
            try
            {
                refreshed = await _client.Orders.GetOrder(
                    id: paypalOrderId,
                    fields: "payment_source",
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<GetOrderError> ex)
            {
                throw ToPaymentException(ex.Error, "GetOrder");
            }

            EnsureNoBrowserChallenge(refreshed.Status, refreshed.Id);
            hold = FirstAuthorization(refreshed.PurchaseUnits);
        }

        if (hold == null || string.IsNullOrEmpty(hold.Id))
            throw new PaymentProcessingException("PayPal authorized the order but returned no hold id.", 502);

        return new AuthorizationResult
        {
            PayPalOrderId = paypalOrderId,
            PayPalOrderStatus = Wire(authorized.Status),
            AuthorizationId = hold.Id,
            AuthorizationStatus = Wire(hold.Status),
            ExpirationTime = ToTimestamp(hold.ExpirationTime)
        };
    }

    private async Task<RefundResult> MapRefund(string captureId, Refund refund, decimal? requestedAmount, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refund.Id))
            refund = await _client.Payments.GetRefund(refund.Id ?? string.Empty, payPalMockResponse: null, payPalAuthAssertion: null, requestOptions: null, ct: ct);

        var refundAmount = ParseMoney(refund.Amount) ?? requestedAmount ?? 0m;
        string? captureStatus = null;
        try
        {
            var capture = await _client.Payments.GetCapturedPayment(captureId, payPalMockResponse: null, requestOptions: null, ct: ct);
            captureStatus = Wire(capture.Status);
        }
        catch (Exception ex) when (ex is SdkException<GetCapturedPaymentError> || IsTransportOrParse(ex))
        {
            _logger.LogWarning("Could not refresh capture {CaptureId} after refund", captureId);
        }

        return new RefundResult
        {
            RefundId = refund.Id ?? string.Empty,
            Status = Wire(refund.Status),
            Amount = refundAmount,
            CaptureStatus = captureStatus
        };
    }

    private async Task<RefundResult> RecoverRefund(string captureId, string paypalOrderId, decimal? amount, CancellationToken ct)
    {
        Order refreshed;
        try
        {
            refreshed = await _client.Orders.GetOrder(
                id: paypalOrderId,
                fields: null,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<GetOrderError> ex)
        {
            throw ToPaymentException(ex.Error, "GetOrder");
        }

        var refund = refreshed.PurchaseUnits?.FirstOrDefault()?.Payments?.Refunds?.LastOrDefault();
        if (refund == null || string.IsNullOrEmpty(refund.Id))
            throw new PaymentProcessingException("PayPal reported a duplicate refund request but no refund could be loaded.", 409);

        return await MapRefund(captureId, refund, amount, ct);
    }

    private static bool IsDuplicateRequest(RefundCapturedPaymentError error)
    {
        if (!error.TryGetError(out Error typed))
            return false;
        if (string.Equals(typed.Name, "DUPLICATE_REQUEST_ID", StringComparison.OrdinalIgnoreCase))
            return true;
        return typed.Details?.Any(d => string.Equals(d.Issue, "DUPLICATE_INVOICE_ID", StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.Issue, "DUPLICATE_REQUEST_ID", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static bool IsAlreadyAuthorized(AuthorizeOrderError error)
    {
        if (!error.TryGetError(out Error typed) || typed.Details == null)
            return false;
        return typed.Details.Any(d => string.Equals(d.Issue, "ORDER_ALREADY_AUTHORIZED", StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureNoBrowserChallenge(OrderStatus? status, string? paypalOrderId)
    {
        if (status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentProcessingException(
                $"PayPal required a shopper approval challenge for order {paypalOrderId}. This integration does not support a browser round-trip (GAP).",
                409,
                operatorActionable: true);
        }
    }

    private static CardRequest BuildCardRequest(CardPaymentInput card) =>
        new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = MapAddress(card.BillingAddress)
        };

    private static Address? MapAddress(CardBillingAddress? address)
    {
        if (address == null)
            return null;
        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? units) =>
        units?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    private static CaptureResult MapCapture(CapturedPayment captured)
    {
        var breakdown = captured.SellerReceivableBreakdown;
        return new CaptureResult
        {
            CaptureId = captured.Id ?? string.Empty,
            CaptureStatus = Wire(captured.Status),
            CapturedAmount = ParseMoney(captured.Amount) ?? ParseMoney(breakdown?.GrossAmount) ?? 0m,
            PaypalFee = ParseMoney(breakdown?.PaypalFee),
            NetAmount = ParseMoney(breakdown?.NetAmount),
            AuthorizationStatus = "CAPTURED"
        };
    }

    private static Money MoneyOf(string currency, decimal amount) =>
        new Money
        {
            CurrencyCode = currency,
            Value = FormatAmount(amount)
        };

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value == null)
            return null;
        if (decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return value;
        return null;
    }

    private static string? AmountValue(object? amount)
    {
        if (amount is Money money)
            return money.Value;
        return amount?.ToString();
    }

    private static string? AmountCurrency(object? amount)
    {
        if (amount is Money money)
            return money.CurrencyCode;
        return null;
    }

    private static string? Wire(dynamic? value)
    {
        if (value == null)
            return null;
        try
        {
            return (string)value.Value;
        }
        catch
        {
            return value.ToString();
        }
    }

    private static DateTimeOffset? ToTimestamp(dynamic? value)
    {
        if (value == null)
            return null;
        if (value is DateTimeOffset dto)
            return dto;
        if (value is DateTime dt)
            return new DateTimeOffset(dt);
        if (value is string s && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed;
        return null;
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> Windows(DateTimeOffset from, DateTimeOffset to)
    {
        var cursor = from;
        while (cursor <= to)
        {
            var windowEnd = cursor.AddDays(31).AddSeconds(-1);
            if (windowEnd > to)
                windowEnd = to;
            yield return (cursor, windowEnd);
            if (windowEnd >= to)
                yield break;
            cursor = windowEnd.AddSeconds(1);
        }
    }

    private static string Rfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct, TimeSpan? budget = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget ?? CallBudget);
        return await call(cts.Token);
    }

    private static bool IsTransportOrParse(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException or OperationCanceledException;

    private static PaymentProcessingException TranslateBoundary(Exception ex, bool operatorActionable = false)
    {
        if (ex is JsonException)
        {
            var status = LastStatusHandler.LastStatus.Value;
            if (status is >= HttpStatusCode.BadRequest and < HttpStatusCode.InternalServerError)
                return new PaymentProcessingException("PayPal rejected the request.", ex, (int)status.Value, operatorActionable);
            return new PaymentProcessingException("The provider returned a response that could not be processed.", ex, 502, operatorActionable);
        }

        return new PaymentProcessingException("PayPal is unreachable.", ex, 503, operatorActionable);
    }

    private static PaymentProcessingException ToPaymentException(dynamic error, string operation, bool operatorActionable = false)
    {
        if (error.TryGetError(out Error typed))
            return FromError(typed, operatorActionable);
        if (HasNoContent(error, out RawError noContent))
            return new PaymentProcessingException($"PayPal {operation} failed with HTTP {(int)noContent.StatusCode}.", (int)noContent.StatusCode, operatorActionable);
        if (error.TryGetRawError(out RawError raw))
            return new PaymentProcessingException($"PayPal {operation} failed (HTTP {(int)raw.StatusCode}): {SafeRaw(raw)}", (int)raw.StatusCode, operatorActionable);
        return new PaymentProcessingException($"PayPal {operation} failed.", StatusOr(502), operatorActionable);
    }

    private static PaymentProcessingException ToVaultException(dynamic error, string operation)
    {
        if (error.TryGetError1(out Error1 typed))
            return FromError1(typed);
        if (error.TryGetRawError(out RawError raw))
            return new PaymentProcessingException($"PayPal {operation} failed (HTTP {(int)raw.StatusCode}): {SafeRaw(raw)}", (int)raw.StatusCode);
        return new PaymentProcessingException($"PayPal {operation} failed.", StatusOr(502));
    }

    private static bool HasNoContent(dynamic error, out RawError raw)
    {
        try
        {
            return error.TryGetNoContent(out raw);
        }
        catch
        {
            raw = null!;
            return false;
        }
    }

    private static PaymentProcessingException FromError(Error error, bool operatorActionable)
    {
        var status = StatusOr(400);
        return new PaymentProcessingException(Format(error), status, operatorActionable || status is 409 or 422);
    }

    private static PaymentProcessingException FromError1(Error1 error)
    {
        var status = StatusOr(400);
        return new PaymentProcessingException(Format(error), status);
    }

    private static string Format(Error error)
    {
        var details = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(d => $"{d.Issue}{(string.IsNullOrEmpty(d.Field) ? string.Empty : " [" + d.Field + "]")}{(string.IsNullOrEmpty(d.Description) ? string.Empty : ": " + d.Description)}"))
            : null;
        return string.IsNullOrEmpty(details)
            ? $"{error.Name}: {error.Message} (debug_id={error.DebugId})"
            : $"{error.Name}: {error.Message} ({details}) (debug_id={error.DebugId})";
    }

    private static string Format(Error1 error)
    {
        var details = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(d => $"{d.Issue}{(string.IsNullOrEmpty(d.Field) ? string.Empty : " [" + d.Field + "]")}{(string.IsNullOrEmpty(d.Description) ? string.Empty : ": " + d.Description)}"))
            : null;
        return string.IsNullOrEmpty(details)
            ? $"{error.Name}: {error.Message} (debug_id={error.DebugId})"
            : $"{error.Name}: {error.Message} ({details}) (debug_id={error.DebugId})";
    }

    private static string SafeRaw(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return raw.StatusCode.ToString();
        }
    }

    private static int StatusOr(int fallback) =>
        LastStatusHandler.LastStatus.Value is { } status ? (int)status : fallback;
}
