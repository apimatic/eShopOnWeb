using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan OperationBudget = TimeSpan.FromSeconds(30);

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public string Currency => _options.Currency;

    public Task<AuthorizationHold> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        CardPaymentSource card,
        string payPalRequestId,
        string? existingPayPalOrderId,
        CancellationToken cancellationToken)
    {
        var paymentSource = new OrderAuthorizeRequestPaymentSource
        {
            Card = MapCard(card)
        };
        return AuthorizeAsync(orderId, amount, paymentSource, payPalRequestId, existingPayPalOrderId, cancellationToken);
    }

    public Task<AuthorizationHold> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string vaultId,
        string payPalRequestId,
        string? existingPayPalOrderId,
        CancellationToken cancellationToken)
    {
        var paymentSource = new OrderAuthorizeRequestPaymentSource
        {
            Card = new CardRequest
            {
                VaultId = vaultId,
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Customer,
                    PaymentType = StoredPaymentSourcePaymentType.OneTime,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
            }
        };
        return AuthorizeAsync(orderId, amount, paymentSource, payPalRequestId, existingPayPalOrderId, cancellationToken);
    }

    public Task<AuthorizationHold> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            try
            {
                var auth = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: ct);
                return MapAuthorization(auth, paypalOrderId: string.Empty);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                throw TranslateGetAuthorizedPayment(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw TranslateBoundary(ex, "PayPal could not retrieve the authorization.");
            }
        }, cancellationToken);

    public Task<AuthorizationHold> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string payPalRequestId,
        CancellationToken cancellationToken) =>
        BoundedWrite(async ct =>
        {
            try
            {
                var auth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money
                        {
                            CurrencyCode = Currency,
                            Value = MoneyFormatter.ToPayPalValue(amount, Currency)
                        }
                    },
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);
                return MapAuthorization(auth, paypalOrderId: string.Empty);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw TranslateReauthorize(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw TranslateBoundary(ex, "PayPal could not renew the authorization.");
            }
        }, cancellationToken);

    public Task<CaptureDetails> CaptureAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken) =>
        BoundedWrite(async ct =>
        {
            try
            {
                var capture = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest { FinalCapture = true },
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);
                return MapCapture(capture);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw TranslateCapture(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw TranslateBoundary(ex, "PayPal could not capture the authorization.");
            }
        }, cancellationToken);

    public Task VoidAsync(string authorizationId, string payPalRequestId, CancellationToken cancellationToken) =>
        BoundedWrite(async ct =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: payPalRequestId,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);
                return 0;
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw TranslateVoid(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw TranslateBoundary(ex, "PayPal could not release the authorization.");
            }
        }, cancellationToken);

    public Task<RefundDetails> RefundAsync(
        string captureId,
        decimal? amount,
        string payPalRequestId,
        CancellationToken cancellationToken) =>
        BoundedWrite(async ct =>
        {
            try
            {
                RefundRequest? body = null;
                if (amount is decimal refundAmount)
                {
                    body = new RefundRequest
                    {
                        Amount = new Money
                        {
                            CurrencyCode = Currency,
                            Value = MoneyFormatter.ToPayPalValue(refundAmount, Currency)
                        }
                    };
                }

                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);
                return MapRefund(refund, amount);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw TranslateRefund(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw TranslateBoundary(ex, "PayPal could not refund the capture.");
            }
        }, cancellationToken);

    public Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            var results = new List<ProviderTransaction>();
            foreach (var (start, end) in SplitWindows(from, to))
            {
                var page = 1;
                int totalPages;
                do
                {
                    SearchResponse response;
                    try
                    {
                        response = await _client.TransactionSearch.SearchTransactions(
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
                            balanceAffectingRecordsOnly: "Y",
                            pageSize: 100,
                            page: page,
                            requestOptions: null,
                            ct: ct);
                    }
                    catch (SdkException<RawError> ex)
                    {
                        if ((int)ex.Error.StatusCode == 404)
                        {
                            break;
                        }

                        throw MapRaw(ex.Error, "PayPal transaction search failed.");
                    }
                    catch (Exception ex) when (IsBoundary(ex))
                    {
                        throw TranslateBoundary(ex, "PayPal transaction search failed.");
                    }

                    if (response.TransactionDetails is not null)
                    {
                        foreach (var detail in response.TransactionDetails)
                        {
                            var info = detail.TransactionInfo;
                            if (info is null)
                            {
                                continue;
                            }

                            results.Add(new ProviderTransaction(
                                info.TransactionId ?? string.Empty,
                                info.InvoiceId,
                                info.CustomField,
                                info.PaypalReferenceId,
                                ParsePayPalTimestamp(info.TransactionInitiationDate),
                                info.TransactionAmount?.Value,
                                info.FeeAmount?.Value,
                                info.TransactionAmount?.CurrencyCode,
                                info.TransactionStatus,
                                info.PaymentMethodType));
                        }
                    }

                    totalPages = response.TotalPages ?? 1;
                    page++;
                } while (page <= totalPages);
            }

            return (IReadOnlyList<ProviderTransaction>)results;
        }, cancellationToken);

    public Task<VaultedCard> SaveCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        CardPaymentSource card,
        string payPalRequestId,
        CancellationToken cancellationToken) =>
        BoundedWrite(async ct =>
        {
            try
            {
                var customer = new Customer { MerchantCustomerId = merchantCustomerId };
                if (!string.IsNullOrEmpty(payPalCustomerId))
                {
                    customer = new Customer
                    {
                        Id = payPalCustomerId,
                        MerchantCustomerId = merchantCustomerId
                    };
                }

                var response = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: payPalRequestId,
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
                    ct: ct);

                StopIfPayerActionRequired(response.Links, "saving the card");

                var cardEntity = response.PaymentSource?.Card;
                return new VaultedCard(
                    response.Id ?? throw new CheckoutException("PayPal did not return a payment token id.", 502),
                    cardEntity?.LastDigits,
                    cardEntity?.Brand?.Value,
                    cardEntity?.Expiry,
                    response.Customer?.Id,
                    response.Customer?.MerchantCustomerId);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw TranslateCreatePaymentToken(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw TranslateBoundary(ex, "PayPal could not save the card.");
            }
        }, cancellationToken);

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken) =>
        BoundedWrite(async ct =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, requestOptions: null, ct: ct);
                return 0;
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw TranslateDeletePaymentToken(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw TranslateBoundary(ex, "PayPal could not delete the saved card.");
            }
        }, cancellationToken);

    private async Task<AuthorizationHold> AuthorizeAsync(
        int orderId,
        decimal amount,
        OrderAuthorizeRequestPaymentSource paymentSource,
        string payPalRequestId,
        string? existingPayPalOrderId,
        CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            var paypalOrderId = existingPayPalOrderId;
            if (string.IsNullOrEmpty(paypalOrderId))
            {
                using (PayPalWriteOnceHandler.BeginWrite())
                {
                    paypalOrderId = await CreatePayPalOrder(orderId, amount, $"{payPalRequestId}-create", ct);
                }
            }

            try
            {
                OrderAuthorizeResponse authorized;
                using (PayPalWriteOnceHandler.BeginWrite())
                {
                    authorized = await _client.Orders.AuthorizeOrder(
                        id: paypalOrderId,
                        payPalMockResponse: null,
                        payPalRequestId: payPalRequestId,
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: new OrderAuthorizeRequest { PaymentSource = paymentSource },
                        prefer: "return=representation",
                        requestOptions: null,
                        ct: ct);
                }

                StopIfPayerActionRequired(authorized.Status, authorized.Links, "authorizing the payment");
                return MapHold(authorized, paypalOrderId);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw TranslateAuthorizeOrder(ex);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw TranslateBoundary(ex, "PayPal could not authorize the payment.");
            }
        }, cancellationToken);
    }

    private async Task<string> CreatePayPalOrder(int orderId, decimal amount, string requestId, CancellationToken ct)
    {
        try
        {
            var created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits = new List<PurchaseUnitRequest>
                    {
                        new()
                        {
                            Amount = new AmountWithBreakdown
                            {
                                CurrencyCode = Currency,
                                Value = MoneyFormatter.ToPayPalValue(amount, Currency)
                            },
                            CustomId = orderId.ToString(),
                            InvoiceId = $"eShop-{orderId}-{Guid.NewGuid():N}"
                        }
                    }
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            StopIfPayerActionRequired(created.Status, created.Links, "creating the payment");
            if (string.IsNullOrEmpty(created.Id))
            {
                throw new CheckoutException("PayPal did not return an order id.", 502);
            }

            return created.Id;
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrder(ex);
        }
        catch (CheckoutException)
        {
            throw;
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw TranslateBoundary(ex, "PayPal could not create the payment order.");
        }
    }

    private static AuthorizationHold MapHold(OrderAuthorizeResponse response, string paypalOrderId)
    {
        var authorization = response.PurchaseUnits?
            .SelectMany(unit => unit.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new CheckoutException("PayPal did not return an authorization id.", 502);
        }

        return new AuthorizationHold(
            response.Id ?? paypalOrderId,
            authorization.Id,
            authorization.Status?.Value ?? string.Empty,
            ParsePayPalTimestamp(authorization.ExpirationTime),
            ParsePayPalTimestamp(authorization.CreateTime),
            authorization.Amount?.CurrencyCode ?? string.Empty);
    }

    private AuthorizationHold MapAuthorization(PaymentAuthorization auth, string paypalOrderId)
    {
        if (string.IsNullOrEmpty(auth.Id))
        {
            throw new CheckoutException("PayPal did not return an authorization id.", 502);
        }

        return new AuthorizationHold(
            paypalOrderId,
            auth.Id,
            auth.Status?.Value ?? string.Empty,
            ParsePayPalTimestamp(auth.ExpirationTime),
            ParsePayPalTimestamp(auth.CreateTime),
            auth.Amount?.CurrencyCode ?? Currency);
    }

    private CaptureDetails MapCapture(CapturedPayment capture)
    {
        if (string.IsNullOrEmpty(capture.Id))
        {
            throw new CheckoutException("PayPal did not return a capture id.", 502);
        }

        var breakdown = capture.SellerReceivableBreakdown;
        return new CaptureDetails(
            capture.Id,
            capture.Status?.Value ?? string.Empty,
            MoneyFormatter.Parse(capture.Amount?.Value),
            breakdown?.PaypalFee is null ? null : MoneyFormatter.Parse(breakdown.PaypalFee.Value),
            breakdown?.NetAmount is null ? null : MoneyFormatter.Parse(breakdown.NetAmount.Value),
            capture.Amount?.CurrencyCode ?? Currency);
    }

    private RefundDetails MapRefund(Refund refund, decimal? requestedAmount)
    {
        if (string.IsNullOrEmpty(refund.Id))
        {
            throw new CheckoutException("PayPal did not return a refund id.", 502);
        }

        var amount = refund.Amount?.Value is string value
            ? MoneyFormatter.Parse(value)
            : requestedAmount ?? 0m;

        return new RefundDetails(
            refund.Id,
            refund.Status?.Value ?? string.Empty,
            amount,
            refund.Amount?.CurrencyCode ?? Currency);
    }

    private static CardRequest MapCard(CardPaymentSource card) =>
        new()
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = MapAddress(card.BillingAddress)
        };

    private static PayPalAddress? MapAddress(CardBillingAddress? address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.CountryCode))
        {
            return null;
        }

        return new PayPalAddress
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static void StopIfPayerActionRequired(OrderStatus? status, IReadOnlyList<LinkDescription>? links, string action)
    {
        if (status == OrderStatus.PayerActionRequired || HasPayerActionLink(links))
        {
            throw new CheckoutException(
                $"PayPal required a shopper approval step (for example 3-D Secure) while {action}. This integration does not collect in-browser approval, so the payment was not completed.",
                409);
        }
    }

    private static void StopIfPayerActionRequired(IReadOnlyList<LinkDescription>? links, string action)
    {
        if (HasPayerActionLink(links))
        {
            throw new CheckoutException(
                $"PayPal required a shopper approval step (for example 3-D Secure) while {action}. This integration does not collect in-browser approval, so the card was not saved.",
                409);
        }
    }

    private static DateTimeOffset? ParsePayPalTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool HasPayerActionLink(IReadOnlyList<LinkDescription>? links) =>
        links?.Any(link =>
            string.Equals(link.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) == true;

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitWindows(DateTimeOffset from, DateTimeOffset to)
    {
        var cursor = from;
        while (cursor < to)
        {
            var windowEnd = cursor.AddDays(30);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            yield return (cursor, windowEnd);
            cursor = windowEnd;
            if (cursor < to)
            {
                cursor = cursor.AddSeconds(1);
            }
        }
    }

    private static string ToRfc3339(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(OperationBudget);
        PayPalCallContext.LastStatusCode = null;
        return await call(cts.Token);
    }

    private Task<T> BoundedWrite<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct) =>
        Bounded(async inner =>
        {
            using (PayPalWriteOnceHandler.BeginWrite())
            {
                return await call(inner);
            }
        }, ct);

    private static bool IsBoundary(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException or PayPalDuplicateSendException
            or AuthSchemeException or CheckoutException;

    private static CheckoutException TranslateBoundary(Exception ex, string fallback)
    {
        if (ex is CheckoutException checkout)
        {
            return checkout;
        }

        if (ex is PayPalDuplicateSendException)
        {
            return new CheckoutException(
                "The PayPal request may already have been sent. Refresh the order and retry if its payment state is unchanged.",
                409, ex);
        }

        if (ex is JsonException)
        {
            var status = PayPalCallContext.LastStatusCode;
            if (status is >= 400 and < 500)
            {
                return new CheckoutException("PayPal rejected the request.", status.Value, ex);
            }

            return new CheckoutException("The provider returned a response that could not be processed.", 502, ex);
        }

        if (ex is AuthSchemeException)
        {
            return new CheckoutException("PayPal authentication is not configured correctly.", 500, ex);
        }

        return new CheckoutException(fallback, 503, ex);
    }

    private static CheckoutException TranslateCreateOrder(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "PayPal could not create the payment order.");
        }

        return new CheckoutException("PayPal could not create the payment order.", 502);
    }

    private static CheckoutException TranslateAuthorizeOrder(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "PayPal could not authorize the payment.");
        }

        return new CheckoutException("PayPal could not authorize the payment.", 502);
    }

    private static CheckoutException TranslateGetAuthorizedPayment(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return MapRaw(noContent, "PayPal could not retrieve the authorization.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "PayPal could not retrieve the authorization.");
        }

        return new CheckoutException("PayPal could not retrieve the authorization.", 502);
    }

    private static CheckoutException TranslateReauthorize(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return MapRaw(noContent, "PayPal could not renew the authorization.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "PayPal could not renew the authorization.");
        }

        return new CheckoutException("PayPal could not renew the authorization.", 502);
    }

    private static CheckoutException TranslateCapture(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return MapRaw(noContent, "PayPal could not capture the authorization.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "PayPal could not capture the authorization.");
        }

        return new CheckoutException("PayPal could not capture the authorization.", 502);
    }

    private static CheckoutException TranslateVoid(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return MapRaw(noContent, "PayPal could not release the authorization.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "PayPal could not release the authorization.");
        }

        return new CheckoutException("PayPal could not release the authorization.", 502);
    }

    private static CheckoutException TranslateRefund(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return FromError(error);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return MapRaw(noContent, "PayPal could not refund the capture.");
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "PayPal could not refund the capture.");
        }

        return new CheckoutException("PayPal could not refund the capture.", 502);
    }

    private static CheckoutException TranslateCreatePaymentToken(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            return FromError1(error);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "PayPal could not save the card.");
        }

        return new CheckoutException("PayPal could not save the card.", 502);
    }

    private static CheckoutException TranslateDeletePaymentToken(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            return FromError1(error);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw, "PayPal could not delete the saved card.");
        }

        return new CheckoutException("PayPal could not delete the saved card.", 502);
    }

    private static CheckoutException FromError(Error error)
    {
        var issue = error.Details?.FirstOrDefault()?.Issue;
        var description = error.Details?.FirstOrDefault()?.Description;
        return new CheckoutException(
            FormatPayPalMessage(error.Name, error.Message, issue, description, error.DebugId),
            MapIssueStatus(error.Name, issue));
    }

    private static CheckoutException FromError1(Error1 error)
    {
        var issue = error.Details?.FirstOrDefault()?.Issue;
        var description = error.Details?.FirstOrDefault()?.Description;
        return new CheckoutException(
            FormatPayPalMessage(error.Name, error.Message, issue, description, error.DebugId),
            MapIssueStatus(error.Name, issue));
    }

    private static CheckoutException MapRaw(RawError raw, string fallback)
    {
        var status = (int)raw.StatusCode;
        if (status is 0)
        {
            status = 502;
        }

        string? body = null;
        try
        {
            body = raw.ReadAsString();
        }
        catch (Exception)
        {
            // Body is optional; status is enough to map the failure.
        }

        var safe = string.IsNullOrWhiteSpace(body)
            ? fallback
            : $"{fallback} PayPal returned HTTP {status}.";
        return new CheckoutException(safe, status is >= 400 and < 600 ? status : 502);
    }

    private static string FormatPayPalMessage(string? name, string? message, string? issue, string? description, string? debugId)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(message))
        {
            parts.Add(message);
        }

        if (!string.IsNullOrWhiteSpace(issue))
        {
            parts.Add(issue);
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            parts.Add(description);
        }

        if (!string.IsNullOrWhiteSpace(name) && parts.Count == 0)
        {
            parts.Add(name);
        }

        if (!string.IsNullOrWhiteSpace(debugId))
        {
            parts.Add($"debug_id={debugId}");
        }

        return parts.Count == 0 ? "PayPal rejected the request." : string.Join(" ", parts);
    }

    private static int MapIssueStatus(string? name, string? issue)
    {
        var token = $"{name} {issue}".ToUpperInvariant();
        if (token.Contains("NOT_FOUND", StringComparison.Ordinal) || token.Contains("RESOURCE_NOT_FOUND", StringComparison.Ordinal))
        {
            return 404;
        }

        if (token.Contains("AUTHORIZATION_EXPIRED", StringComparison.Ordinal) ||
            token.Contains("AUTH_EXPIRED", StringComparison.Ordinal))
        {
            return 409;
        }

        if (token.Contains("INSTRUMENT_DECLINED", StringComparison.Ordinal) ||
            token.Contains("DECLINED", StringComparison.Ordinal))
        {
            return 400;
        }

        if (token.Contains("PERMISSION", StringComparison.Ordinal) || token.Contains("NOT_AUTHORIZED", StringComparison.Ordinal))
        {
            return 403;
        }

        if (token.Contains("AUTHENTICATION", StringComparison.Ordinal) || token.Contains("UNAUTHORIZED", StringComparison.Ordinal))
        {
            return 401;
        }

        if (token.Contains("CONFLICT", StringComparison.Ordinal))
        {
            return 409;
        }

        return 400;
    }
}
