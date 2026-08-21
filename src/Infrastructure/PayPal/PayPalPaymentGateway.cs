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
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The only class that talks to the PayPal SDK. It translates the SDK's models and errors into the
/// SDK-agnostic contracts in <see cref="ApplicationCore.PayPal"/>, and every provider failure into a
/// <see cref="PaymentException"/> subtype so the layers above see one failure type.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ----- Step 1 + 2: place a hold (create order intent=AUTHORIZE, then authorize) -----

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        var currency = request.CurrencyCode;
        var card = BuildCardRequest(request);
        var invoiceId = $"{ReconciliationService.InvoicePrefix}{request.OrderReference}-{request.InvoiceReference}";

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = CurrencyFormatter.Format(request.Amount, currency)
                    },
                    CustomId = request.OrderReference.ToString(CultureInfo.InvariantCulture),
                    InvoiceId = invoiceId
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        // Create the order with the card attached.
        var created = await CreateOrderAsync(orderRequest, request.IdempotencyKey + "-create", cancellationToken);
        var payPalOrderId = created.Id ?? throw new PaymentGatewayException("PayPal did not return an order id.");

        _logger.LogDebug("PayPal CreateOrder OrderId={OrderId} Status={Status} PurchaseUnits={PuCount} Auths={AuthCount}",
            payPalOrderId, created.Status?.Value, created.PurchaseUnits?.Count,
            created.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.Count);

        if (created.Status is { } createStatus && createStatus == OrderStatus.PayerActionRequired)
        {
            return new PayPalAuthorizationResult { PayPalOrderId = payPalOrderId, RequiresAction = true };
        }

        // Advanced card processing can produce the authorization directly on create; use it if present.
        var directAuth = ExtractAuthorization(created.PurchaseUnits);
        if (directAuth is not null)
        {
            return BuildAuthorizationResult(payPalOrderId, directAuth);
        }

        // Otherwise authorize the created order explicitly.
        var authResponse = await AuthorizeOrderAsync(payPalOrderId, card, request.IdempotencyKey, cancellationToken);

        if (authResponse.Status is { } authStatus && authStatus == OrderStatus.PayerActionRequired)
        {
            return new PayPalAuthorizationResult { PayPalOrderId = payPalOrderId, RequiresAction = true };
        }

        var authorization = ExtractAuthorization(authResponse.PurchaseUnits);
        if (authorization is null)
        {
            throw new PaymentGatewayException("PayPal did not return an authorization for the card payment.");
        }

        return BuildAuthorizationResult(payPalOrderId, authorization);
    }

    // ----- Step 3: capture the authorization at fulfilment -----

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw new PaymentGatewayException(Describe(ex.Error), ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex);
        }

        var breakdown = captured.SellerReceivableBreakdown;
        var gross = breakdown?.GrossAmount is { } g
            ? CurrencyFormatter.Parse(g.Value)
            : CurrencyFormatter.Parse(captured.Amount?.Value);
        var currency = breakdown?.GrossAmount?.CurrencyCode ?? captured.Amount?.CurrencyCode ?? string.Empty;

        return new PayPalCaptureResult
        {
            CaptureId = captured.Id ?? throw new PaymentGatewayException("PayPal did not return a capture id."),
            Status = captured.Status?.Value,
            GrossAmount = gross,
            PayPalFee = breakdown?.PaypalFee is { } fee ? CurrencyFormatter.Parse(fee.Value) : null,
            NetAmount = breakdown?.NetAmount is { } net ? CurrencyFormatter.Parse(net.Value) : null,
            CurrencyCode = currency
        };
    }

    // ----- Step 4: renew a stale authorization -----

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        PaymentAuthorization reauth;
        try
        {
            reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currencyCode, Value = CurrencyFormatter.Format(amount, currencyCode) }
                },
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            // A stale authorization that can no longer be renewed surfaces here — give the operator PayPal's own reason.
            throw new AuthorizationRenewalException(Describe(ex.Error), ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex);
        }

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = string.Empty,
            AuthorizationId = reauth.Id,
            AuthorizationStatus = reauth.Status?.Value,
            ExpiresAt = ParseDate(reauth.ExpirationTime)
        };
    }

    // ----- Step 5: void a hold (release funds) -----

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                // Ask for a representation body: a bare 204 has no body for the SDK's declared
                // PaymentAuthorization return type to deserialize, which would surface as a JsonException.
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw new PaymentGatewayException(Describe(ex.Error), ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex);
        }
    }

    // ----- Step 6: refund a capture -----

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Refund refund;
        try
        {
            var body = amount.HasValue
                ? new RefundRequest
                {
                    Amount = new Money { CurrencyCode = currencyCode, Value = CurrencyFormatter.Format(amount.Value, currencyCode) }
                }
                : null;

            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                // Scope the caller's idempotency key to this capture so the same key stays idempotent
                // for this capture, while the same key used against a different capture never collides.
                payPalRequestId: $"refund-{captureId}-{idempotencyKey}",
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw new PaymentGatewayException(Describe(ex.Error), ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex);
        }

        var refundedValue = refund.Amount is { } m ? CurrencyFormatter.Parse(m.Value) : (amount ?? 0m);
        return new PayPalRefundResult
        {
            RefundId = refund.Id ?? throw new PaymentGatewayException("PayPal did not return a refund id."),
            Status = refund.Status?.Value,
            Amount = refundedValue,
            CurrencyCode = refund.Amount?.CurrencyCode ?? currencyCode
        };
    }

    // ----- Step 7: transaction search for reconciliation (paged across the whole range) -----

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalTransaction>();
        var startDate = FormatSearchDate(from);
        var endDate = FormatSearchDate(to);

        var page = 1;
        int totalPages;
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
                    pageSize: 100,
                    page: page,
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                // SearchTransactions is the SDK's only Case-B operation: read the status/body straight off RawError.
                throw new PaymentGatewayException($"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}", ex);
            }
            catch (Exception ex) when (IsInfrastructureFailure(ex))
            {
                throw ToInfrastructureException(ex);
            }

            if (response.TransactionDetails is { } details)
            {
                foreach (var td in details)
                {
                    var info = td.TransactionInfo;
                    results.Add(new PayPalTransaction
                    {
                        TransactionId = info?.TransactionId ?? string.Empty,
                        Amount = info?.TransactionAmount is { } m ? CurrencyFormatter.Parse(m.Value) : null,
                        CurrencyCode = info?.TransactionAmount?.CurrencyCode,
                        Status = info?.TransactionStatus,
                        InvoiceId = info?.InvoiceId,
                        CustomField = info?.CustomField
                    });
                }
            }

            totalPages = response.TotalPages ?? 1;
            page++;
        }
        while (page <= totalPages);

        return results;
    }

    // ----- Step 8: vault a card / delete a vaulted card -----

    public async Task<PayPalVaultResult> VaultCardAsync(PayPalCardDetails card, string customerId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        PaymentTokenResponse token;
        try
        {
            var body = new PaymentTokenRequest
            {
                Customer = new Customer { MerchantCustomerId = customerId },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Name = card.CardholderName,
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode
                    }
                }
            };

            token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                ct: cancellationToken);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw new PaymentGatewayException(Describe(ex.Error), ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex);
        }

        var cardEntity = token.PaymentSource?.Card;
        return new PayPalVaultResult
        {
            VaultId = token.Id ?? throw new PaymentGatewayException("PayPal did not return a vault token id."),
            CardBrand = cardEntity?.Brand?.Value ?? "UNKNOWN",
            LastFourDigits = cardEntity?.LastDigits ?? string.Empty,
            Expiry = cardEntity?.Expiry
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw new PaymentGatewayException(Describe(ex.Error), ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex);
        }
    }

    // ----- SDK-call wrappers with per-operation error translation -----

    private async Task<Order> CreateOrderAsync(OrderRequest body, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw new PaymentGatewayException(Describe(ex.Error), ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex);
        }
    }

    private async Task<OrderAuthorizeResponse> AuthorizeOrderAsync(string orderId, CardRequest card, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.Orders.AuthorizeOrder(
                id: orderId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = card }
                },
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw new PaymentGatewayException(Describe(ex.Error), ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex);
        }
    }

    // ----- helpers -----

    private static CardRequest BuildCardRequest(PayPalAuthorizationRequest request)
    {
        if (!string.IsNullOrEmpty(request.VaultId))
        {
            // Pay with a saved card: reference the vault id, never send raw card fields.
            return new CardRequest { VaultId = request.VaultId };
        }

        if (request.Card is { } card)
        {
            return new CardRequest
            {
                Name = card.CardholderName,
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode
            };
        }

        throw new InvalidPaymentOperationException("A card or a saved payment method must be supplied to pay.");
    }

    private static AuthorizationInfo? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        var authorization = purchaseUnits?
            .Select(pu => pu.Payments?.Authorizations)
            .Where(a => a is { Count: > 0 })
            .Select(a => a![0])
            .FirstOrDefault();

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            return null;
        }

        return new AuthorizationInfo(authorization.Id, authorization.Status?.Value, ParseDate(authorization.ExpirationTime));
    }

    private static PayPalAuthorizationResult BuildAuthorizationResult(string payPalOrderId, AuthorizationInfo authorization) =>
        new PayPalAuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authorization.Id,
            AuthorizationStatus = authorization.Status,
            ExpiresAt = authorization.ExpiresAt,
            RequiresAction = false
        };

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static bool IsInfrastructureFailure(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException or OperationCanceledException;

    private static PaymentGatewayException ToInfrastructureException(Exception ex) => ex switch
    {
        JsonException => new PaymentGatewayException("PayPal returned a response that could not be processed.", ex),
        _ => new PaymentGatewayException("PayPal is currently unreachable.", ex)
    };

    // ----- error description (typed Error / Error1 first, RawError fallback) -----

    private static string Describe(CreateOrderError e) =>
        e.TryGetError(out var typed) ? Summarize(typed) : e.TryGetRawError(out var raw) ? Summarize(raw) : DefaultError;

    private static string Describe(AuthorizeOrderError e) =>
        e.TryGetError(out var typed) ? Summarize(typed) : e.TryGetRawError(out var raw) ? Summarize(raw) : DefaultError;

    private static string Describe(CaptureAuthorizedPaymentError e) =>
        e.TryGetError(out var typed) ? Summarize(typed)
            : e.TryGetNoContent(out var nc) ? Summarize(nc)
            : e.TryGetRawError(out var raw) ? Summarize(raw) : DefaultError;

    private static string Describe(ReauthorizePaymentError e) =>
        e.TryGetError(out var typed) ? Summarize(typed)
            : e.TryGetNoContent(out var nc) ? Summarize(nc)
            : e.TryGetRawError(out var raw) ? Summarize(raw) : DefaultError;

    private static string Describe(VoidPaymentError e) =>
        e.TryGetError(out var typed) ? Summarize(typed)
            : e.TryGetNoContent(out var nc) ? Summarize(nc)
            : e.TryGetRawError(out var raw) ? Summarize(raw) : DefaultError;

    private static string Describe(RefundCapturedPaymentError e) =>
        e.TryGetError(out var typed) ? Summarize(typed)
            : e.TryGetNoContent(out var nc) ? Summarize(nc)
            : e.TryGetRawError(out var raw) ? Summarize(raw) : DefaultError;

    private static string Describe(CreatePaymentTokenError e) =>
        e.TryGetError1(out var typed) ? Summarize(typed) : e.TryGetRawError(out var raw) ? Summarize(raw) : DefaultError;

    private static string Describe(DeletePaymentTokenError e) =>
        e.TryGetError1(out var typed) ? Summarize(typed) : e.TryGetRawError(out var raw) ? Summarize(raw) : DefaultError;

    private const string DefaultError = "PayPal rejected the request.";

    private static string Summarize(Error error)
    {
        var issues = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(FormatIssue))
            : null;
        var message = string.IsNullOrWhiteSpace(error.Message) ? DefaultError : error.Message;
        return string.IsNullOrEmpty(issues) ? message : $"{message} ({issues})";
    }

    private static string Summarize(Error1 error)
    {
        var issues = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(d => string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}"))
            : null;
        var message = string.IsNullOrWhiteSpace(error.Message) ? DefaultError : error.Message;
        return string.IsNullOrEmpty(issues) ? message : $"{message} ({issues})";
    }

    private static string Summarize(RawError raw) => $"PayPal returned HTTP {(int)raw.StatusCode}.";

    private static string FormatIssue(ErrorDetails d) =>
        string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}";

    private sealed record AuthorizationInfo(string Id, string? Status, DateTimeOffset? ExpiresAt);
}
