using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPal;
using PayPal.Core;
using PayPal.Core.ErrorResponse;
using PayPal.Core.Exceptions;
using PayPal.Errors;
using PayPal.Models;
using PayPal.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single boundary to PayPal, implemented over the vendored PayPal .NET SDK. Every SDK call is
/// bounded by a whole-call deadline, and every provider failure is translated to
/// <see cref="PayPalException"/> (never leaking raw SDK/JSON exception detail). Card details are passed
/// straight through and never persisted or logged here.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private const string Representation = "return=representation";
    private const int MaxReconciliationPages = 100;

    private readonly PayPalClient _client;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalClient client, IOptions<PayPalSettings> settings,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<AuthorizeResult> AuthorizeAsync(int orderId, string invoiceId, string paymentReference,
        decimal amount, string currency, CardDetails? card, string? vaultId, CancellationToken ct)
    {
        var paymentSource = new PaymentSource { Card = BuildCard(card, vaultId) };

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = Format(amount) },
                    InvoiceId = invoiceId,
                    CustomId = orderId.ToString(CultureInfo.InvariantCulture)
                }
            },
            PaymentSource = paymentSource
        };

        // 1) Create the PayPal order (intent AUTHORIZE) with the card payment source. Providing the card
        //    here is the single-step flow: PayPal processes and authorizes during creation, so the
        //    authorization comes back on this response (request a representation to receive it). The
        //    PayPal-Request-Id is seeded from the order's payment reference so a double-click is safe.
        var created = await InvokeAsync<Order, CreateOrderError>(
            "CreateOrder",
            token => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"create-{paymentReference}",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: Representation,
                ct: token),
            e => e.TryGetError(out var err) ? err : null,
            ct);

        var payPalOrderId = created.Id
            ?? throw new PayPalException("PayPal did not return an order id when creating the order.");
        RequireNoApproval(created.Status?.Value, "Order creation");

        var authorization = ExtractAuthorization(created.PurchaseUnits);

        // 2) Fallback: if the create step did not already authorize (e.g. the order is only APPROVED),
        //    authorize it explicitly. Request a representation so the authorization details come back.
        if (authorization?.Id is null)
        {
            var authResponse = await InvokeAsync<OrderAuthorizeResponse, AuthorizeOrderError>(
                "AuthorizeOrder",
                token => _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: $"auth-{paymentReference}",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: Representation,
                    ct: token),
                e => e.TryGetError(out var err) ? err : null,
                ct);

            RequireNoApproval(authResponse.Status?.Value, "Order authorization");
            authorization = ExtractAuthorization(authResponse.PurchaseUnits);
        }

        if (authorization?.Id is null)
        {
            throw new PayPalException(
                "PayPal accepted the order but returned no authorization to hold the funds " +
                $"(order status '{created.Status?.Value}').");
        }

        _logger.LogInformation(
            "PayPal authorization {AuthorizationId} created for eShop order {OrderId} (PayPal order {PayPalOrderId}), status {Status}",
            authorization.Id, orderId, payPalOrderId, authorization.Status?.Value);

        return new AuthorizeResult(
            payPalOrderId,
            authorization.Id,
            authorization.Status?.Value ?? "UNKNOWN",
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string invoiceId, decimal amount,
        string currency, CancellationToken ct)
    {
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = Format(amount) },
            InvoiceId = invoiceId,
            FinalCapture = true
        };

        var capture = await InvokeAsync<CapturedPayment, CaptureAuthorizedPaymentError>(
            "CaptureAuthorizedPayment",
            token => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: $"cap-{authorizationId}",
                payPalAuthAssertion: null,
                body: body,
                prefer: Representation,
                ct: token),
            e => e.TryGetError(out var err) ? err : null,
            ct);

        var captureId = capture.Id
            ?? throw new PayPalException("PayPal did not return a capture id when capturing the payment.");

        var breakdown = capture.SellerReceivableBreakdown;
        var gross = ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(capture.Amount) ?? amount;
        var fee = ParseMoney(breakdown?.PaypalFee);
        var net = ParseMoney(breakdown?.NetAmount);

        _logger.LogInformation(
            "PayPal capture {CaptureId} for invoice {InvoiceId}: gross {Gross}, fee {Fee}, net {Net}, status {Status}",
            captureId, invoiceId, gross, fee, net, capture.Status?.Value);

        return new CaptureResult(captureId, capture.Status?.Value ?? "UNKNOWN", gross, fee, net);
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, CancellationToken ct)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = Format(amount) }
        };

        var reauth = await InvokeAsync<PaymentAuthorization, ReauthorizePaymentError>(
            "ReauthorizePayment",
            token => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: $"reauth-{authorizationId}",
                payPalAuthAssertion: null,
                body: body,
                prefer: Representation,
                ct: token),
            e => e.TryGetError(out var err) ? err : null,
            ct);

        var newId = reauth.Id
            ?? throw new PayPalException("PayPal did not return an authorization id when re-authorizing.");

        _logger.LogInformation(
            "PayPal re-authorization {AuthorizationId} (from {OldAuthorizationId}), status {Status}",
            newId, authorizationId, reauth.Status?.Value);

        return new ReauthorizeResult(newId, reauth.Status?.Value ?? "UNKNOWN", ParseDate(reauth.ExpirationTime));
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            await Bounded(token => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: $"void-{authorizationId}",
                ct: token), ct);
        }
        catch (JsonException)
        {
            // A successful void returns 204 No Content; the SDK cannot deserialize the empty body into a
            // PaymentAuthorization and throws. The void itself succeeded — treat this as success.
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw Translate("VoidPayment", err);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"VoidPayment failed: HTTP {(int)raw.StatusCode}",
                    (int)raw.StatusCode, innerException: ex);
            throw new PayPalException("VoidPayment failed with an unrecognised error shape.", innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw new PayPalException($"VoidPayment failed: HTTP {(int)ex.Error.StatusCode}",
                (int)ex.Error.StatusCode, innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            throw new PayPalException("VoidPayment: PayPal was unreachable or timed out.", innerException: ex);
        }

        _logger.LogInformation("PayPal authorization {AuthorizationId} voided", authorizationId);
    }

    public async Task<RefundResult> RefundAsync(string captureId, string invoiceId, decimal? amount,
        string currency, string idempotencyKey, CancellationToken ct)
    {
        var body = new RefundRequest
        {
            Amount = amount.HasValue
                ? new Money { CurrencyCode = currency, Value = Format(amount.Value) }
                : null,
            InvoiceId = invoiceId
        };

        var refund = await InvokeAsync<Refund, RefundCapturedPaymentError>(
            "RefundCapturedPayment",
            token => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: Representation,
                ct: token),
            e => e.TryGetError(out var err) ? err : null,
            ct);

        var refundId = refund.Id
            ?? throw new PayPalException("PayPal did not return a refund id when refunding the payment.");
        var refundedAmount = ParseMoney(refund.Amount) ?? amount ?? 0m;

        _logger.LogInformation(
            "PayPal refund {RefundId} for invoice {InvoiceId}: amount {Amount}, status {Status}",
            refundId, invoiceId, refundedAmount, refund.Status?.Value);

        return new RefundResult(refundId, refund.Status?.Value ?? "UNKNOWN", refundedAmount);
    }

    public async Task<VaultedCardResult> VaultCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        var customer = new Customer { Id = CustomerId(buyerId), MerchantCustomerId = MerchantCustomerId(buyerId) };

        // 1) Setup token holding the raw card.
        var setup = await InvokeAsync<SetupTokenResponse, CreateSetupTokenError>(
            "CreateSetupToken",
            token => _client.Vault.CreateSetupToken(
                payPalRequestId: null,
                body: new SetupTokenRequest
                {
                    Customer = customer,
                    PaymentSource = new SetupTokenRequestPaymentSource
                    {
                        Card = new SetupTokenRequestCard
                        {
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            Name = card.Name,
                            BillingAddress = BuildAddress(card)
                        }
                    }
                },
                ct: token),
            e => e.TryGetError(out var err) ? err : null,
            ct);

        var setupTokenId = setup.Id
            ?? throw new PayPalException("PayPal did not return a setup token id when saving the card.");
        RequireNoApproval(setup.Status?.Value, "Card setup");

        // 2) Exchange the setup token for a permanent payment (vault) token.
        var paymentToken = await InvokeAsync<PaymentTokenResponse, CreatePaymentTokenError>(
            "CreatePaymentToken",
            token => _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: new PaymentTokenRequest
                {
                    Customer = customer,
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest
                        {
                            Id = setupTokenId,
                            Type = VaultTokenRequestType.SetupToken
                        }
                    }
                },
                ct: token),
            e => e.TryGetError(out var err) ? err : null,
            ct);

        var vaultId = paymentToken.Id
            ?? throw new PayPalException("PayPal did not return a vault token id when saving the card.");
        var vaulted = paymentToken.PaymentSource?.Card;

        _logger.LogInformation("PayPal card vaulted for buyer (vault id {VaultId}, brand {Brand})",
            vaultId, vaulted?.Brand?.Value);

        return new VaultedCardResult(vaultId, vaulted?.Brand?.Value, vaulted?.LastDigits, vaulted?.Expiry);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        await InvokeAsync<bool, DeletePaymentTokenError>(
            "DeletePaymentToken",
            async token =>
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, ct: token);
                return true;
            },
            e => e.TryGetError(out var err) ? err : null,
            ct);

        _logger.LogInformation("PayPal vaulted card {VaultId} deleted", vaultId);
    }

    public async Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<ReconciliationTransaction>();
        var startDate = FormatSearchDate(from);
        var endDate = FormatSearchDate(to);

        var page = 1;
        var pages = 1; // updated from the first response
        do
        {
            var currentPage = page;
            SearchResponse response;
            try
            {
                response = await Bounded(token => _client.TransactionSearch.SearchTransactions(
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
                    page: currentPage,
                    ct: token), ct);
            }
            catch (SdkException<RawError> ex) // TransactionSearch is Case B
            {
                throw new PayPalException(
                    $"SearchTransactions failed: HTTP {(int)ex.Error.StatusCode}", (int)ex.Error.StatusCode,
                    innerException: ex);
            }
            catch (JsonException ex)
            {
                throw new PayPalException("SearchTransactions returned a response that could not be processed.",
                    innerException: ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                throw new PayPalException("SearchTransactions: PayPal was unreachable or timed out.",
                    innerException: ex);
            }

            foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info is null) continue;
                results.Add(new ReconciliationTransaction(
                    info.TransactionId,
                    info.TransactionStatus,
                    ParseMoney(info.TransactionAmount),
                    info.TransactionAmount?.CurrencyCode,
                    ParseDate(info.TransactionInitiationDate),
                    info.InvoiceId));
            }

            pages = response.TotalPages ?? 1;
            page++;
        }
        while (page <= pages && page <= MaxReconciliationPages);

        _logger.LogInformation("PayPal transaction search returned {Count} transactions across {Pages} page(s)",
            results.Count, Math.Min(pages, MaxReconciliationPages));

        return results;
    }

    // ---- helpers ----

    /// <summary>Runs an SDK call under the whole-call budget and translates every failure to PayPalException.</summary>
    private async Task<TResult> InvokeAsync<TResult, TError>(
        string operation,
        Func<CancellationToken, Task<TResult>> call,
        Func<TError, Error?> tryGetTypedError,
        CancellationToken ct)
        where TError : ApiError
    {
        try
        {
            return await Bounded(call, ct);
        }
        catch (SdkException<TError> ex)
        {
            var typed = tryGetTypedError(ex.Error);
            if (typed != null) throw Translate(operation, typed);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"{operation} failed: HTTP {(int)raw.StatusCode}",
                    (int)raw.StatusCode, innerException: ex);
            throw new PayPalException($"{operation} failed with an unrecognised error shape.", innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw new PayPalException($"{operation} failed: HTTP {(int)ex.Error.StatusCode}",
                (int)ex.Error.StatusCode, innerException: ex);
        }
        catch (JsonException ex)
        {
            // A drifted 2xx body, or an error body that didn't match the generated error shape.
            throw new PayPalException($"{operation} returned a response that could not be processed.",
                innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) throw; // caller cancelled — propagate cancellation
            throw new PayPalException($"{operation}: PayPal was unreachable or timed out.", innerException: ex);
        }
    }

    private PayPalException Translate(string operation, Error error)
    {
        var issue = error.Details?.FirstOrDefault()?.Issue;
        _logger.LogWarning("PayPal {Operation} error: name={Name} issue={Issue} debug_id={DebugId} message={Message}",
            operation, error.Name, issue, error.DebugId, error.Message);
        return new PayPalException($"{operation} was rejected by PayPal: {error.Message}",
            statusCode: null, issue: issue ?? error.Name, debugId: error.DebugId);
    }

    private async Task<TResult> Bounded<TResult>(Func<CancellationToken, Task<TResult>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSeconds)));
        return await call(cts.Token);
    }

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

    private static void RequireNoApproval(string? status, string what)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalApprovalRequiredException(
                $"{what} requires the shopper to approve the payment in a browser " +
                "(PayPal returned PAYER_ACTION_REQUIRED). This integration does not perform a browser " +
                "approval round-trip; use a card that authorizes without a challenge.");
        }
    }

    private CardRequest BuildCard(CardDetails? card, string? vaultId)
    {
        if (!string.IsNullOrEmpty(vaultId))
            return new CardRequest { VaultId = vaultId };

        if (card is null)
            throw new PayPalException("A card or a saved card must be supplied to authorize the payment.");

        return new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = BuildAddress(card)
        };
    }

    private static Address? BuildAddress(CardDetails card)
    {
        var hasAddress = !string.IsNullOrWhiteSpace(card.BillingAddressLine1)
            || !string.IsNullOrWhiteSpace(card.BillingCity)
            || !string.IsNullOrWhiteSpace(card.BillingPostalCode)
            || !string.IsNullOrWhiteSpace(card.BillingCountryCode);
        if (!hasAddress) return null;

        return new Address
        {
            AddressLine1 = card.BillingAddressLine1,
            AddressLine2 = card.BillingAddressLine2,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode,
            CountryCode = (card.BillingCountryCode ?? "US").ToUpperInvariant()
        };
    }

    private static string Format(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money?.Value is { } v && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : (decimal?)null;

    private static DateTimeOffset? ParseDate(string? value) =>
        !string.IsNullOrEmpty(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : (DateTimeOffset?)null;

    /// <summary>Deterministic PayPal customer id (≤22 chars, [0-9a-zA-Z_-]) derived from the shopper.</summary>
    private static string CustomerId(string buyerId)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return hex.Substring(0, 20);
    }

    private static string MerchantCustomerId(string buyerId) =>
        buyerId.Length <= 64 ? buyerId : buyerId.Substring(0, 64);
}
