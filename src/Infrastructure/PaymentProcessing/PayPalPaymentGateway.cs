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
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

/// <summary>
/// Implements <see cref="IPaymentGateway"/> against PayPal's Orders/Payments/Vault/Transaction
/// Search APIs via the PayPal .NET SDK. Every write is sent with a caller-supplied
/// PayPal-Request-Id so a retried call has no duplicate effect. Card verification is left at the
/// SDK's default (SCA-when-required) -- the live sandbox account rejects AVS_CVV outright, so a
/// PayerActionRequired response is a real possible outcome, surfaced as
/// <see cref="PaymentActionRequiredException"/> rather than silently retried or worked around.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly global::PayPalServerSdk.PayPalServerSdkClient _client;

    public PayPalPaymentGateway(global::PayPalServerSdk.PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<CardAuthorizationResult> AuthorizeWithCardAsync(CardDetails card, decimal amount, string currency, string requestId, CancellationToken ct)
    {
        var cardRequest = new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = new Address
            {
                AddressLine1 = card.AddressLine1,
                AddressLine2 = card.AddressLine2,
                AdminArea2 = card.City,
                AdminArea1 = card.State,
                PostalCode = card.PostalCode,
                CountryCode = card.CountryCode
            }
            // Attributes.Verification.Method intentionally left unset: PayPal's live sandbox
            // rejects AVS_CVV for this field (422 INVALID_PARAMETER_VALUE on
            // /payment_source/card/attributes/verification/method) even though the SDK's own
            // OrdersCardVerificationMethod enum declares it. No other member of that enum is
            // documented as unconditionally headless either (SCA_ALWAYS and 3D_SECURE both
            // explicitly trigger a redirect contingency by design; SCA_WHEN_REQUIRED, the SDK
            // default, only avoids it when local regulation doesn't mandate SCA for the
            // card/region/amount). Leaving this unset falls back to PayPal's own default
            // (SCA_WHEN_REQUIRED) and relies on the PayerActionRequired branch below to report
            // the (real, not fully avoidable) redirect case rather than forcing a value the live
            // API has already rejected outright.
        };

        return AuthorizeCoreAsync(cardRequest, amount, currency, requestId, ct);
    }

    public Task<CardAuthorizationResult> AuthorizeWithVaultedCardAsync(string vaultId, decimal amount, string currency, string requestId, CancellationToken ct)
    {
        var cardRequest = new CardRequest
        {
            VaultId = vaultId
            // See AuthorizeWithCardAsync above for why Attributes.Verification is left unset.
        };

        return AuthorizeCoreAsync(cardRequest, amount, currency, requestId, ct);
    }

    private async Task<CardAuthorizationResult> AuthorizeCoreAsync(CardRequest cardRequest, decimal amount, string currency, string requestId, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            // Single-step pattern: supply payment_source.card directly on CreateOrder (PayPal's own
            // doc-comment names this "single-step create order"). CreateOrder may already return the
            // authorization synchronously; only fall back to a separate AuthorizeOrder(body: null)
            // call when it doesn't, since neither the map nor source states which happens.
            var orderRequestBody = new OrderRequest
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
                PaymentSource = new PaymentSource { Card = cardRequest }
            };

            Order createResponse;
            try
            {
                createResponse = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: orderRequestBody,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                throw MapCreateOrderError(ex);
            }

            if (createResponse.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return an order id when creating the order.");
            }

            if (createResponse.Status == OrderStatus.PayerActionRequired)
            {
                throw new PaymentActionRequiredException(
                    "PayPal requires the shopper to complete a browser-based approval step (3-D Secure) for this card/amount; this integration is headless and cannot complete that step.");
            }

            var authorization = createResponse.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

            if (authorization?.Id is null)
            {
                OrderAuthorizeResponse authorizeResponse;
                try
                {
                    authorizeResponse = await _client.Orders.AuthorizeOrder(
                        id: createResponse.Id,
                        payPalMockResponse: null,
                        payPalRequestId: requestId,
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: null,
                        prefer: "return=representation",
                        requestOptions: null,
                        ct: ct);
                }
                catch (SdkException<AuthorizeOrderError> ex)
                {
                    throw MapAuthorizeOrderError(ex);
                }

                if (authorizeResponse.Status == OrderStatus.PayerActionRequired)
                {
                    throw new PaymentActionRequiredException(
                        "PayPal requires the shopper to complete a browser-based approval step (3-D Secure) for this card/amount; this integration is headless and cannot complete that step.");
                }

                authorization = authorizeResponse.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            }

            if (authorization?.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return an authorization for this order.");
            }

            return new CardAuthorizationResult(
                createResponse.Id,
                authorization.Id,
                authorization.Status?.Value ?? "UNKNOWN",
                ParseDate(authorization.ExpirationTime));
        }, nameof(AuthorizeCoreAsync));
    }

    public async Task<SaveCardResult> SaveCardAsync(CardDetails card, string requestId, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var body = new PaymentTokenRequest
            {
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Name = card.Name,
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        BillingAddress = new Address
                        {
                            AddressLine1 = card.AddressLine1,
                            AddressLine2 = card.AddressLine2,
                            AdminArea2 = card.City,
                            AdminArea1 = card.State,
                            PostalCode = card.PostalCode,
                            CountryCode = card.CountryCode
                        }
                    }
                }
            };

            PaymentTokenResponse response;
            try
            {
                response = await _client.Vault.CreatePaymentToken(requestId, body, requestOptions: null, ct: ct);
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                throw MapCreatePaymentTokenError(ex);
            }

            var cardEntity = response.PaymentSource?.Card;
            if (response.Id is null || cardEntity is null)
            {
                throw new PaymentGatewayException("PayPal did not return a saved card token.");
            }

            return new SaveCardResult(
                response.Id,
                cardEntity.Brand?.Value ?? "Unknown",
                cardEntity.LastDigits ?? "????",
                cardEntity.Expiry ?? string.Empty);
        }, nameof(SaveCardAsync));
    }

    public async Task DeleteSavedCardAsync(string vaultId, CancellationToken ct)
    {
        await ExecuteAsync(async () =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(vaultId, requestOptions: null, ct: ct);
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                throw MapDeletePaymentTokenError(ex);
            }

            return true;
        }, nameof(DeleteSavedCardAsync));
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            CapturedPayment response;
            try
            {
                response = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                throw MapCaptureError(ex);
            }

            if (response.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a capture id.");
            }

            var capturedAmount = ParseAmount(response.Amount) ?? 0m;
            var fee = ParseAmount(response.SellerReceivableBreakdown?.PaypalFee);
            var net = ParseAmount(response.SellerReceivableBreakdown?.NetAmount);

            return new CaptureResult(response.Id, response.Status?.Value ?? "UNKNOWN", capturedAmount, fee, net);
        }, nameof(CaptureAsync));
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var body = new ReauthorizeRequest
            {
                Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
            };

            PaymentAuthorization response;
            try
            {
                response = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                throw MapReauthorizeError(ex);
            }

            if (response.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a reauthorization id.");
            }

            return new ReauthorizeResult(response.Id, response.Status?.Value ?? "UNKNOWN", ParseDate(response.ExpirationTime));
        }, nameof(ReauthorizeAsync));
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        await ExecuteAsync(async () =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: requestId,
                    prefer: "return=minimal",
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw MapVoidError(ex);
            }

            return true;
        }, nameof(VoidAsync));
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var body = amount.HasValue
                ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) } }
                : null;

            PayPalServerSdk.Models.Refund response;
            try
            {
                response = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                throw MapRefundError(ex);
            }

            if (response.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a refund id.");
            }

            var refundedAmount = ParseAmount(response.Amount) ?? amount ?? 0m;

            return new RefundResult(response.Id, response.Status?.Value ?? "UNKNOWN", refundedAmount);
        }, nameof(RefundAsync));
    }

    public async Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        return await ExecuteAsync(async () =>
        {
            var results = new List<TransactionRecord>();
            var startDate = from.ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture);
            var endDate = to.ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture);

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
                        requestOptions: null,
                        ct: ct);
                }
                catch (SdkException<RawError> ex)
                {
                    throw new PaymentGatewayException($"PayPal returned HTTP {(int)ex.Error.StatusCode} while searching transactions: {ex.Error.ReadAsString()}");
                }

                foreach (var txn in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
                {
                    var info = txn.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    results.Add(new TransactionRecord(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.PaypalReferenceIdType?.Value,
                        ParseAmount(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionStatus,
                        ParseDate(info.TransactionInitiationDate)));
                }

                totalPages = response.TotalPages ?? 1;
                page++;
            } while (page <= totalPages);

            return (IReadOnlyList<TransactionRecord>)results;
        }, nameof(SearchTransactionsAsync));
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(Money? money)
    {
        if (money?.Value is null)
        {
            return null;
        }

        return decimal.TryParse(money.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return !string.IsNullOrEmpty(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static string DescribeError(Error error)
    {
        var details = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(DescribeDetail))
            : null;
        return details is null ? error.Message : $"{error.Message} ({details})";
    }

    private static string DescribeDetail(ErrorDetails detail) =>
        $"{detail.Field}: {detail.Issue}" + (string.IsNullOrEmpty(detail.Description) ? string.Empty : $" - {detail.Description}");

    private static string DescribeError1(Error1 error)
    {
        var details = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(DescribeDetail1))
            : null;
        return details is null ? error.Message : $"{error.Message} ({details})";
    }

    private static string DescribeDetail1(ErrorDetails1 detail) =>
        $"{detail.Field}: {detail.Issue}" + (string.IsNullOrEmpty(detail.Description) ? string.Empty : $" - {detail.Description}");

    private static bool LooksLikeExpiredAuthorization(Error error)
    {
        var haystack = string.Join(" ", (error.Details ?? Array.Empty<ErrorDetails>()).SelectMany(d => new[] { d.Issue, d.Description }))
            + " " + error.Name + " " + error.Message;
        return haystack.Contains("EXPIR", StringComparison.OrdinalIgnoreCase)
            || haystack.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase);
    }

    private static Exception MapCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentDeclinedException(DescribeError(error));
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal returned HTTP {(int)raw.StatusCode} while creating the order: {raw.ReadAsString()}");
        }
        return new PaymentGatewayException("PayPal rejected order creation for an unrecognised reason.");
    }

    private static Exception MapAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentDeclinedException(DescribeError(error));
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal returned HTTP {(int)raw.StatusCode} while authorizing the payment: {raw.ReadAsString()}");
        }
        return new PaymentGatewayException("PayPal rejected the authorization for an unrecognised reason.");
    }

    private static Exception MapCaptureError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return LooksLikeExpiredAuthorization(error)
                ? new AuthorizationExpiredException(DescribeError(error))
                : new PaymentDeclinedException(DescribeError(error));
        }
        if (ex.Error.TryGetNoContent(out var raw500))
        {
            return new PaymentGatewayException($"PayPal returned HTTP {(int)raw500.StatusCode} while capturing the payment.");
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal returned HTTP {(int)raw.StatusCode} while capturing the payment: {raw.ReadAsString()}");
        }
        return new PaymentGatewayException("PayPal rejected the capture for an unrecognised reason.");
    }

    private static Exception MapReauthorizeError(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new AuthorizationNotRenewableException(DescribeError(error));
        }
        if (ex.Error.TryGetNoContent(out var raw500))
        {
            return new PaymentGatewayException($"PayPal returned HTTP {(int)raw500.StatusCode} while reauthorizing the payment.");
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal returned HTTP {(int)raw.StatusCode} while reauthorizing the payment: {raw.ReadAsString()}");
        }
        return new AuthorizationNotRenewableException("PayPal rejected the reauthorization for an unrecognised reason.");
    }

    private static Exception MapVoidError(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentGatewayException(DescribeError(error));
        }
        if (ex.Error.TryGetNoContent(out var raw500))
        {
            return new PaymentGatewayException($"PayPal returned HTTP {(int)raw500.StatusCode} while voiding the authorization.");
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal returned HTTP {(int)raw.StatusCode} while voiding the authorization: {raw.ReadAsString()}");
        }
        return new PaymentGatewayException("PayPal rejected the void for an unrecognised reason.");
    }

    private static Exception MapRefundError(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentDeclinedException(DescribeError(error));
        }
        if (ex.Error.TryGetNoContent(out var raw500))
        {
            return new PaymentGatewayException($"PayPal returned HTTP {(int)raw500.StatusCode} while refunding the payment.");
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal returned HTTP {(int)raw.StatusCode} while refunding the payment: {raw.ReadAsString()}");
        }
        return new PaymentGatewayException("PayPal rejected the refund for an unrecognised reason.");
    }

    private static Exception MapCreatePaymentTokenError(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            return new PaymentDeclinedException(DescribeError1(error));
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal returned HTTP {(int)raw.StatusCode} while saving the card: {raw.ReadAsString()}");
        }
        return new PaymentGatewayException("PayPal rejected saving the card for an unrecognised reason.");
    }

    private static Exception MapDeletePaymentTokenError(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            return new PaymentGatewayException(DescribeError1(error));
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal returned HTTP {(int)raw.StatusCode} while deleting the saved card: {raw.ReadAsString()}");
        }
        return new PaymentGatewayException("PayPal rejected deleting the saved card for an unrecognised reason.");
    }

    private static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName)
    {
        try
        {
            return await operation();
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException($"PayPal returned a response for {operationName} that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException($"PayPal was unreachable while calling {operationName}.", ex);
        }
    }
}
