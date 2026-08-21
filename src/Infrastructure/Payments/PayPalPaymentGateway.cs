using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private const string PreferRepresentation = "return=representation";
    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<string> CreateOrderWithCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentDetails card,
        string requestId,
        CancellationToken ct)
    {
        return Bounded(innerCt => CreateOrderAsync(orderId, amount, currency, BuildCardRequest(card), requestId, innerCt), ct);
    }

    public Task<string> CreateOrderWithVaultIdAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string requestId,
        CancellationToken ct)
    {
        return Bounded(innerCt => CreateOrderAsync(orderId, amount, currency, BuildVaultCardRequest(vaultId), requestId, innerCt), ct);
    }

    public Task<AuthorizationResult> AuthorizeExistingOrderAsync(
        string payPalOrderId,
        string requestId,
        CancellationToken ct)
    {
        return Bounded(async innerCt =>
        {
            try
            {
                var authorized = await _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: innerCt);

                var mapped = MapAuthorization(
                    authorized.Id ?? payPalOrderId,
                    authorized.Status,
                    authorized.PurchaseUnits,
                    authorized.Links);

                if (mapped.RequiresPayerAction)
                {
                    return mapped;
                }

                if (!string.IsNullOrEmpty(mapped.AuthorizationId))
                {
                    return mapped;
                }

                var fetched = await _client.Orders.GetOrder(
                    id: payPalOrderId,
                    fields: null,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: innerCt);

                return MapAuthorization(
                    fetched.Id ?? payPalOrderId,
                    fetched.Status,
                    fetched.PurchaseUnits,
                    fetched.Links);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw ToPaymentException(ex.Error, 400);
            }
            catch (SdkException<GetOrderError> ex)
            {
                throw ToPaymentException(ex.Error, 404);
            }
        }, ct);
    }

    public Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken ct)
    {
        return Bounded(async innerCt =>
        {
            try
            {
                var body = new ReauthorizeRequest
                {
                    Amount = MoneyOf(amount, currency)
                };

                var auth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: innerCt);

                if (string.IsNullOrEmpty(auth.Id))
                {
                    throw new PaymentException(502, "PayPal reauthorized the hold but did not return an authorization id.");
                }

                return new AuthorizationResult(
                    PayPalOrderId: string.Empty,
                    OrderStatus: auth.Status?.Value ?? string.Empty,
                    AuthorizationId: auth.Id,
                    AuthorizationStatus: auth.Status?.Value ?? string.Empty,
                    ExpirationTime: ToDateTimeOffset(auth.ExpirationTime),
                    HeldAmount: PayPalMoney.Parse(auth.Amount?.Value),
                    RequiresPayerAction: false);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw ToPaymentException(ex.Error, 422);
            }
        }, ct);
    }

    public Task<CaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string invoiceId,
        string requestId,
        CancellationToken ct)
    {
        return Bounded(async innerCt =>
        {
            try
            {
                var body = new CaptureRequest
                {
                    Amount = MoneyOf(amount, currency),
                    InvoiceId = invoiceId,
                    FinalCapture = true
                };

                var captured = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: innerCt);

                return MapCapture(captured);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw ToPaymentException(ex.Error, 400);
            }
        }, ct);
    }

    public Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken ct)
    {
        return Bounded(async innerCt =>
        {
            try
            {
                var captured = await _client.Payments.GetCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    requestOptions: null,
                    ct: innerCt);
                return MapCapture(captured);
            }
            catch (SdkException<GetCapturedPaymentError> ex)
            {
                throw ToPaymentException(ex.Error, 404);
            }
        }, ct);
    }

    public Task VoidAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        return Bounded(async innerCt =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: requestId,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: innerCt);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw ToPaymentException(ex.Error, 409);
            }
        }, ct);
    }

    public Task<RefundGatewayResult> RefundAsync(
        string captureId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken ct)
    {
        return Bounded(async innerCt =>
        {
            try
            {
                var body = new RefundRequest
                {
                    Amount = MoneyOf(amount, currency)
                };

                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: innerCt);

                if (string.IsNullOrEmpty(refund.Id))
                {
                    throw new PaymentException(502, "PayPal accepted the refund but did not return a refund id.");
                }

                return new RefundGatewayResult(
                    refund.Id,
                    refund.Status?.Value ?? string.Empty,
                    PayPalMoney.Parse(refund.Amount?.Value));
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw ToPaymentException(ex.Error, 400);
            }
        }, ct);
    }

    public Task<VaultedCardResult> VaultCardAsync(
        string merchantCustomerId,
        CardPaymentDetails card,
        string requestId,
        CancellationToken ct)
    {
        return Bounded(async innerCt =>
        {
            try
            {
                var body = new PaymentTokenRequest
                {
                    Customer = new Customer
                    {
                        MerchantCustomerId = merchantCustomerId
                    },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = BuildVaultCard(card)
                    }
                };

                var token = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: requestId,
                    body: body,
                    requestOptions: null,
                    ct: innerCt);

                if (string.IsNullOrEmpty(token.Id))
                {
                    throw new PaymentException(502, "PayPal vaulted the card but did not return a payment token id.");
                }

                var entity = token.PaymentSource?.Card;
                var requiresAction = RelLooksLikePayerAction(token.Links)
                    || entity?.VerificationStatus == CardVerificationStatus.Failed;

                return new VaultedCardResult(
                    token.Id,
                    token.Customer?.Id,
                    token.Customer?.MerchantCustomerId,
                    entity?.LastDigits,
                    entity?.Brand?.Value,
                    entity?.Expiry,
                    entity?.Name,
                    requiresAction);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw ToVaultPaymentException(ex.Error, 400);
            }
        }, ct);
    }

    public Task<IReadOnlyList<VaultedCardResult>> ListVaultedCardsAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        CancellationToken ct)
    {
        return Bounded(async innerCt =>
        {
            try
            {
                var listed = await ListAllTokens(merchantCustomerId, innerCt);
                if (listed.Count == 0 && !string.IsNullOrEmpty(payPalCustomerId)
                    && !string.Equals(payPalCustomerId, merchantCustomerId, StringComparison.Ordinal))
                {
                    listed = await ListAllTokens(payPalCustomerId, innerCt);
                }

                return listed;
            }
            catch (SdkException<ListCustomerPaymentTokensError> ex)
            {
                if (!string.IsNullOrEmpty(payPalCustomerId)
                    && !string.Equals(payPalCustomerId, merchantCustomerId, StringComparison.Ordinal))
                {
                    try
                    {
                        return await ListAllTokens(payPalCustomerId, innerCt);
                    }
                    catch (SdkException<ListCustomerPaymentTokensError> retry)
                    {
                        throw ToVaultPaymentException(retry.Error, 400);
                    }
                }

                throw ToVaultPaymentException(ex.Error, 400);
            }
        }, ct);
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        return Bounded(async innerCt =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(
                    id: vaultId,
                    requestOptions: null,
                    ct: innerCt);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw ToVaultPaymentException(ex.Error, 400);
            }
        }, ct);
    }

    public Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        return Bounded(async innerCt =>
        {
            var results = new List<PayPalTransactionRecord>();
            foreach (var (start, end) in SplitWindows(from, to))
            {
                var page = 1;
                int? totalPages = null;
                do
                {
                    SearchResponse pageResponse;
                    try
                    {
                        pageResponse = await _client.TransactionSearch.SearchTransactions(
                            startDate: ToRfc3339(start),
                            endDate: ToRfc3339(end),
                            transactionId: null,
                            transactionType: null,
                            transactionStatus: null,
                            transactionAmount: null,
                            transactionCurrency: null,
                            paymentInstrumentType: null,
                            storeId: null,
                            terminalId: null,
                            fields: "transaction_info",
                            balanceAffectingRecordsOnly: "N",
                            pageSize: 100,
                            page: page,
                            requestOptions: null,
                            ct: innerCt);
                    }
                    catch (SdkException<RawError> ex)
                    {
                        var body = SafeRead(ex.Error);
                        throw new PaymentException(
                            (int)ex.Error.StatusCode,
                            string.IsNullOrWhiteSpace(body)
                                ? "PayPal transaction search failed."
                                : $"PayPal transaction search failed: {TrimForCaller(body)}",
                            ex);
                    }

                    if (pageResponse.TransactionDetails != null)
                    {
                        foreach (var detail in pageResponse.TransactionDetails)
                        {
                            var info = detail.TransactionInfo;
                            results.Add(new PayPalTransactionRecord(
                                info?.TransactionId,
                                info?.PaypalReferenceId,
                                info?.InvoiceId,
                                info?.CustomField,
                                info?.TransactionStatus,
                                info?.TransactionAmount?.Value,
                                info?.FeeAmount?.Value,
                                info?.TransactionInitiationDate));
                        }
                    }

                    totalPages = pageResponse.TotalPages;
                    page++;
                    if (pageResponse.TransactionDetails == null || pageResponse.TransactionDetails.Count == 0)
                    {
                        break;
                    }
                } while (totalPages.HasValue && page <= totalPages.Value);
            }

            return (IReadOnlyList<PayPalTransactionRecord>)results;
        }, ct);
    }

    private async Task<string> CreateOrderAsync(
        int orderId,
        decimal amount,
        string currency,
        CardRequest cardRequest,
        string requestId,
        CancellationToken ct)
    {
        try
        {
            var body = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = currency,
                            Value = PayPalMoney.Format(amount, currency)
                        },
                        InvoiceId = orderId.ToString(CultureInfo.InvariantCulture),
                        CustomId = orderId.ToString(CultureInfo.InvariantCulture)
                    }
                },
                PaymentSource = new PaymentSource
                {
                    Card = cardRequest
                }
            };

            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: PreferRepresentation,
                requestOptions: null,
                ct: ct);

            if (order.Status == OrderStatus.PayerActionRequired || RelLooksLikePayerAction(order.Links))
            {
                throw new PaymentException(409,
                    "PayPal required a shopper approval challenge (3DS / payer-action). This integration does not implement a browser round-trip.");
            }

            if (string.IsNullOrEmpty(order.Id))
            {
                throw new PaymentException(502, "PayPal created an order but did not return an id.");
            }

            return order.Id;
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw ToPaymentException(ex.Error, 400);
        }
    }

    private async Task<IReadOnlyList<VaultedCardResult>> ListAllTokens(string customerId, CancellationToken ct)
    {
        var all = new List<VaultedCardResult>();
        var page = 1;
        int? totalPages = null;
        do
        {
            var response = await _client.Vault.ListCustomerPaymentTokens(
                customerId: customerId,
                pageSize: 20,
                page: page,
                totalRequired: true,
                requestOptions: null,
                ct: ct);

            if (response.PaymentTokens != null)
            {
                foreach (var token in response.PaymentTokens)
                {
                    if (string.IsNullOrEmpty(token.Id))
                    {
                        continue;
                    }

                    var entity = token.PaymentSource?.Card;
                    all.Add(new VaultedCardResult(
                        token.Id,
                        token.Customer?.Id,
                        token.Customer?.MerchantCustomerId,
                        entity?.LastDigits,
                        entity?.Brand?.Value,
                        entity?.Expiry,
                        entity?.Name,
                        RequiresPayerAction: false));
                }
            }

            totalPages = response.TotalPages;
            page++;
            if (response.PaymentTokens == null || response.PaymentTokens.Count == 0)
            {
                break;
            }
        } while (totalPages.HasValue && page <= totalPages.Value);

        return all;
    }

    private static AuthorizationResult MapAuthorization(
        string orderId,
        OrderStatus? status,
        IReadOnlyList<PurchaseUnit>? units,
        IReadOnlyList<LinkDescription>? links)
    {
        var requiresAction = status == OrderStatus.PayerActionRequired || RelLooksLikePayerAction(links);
        var auth = units?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        return new AuthorizationResult(
            orderId,
            status?.Value ?? string.Empty,
            auth?.Id ?? string.Empty,
            auth?.Status?.Value ?? string.Empty,
            ToDateTimeOffset(auth?.ExpirationTime),
            PayPalMoney.Parse(auth?.Amount?.Value),
            requiresAction);
    }

    private static CaptureResult MapCapture(CapturedPayment captured)
    {
        if (string.IsNullOrEmpty(captured.Id))
        {
            throw new PaymentException(502, "PayPal capture response did not include a capture id.");
        }

        var pending = captured.Status == CaptureStatus.Pending;
        var breakdown = captured.SellerReceivableBreakdown;

        return new CaptureResult(
            captured.Id,
            captured.Status?.Value ?? string.Empty,
            PayPalMoney.Parse(captured.Amount?.Value),
            pending ? null : PayPalMoneyOrNull(breakdown?.PaypalFee),
            pending ? null : PayPalMoneyOrNull(breakdown?.NetAmount),
            PayPalMoneyOrNull(breakdown?.GrossAmount) ?? PayPalMoney.Parse(captured.Amount?.Value),
            pending);
    }

    private static decimal? PayPalMoneyOrNull(Money? money)
    {
        return money?.Value == null ? null : PayPalMoney.Parse(money.Value);
    }

    private static CardRequest BuildCardRequest(CardPaymentDetails card)
    {
        return new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToAddress(card.BillingAddress)
        };
    }

    private static CardRequest BuildVaultCardRequest(string vaultId)
    {
        return new CardRequest
        {
            VaultId = vaultId,
            StoredCredential = new CardStoredCredential
            {
                PaymentInitiator = PaymentInitiator.Customer,
                PaymentType = StoredPaymentSourcePaymentType.Unscheduled,
                Usage = StoredPaymentSourceUsageType.Subsequent
            }
        };
    }

    private static PaymentTokenRequestCard BuildVaultCard(CardPaymentDetails card)
    {
        return new PaymentTokenRequestCard
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToAddress(card.BillingAddress)
        };
    }

    private static Address? ToAddress(BillingAddressDetails? billing)
    {
        if (billing == null)
        {
            return new Address { CountryCode = "US" };
        }

        return new Address
        {
            AddressLine1 = billing.AddressLine1,
            AddressLine2 = billing.AddressLine2,
            AdminArea2 = billing.AdminArea2,
            AdminArea1 = billing.AdminArea1,
            PostalCode = billing.PostalCode,
            CountryCode = string.IsNullOrWhiteSpace(billing.CountryCode) ? "US" : billing.CountryCode
        };
    }

    private static Money MoneyOf(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = PayPalMoney.Format(amount, currency)
    };

    private static DateTimeOffset? ToDateTimeOffset(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case DateTimeOffset dto:
                return dto;
            case DateTime dt:
                return new DateTimeOffset(dt.ToUniversalTime());
            case string s when DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed):
                return parsed;
            default:
                return null;
        }
    }

    private static bool RelLooksLikePayerAction(IReadOnlyList<LinkDescription>? links)
    {
        if (links == null)
        {
            return false;
        }

        foreach (var link in links)
        {
            if (RelLooksLikePayerAction(link.Rel))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RelLooksLikePayerAction(string? rel)
    {
        if (string.IsNullOrEmpty(rel))
        {
            return false;
        }

        return rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase)
            || rel.Equals("approve", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitWindows(DateTimeOffset from, DateTimeOffset to)
    {
        var cursor = from;
        while (cursor <= to)
        {
            var windowEnd = cursor.AddDays(31).AddSeconds(-1);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            yield return (cursor, windowEnd);
            if (windowEnd == to)
            {
                yield break;
            }

            cursor = windowEnd.AddSeconds(1);
        }
    }

    private static string ToRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        PayPalCallContext.BeginWriteGuard();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(30));
            return await call(cts.Token);
        }
        catch (PaymentException)
        {
            throw;
        }
        catch (DuplicateWriteRefusedException ex)
        {
            throw new PaymentException(503,
                "PayPal write outcome is unknown after a transport retry was blocked. Re-read payment state before retrying.",
                ex);
        }
        catch (JsonException ex)
        {
            var status = PayPalCallContext.LastStatusCode;
            if (status is >= 400 and < 500)
            {
                throw new PaymentException(status.Value, "PayPal rejected the request.", ex);
            }

            throw new PaymentException(502, "The provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException(503, "PayPal is unreachable. Try again shortly.", ex);
        }
        finally
        {
            PayPalCallContext.EndWriteGuard();
        }
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        await Bounded(async inner =>
        {
            await call(inner);
            return true;
        }, ct);
    }

    private static PaymentException ToPaymentException(CreateOrderError error, int fallback)
    {
        if (error.TryGetError(out Error typed))
        {
            return FromTyped(typed, fallback);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, "PayPal could not create the payment order.");
        }

        return new PaymentException(fallback, "PayPal could not create the payment order.");
    }

    private static PaymentException ToPaymentException(AuthorizeOrderError error, int fallback)
    {
        if (error.TryGetError(out Error typed))
        {
            return FromTyped(typed, fallback);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, "PayPal could not authorize the payment.");
        }

        return new PaymentException(fallback, "PayPal could not authorize the payment.");
    }

    private static PaymentException ToPaymentException(GetOrderError error, int fallback)
    {
        if (error.TryGetError(out Error typed))
        {
            return FromTyped(typed, fallback);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, "PayPal could not load the payment order.");
        }

        return new PaymentException(fallback, "PayPal could not load the payment order.");
    }

    private static PaymentException ToPaymentException(ReauthorizePaymentError error, int fallback)
    {
        const string generic = "The authorization hold cannot be renewed. Ask the shopper to authorize a new payment.";
        if (error.TryGetError(out Error typed))
        {
            return FromTyped(typed, fallback);
        }

        if (error.TryGetNoContent(out RawError noContent))
        {
            return new PaymentException((int)noContent.StatusCode, generic);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, generic);
        }

        return new PaymentException(fallback, generic);
    }

    private static PaymentException ToPaymentException(CaptureAuthorizedPaymentError error, int fallback)
    {
        const string generic = "PayPal could not capture the authorized payment.";
        if (error.TryGetError(out Error typed))
        {
            return FromTyped(typed, fallback);
        }

        if (error.TryGetNoContent(out RawError noContent))
        {
            return new PaymentException((int)noContent.StatusCode, generic);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, generic);
        }

        return new PaymentException(fallback, generic);
    }

    private static PaymentException ToPaymentException(GetCapturedPaymentError error, int fallback)
    {
        const string generic = "PayPal could not load the captured payment.";
        if (error.TryGetError(out Error typed))
        {
            return FromTyped(typed, fallback);
        }

        if (error.TryGetNoContent(out RawError noContent))
        {
            return new PaymentException((int)noContent.StatusCode, generic);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, generic);
        }

        return new PaymentException(fallback, generic);
    }

    private static PaymentException ToPaymentException(VoidPaymentError error, int fallback)
    {
        const string generic = "PayPal could not release the authorized funds.";
        if (error.TryGetError(out Error typed))
        {
            return FromTyped(typed, fallback);
        }

        if (error.TryGetNoContent(out RawError noContent))
        {
            return new PaymentException((int)noContent.StatusCode, generic);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, generic);
        }

        return new PaymentException(fallback, generic);
    }

    private static PaymentException ToPaymentException(RefundCapturedPaymentError error, int fallback)
    {
        const string generic = "PayPal could not refund the captured payment.";
        if (error.TryGetError(out Error typed))
        {
            return FromTyped(typed, fallback);
        }

        if (error.TryGetNoContent(out RawError noContent))
        {
            return new PaymentException((int)noContent.StatusCode, generic);
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, generic);
        }

        return new PaymentException(fallback, generic);
    }

    private static PaymentException ToVaultPaymentException(CreatePaymentTokenError error, int fallback)
    {
        if (error.TryGetError1(out Error1 typed))
        {
            return new PaymentException(GuessStatus(typed.Name, fallback), FormatError1(typed));
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, "PayPal could not save the card.");
        }

        return new PaymentException(fallback, "PayPal could not save the card.");
    }

    private static PaymentException ToVaultPaymentException(ListCustomerPaymentTokensError error, int fallback)
    {
        if (error.TryGetError1(out Error1 typed))
        {
            return new PaymentException(GuessStatus(typed.Name, fallback), FormatError1(typed));
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, "PayPal could not list saved cards.");
        }

        return new PaymentException(fallback, "PayPal could not list saved cards.");
    }

    private static PaymentException ToVaultPaymentException(DeletePaymentTokenError error, int fallback)
    {
        if (error.TryGetError1(out Error1 typed))
        {
            return new PaymentException(GuessStatus(typed.Name, fallback), FormatError1(typed));
        }

        if (error.TryGetRawError(out RawError raw))
        {
            return FromRaw(raw, "PayPal could not delete the saved card.");
        }

        return new PaymentException(fallback, "PayPal could not delete the saved card.");
    }

    private static PaymentException FromTyped(Error typed, int fallback) =>
        new(GuessStatus(typed.Name, fallback), FormatError(typed.Name, typed.Message, typed.Details));

    private static PaymentException FromRaw(RawError raw, string generic)
    {
        var body = SafeRead(raw);
        return new PaymentException(
            (int)raw.StatusCode,
            string.IsNullOrWhiteSpace(body) ? generic : $"{generic} {TrimForCaller(body)}");
    }

    private static string FormatError(string name, string message, IReadOnlyList<ErrorDetails>? details)
    {
        var extra = details == null || details.Count == 0
            ? string.Empty
            : " " + string.Join("; ", details.Select(d => $"{d.Issue}: {d.Description}".Trim(' ', ':')));
        return $"{name}: {message}{extra}";
    }

    private static string FormatError1(Error1 error)
    {
        var extra = error.Details == null || error.Details.Count == 0
            ? string.Empty
            : " " + string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}".Trim(' ', ':')));
        return $"{error.Name}: {error.Message}{extra}";
    }

    private static int GuessStatus(string? name, int fallback)
    {
        return name?.ToUpperInvariant() switch
        {
            "AUTHENTICATION_FAILURE" => 401,
            "NOT_AUTHORIZED" or "PERMISSION_DENIED" or "NOT_AUTHORIZED_FOR_PAYMENT_SOURCE" => 403,
            "RESOURCE_NOT_FOUND" => 404,
            "UNPROCESSABLE_ENTITY" => 422,
            "CONFLICT" => 409,
            _ => fallback
        };
    }

    private static string SafeRead(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string TrimForCaller(string body)
    {
        var trimmed = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400];
    }
}
