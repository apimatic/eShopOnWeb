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
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalAddress = PayPalServerSdk.Models.Address;
using PayPalOrder = PayPalServerSdk.Models.Order;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private const string Representation = "return=representation";
    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<AuthorizationHold> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var cardRequest = new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = MapAddress(card.Address)
        };
        return AuthorizeAsync(orderId, amount, currency, cardRequest, idempotencyKey, cancellationToken);
    }

    public Task<AuthorizationHold> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var cardRequest = new CardRequest
        {
            VaultId = vaultId
        };
        return AuthorizeAsync(orderId, amount, currency, cardRequest, idempotencyKey, cancellationToken);
    }

    public async Task<AuthorizationHold> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await Bounded(ct => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct), cancellationToken, write: false);
            return ToHoldFromPaymentAuthorization(auth);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw MapGetAuthorizedPaymentError(ex);
        }
    }

    public async Task<AuthorizationHold> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = new ReauthorizeRequest
            {
                Amount = MoneyOf(currency, amount)
            };
            var auth = await Bounded(ct => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: Representation,
                ct: ct), cancellationToken, write: true);
            return ToHoldFromPaymentAuthorization(auth);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw MapReauthorizeError(ex);
        }
    }

    public async Task<CaptureDetails> CaptureAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var captured = await Bounded(ct => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: Representation,
                ct: ct), cancellationToken, write: true);
            return ToCaptureDetails(captured);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw MapCaptureError(ex);
        }
    }

    public async Task<CaptureDetails> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        try
        {
            var captured = await Bounded(ct => _client.Payments.GetCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                ct: ct), cancellationToken, write: false);
            return ToCaptureDetails(captured);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw MapGetCaptureError(ex);
        }
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            await Bounded(ct => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: Representation,
                ct: ct), cancellationToken, write: true);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error typed) && IsConflict(typed))
            {
                return;
            }

            throw MapVoidError(ex);
        }
    }

    public async Task<RefundDetails> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            RefundRequest? body = null;
            if (amount.HasValue)
            {
                body = new RefundRequest
                {
                    Amount = MoneyOf(currency, amount.Value)
                };
            }

            var refund = await Bounded(ct => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: Representation,
                ct: ct), cancellationToken, write: true);

            return new RefundDetails(
                refund.Id ?? throw new PaymentException("PayPal did not return a refund id.", 502),
                refund.Status?.Value ?? "COMPLETED",
                ParseMoney(refund.Amount));
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw MapRefundError(ex);
        }
    }

    public async Task<VaultedCardDetails> SaveCardAsync(
        string merchantCustomerId,
        string? existingPayPalCustomerId,
        CardPaymentDetails card,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var customer = new Customer
            {
                Id = existingPayPalCustomerId,
                MerchantCustomerId = merchantCustomerId
            };

            var body = new PaymentTokenRequest
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
                        BillingAddress = MapAddress(card.Address)
                    }
                }
            };

            var token = await Bounded(ct => _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                ct: ct), cancellationToken, write: true);

            EnsureNoVaultChallenge(token);

            var paypalCustomerId = token.Customer?.Id;
            if (string.IsNullOrEmpty(paypalCustomerId))
            {
                throw new PaymentException("PayPal did not return a customer id for the saved card.", 502);
            }

            if (string.IsNullOrEmpty(token.Id))
            {
                throw new PaymentException("PayPal did not return a payment token id.", 502);
            }

            return new VaultedCardDetails(
                token.Id,
                paypalCustomerId,
                token.PaymentSource?.Card?.LastDigits,
                token.PaymentSource?.Card?.Brand?.Value,
                token.PaymentSource?.Card?.Expiry,
                token.PaymentSource?.Card?.Name);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw MapCreatePaymentTokenError(ex);
        }
    }

    public async Task DeletePaymentTokenAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        try
        {
            await Bounded(ct => _client.Vault.DeletePaymentToken(id: paymentTokenId, ct: ct), cancellationToken, write: true);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetRawError(out RawError raw) && raw.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            throw MapDeletePaymentTokenError(ex);
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var records = new List<PayPalTransactionRecord>();
        foreach (var (start, end) in SplitIntoWindows(from, to))
        {
            var page = 1;
            var totalPages = 1;
            do
            {
                SearchResponse response;
                try
                {
                    var currentPage = page;
                    response = await Bounded(ct => _client.TransactionSearch.SearchTransactions(
                        startDate: FormatRfc3339(start),
                        endDate: FormatRfc3339(end),
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
                        page: currentPage,
                        ct: ct), cancellationToken, write: false);
                }
                catch (SdkException<RawError> ex)
                {
                    if (ex.Error.StatusCode == HttpStatusCode.NotFound)
                    {
                        // Reporting can 404 when the sandbox app has no transaction-search
                        // product (or the range is empty at the reporting edge). Walk the rest
                        // of the range; eShop-only rows still surface in reconciliation.
                        break;
                    }

                    throw FromRaw(ex.Error);
                }

                if (response.TransactionDetails != null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info == null)
                        {
                            continue;
                        }

                        records.Add(new PayPalTransactionRecord(
                            info.TransactionId,
                            info.TransactionAmount == null ? null : ParseMoney(info.TransactionAmount),
                            info.TransactionAmount?.CurrencyCode,
                            info.TransactionStatus,
                            info.TransactionInitiationDate ?? info.TransactionUpdatedDate,
                            info.FeeAmount == null ? null : ParseMoney(info.FeeAmount),
                            info.InvoiceId,
                            info.CustomField,
                            info.PaypalReferenceId,
                            info.PaypalReferenceIdType?.Value,
                            info.TransactionEventCode));
                    }
                }

                totalPages = response.TotalPages.GetValueOrDefault(1);
                if (totalPages < 1)
                {
                    totalPages = 1;
                }

                page++;
            } while (page <= totalPages);
        }

        return records;
    }

    private async Task<AuthorizationHold> AuthorizeAsync(
        int orderId,
        decimal amount,
        string currency,
        CardRequest cardRequest,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = FormatAmount(amount)
                    },
                    CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                    InvoiceId = "o" + orderId.ToString(CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8]
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = cardRequest
            }
        };

        PayPalOrder created;
        try
        {
            created = await Bounded(ct => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: Representation,
                ct: ct), cancellationToken, write: true);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapCreateOrderError(ex);
        }

        EnsureNoPayerAction(created);
        if (string.IsNullOrEmpty(created.Id))
        {
            throw new PaymentException("PayPal did not return an order id.", 502);
        }

        // Direct card (payment_source on create) is a single-step authorize: the hold may
        // already be present. A second AuthorizeOrder then fails with ORDER_ALREADY_AUTHORIZED.
        var fromCreate = TryReadHold(created.Id, created.PurchaseUnits, created.Id);
        if (fromCreate != null)
        {
            return fromCreate;
        }

        OrderAuthorizeResponse authorized;
        try
        {
            authorized = await Bounded(ct => _client.Orders.AuthorizeOrder(
                id: created.Id,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey + "-auth",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: Representation,
                ct: ct), cancellationToken, write: true);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (IsAlreadyAuthorized(ex))
            {
                return await GetHoldFromOrder(created.Id, cancellationToken);
            }

            throw MapAuthorizeOrderError(ex);
        }

        EnsureNoPayerAction(authorized);
        return ReadHold(created.Id, authorized);
    }

    private async Task<AuthorizationHold> GetHoldFromOrder(string paypalOrderId, CancellationToken cancellationToken)
    {
        PayPalOrder order;
        try
        {
            order = await Bounded(ct => _client.Orders.GetOrder(
                id: paypalOrderId,
                fields: null,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct), cancellationToken, write: false);
        }
        catch (SdkException<GetOrderError> ex)
        {
            if (ex.Error.TryGetError(out Error typed))
            {
                throw FromError(typed);
            }

            if (ex.Error.TryGetRawError(out RawError raw))
            {
                throw FromRaw(raw);
            }

            throw new PaymentException("PayPal could not load the order after authorization.", 502);
        }

        EnsureNoPayerAction(order);
        return ReadHoldFromUnits(paypalOrderId, order.PurchaseUnits, order.Id);
    }

    private static bool IsAlreadyAuthorized(SdkException<AuthorizeOrderError> ex)
    {
        if (!ex.Error.TryGetError(out Error typed) || typed.Details == null)
        {
            return false;
        }

        return typed.Details.Any(d =>
            string.Equals(d.Issue, "ORDER_ALREADY_AUTHORIZED", StringComparison.OrdinalIgnoreCase));
    }

    private static AuthorizationHold? TryReadHold(string paypalOrderId, IReadOnlyList<PurchaseUnit>? units, string? responseId)
    {
        var auth = units?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (auth == null || string.IsNullOrEmpty(auth.Id))
        {
            return null;
        }

        RejectIfDeniedOrChallenged(auth.Status, auth.ProcessorResponse);
        return ToHold(responseId ?? paypalOrderId, auth);
    }

    private static AuthorizationHold ReadHoldFromUnits(string paypalOrderId, IReadOnlyList<PurchaseUnit>? units, string? responseId)
    {
        return TryReadHold(paypalOrderId, units, responseId)
            ?? throw new PaymentException("PayPal did not return an authorization hold.", 502);
    }

    private static AuthorizationHold ReadHold(string paypalOrderId, OrderAuthorizeResponse response)
    {
        var auth = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (auth == null || string.IsNullOrEmpty(auth.Id))
        {
            throw new PaymentException("PayPal did not return an authorization hold.", 502);
        }

        RejectIfDeniedOrChallenged(auth.Status, auth.ProcessorResponse);
        return ToHold(response.Id ?? paypalOrderId, auth);
    }

    private static AuthorizationHold ToHold(string paypalOrderId, AuthorizationWithAdditionalData auth)
    {
        return new AuthorizationHold(
            paypalOrderId,
            auth.Id!,
            auth.Status?.Value ?? AuthorizationStatus.Created.Value,
            ParseMoney(auth.Amount),
            auth.Amount?.CurrencyCode ?? string.Empty,
            ParseTime(auth.ExpirationTime),
            ParseTime(auth.CreateTime));
    }

    private static AuthorizationHold ToHoldFromPaymentAuthorization(PaymentAuthorization auth)
    {
        if (string.IsNullOrEmpty(auth.Id))
        {
            throw new PaymentException("PayPal did not return an authorization id.", 502);
        }

        // PaymentAuthorization has no ProcessorResponse (unlike AuthorizationWithAdditionalData).
        RejectIfDeniedOrChallenged(auth.Status, null);

        return new AuthorizationHold(
            auth.Id,
            auth.Id,
            auth.Status?.Value ?? AuthorizationStatus.Created.Value,
            ParseMoney(auth.Amount),
            auth.Amount?.CurrencyCode ?? string.Empty,
            ParseTime(auth.ExpirationTime),
            ParseTime(auth.CreateTime));
    }

    private static CaptureDetails ToCaptureDetails(CapturedPayment captured)
    {
        if (string.IsNullOrEmpty(captured.Id))
        {
            throw new PaymentException("PayPal did not return a capture id.", 502);
        }

        if (captured.Status == CaptureStatus.Declined || captured.Status == CaptureStatus.Failed)
        {
            throw new PaymentException($"PayPal capture {captured.Status?.Value}.", 402);
        }

        var breakdown = captured.SellerReceivableBreakdown;
        var capturedAmount = breakdown != null ? ParseMoney(breakdown.GrossAmount) : ParseMoney(captured.Amount);
        return new CaptureDetails(
            captured.Id,
            captured.Status?.Value ?? CaptureStatus.Completed.Value,
            capturedAmount,
            breakdown?.PaypalFee == null ? null : ParseMoney(breakdown.PaypalFee),
            breakdown?.NetAmount == null ? null : ParseMoney(breakdown.NetAmount),
            captured.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? string.Empty);
    }

    private static void EnsureNoPayerAction(PayPalOrder order)
    {
        if (order.Status == OrderStatus.PayerActionRequired
            || HasPayerActionLink(order.Links)
            || IsChallenge(order.PaymentSource?.Card?.AuthenticationResult?.ThreeDSecure?.AuthenticationStatus))
        {
            throw ChallengeRequired();
        }
    }

    private static void EnsureNoPayerAction(OrderAuthorizeResponse response)
    {
        if (response.Status == OrderStatus.PayerActionRequired
            || HasPayerActionLink(response.Links)
            || IsChallenge(response.PaymentSource?.Card?.AuthenticationResult?.ThreeDSecure?.AuthenticationStatus))
        {
            throw ChallengeRequired();
        }
    }

    private static void EnsureNoVaultChallenge(PaymentTokenResponse token)
    {
        if (IsChallenge(token.PaymentSource?.Card?.AuthenticationResult?.ThreeDSecure?.AuthenticationStatus))
        {
            throw ChallengeRequired();
        }
    }

    private static bool HasPayerActionLink(IReadOnlyList<LinkDescription>? links)
    {
        return links != null && links.Any(l => l.Rel == "payer-action");
    }

    private static bool IsChallenge(ParesStatus? status)
    {
        return status == ParesStatus.C || status == ParesStatus.D || status == ParesStatus.R;
    }

    private static void RejectIfDeniedOrChallenged(AuthorizationStatus? status, ProcessorResponse? processor)
    {
        if (processor?.ResponseCode == ProcessorResponseCode._5650)
        {
            throw ChallengeRequired();
        }

        if (status == AuthorizationStatus.Denied)
        {
            throw new PaymentException("The card was declined.", 402);
        }

        if (processor?.ResponseCode == ProcessorResponseCode._5120)
        {
            throw new PaymentException("The card was declined (insufficient funds).", 402);
        }

        if (processor?.ResponseCode == ProcessorResponseCode._5100
            || processor?.ResponseCode == ProcessorResponseCode._5110
            || processor?.ResponseCode == ProcessorResponseCode._5400)
        {
            throw new PaymentException("The card was declined.", 402);
        }
    }

    private static PaymentException ChallengeRequired()
    {
        return new PaymentException(
            "PayPal required a shopper challenge (3-D Secure / payer action). This integration does not support a browser approval round-trip.",
            409);
    }

    private static PayPalAddress? MapAddress(BillingAddressDetails? address)
    {
        if (address == null)
        {
            return null;
        }

        return new PayPalAddress
        {
            AddressLine1 = address.Line1,
            AddressLine2 = address.Line2,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static Money MoneyOf(string currency, decimal amount)
    {
        return new Money
        {
            CurrencyCode = currency,
            Value = FormatAmount(amount)
        };
    }

    private static string FormatAmount(decimal amount)
    {
        return amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static decimal ParseMoney(Money? money)
    {
        if (money?.Value == null)
        {
            return 0m;
        }

        return decimal.Parse(money.Value, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatRfc3339(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitIntoWindows(DateTimeOffset from, DateTimeOffset to)
    {
        var windowStart = from;
        var maxWindow = TimeSpan.FromDays(31);
        while (windowStart < to)
        {
            var windowEnd = windowStart.Add(maxWindow);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            yield return (windowStart, windowEnd);
            windowStart = windowEnd;
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct, bool write)
    {
        if (write)
        {
            PayPalWriteOnceHandler.Reset();
        }

        PayPalStatusCaptureHandler.Reset();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            return await call(cts.Token);
        }
        catch (PayPalDuplicateSendException)
        {
            throw new PaymentException(
                "The payment request may already have reached PayPal. Refresh the order before retrying.",
                409);
        }
        catch (JsonException ex)
        {
            throw MapJsonException(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal is unreachable.", 502, innerException: ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new PaymentException("PayPal authentication could not be applied.", 502, innerException: ex);
        }
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct, bool write)
    {
        await Bounded(async token =>
        {
            await call(token);
            return 0;
        }, ct, write);
    }

    private static PaymentException MapJsonException(JsonException ex)
    {
        var status = PayPalStatusCaptureHandler.LastStatus;
        if (status.HasValue && (int)status.Value >= 400)
        {
            return new PaymentException(
                $"PayPal rejected the request (HTTP {(int)status.Value}).",
                MapHttpStatus(status.Value),
                innerException: ex);
        }

        return new PaymentException(
            "PayPal returned a response that could not be processed.",
            502,
            innerException: ex);
    }

    private static int MapHttpStatus(HttpStatusCode status)
    {
        var code = (int)status;
        if (code == 401 || code == 403)
        {
            return code;
        }

        if (code >= 400 && code < 500)
        {
            return code;
        }

        return 502;
    }

    private static PaymentException FromError(Error error)
    {
        var details = error.Details == null
            ? string.Empty
            : string.Join("; ", error.Details.Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"));
        var message = string.IsNullOrEmpty(details)
            ? $"{error.Name}: {error.Message}"
            : $"{error.Name}: {error.Message} ({details})";
        return new PaymentException(message, StatusFromName(error.Name), error.DebugId);
    }

    private static PaymentException FromError1(Error1 error)
    {
        var details = error.Details == null
            ? string.Empty
            : string.Join("; ", error.Details.Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"));
        var message = string.IsNullOrEmpty(details)
            ? $"{error.Name}: {error.Message}"
            : $"{error.Name}: {error.Message} ({details})";
        return new PaymentException(message, StatusFromName(error.Name), error.DebugId);
    }

    private static PaymentException FromRaw(RawError raw)
    {
        var body = raw.ReadAsString();
        var message = string.IsNullOrWhiteSpace(body)
            ? $"PayPal request failed (HTTP {(int)raw.StatusCode})."
            : $"PayPal request failed (HTTP {(int)raw.StatusCode}).";
        return new PaymentException(message, MapHttpStatus(raw.StatusCode));
    }

    private static int StatusFromName(string name)
    {
        return name switch
        {
            "AUTHENTICATION_FAILURE" => 401,
            "NOT_AUTHORIZED" => 403,
            "RESOURCE_NOT_FOUND" => 404,
            "RESOURCE_CONFLICT" => 409,
            "UNPROCESSABLE_ENTITY" => 422,
            "INVALID_REQUEST" => 400,
            "INTERNAL_SERVER_ERROR" => 502,
            _ => 400
        };
    }

    private static bool IsConflict(Error error)
    {
        return string.Equals(error.Name, "RESOURCE_CONFLICT", StringComparison.OrdinalIgnoreCase)
            || error.Details?.Any(d => d.Issue != null && d.Issue.Contains("ALREADY", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static PaymentException MapCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error typed))
        {
            return FromError(typed);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new PaymentException("PayPal could not create the payment order.", 502);
    }

    private static PaymentException MapAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error typed))
        {
            return FromError(typed);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new PaymentException("PayPal could not authorize the payment.", 502);
    }

    private static PaymentException MapCaptureError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error typed))
        {
            return FromError(typed);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new PaymentException("PayPal could not capture the authorization.", 502);
    }

    private static PaymentException MapReauthorizeError(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error typed))
        {
            return new PaymentException(
                "The authorization could not be renewed. If more than 30 days have passed since the original hold, ask the shopper to pay again. "
                + FromError(typed).Message,
                FromError(typed).StatusCode,
                typed.DebugId);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new PaymentException("PayPal could not renew the authorization. Ask the shopper to pay again.", 409);
    }

    private static PaymentException MapVoidError(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error typed))
        {
            return FromError(typed);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new PaymentException("PayPal could not release the authorization.", 502);
    }

    private static PaymentException MapRefundError(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error typed))
        {
            return FromError(typed);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new PaymentException("PayPal could not refund the capture.", 502);
    }

    private static PaymentException MapGetAuthorizedPaymentError(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error typed))
        {
            return FromError(typed);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new PaymentException("PayPal could not load the authorization.", 502);
    }

    private static PaymentException MapGetCaptureError(SdkException<GetCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error typed))
        {
            return FromError(typed);
        }

        if (ex.Error.TryGetNoContent(out RawError noContent))
        {
            return FromRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new PaymentException("PayPal could not load the capture.", 502);
    }

    private static PaymentException MapCreatePaymentTokenError(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 typed))
        {
            return FromError1(typed);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new PaymentException("PayPal could not save the card.", 502);
    }

    private static PaymentException MapDeletePaymentTokenError(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1 typed))
        {
            return FromError1(typed);
        }

        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw);
        }

        return new PaymentException("PayPal could not delete the saved card.", 502);
    }
}
