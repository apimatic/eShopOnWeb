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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/> over the PayPalServerSdk client.
/// Raw card details pass through to PayPal only; they are never persisted or logged here.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private const string PreferRepresentation = "return=representation";
    private static readonly TimeSpan MaxTransactionSearchRange = TimeSpan.FromDays(31);

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizeOrderAsync(AuthorizePaymentCommand command, CancellationToken ct = default)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = command.Currency,
                        Value = FormatAmount(command.Amount)
                    },
                    ReferenceId = command.OrderReference,
                    CustomId = command.OrderReference,
                    Description = $"eShopOnWeb {command.OrderReference}"
                }
            },
            PaymentSource = BuildPaymentSource(command)
        };

        Order order;
        try
        {
            order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: command.CreateOrderIdempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: PreferRepresentation,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrderError(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex, ct))
        {
            throw TranslateBoundaryFailure(ex, ct);
        }

        ThrowIfPayerActionRequired(order.Status?.Value, order.Id);

        // A create call that carries payment source information is single-step: with
        // intent=AUTHORIZE the authorization is performed by the create itself. Only call
        // AuthorizeOrder when the create response carries no authorization.
        var authorization = ExtractAuthorization(order.PurchaseUnits);

        if (authorization?.Id is null)
        {
            OrderAuthorizeResponse? authorizationResponse = null;
            try
            {
                authorizationResponse = await _client.Orders.AuthorizeOrder(
                    id: order.Id!,
                    payPalMockResponse: null,
                    payPalRequestId: command.AuthorizeIdempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<AuthorizeOrderError> ex) when (IsOrderAlreadyAuthorized(ex))
            {
                // The order was already authorized (e.g. a retried request landed twice);
                // re-read it instead of failing the checkout.
                var reread = await GetOrderAsync(order.Id!, ct);
                authorization = ExtractAuthorization(reread.PurchaseUnits);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                throw TranslateAuthorizeOrderError(ex);
            }
            catch (Exception ex) when (IsBoundaryFailure(ex, ct))
            {
                throw TranslateBoundaryFailure(ex, ct);
            }

            if (authorization?.Id is null && authorizationResponse is not null)
            {
                ThrowIfPayerActionRequired(authorizationResponse.Status?.Value, order.Id);
                authorization = ExtractAuthorization(authorizationResponse.PurchaseUnits);
            }
        }

        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException("PayPal did not return an authorization for the order.");
        }

        return new AuthorizationResult
        {
            PayPalOrderId = order.Id!,
            AuthorizationId = authorization.Id,
            Status = authorization.Status?.Value ?? string.Empty,
            Amount = ParseAmount(authorization.Amount?.Value) ?? command.Amount,
            Currency = authorization.Amount?.CurrencyCode ?? command.Currency,
            ExpiresAt = ParseTimestamp(authorization.ExpirationTime)
        };
    }

    public async Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var authorization = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: ct);

            return ToAuthorizationState(authorization);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw TranslateGetAuthorizedPaymentError(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex, ct))
        {
            throw TranslateBoundaryFailure(ex, ct);
        }
    }

    public async Task<AuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var authorization = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
                },
                prefer: PreferRepresentation,
                requestOptions: null,
                ct: ct);

            _logger.LogInformation("Reauthorized PayPal authorization {AuthorizationId}", authorizationId);
            return ToAuthorizationState(authorization);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw TranslateReauthorizePaymentError(ex, authorizationId);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex, ct))
        {
            throw TranslateBoundaryFailure(ex, ct);
        }
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: PreferRepresentation,
                requestOptions: null,
                ct: ct);

            if (capture.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a capture for the authorization.");
            }

            return new CaptureResult
            {
                CaptureId = capture.Id,
                Status = capture.Status?.Value ?? string.Empty,
                Amount = ParseAmount(capture.Amount?.Value) ?? 0m,
                Currency = capture.Amount?.CurrencyCode ?? string.Empty,
                PayPalFee = ParseAmount(capture.SellerReceivableBreakdown?.PaypalFee?.Value),
                NetAmount = ParseAmount(capture.SellerReceivableBreakdown?.NetAmount?.Value)
            };
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw TranslateCaptureAuthorizedPaymentError(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex, ct))
        {
            throw TranslateBoundaryFailure(ex, ct);
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: PreferRepresentation,
                requestOptions: null,
                ct: ct);

            _logger.LogInformation("Voided PayPal authorization {AuthorizationId}", authorizationId);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw TranslateVoidPaymentError(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex, ct))
        {
            throw TranslateBoundaryFailure(ex, ct);
        }
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        // An explicit amount equal to the remaining captured amount is a full refund;
        // anything less is a partial refund.
        var body = amount is null
            ? null
            : new RefundRequest
            {
                Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) }
            };

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: PreferRepresentation,
                requestOptions: null,
                ct: ct);

            if (refund.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a refund for the capture.");
            }

            return new RefundResult
            {
                RefundId = refund.Id,
                Status = refund.Status?.Value ?? string.Empty,
                Amount = ParseAmount(refund.Amount?.Value),
                Currency = refund.Amount?.CurrencyCode ?? currency
            };
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw TranslateRefundCapturedPaymentError(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex, ct))
        {
            throw TranslateBoundaryFailure(ex, ct);
        }
    }

    public async Task<SavedCardResult> SaveCardAsync(SaveCardCommand command, CancellationToken ct = default)
    {
        var request = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = command.Card.Number,
                    Expiry = command.Card.Expiry,
                    SecurityCode = command.Card.SecurityCode,
                    Name = command.Card.Name,
                    BillingAddress = ToSdkAddress(command.Card.BillingAddress)
                }
            },
            Customer = command.PayPalCustomerId is not null
                ? new Customer { Id = command.PayPalCustomerId }
                : new Customer { MerchantCustomerId = command.BuyerId }
        };

        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: command.IdempotencyKey,
                body: request,
                requestOptions: null,
                ct: ct);

            if (token.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a vaulted payment token.");
            }

            return new SavedCardResult
            {
                VaultTokenId = token.Id,
                PayPalCustomerId = token.Customer?.Id,
                Brand = token.PaymentSource?.Card?.Brand?.Value,
                LastDigits = token.PaymentSource?.Card?.LastDigits,
                Expiry = token.PaymentSource?.Card?.Expiry,
                CardholderName = token.PaymentSource?.Card?.Name
            };
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateCreatePaymentTokenError(ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex, ct))
        {
            throw TranslateBoundaryFailure(ex, ct);
        }
    }

    public async Task DeleteSavedCardAsync(string vaultTokenId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(
                id: vaultTokenId,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
            {
                // Already gone at PayPal is the desired end state of a delete.
                if (error.Name == "RESOURCE_NOT_FOUND")
                {
                    return;
                }
                throw new PaymentGatewayException($"PayPal could not delete the saved card: {error.Message} (debug id {error.DebugId}).");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                if ((int)raw.StatusCode == 404)
                {
                    return;
                }
                throw new PaymentGatewayException("PayPal could not delete the saved card.", (int)raw.StatusCode, ex);
            }
            throw new PaymentGatewayException("PayPal could not delete the saved card.", null, ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex, ct))
        {
            throw TranslateBoundaryFailure(ex, ct);
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<GatewayTransaction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // The reporting API accepts a maximum range of 31 days per request.
        for (var windowStart = from; windowStart < to; windowStart = windowStart.Add(MaxTransactionSearchRange))
        {
            var windowEnd = windowStart.Add(MaxTransactionSearchRange) < to ? windowStart.Add(MaxTransactionSearchRange) : to;

            var page = 1;
            var totalPages = 1;
            while (page <= totalPages)
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: FormatTimestamp(windowStart),
                        endDate: FormatTimestamp(windowEnd),
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
                catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
                {
                    // A 404 on this collection GET means "no transactions in range" — an
                    // empty page, not a failure.
                    break;
                }
                catch (SdkException<RawError> ex)
                {
                    throw new PaymentGatewayException(
                        "PayPal could not return the transaction report.", (int)ex.Error.StatusCode, ex);
                }
                catch (Exception ex) when (IsBoundaryFailure(ex, ct))
                {
                    throw TranslateBoundaryFailure(ex, ct);
                }

                totalPages = response.TotalPages ?? 1;

                foreach (var detail in response.TransactionDetails ?? (IReadOnlyList<TransactionDetails>)Array.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }

                    // Transaction ids are not unique in the reporting system; the event code disambiguates.
                    var dedupeKey = $"{info.TransactionId}|{info.TransactionEventCode}";
                    if (!seen.Add(dedupeKey))
                    {
                        continue;
                    }

                    results.Add(new GatewayTransaction
                    {
                        TransactionId = info.TransactionId,
                        ReferenceId = info.PaypalReferenceId,
                        ReferenceIdType = info.PaypalReferenceIdType?.Value,
                        EventCode = info.TransactionEventCode,
                        Status = info.TransactionStatus,
                        Amount = ParseAmount(info.TransactionAmount?.Value),
                        Currency = info.TransactionAmount?.CurrencyCode,
                        Fee = ParseAmount(info.FeeAmount?.Value),
                        InvoiceId = info.InvoiceId,
                        InitiatedAt = ParseTimestamp(info.TransactionInitiationDate),
                        UpdatedAt = ParseTimestamp(info.TransactionUpdatedDate)
                    });
                }

                page++;
            }
        }

        return results;
    }

    private static PaymentSource BuildPaymentSource(AuthorizePaymentCommand command)
    {
        if (command.VaultTokenId is not null)
        {
            // Buyer-present checkout charging a previously vaulted card.
            return new PaymentSource
            {
                Card = new CardRequest
                {
                    VaultId = command.VaultTokenId,
                    StoredCredential = new CardStoredCredential
                    {
                        PaymentInitiator = PaymentInitiator.Customer,
                        PaymentType = StoredPaymentSourcePaymentType.Unscheduled,
                        Usage = StoredPaymentSourceUsageType.Subsequent
                    }
                }
            };
        }

        return new PaymentSource
        {
            Card = new CardRequest
            {
                Number = command.Card!.Number,
                Expiry = command.Card.Expiry,
                SecurityCode = command.Card.SecurityCode,
                Name = command.Card.Name,
                BillingAddress = ToSdkAddress(command.Card.BillingAddress)
            }
        };
    }

    private static PayPalServerSdk.Models.Address? ToSdkAddress(CardBillingAddress? address) =>
        address is null
            ? null
            : new PayPalServerSdk.Models.Address
            {
                CountryCode = address.CountryCode,
                AddressLine1 = address.AddressLine1,
                AddressLine2 = address.AddressLine2,
                AdminArea2 = address.City,
                AdminArea1 = address.State,
                PostalCode = address.PostalCode
            };

    private async Task<Order> GetOrderAsync(string orderId, CancellationToken ct)
    {
        try
        {
            return await _client.Orders.GetOrder(
                id: orderId,
                fields: null,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<GetOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                throw new PaymentGatewayException($"PayPal could not read the order: {Describe(error)}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PaymentGatewayException("PayPal could not read the order.", (int)raw.StatusCode, ex);
            }
            throw new PaymentGatewayException("PayPal could not read the order.", null, ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex, ct))
        {
            throw TranslateBoundaryFailure(ex, ct);
        }
    }

    private static bool IsOrderAlreadyAuthorized(SdkException<AuthorizeOrderError> ex) =>
        ex.Error.TryGetError(out var error) &&
        error.Details?.Any(d => d.Issue == "ORDER_ALREADY_AUTHORIZED") == true;

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? (IReadOnlyList<AuthorizationWithAdditionalData>)Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

    private static AuthorizationState ToAuthorizationState(PaymentAuthorization authorization) => new()
    {
        AuthorizationId = authorization.Id ?? string.Empty,
        Status = authorization.Status?.Value ?? string.Empty,
        Amount = ParseAmount(authorization.Amount?.Value),
        ExpiresAt = ParseTimestamp(authorization.ExpirationTime)
    };

    private void ThrowIfPayerActionRequired(string? orderStatus, string? payPalOrderId)
    {
        if (orderStatus == OrderStatus.PayerActionRequired.Value)
        {
            _logger.LogWarning("PayPal order {PayPalOrderId} requires payer action (e.g. 3-D Secure); this integration is server-to-server only.", payPalOrderId);
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This integration is server-to-server only and cannot complete an approval round-trip.");
        }
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(DateTimeOffset timestamp) =>
        timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null;

    private static string Describe(Error error)
    {
        var details = error.Details is null
            ? string.Empty
            : " [" + string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}")) + "]";
        return $"{error.Message}{details} (debug id {error.DebugId})";
    }

    private static string Describe(Error1 error)
    {
        var details = error.Details is null
            ? string.Empty
            : " [" + string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}")) + "]";
        return $"{error.Message}{details} (debug id {error.DebugId})";
    }

    private static bool IsBoundaryFailure(Exception ex, CancellationToken ct) =>
        ex is JsonException or HttpRequestException ||
        (ex is TaskCanceledException && !ct.IsCancellationRequested);

    private static PaymentGatewayException TranslateBoundaryFailure(Exception ex, CancellationToken ct) =>
        ex switch
        {
            // Covers both directions: an unreadable 2xx body (outcome unknown) and an unreadable
            // error body (the rejection detail was lost). Either way the caller gets a safe message.
            JsonException => new PaymentGatewayException("PayPal returned a response that could not be processed.", null, ex),
            TaskCanceledException => new PaymentGatewayException("PayPal did not respond in time.", null, ex),
            _ => new PaymentGatewayException("PayPal is unreachable.", null, ex)
        };

    private static PaymentGatewayException TranslateCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentGatewayException($"PayPal could not create the order: {Describe(error)}");
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException("PayPal could not create the order.", (int)raw.StatusCode, ex);
        }
        return new PaymentGatewayException("PayPal could not create the order.", null, ex);
    }

    private static PaymentGatewayException TranslateAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentGatewayException($"PayPal could not authorize the payment: {Describe(error)}");
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException("PayPal could not authorize the payment.", (int)raw.StatusCode, ex);
        }
        return new PaymentGatewayException("PayPal could not authorize the payment.", null, ex);
    }

    private static PaymentGatewayException TranslateGetAuthorizedPaymentError(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentGatewayException($"PayPal could not read the authorization: {Describe(error)}");
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return new PaymentGatewayException("PayPal could not read the authorization.", (int)noContent.StatusCode, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException("PayPal could not read the authorization.", (int)raw.StatusCode, ex);
        }
        return new PaymentGatewayException("PayPal could not read the authorization.", null, ex);
    }

    private static PaymentGatewayException TranslateReauthorizePaymentError(SdkException<ReauthorizePaymentError> ex, string authorizationId)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentGatewayException(
                $"PayPal refused to renew authorization {authorizationId}: {Describe(error)} " +
                "Cancel the order and ask the shopper to pay again.");
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return new PaymentGatewayException($"PayPal refused to renew authorization {authorizationId}.", (int)noContent.StatusCode, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal refused to renew authorization {authorizationId}.", (int)raw.StatusCode, ex);
        }
        return new PaymentGatewayException($"PayPal refused to renew authorization {authorizationId}.", null, ex);
    }

    private static PaymentGatewayException TranslateCaptureAuthorizedPaymentError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentGatewayException($"PayPal could not capture the payment: {Describe(error)}");
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return new PaymentGatewayException("PayPal could not capture the payment.", (int)noContent.StatusCode, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException("PayPal could not capture the payment.", (int)raw.StatusCode, ex);
        }
        return new PaymentGatewayException("PayPal could not capture the payment.", null, ex);
    }

    private static PaymentGatewayException TranslateVoidPaymentError(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentGatewayException($"PayPal could not release the held funds: {Describe(error)}");
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return new PaymentGatewayException("PayPal could not release the held funds.", (int)noContent.StatusCode, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException("PayPal could not release the held funds.", (int)raw.StatusCode, ex);
        }
        return new PaymentGatewayException("PayPal could not release the held funds.", null, ex);
    }

    private static PaymentGatewayException TranslateRefundCapturedPaymentError(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            return new PaymentGatewayException($"PayPal could not refund the payment: {Describe(error)}");
        }
        if (ex.Error.TryGetNoContent(out var noContent))
        {
            return new PaymentGatewayException("PayPal could not refund the payment.", (int)noContent.StatusCode, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException("PayPal could not refund the payment.", (int)raw.StatusCode, ex);
        }
        return new PaymentGatewayException("PayPal could not refund the payment.", null, ex);
    }

    private static PaymentGatewayException TranslateCreatePaymentTokenError(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
        {
            return new PaymentGatewayException($"PayPal could not save the card: {Describe(error)}");
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException("PayPal could not save the card.", (int)raw.StatusCode, ex);
        }
        return new PaymentGatewayException("PayPal could not save the card.", null, ex);
    }
}
