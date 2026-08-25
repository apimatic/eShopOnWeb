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
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IPayPalGateway"/> against the PayPal .NET SDK. Every call is guarded for
/// transport failures (<see cref="GuardTransportAsync{T}"/>) in addition to its own PayPal API
/// error translation, per the dotnet-error-handling companion skill.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<AuthorizePaymentOutcome> AuthorizeAsync(decimal amount, string currency, PaymentSourceRequest paymentSource, string idempotencyKey, CancellationToken ct) =>
        GuardTransportAsync<AuthorizePaymentOutcome>(async () =>
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
                            Value = FormatAmount(amount)
                        }
                    }
                },
                PaymentSource = new PaymentSource
                {
                    Card = BuildOrderCardRequest(paymentSource)
                }
            };

            Order order;
            try
            {
                order = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    ct: ct);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                if (ex.Error.TryGetError(out var typed))
                {
                    throw new PaymentGatewayException($"PayPal rejected the authorization request: {Describe(typed)}", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new PaymentGatewayException($"PayPal rejected the authorization request (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", ex);
                }
                throw new PaymentGatewayException("PayPal rejected the authorization request.", ex);
            }

            if (order.Status == PayPalServerSdk.Models.Enums.OrderStatus.PayerActionRequired)
            {
                var payerActionUrl = order.Links?.FirstOrDefault(l => l.Rel == "payer-action")?.Href;
                if (string.IsNullOrEmpty(payerActionUrl))
                {
                    throw new PaymentGatewayException("PayPal requires shopper action but did not return a payer-action link.");
                }
                return new AuthorizePaymentRequiresAction(order.Id ?? string.Empty, payerActionUrl);
            }

            var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (authorization is null || string.IsNullOrEmpty(authorization.Id) || string.IsNullOrEmpty(order.Id))
            {
                throw new PaymentGatewayException("PayPal did not return an authorization for this order.");
            }

            return new AuthorizePaymentAuthorized(
                order.Id,
                authorization.Id,
                authorization.Status?.Value ?? "UNKNOWN",
                authorization.Amount?.Value is { } authorizedValue ? ParseAmount(authorizedValue) : amount,
                authorization.Amount?.CurrencyCode ?? currency,
                ParseDate(authorization.ExpirationTime));
        }, ct);

    public Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct) =>
        GuardTransportAsync(async () =>
        {
            PaymentAuthorization auth;
            try
            {
                auth = await _client.Payments.GetAuthorizedPayment(authorizationId, payPalMockResponse: null, payPalAuthAssertion: null, ct: ct);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var typed))
                {
                    throw new PaymentGatewayException($"PayPal rejected the authorization lookup: {Describe(typed)}", ex);
                }
                if (ex.Error.TryGetNoContent(out var noContent))
                {
                    throw new PaymentGatewayException($"PayPal returned an internal error looking up the authorization (HTTP {(int)noContent.StatusCode}).", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new PaymentGatewayException($"PayPal rejected the authorization lookup (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", ex);
                }
                throw new PaymentGatewayException("PayPal rejected the authorization lookup.", ex);
            }

            return new AuthorizationSnapshot(
                auth.Id ?? authorizationId,
                auth.Status?.Value ?? "UNKNOWN",
                auth.Amount?.Value is { } v ? ParseAmount(v) : 0m,
                ParseDate(auth.ExpirationTime));
        }, ct);

    public Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId, CancellationToken ct) =>
        GuardTransportAsync(async () =>
        {
            PaymentAuthorization renewed;
            try
            {
                renewed = await _client.Payments.ReauthorizePayment(
                    authorizationId,
                    payPalRequestId: Guid.NewGuid().ToString(),
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation", // we read Id/Status/ExpirationTime back — "return=minimal" (the default) can omit the body entirely (see VoidAsync's JsonException note)
                    ct: ct);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                if (ex.Error.TryGetError(out var typed))
                {
                    throw new ReauthorizationNotPossibleException(authorizationId, Describe(typed));
                }
                if (ex.Error.TryGetNoContent(out var noContent))
                {
                    throw new PaymentGatewayException($"PayPal returned an internal error while reauthorizing (HTTP {(int)noContent.StatusCode}).");
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new ReauthorizationNotPossibleException(authorizationId, $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}");
                }
                throw new ReauthorizationNotPossibleException(authorizationId, "PayPal rejected the reauthorization request.");
            }

            return new AuthorizationSnapshot(
                renewed.Id ?? authorizationId,
                renewed.Status?.Value ?? "UNKNOWN",
                renewed.Amount?.Value is { } v ? ParseAmount(v) : 0m,
                ParseDate(renewed.ExpirationTime));
        }, ct);

    public Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct) =>
        GuardTransportAsync(async () =>
        {
            var body = new CaptureRequest
            {
                Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) },
                FinalCapture = true
            };

            CapturedPayment captured;
            try
            {
                captured = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation", // "return=minimal" (the default) omits SellerReceivableBreakdown (fee/net)
                    ct: ct);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var typed))
                {
                    throw new PaymentGatewayException($"PayPal rejected the capture: {Describe(typed)}", ex);
                }
                if (ex.Error.TryGetNoContent(out var noContent))
                {
                    throw new PaymentGatewayException($"PayPal returned an internal error while capturing (HTTP {(int)noContent.StatusCode}).", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new PaymentGatewayException($"PayPal rejected the capture (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", ex);
                }
                throw new PaymentGatewayException("PayPal rejected the capture.", ex);
            }

            var breakdown = captured.SellerReceivableBreakdown;
            if (string.IsNullOrEmpty(captured.Id))
            {
                throw new PaymentGatewayException("PayPal did not return a capture id.");
            }

            return new CaptureResult(
                captured.Id,
                captured.Status?.Value ?? "UNKNOWN",
                breakdown?.GrossAmount?.Value is { } gv ? ParseAmount(gv) : amount,
                breakdown?.PaypalFee?.Value is { } fv ? ParseAmount(fv) : null,
                breakdown?.NetAmount?.Value is { } nv ? ParseAmount(nv) : null);
        }, ct);

    public Task VoidAsync(string authorizationId, CancellationToken ct) =>
        GuardTransportAsync(async () =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: $"void:{authorizationId}",
                    ct: ct);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var typed))
                {
                    throw new PaymentGatewayException($"PayPal rejected the cancellation: {Describe(typed)}", ex);
                }
                if (ex.Error.TryGetNoContent(out var noContent))
                {
                    throw new PaymentGatewayException($"PayPal returned an internal error while cancelling (HTTP {(int)noContent.StatusCode}).", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new PaymentGatewayException($"PayPal rejected the cancellation (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", ex);
                }
                throw new PaymentGatewayException("PayPal rejected the cancellation.", ex);
            }
            catch (JsonException)
            {
                // Known SDK defect: VoidPayment's default prefer="return=minimal" gets a legitimate
                // 204 empty body from PayPal on success, but the SDK's JsonResponse<T> mapper
                // deserializes unconditionally and throws JsonException even though the void
                // succeeded. Confirm independently rather than assuming success, since a genuinely
                // malformed 2xx body throws the identical exception type.
                var authorization = await _client.Payments.GetAuthorizedPayment(
                    authorizationId, payPalMockResponse: null, payPalAuthAssertion: null, ct: ct);

                if (authorization.Status?.Value != AuthorizationStatus.Voided.Value)
                {
                    throw; // not actually voided — a real failure, don't mask it
                }
            }
        }, ct);

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct) =>
        GuardTransportAsync(async () =>
        {
            var body = amount is { } requested
                ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(requested) } }
                : null;

            PayPalServerSdk.Models.Refund refund;
            try
            {
                refund = await _client.Payments.RefundCapturedPayment(
                    captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation", // "return=minimal" (the default) omits SellerPayableBreakdown
                    ct: ct);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                if (ex.Error.TryGetError(out var typed))
                {
                    throw new PaymentGatewayException($"PayPal rejected the refund: {Describe(typed)}", ex);
                }
                if (ex.Error.TryGetNoContent(out var noContent))
                {
                    throw new PaymentGatewayException($"PayPal returned an internal error while refunding (HTTP {(int)noContent.StatusCode}).", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new PaymentGatewayException($"PayPal rejected the refund (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", ex);
                }
                throw new PaymentGatewayException("PayPal rejected the refund.", ex);
            }

            if (string.IsNullOrEmpty(refund.Id))
            {
                throw new PaymentGatewayException("PayPal did not return a refund id.");
            }

            return new RefundResult(
                refund.Id,
                refund.Status?.Value ?? "UNKNOWN",
                refund.Amount?.Value is { } v ? ParseAmount(v) : (amount ?? 0m));
        }, ct);

    public Task<SavedCard> SaveCardAsync(string? payPalCustomerId, string merchantBuyerId, CardDetails card, CancellationToken ct) =>
        GuardTransportAsync(async () =>
        {
            var body = new PaymentTokenRequest
            {
                Customer = new Customer
                {
                    Id = payPalCustomerId,
                    MerchantCustomerId = merchantBuyerId
                },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Name = card.CardholderName,
                        Number = card.Number,
                        Expiry = card.ExpiryYearMonth,
                        SecurityCode = card.SecurityCode,
                        BillingAddress = BuildAddress(card.BillingAddress)
                    }
                }
            };

            PaymentTokenResponse token;
            try
            {
                token = await _client.Vault.CreatePaymentToken(payPalRequestId: Guid.NewGuid().ToString(), body: body, ct: ct);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                if (ex.Error.TryGetError1(out var typed))
                {
                    throw new PaymentGatewayException($"PayPal rejected saving this card: {Describe(typed)}", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new PaymentGatewayException($"PayPal rejected saving this card (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", ex);
                }
                throw new PaymentGatewayException("PayPal rejected saving this card.", ex);
            }

            var cardEntity = token.PaymentSource?.Card;
            if (string.IsNullOrEmpty(token.Id))
            {
                throw new PaymentGatewayException("PayPal did not return a vault id for the saved card.");
            }

            var customerId = token.Customer?.Id ?? payPalCustomerId;
            if (string.IsNullOrEmpty(customerId))
            {
                throw new PaymentGatewayException("PayPal did not return a customer id for the saved card.");
            }

            return new SavedCard(
                token.Id,
                cardEntity?.Brand?.Value,
                cardEntity?.LastDigits ?? "????",
                cardEntity?.Expiry ?? string.Empty,
                cardEntity?.Name,
                customerId);
        }, ct);

    public Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string payPalCustomerId, CancellationToken ct) =>
        GuardTransportAsync<IReadOnlyList<SavedCard>>(async () =>
        {
            var results = new List<SavedCard>();
            var page = 1;
            var totalPages = 1;
            do
            {
                CustomerVaultPaymentTokensResponse response;
                try
                {
                    response = await _client.Vault.ListCustomerPaymentTokens(payPalCustomerId, pageSize: 20, page: page, totalRequired: true, ct: ct);
                }
                catch (SdkException<ListCustomerPaymentTokensError> ex)
                {
                    if (ex.Error.TryGetError1(out var typed))
                    {
                        throw new PaymentGatewayException($"PayPal rejected listing saved cards: {Describe(typed)}", ex);
                    }
                    if (ex.Error.TryGetRawError(out var raw))
                    {
                        throw new PaymentGatewayException($"PayPal rejected listing saved cards (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", ex);
                    }
                    throw new PaymentGatewayException("PayPal rejected listing saved cards.", ex);
                }

                totalPages = response.TotalPages ?? 1;
                if (response.PaymentTokens is not null)
                {
                    foreach (var token in response.PaymentTokens)
                    {
                        var card = token.PaymentSource?.Card;
                        if (card is null || string.IsNullOrEmpty(token.Id))
                        {
                            continue;
                        }

                        results.Add(new SavedCard(
                            token.Id,
                            card.Brand?.Value,
                            card.LastDigits ?? "????",
                            card.Expiry ?? string.Empty,
                            card.Name,
                            token.Customer?.Id ?? payPalCustomerId));
                    }
                }

                page++;
            } while (page <= totalPages);

            return results;
        }, ct);

    public Task DeleteSavedCardAsync(string vaultId, CancellationToken ct) =>
        GuardTransportAsync(async () =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(vaultId, ct: ct);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                if (ex.Error.TryGetError1(out var typed))
                {
                    throw new PaymentGatewayException($"PayPal rejected removing this card: {Describe(typed)}", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new PaymentGatewayException($"PayPal rejected removing this card (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", ex);
                }
                throw new PaymentGatewayException("PayPal rejected removing this card.", ex);
            }
        }, ct);

    public Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        GuardTransportAsync<IReadOnlyList<PayPalTransactionRecord>>(async () =>
        {
            var results = new List<PayPalTransactionRecord>();
            var startDate = from.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
            var endDate = to.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

            var page = 1;
            var totalPages = 1;
            do
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: startDate,
                        endDate: endDate,
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
                        ct: ct);
                }
                catch (SdkException<RawError> ex)
                {
                    throw new PaymentGatewayException($"PayPal rejected the transaction search (HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}", ex);
                }

                totalPages = response.TotalPages ?? 1;
                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null)
                        {
                            continue;
                        }

                        results.Add(new PayPalTransactionRecord(
                            info.TransactionId ?? string.Empty,
                            info.TransactionAmount?.Value is { } av ? ParseAmount(av) : null,
                            info.TransactionAmount?.CurrencyCode,
                            info.TransactionStatus,
                            ParseDate(info.TransactionInitiationDate),
                            ParseDate(info.TransactionUpdatedDate),
                            info.FeeAmount?.Value is { } fv ? ParseAmount(fv) : null));
                    }
                }

                page++;
            } while (page <= totalPages);

            return results;
        }, ct);

    private static CardRequest BuildOrderCardRequest(PaymentSourceRequest paymentSource)
    {
        if (!string.IsNullOrEmpty(paymentSource.VaultId))
        {
            return new CardRequest { VaultId = paymentSource.VaultId };
        }

        if (paymentSource.Card is { } card)
        {
            return new CardRequest
            {
                Name = card.CardholderName,
                Number = card.Number,
                Expiry = card.ExpiryYearMonth,
                SecurityCode = card.SecurityCode,
                BillingAddress = BuildAddress(card.BillingAddress)
            };
        }

        throw new ArgumentException("PaymentSource must specify either a vaulted card (VaultId) or raw card details (Card).", nameof(paymentSource));
    }

    private static PayPalServerSdk.Models.Address? BuildAddress(PaymentAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new PayPalServerSdk.Models.Address
        {
            AddressLine1 = address.AddressLine1,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrEmpty(value) ? null : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string Describe(object errorBody) => JsonSerializer.Serialize(errorBody);

    private static async Task<T> GuardTransportAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            return await action();
        }
        catch (PaymentGatewayException)
        {
            throw;
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            throw new PaymentGatewayException("The request to PayPal timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException("PayPal could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", ex);
        }
    }

    private static async Task GuardTransportAsync(Func<Task> action, CancellationToken ct)
    {
        await GuardTransportAsync(async () =>
        {
            await action();
            return true;
        }, ct);
    }
}
