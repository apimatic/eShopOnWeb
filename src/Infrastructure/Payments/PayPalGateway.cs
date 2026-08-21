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
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentInput card,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // This sandbox merchant refuses raw PAN on POST /v2/checkout/orders (TRANSACTION_REFUSED)
        // but accepts the same card through vault, then vault_id on the order.
        var vaulted = await SaveCardAsync(
            $"order-{orderId}",
            card,
            $"{idempotencyKey}-vault",
            cancellationToken);
        return await AuthorizeSavedCardAsync(
            orderId, amount, currency, vaulted.TokenId, idempotencyKey, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeSavedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var paymentSource = new PaymentSource
        {
            Card = new CardRequest { VaultId = vaultId }
        };
        return AuthorizeAsync(orderId, amount, currency, paymentSource, idempotencyKey, cancellationToken);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            using (SingleSendHandler.BeginWrite())
            {
                try
                {
                    var captured = await _client.Payments.CaptureAuthorizedPayment(
                        authorizationId: authorizationId,
                        payPalMockResponse: null,
                        payPalRequestId: idempotencyKey,
                        payPalAuthAssertion: null,
                        body: new CaptureRequest
                        {
                            Amount = new Money
                            {
                                CurrencyCode = currency,
                                Value = PayPalMoney.ToValue(amount)
                            },
                            FinalCapture = true,
                            InvoiceId = idempotencyKey
                        },
                        prefer: "return=representation",
                        requestOptions: null,
                        ct: ct);

                    return MapCapture(captured);
                }
                catch (SdkException<CaptureAuthorizedPaymentError> ex)
                {
                    throw MapCaptureError(ex);
                }
                catch (Exception ex)
                {
                    throw MapTransport(ex);
                }
            }
        }, cancellationToken);
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            try
            {
                var captured = await _client.Payments.GetCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    requestOptions: null,
                    ct: ct);
                return MapCapture(captured);
            }
            catch (SdkException<GetCapturedPaymentError> ex)
            {
                throw MapGetCaptureError(ex);
            }
            catch (Exception ex)
            {
                throw MapTransport(ex);
            }
        }, cancellationToken);
    }

    public async Task<PayPalCaptureResult?> FindCaptureForPayPalOrderAsync(string payPalOrderId, CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            try
            {
                var order = await _client.Orders.GetOrder(
                    id: payPalOrderId,
                    fields: null,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: ct);
                return FirstCapture(order);
            }
            catch (SdkException<GetOrderError> ex)
            {
                throw MapGetOrderError(ex);
            }
            catch (Exception ex)
            {
                throw MapTransport(ex);
            }
        }, cancellationToken);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            using (SingleSendHandler.BeginWrite())
            {
                try
                {
                    var auth = await _client.Payments.ReauthorizePayment(
                        authorizationId: authorizationId,
                        payPalRequestId: idempotencyKey,
                        payPalAuthAssertion: null,
                        body: new ReauthorizeRequest
                        {
                            Amount = new Money
                            {
                                CurrencyCode = currency,
                                Value = PayPalMoney.ToValue(amount)
                            }
                        },
                        prefer: "return=representation",
                        requestOptions: null,
                        ct: ct);

                    return new PayPalAuthorizationResult(
                        PayPalOrderId: string.Empty,
                        AuthorizationId: RequireId(auth.Id, "authorization"),
                        AuthorizationStatus: auth.Status?.Value,
                        Expiration: PayPalMoney.ParseTimestamp(auth.ExpirationTime),
                        PayerActionRequired: false);
                }
                catch (SdkException<ReauthorizePaymentError> ex)
                {
                    throw MapReauthorizeError(ex);
                }
                catch (Exception ex)
                {
                    throw MapTransport(ex);
                }
            }
        }, cancellationToken);
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        await Bounded(async ct =>
        {
            using (SingleSendHandler.BeginWrite())
            {
                try
                {
                    await _client.Payments.VoidPayment(
                        authorizationId: authorizationId,
                        payPalMockResponse: null,
                        payPalAuthAssertion: null,
                        payPalRequestId: idempotencyKey,
                        prefer: "return=representation",
                        requestOptions: null,
                        ct: ct);
                    return 0;
                }
                catch (SdkException<VoidPaymentError> ex)
                {
                    throw MapVoidError(ex);
                }
                catch (Exception ex)
                {
                    throw MapTransport(ex);
                }
            }
        }, cancellationToken);
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            using (SingleSendHandler.BeginWrite())
            {
                try
                {
                    RefundRequest? body = null;
                    if (amount.HasValue)
                    {
                        body = new RefundRequest
                        {
                            Amount = new Money
                            {
                                CurrencyCode = currency,
                                Value = PayPalMoney.ToValue(amount.Value)
                            }
                        };
                    }

                    var refund = await _client.Payments.RefundCapturedPayment(
                        captureId: captureId,
                        payPalMockResponse: null,
                        payPalRequestId: idempotencyKey,
                        payPalAuthAssertion: null,
                        body: body,
                        prefer: "return=representation",
                        requestOptions: null,
                        ct: ct);

                    var refundedAmount = refund.Amount?.Value != null
                        ? PayPalMoney.Parse(refund.Amount.Value)
                        : amount ?? 0m;

                    return new PayPalRefundResult(
                        RefundId: RequireId(refund.Id, "refund"),
                        Status: refund.Status?.Value,
                        Amount: refundedAmount);
                }
                catch (SdkException<RefundCapturedPaymentError> ex)
                {
                    throw MapRefundError(ex);
                }
                catch (Exception ex)
                {
                    throw MapTransport(ex);
                }
            }
        }, cancellationToken);
    }

    public async Task<PayPalVaultResult> SaveCardAsync(
        string buyerId,
        CardPaymentInput card,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return await Bounded(async ct =>
        {
            using (SingleSendHandler.BeginWrite())
            {
                try
                {
                    var created = await _client.Vault.CreatePaymentToken(
                        payPalRequestId: idempotencyKey,
                        body: new PaymentTokenRequest
                        {
                            Customer = new Customer { MerchantCustomerId = buyerId },
                            PaymentSource = new PaymentTokenRequestPaymentSource
                            {
                                Card = new PaymentTokenRequestCard
                                {
                                    Name = card.Name,
                                    Number = card.Number,
                                    Expiry = card.Expiry,
                                    SecurityCode = card.SecurityCode,
                                    BillingAddress = ToPayPalAddress(card.BillingAddress)
                                }
                            }
                        },
                        requestOptions: null,
                        ct: ct);

                    var cardSource = created.PaymentSource?.Card;
                    return new PayPalVaultResult(
                        TokenId: RequireId(created.Id, "payment token"),
                        CustomerId: created.Customer?.Id,
                        Brand: cardSource?.Brand?.Value,
                        LastDigits: cardSource?.LastDigits,
                        Expiry: cardSource?.Expiry);
                }
                catch (SdkException<CreatePaymentTokenError> ex)
                {
                    throw MapVaultError(ex.Error);
                }
                catch (Exception ex)
                {
                    throw MapTransport(ex);
                }
            }
        }, cancellationToken);
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        await Bounded(async ct =>
        {
            using (SingleSendHandler.BeginWrite())
            {
                try
                {
                    await _client.Vault.DeletePaymentToken(
                        id: tokenId,
                        requestOptions: null,
                        ct: ct);
                    return 0;
                }
                catch (SdkException<DeletePaymentTokenError> ex)
                {
                    throw MapVaultError(ex.Error);
                }
                catch (Exception ex)
                {
                    throw MapTransport(ex);
                }
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<PayPalReportedTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<PayPalReportedTransaction>();
        foreach (var (chunkFrom, chunkTo) in ChunkRange(from, to))
        {
            var page = 1;
            var totalPages = 1;
            do
            {
                var currentPage = page;
                var response = await Bounded(async ct =>
                {
                    try
                    {
                        return await _client.TransactionSearch.SearchTransactions(
                            startDate: PayPalMoney.ToRfc3339(chunkFrom),
                            endDate: PayPalMoney.ToRfc3339(chunkTo),
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
                            requestOptions: null,
                            ct: ct);
                    }
                    catch (SdkException<RawError> ex)
                    {
                        throw MapRaw(ex.Error);
                    }
                    catch (Exception ex)
                    {
                        throw MapTransport(ex);
                    }
                }, cancellationToken);

                if (response.TransactionDetails != null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info == null)
                        {
                            continue;
                        }

                        results.Add(new PayPalReportedTransaction(
                            TransactionId: info.TransactionId,
                            Status: info.TransactionStatus,
                            Amount: info.TransactionAmount?.Value,
                            Currency: info.TransactionAmount?.CurrencyCode,
                            FeeAmount: info.FeeAmount?.Value,
                            InitiationDate: info.TransactionInitiationDate,
                            UpdatedDate: info.TransactionUpdatedDate,
                            InvoiceId: info.InvoiceId,
                            CustomField: info.CustomField,
                            PaypalReferenceId: info.PaypalReferenceId,
                            PaypalReferenceIdType: info.PaypalReferenceIdType?.Value));
                    }
                }

                totalPages = response.TotalPages ?? page;
                page++;
            } while (page <= totalPages);
        }

        return results;
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        string currency,
        PaymentSource paymentSource,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var invoiceId = idempotencyKey;
        var customId = orderId.ToString(CultureInfo.InvariantCulture);
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    ReferenceId = customId,
                    InvoiceId = invoiceId,
                    CustomId = invoiceId,
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = PayPalMoney.ToValue(amount)
                    }
                }
            },
            PaymentSource = paymentSource
        };

        return await Bounded(async ct =>
        {
            using (SingleSendHandler.BeginWrite())
            {
                try
                {
                    var created = await _client.Orders.CreateOrder(
                        payPalMockResponse: null,
                        payPalRequestId: idempotencyKey,
                        payPalPartnerAttributionId: null,
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: body,
                        prefer: "return=representation",
                        requestOptions: null,
                        ct: ct);

                    return await ReadAuthorizationAsync(created, ct);
                }
                catch (SdkException<CreateOrderError> ex)
                {
                    throw MapCreateOrderError(ex);
                }
                catch (Exception ex)
                {
                    throw MapTransport(ex);
                }
            }
        }, cancellationToken);
    }

    private async Task<PayPalAuthorizationResult> ReadAuthorizationAsync(Order created, CancellationToken ct)
    {
        if (created.Status == OrderStatus.PayerActionRequired)
        {
            throw new PayerActionRequiredException(created.Id ?? string.Empty);
        }

        var authorization = FirstAuthorization(created);
        if (authorization == null)
        {
            try
            {
                var authorized = await _client.Orders.AuthorizeOrder(
                    id: RequireId(created.Id, "PayPal order"),
                    payPalMockResponse: null,
                    payPalRequestId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    requestOptions: null,
                    ct: ct);

                if (authorized.Status == OrderStatus.PayerActionRequired)
                {
                    throw new PayerActionRequiredException(authorized.Id ?? created.Id ?? string.Empty);
                }

                authorization = FirstAuthorizationFromAuthorizeResponse(authorized);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw MapAuthorizeOrderError(ex);
            }
        }

        if (authorization == null)
        {
            try
            {
                var fetched = await _client.Orders.GetOrder(
                    id: RequireId(created.Id, "PayPal order"),
                    fields: null,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: ct);

                if (fetched.Status == OrderStatus.PayerActionRequired)
                {
                    throw new PayerActionRequiredException(fetched.Id ?? created.Id ?? string.Empty);
                }

                authorization = FirstAuthorization(fetched);
                created = fetched;
            }
            catch (PayerActionRequiredException)
            {
                throw;
            }
            catch (SdkException<GetOrderError> ex)
            {
                throw MapGetOrderError(ex);
            }
        }

        if (authorization == null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PaymentException(502, "PayPal did not return an authorization id for the hold.");
        }

        return new PayPalAuthorizationResult(
            PayPalOrderId: RequireId(created.Id, "PayPal order"),
            AuthorizationId: authorization.Id,
            AuthorizationStatus: authorization.Status?.Value,
            Expiration: PayPalMoney.ParseTimestamp(authorization.ExpirationTime),
            PayerActionRequired: false,
            ExistingCapture: FirstCapture(created));
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(Order order)
    {
        return order.PurchaseUnits?
            .SelectMany(unit => unit.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault(a => !string.IsNullOrEmpty(a.Id));
    }

    private static AuthorizationWithAdditionalData? FirstAuthorizationFromAuthorizeResponse(OrderAuthorizeResponse response)
    {
        return response.PurchaseUnits?
            .SelectMany(unit => unit.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault(a => !string.IsNullOrEmpty(a.Id));
    }

    private static PayPalCaptureResult? FirstCapture(Order order)
    {
        var capture = order.PurchaseUnits?
            .SelectMany(unit => unit.Payments?.Captures ?? Array.Empty<OrdersCapture>())
            .FirstOrDefault(c => !string.IsNullOrEmpty(c.Id));
        return capture == null ? null : MapOrdersCapture(capture);
    }

    private static PayPalCaptureResult MapOrdersCapture(OrdersCapture captured)
    {
        var amount = captured.SellerReceivableBreakdown?.GrossAmount?.Value
                     ?? captured.Amount?.Value;
        var fee = captured.SellerReceivableBreakdown?.PaypalFee?.Value;
        var net = captured.SellerReceivableBreakdown?.NetAmount?.Value;

        return new PayPalCaptureResult(
            CaptureId: RequireId(captured.Id, "capture"),
            Status: captured.Status?.Value,
            CapturedAmount: PayPalMoney.Parse(amount),
            PaypalFee: fee == null ? null : PayPalMoney.Parse(fee),
            NetAmount: net == null ? null : PayPalMoney.Parse(net));
    }

    private static CardRequest BuildCardRequest(CardPaymentInput card)
    {
        return new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToPayPalAddress(card.BillingAddress)
        };
    }

    private static PayPalAddress? ToPayPalAddress(CardBillingAddress? address)
    {
        if (address == null)
        {
            return new PayPalAddress
            {
                CountryCode = "US",
                AddressLine1 = "123 Main St.",
                AdminArea2 = "Kent",
                AdminArea1 = "OH",
                PostalCode = "44240"
            };
        }

        if (string.IsNullOrWhiteSpace(address.CountryCode))
        {
            throw new PaymentException(400, "Billing address countryCode is required.");
        }

        return new PayPalAddress
        {
            CountryCode = address.CountryCode,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode
        };
    }

    private static PayPalCaptureResult MapCapture(CapturedPayment captured)
    {
        var amount = captured.SellerReceivableBreakdown?.GrossAmount?.Value
                     ?? captured.Amount?.Value;
        var fee = captured.SellerReceivableBreakdown?.PaypalFee?.Value;
        var net = captured.SellerReceivableBreakdown?.NetAmount?.Value;

        return new PayPalCaptureResult(
            CaptureId: RequireId(captured.Id, "capture"),
            Status: captured.Status?.Value,
            CapturedAmount: PayPalMoney.Parse(amount),
            PaypalFee: fee == null ? null : PayPalMoney.Parse(fee),
            NetAmount: net == null ? null : PayPalMoney.Parse(net));
    }

    private static PaymentException MapGetCaptureError(SdkException<GetCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return MapPayPalError(error, 404);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return MapRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw);
        }

        return UnknownProviderError();
    }

    private static PaymentException MapGetOrderError(SdkException<GetOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return MapPayPalError(error, 404);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw);
        }

        return UnknownProviderError();
    }

    private static PaymentException MapCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return MapPayPalError(error, 400);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw);
        }

        return UnknownProviderError();
    }

    private static PaymentException MapAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return MapPayPalError(error, 400);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw);
        }

        return UnknownProviderError();
    }

    private static PaymentException MapCaptureError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return MapPayPalError(error, 400);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return MapRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw);
        }

        return UnknownProviderError();
    }

    private static PaymentException MapReauthorizeError(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return MapPayPalError(error, 422);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return MapRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw);
        }

        return UnknownProviderError();
    }

    private static PaymentException MapVoidError(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return MapPayPalError(error, 409);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return MapRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw);
        }

        return UnknownProviderError();
    }

    private static PaymentException MapRefundError(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return MapPayPalError(error, 400);
        }

        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return MapRaw(noContent);
        }

        if (ex.Error.TryGetRawError(out var raw))
        {
            return MapRaw(raw);
        }

        return UnknownProviderError();
    }

    private static PaymentException MapVaultError(ApiError error)
    {
        // Vault errors expose TryGetError1 / TryGetRawError on the concrete type.
        if (error is CreatePaymentTokenError createError)
        {
            if (createError.TryGetError1(out var typed))
            {
                return MapPayPalError1(typed, 400);
            }

            if (createError.TryGetRawError(out var raw))
            {
                return MapRaw(raw);
            }
        }

        if (error is DeletePaymentTokenError deleteError)
        {
            if (deleteError.TryGetError1(out var typed))
            {
                return MapPayPalError1(typed, 400);
            }

            if (deleteError.TryGetRawError(out var raw))
            {
                return MapRaw(raw);
            }
        }

        return UnknownProviderError();
    }

    private static PaymentException MapPayPalError(Error error, int fallbackStatus)
    {
        var issue = error.Details?.FirstOrDefault()?.Issue;
        return new PaymentException(
            StatusFromName(error.Name, fallbackStatus),
            SafeMessage(error.Message, issue),
            error.DebugId,
            issue);
    }

    private static PaymentException MapPayPalError1(Error1 error, int fallbackStatus)
    {
        var issue = error.Details?.FirstOrDefault()?.Issue;
        return new PaymentException(
            StatusFromName(error.Name, fallbackStatus),
            SafeMessage(error.Message, issue),
            error.DebugId,
            issue);
    }

    private static PaymentException MapRaw(RawError raw)
    {
        return new PaymentException((int)raw.StatusCode, "PayPal rejected the request.");
    }

    private static PaymentException MapTransport(Exception ex)
    {
        if (ex is PaymentException payment)
        {
            return payment;
        }

        if (ex is DuplicateSendRefusedException duplicate)
        {
            return duplicate;
        }

        if (ex is AuthSchemeException)
        {
            return new PaymentException(502, "PayPal authentication failed.");
        }

        if (ex is JsonException)
        {
            var status = LastStatusHandler.LastStatus;
            if (status >= 400)
            {
                return new PaymentException(status.Value, "PayPal rejected the request.");
            }

            return new PaymentException(502, "PayPal returned a response that could not be processed.");
        }

        if (ex is HttpRequestException or TaskCanceledException)
        {
            return new PaymentException(503, "PayPal is unreachable.");
        }

        return new PaymentException(502, "PayPal request failed.");
    }

    private static int StatusFromName(string name, int fallback)
    {
        if (name.Contains("AUTHENTICATION", StringComparison.OrdinalIgnoreCase)
            || name.Contains("NOT_AUTHORIZED", StringComparison.OrdinalIgnoreCase))
        {
            return 401;
        }

        if (name.Contains("RESOURCE_NOT_FOUND", StringComparison.OrdinalIgnoreCase))
        {
            return 404;
        }

        if (name.Contains("PERMISSION", StringComparison.OrdinalIgnoreCase))
        {
            return 403;
        }

        if (name.Contains("UNPROCESSABLE", StringComparison.OrdinalIgnoreCase))
        {
            return 422;
        }

        if (name.Contains("INTERNAL", StringComparison.OrdinalIgnoreCase))
        {
            return 502;
        }

        return fallback;
    }

    private static string SafeMessage(string message, string? issue)
    {
        if (string.IsNullOrWhiteSpace(issue))
        {
            return string.IsNullOrWhiteSpace(message) ? "PayPal rejected the request." : message;
        }

        return string.IsNullOrWhiteSpace(message)
            ? issue
            : $"{message} ({issue})";
    }

    private static string RequireId(string? id, string what)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new PaymentException(502, $"PayPal did not return a {what} id.");
        }

        return id;
    }

    private static PaymentException UnknownProviderError() =>
        new(502, "PayPal rejected the request.");

    private static IEnumerable<(DateTimeOffset From, DateTimeOffset To)> ChunkRange(DateTimeOffset from, DateTimeOffset to)
    {
        var cursor = from;
        var maxChunk = TimeSpan.FromDays(31).Subtract(TimeSpan.FromSeconds(1));
        while (cursor <= to)
        {
            var chunkEnd = cursor + maxChunk;
            if (chunkEnd > to)
            {
                chunkEnd = to;
            }

            yield return (cursor, chunkEnd);
            cursor = chunkEnd.AddSeconds(1);
        }
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }
}
