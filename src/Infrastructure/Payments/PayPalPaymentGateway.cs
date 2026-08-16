using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;   // RawError
using PayPalServerSdk.Core.Exceptions;       // SdkException<TError>
using PayPalServerSdk.Errors;                // per-operation {Operation}Error types
using PayPalServerSdk.Models;                // request/response records, Error, Error1, ErrorDetails*
using PayPalServerSdk.Models.Enums;          // CheckoutPaymentIntent, OrderStatus

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The one place that talks to the PayPal .NET SDK. Translates the app's domain-shaped payment
/// operations onto the SDK's Orders / Payments / Vault / TransactionSearch controllers, and converts
/// every SDK failure into <see cref="PayPalGatewayException"/> so nothing SDK-shaped escapes this seam.
/// Card data flows straight through to PayPal and is never logged.
/// </summary>
public sealed class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private const string Representation = "return=representation";

    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IAppLogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ---- A. Create order (intent AUTHORIZE) + B. Authorize --------------------------------------

    public async Task<AuthorizationResult> AuthorizeAsync(decimal amount, string currency, CardDetails? card, string? vaultId, string requestId, CancellationToken cancellationToken = default)
    {
        if (vaultId is null && card is null)
        {
            throw new ArgumentException("Either card details or a vaultId must be supplied to authorize.", nameof(card));
        }

        var order = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = FormatAmount(amount),
                    },
                },
            },
            PaymentSource = new PaymentSource
            {
                Card = vaultId is not null
                    ? new CardRequest { VaultId = vaultId }
                    : BuildCardRequest(card!),
            },
        };

        var created = await Guard("create order", c => _client.Orders.CreateOrder(
            payPalMockResponse: null,
            payPalRequestId: requestId,
            payPalPartnerAttributionId: null,
            payPalClientMetadataId: null,
            payPalAuthAssertion: null,
            body: order,
            prefer: Representation,
            ct: c), cancellationToken);

        var orderId = created.Id ?? string.Empty;

        if (RequiresApproval(created))
        {
            throw new PayPalGatewayException(
                "PayPal requires interactive buyer approval or a 3DS challenge for this order, which this integration does not support.");
        }

        _logger.LogInformation("PayPal created order {OrderId} status {Status} for authorization.", orderId, created.Status?.Value ?? string.Empty);

        // A direct-card order with intent=AUTHORIZE is already authorized during CreateOrder — PayPal allows
        // only one authorization per order, so a follow-up AuthorizeOrder would 422 (ORDER_ALREADY_AUTHORIZED).
        // Prefer the authorization already present on the CreateOrder response; only authorize when none exists.
        var authorization = FirstAuthorization(created.PurchaseUnits);

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            var authResp = await Guard("authorize order", c => _client.Orders.AuthorizeOrder(
                id: orderId,
                payPalMockResponse: null,
                payPalRequestId: requestId + "-auth",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: Representation,
                ct: c), cancellationToken);

            authorization = FirstAuthorization(authResp.PurchaseUnits);
        }

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PayPalGatewayException("PayPal did not return an authorization for the order.");
        }

        var result = new AuthorizationResult(
            orderId,
            authorization.Id!,
            authorization.Status?.Value ?? string.Empty,
            ParseDate(authorization.ExpirationTime));

        _logger.LogInformation("PayPal authorization {AuthorizationId} status {Status}.", result.AuthorizationId, result.Status);
        return result;
    }

    // ---- G. Get authorization -------------------------------------------------------------------

    public async Task<AuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        var authorization = await Guard("get authorization", c => _client.Payments.GetAuthorizedPayment(
            authorizationId: authorizationId,
            payPalMockResponse: null,
            payPalAuthAssertion: null,
            ct: c), cancellationToken);

        return MapAuthorization(authorization);
    }

    // ---- D. Reauthorize -------------------------------------------------------------------------

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) },
        };

        var authorization = await Guard("reauthorize", c => _client.Payments.ReauthorizePayment(
            authorizationId: authorizationId,
            payPalRequestId: requestId,
            payPalAuthAssertion: null,
            body: body,
            prefer: Representation,
            ct: c), cancellationToken);

        var result = MapAuthorization(authorization);
        _logger.LogInformation("PayPal reauthorized {AuthorizationId} status {Status}.", result.AuthorizationId, result.Status);
        return result;
    }

    // ---- C. Capture -----------------------------------------------------------------------------

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        // Full capture of the authorized amount => null body.
        var capture = await Guard("capture", c => _client.Payments.CaptureAuthorizedPayment(
            authorizationId: authorizationId,
            payPalMockResponse: null,
            payPalRequestId: requestId,
            payPalAuthAssertion: null,
            body: null,
            prefer: Representation,
            ct: c), cancellationToken);

        var breakdown = capture.SellerReceivableBreakdown;
        decimal gross = breakdown is not null ? ParseAmount(breakdown.GrossAmount.Value) : ParseAmount(capture.Amount?.Value);
        decimal fee = breakdown?.PaypalFee is { } feeMoney ? ParseAmount(feeMoney.Value) : 0m;
        decimal net = breakdown?.NetAmount is { } netMoney ? ParseAmount(netMoney.Value) : 0m;

        var result = new CaptureResult(
            capture.Id ?? string.Empty,
            capture.Status?.Value ?? string.Empty,
            gross,
            fee,
            net);

        _logger.LogInformation("PayPal capture {CaptureId} status {Status} gross {Gross} fee {Fee} net {Net}.",
            result.CaptureId, result.Status, result.GrossAmount, result.PayPalFee, result.NetAmount);
        return result;
    }

    // ---- E. Void --------------------------------------------------------------------------------

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        // Void has no request body and a SUCCESSFUL void returns HTTP 204 No Content. The SDK throws a
        // JsonException trying to deserialize that empty body into PaymentAuthorization — for this operation
        // that is success, not a corrupt response, so we swallow it here instead of using the generic Guard
        // (which would translate every JsonException into a gateway error). Genuine failures still come back
        // as SdkException<VoidPaymentError> (e.g. 422 PREVIOUSLY_VOIDED) and must surface.
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                ct: cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            // Empty 204 body — the void succeeded.
        }
        catch (Exception ex) when (Translate("void", ex) is { } mapped)
        {
            throw mapped;
        }
        catch (SdkException<RawError> ex)
        {
            throw RawFail("void", ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw Unreachable("void", ex);
        }

        _logger.LogInformation("PayPal void completed for authorization {AuthorizationId}.", authorizationId);
    }

    // ---- F. Refund ------------------------------------------------------------------------------

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string requestId, CancellationToken cancellationToken = default)
    {
        // Full refund => null body; partial refund => an amount object.
        var body = amount is null
            ? null
            : new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) } };

        var refund = await Guard("refund", c => _client.Payments.RefundCapturedPayment(
            captureId: captureId,
            payPalMockResponse: null,
            payPalRequestId: requestId,
            payPalAuthAssertion: null,
            body: body,
            prefer: Representation,
            ct: c), cancellationToken);

        decimal refunded = refund.Amount is { } m ? ParseAmount(m.Value) : (amount ?? 0m);
        var result = new RefundResult(refund.Id ?? string.Empty, refund.Status?.Value ?? string.Empty, refunded);

        _logger.LogInformation("PayPal refund {RefundId} status {Status} amount {Amount}.", result.RefundId, result.Status, result.Amount);
        return result;
    }

    // ---- H. Vault a card / delete -----------------------------------------------------------------

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerId, CancellationToken cancellationToken = default)
    {
        // Two-step vault (the documented card path): create a setup token from the raw card, then exchange it
        // for a durable payment token. Saving a card moves no money, so each attempt uses a UNIQUE
        // PayPal-Request-Id — a stable key would let PayPal replay a cached failure (e.g. a 500) on every retry.
        var setupRequest = new SetupTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = customerId },
            PaymentSource = new SetupTokenRequestPaymentSource
            {
                Card = new SetupTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = FormatCardExpiry(card),
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = BuildAddress(card),
                },
            },
        };

        var setupToken = await Guard("create setup token", c => _client.Vault.CreateSetupToken(
            payPalRequestId: Guid.NewGuid().ToString(),
            body: setupRequest,
            ct: c), cancellationToken);

        if (string.IsNullOrEmpty(setupToken.Id))
        {
            throw new PayPalGatewayException("PayPal did not return a setup token id.");
        }

        if (SetupTokenRequiresApproval(setupToken))
        {
            throw new PayPalGatewayException(
                "PayPal requires interactive buyer approval to vault this card, which this integration does not support.");
        }

        _logger.LogInformation("PayPal created setup token {SetupTokenId} status {Status}.", setupToken.Id, setupToken.Status?.Value ?? string.Empty);

        var tokenRequest = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = customerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Token = new VaultTokenRequest
                {
                    Id = setupToken.Id!,
                    Type = VaultTokenRequestType.SetupToken,
                },
            },
        };

        var response = await Guard("vault card", c => _client.Vault.CreatePaymentToken(
            payPalRequestId: Guid.NewGuid().ToString(),
            body: tokenRequest,
            ct: c), cancellationToken);

        if (string.IsNullOrEmpty(response.Id))
        {
            throw new PayPalGatewayException("PayPal did not return a vault token id.");
        }

        var cardEntity = response.PaymentSource?.Card;
        var (expMonth, expYear) = ParseCardExpiry(cardEntity?.Expiry);

        var result = new VaultedCardResult(
            response.Id!,
            cardEntity?.LastDigits,
            cardEntity?.Brand?.Value,
            expMonth,
            expYear);

        _logger.LogInformation("PayPal vaulted card token {VaultId} brand {Brand} last4 {Last4}.",
            result.VaultId, result.Brand ?? string.Empty, result.Last4 ?? string.Empty);
        return result;
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await Guard("delete vaulted card", c => _client.Vault.DeletePaymentToken(id: vaultId, ct: c), cancellationToken);
        _logger.LogInformation("PayPal deleted vault token {VaultId}.", vaultId);
    }

    // ---- I. Transaction search (paged over the whole range) -------------------------------------

    public async Task<IReadOnlyList<ReconciliationTransaction>> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var all = new List<ReconciliationTransaction>();
        string start = from.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        string end = to.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        int page = 1;
        int totalPages;
        do
        {
            int currentPage = page;
            var response = await Guard("search transactions", c => _client.TransactionSearch.SearchTransactions(
                startDate: start,
                endDate: end,
                transactionId: null,
                transactionType: null,
                transactionStatus: null,
                transactionAmount: null,
                transactionCurrency: null,
                paymentInstrumentType: null,
                storeId: null,
                terminalId: null,
                fields: "transaction_info",
                pageSize: 500,
                page: currentPage,
                ct: c), cancellationToken);

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }

                    all.Add(new ReconciliationTransaction(
                        info.TransactionId ?? string.Empty,
                        info.TransactionStatus ?? string.Empty,
                        ParseAmount(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode ?? string.Empty,
                        ParseDate(info.TransactionInitiationDate),
                        info.TransactionEventCode));
                }
            }

            totalPages = response.TotalPages ?? currentPage;
            page = currentPage + 1;
        }
        while (page <= totalPages);

        _logger.LogInformation("PayPal reconciliation returned {Count} transaction(s) across {Pages} page(s).", all.Count, totalPages);
        return all;
    }

    // ---- mapping helpers ------------------------------------------------------------------------

    private static AuthorizationResult MapAuthorization(PaymentAuthorization authorization) =>
        new(string.Empty, authorization.Id ?? string.Empty, authorization.Status?.Value ?? string.Empty, ParseDate(authorization.ExpirationTime));

    private static CardRequest BuildCardRequest(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = FormatCardExpiry(card),
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = BuildAddress(card),
    };

    private static Address? BuildAddress(CardDetails card)
    {
        // Address.CountryCode is required by the SDK model, so only build an address when we have one.
        if (string.IsNullOrWhiteSpace(card.BillingCountryCode))
        {
            return null;
        }

        return new Address
        {
            CountryCode = card.BillingCountryCode!,
            AddressLine1 = card.BillingLine1,
            AddressLine2 = card.BillingLine2,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode,
        };
    }

    // Works for both CreateOrder's Order and AuthorizeOrder's OrderAuthorizeResponse — both expose
    // PurchaseUnits as IReadOnlyList<PurchaseUnit>, whose Payments.Authorizations hold the authorization.
    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null)
        {
            return null;
        }

        foreach (var unit in purchaseUnits)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is null)
            {
                continue;
            }

            foreach (var authorization in authorizations)
            {
                if (!string.IsNullOrEmpty(authorization.Id))
                {
                    return authorization;
                }
            }
        }

        return null;
    }

    private static bool RequiresApproval(Order order)
    {
        if (order.Status is not null && order.Status == OrderStatus.PayerActionRequired)
        {
            return true;
        }

        if (order.Links is not null)
        {
            foreach (var link in order.Links)
            {
                if (link.Rel is "payer-action" or "approve")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SetupTokenRequiresApproval(SetupTokenResponse setupToken)
    {
        if (setupToken.Status is not null && setupToken.Status == PaymentTokenStatus.PayerActionRequired)
        {
            return true;
        }

        if (setupToken.Links is not null)
        {
            foreach (var link in setupToken.Links)
            {
                if (link.Rel is "payer-action" or "approve")
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static string FormatCardExpiry(CardDetails card) => $"{card.ExpiryYear:D4}-{card.ExpiryMonth:D2}";

    private static (int? Month, int? Year) ParseCardExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            return (null, null);
        }

        var parts = expiry.Split('-');
        if (parts.Length == 2 && int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var month))
        {
            return (month, year);
        }

        return (null, null);
    }

    // ---- error boundary -------------------------------------------------------------------------

    private async Task<T> Guard<T>(string operation, Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        try
        {
            return await call(ct);
        }
        catch (System.Text.Json.JsonException ex)
        {
            // A drifted 2xx body, or a non-2xx body that did not match the generated {Operation}Error
            // shape (which replaces the SdkException). Either way, do not let it escape as a raw 500.
            throw Corrupt(operation, ex);
        }
        catch (Exception ex) when (Translate(operation, ex) is { } mapped)
        {
            throw mapped;
        }
        catch (SdkException<RawError> ex)
        {
            throw RawFail(operation, ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw Unreachable(operation, ex);
        }
    }

    private Task Guard(string operation, Func<CancellationToken, Task> call, CancellationToken ct) =>
        Guard<object?>(operation, async c => { await call(c); return null; }, ct);

    /// <summary>
    /// Maps a typed Case-A <c>SdkException&lt;{Operation}Error&gt;</c> to a gateway exception. Returns
    /// null for anything else so the caller's remaining catch clauses handle it. The typed TryGet*
    /// accessors are read here, inside the concrete type, never via a shared ApiError helper.
    /// </summary>
    private static PayPalGatewayException? Translate(string operation, Exception exception)
    {
        switch (exception)
        {
            case SdkException<CreateOrderError> ex:
            {
                if (ex.Error.TryGetError(out var typed))
                {
                    return Rejected(operation, typed, ex.Error.TryGetRawError(out var raw) ? raw : null);
                }
                return RawFail(operation, ex.Error.TryGetRawError(out var fallback) ? fallback : null, ex);
            }
            case SdkException<AuthorizeOrderError> ex:
            {
                if (ex.Error.TryGetError(out var typed))
                {
                    return Rejected(operation, typed, ex.Error.TryGetRawError(out var raw) ? raw : null);
                }
                return RawFail(operation, ex.Error.TryGetRawError(out var fallback) ? fallback : null, ex);
            }
            case SdkException<GetAuthorizedPaymentError> ex:
                return MapPaymentsError(operation, ex,
                    ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError);
            case SdkException<ReauthorizePaymentError> ex:
                return MapPaymentsError(operation, ex,
                    ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError);
            case SdkException<CaptureAuthorizedPaymentError> ex:
                return MapPaymentsError(operation, ex,
                    ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError);
            case SdkException<VoidPaymentError> ex:
                return MapPaymentsError(operation, ex,
                    ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError);
            case SdkException<RefundCapturedPaymentError> ex:
                return MapPaymentsError(operation, ex,
                    ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError);
            case SdkException<CreateSetupTokenError> ex:
            {
                if (ex.Error.TryGetError1(out var typed))
                {
                    return Rejected(operation, typed, ex.Error.TryGetRawError(out var raw) ? raw : null);
                }
                return RawFail(operation, ex.Error.TryGetRawError(out var fallback) ? fallback : null, ex);
            }
            case SdkException<CreatePaymentTokenError> ex:
            {
                if (ex.Error.TryGetError1(out var typed))
                {
                    return Rejected(operation, typed, ex.Error.TryGetRawError(out var raw) ? raw : null);
                }
                return RawFail(operation, ex.Error.TryGetRawError(out var fallback) ? fallback : null, ex);
            }
            case SdkException<DeletePaymentTokenError> ex:
            {
                if (ex.Error.TryGetError1(out var typed))
                {
                    return Rejected(operation, typed, ex.Error.TryGetRawError(out var raw) ? raw : null);
                }
                return RawFail(operation, ex.Error.TryGetRawError(out var fallback) ? fallback : null, ex);
            }
            default:
                return null;
        }
    }

    private delegate bool TryGetTyped(out Error error);
    private delegate bool TryGetRaw(out RawError error);

    private static PayPalGatewayException MapPaymentsError(string operation, Exception source, TryGetTyped tryGetError, TryGetRaw tryGetNoContent, TryGetRaw tryGetRawError)
    {
        if (tryGetError(out var typed))
        {
            return Rejected(operation, typed, tryGetRawError(out var raw) ? raw : null);
        }
        if (tryGetNoContent(out var noContent))
        {
            return RawFail(operation, noContent, source);
        }
        return RawFail(operation, tryGetRawError(out var fallback) ? fallback : null, source);
    }

    private static PayPalGatewayException Rejected(string operation, Error error, RawError? raw) =>
        new($"PayPal {operation} was rejected: {Describe(error.Name, error.Message, DetailText(error.Details))}",
            raw is null ? null : (int)raw.StatusCode, error.DebugId);

    private static PayPalGatewayException Rejected(string operation, Error1 error, RawError? raw) =>
        new($"PayPal {operation} was rejected: {Describe(error.Name, error.Message, DetailText(error.Details))}",
            raw is null ? null : (int)raw.StatusCode, error.DebugId);

    private static PayPalGatewayException RawFail(string operation, RawError? raw, Exception? inner)
    {
        if (raw is null)
        {
            return new PayPalGatewayException($"PayPal {operation} failed.", null, null, inner);
        }

        int status = (int)raw.StatusCode;
        return new PayPalGatewayException($"PayPal {operation} failed with HTTP {status}.", status, null, inner);
    }

    private static PayPalGatewayException Corrupt(string operation, System.Text.Json.JsonException inner) =>
        new($"PayPal {operation} returned a response that could not be processed.", null, null, inner);

    private static PayPalGatewayException Unreachable(string operation, Exception inner) =>
        new($"PayPal {operation} could not be reached.", null, null, inner);

    private static string Describe(string name, string message, string details) =>
        string.IsNullOrEmpty(details) ? $"{name}: {message}" : $"{name}: {message} ({details})";

    private static string DetailText(IReadOnlyList<ErrorDetails>? details) =>
        details is null ? string.Empty
            : string.Join("; ", details.Select(d => d.Description is null ? d.Issue : $"{d.Issue}: {d.Description}"));

    private static string DetailText(IReadOnlyList<ErrorDetails1>? details) =>
        details is null ? string.Empty
            : string.Join("; ", details.Select(d => d.Description is null ? d.Issue : $"{d.Issue}: {d.Description}"));
}
