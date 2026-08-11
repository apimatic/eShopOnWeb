using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using JsonException = System.Text.Json.JsonException;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of <see cref="IPayPalPaymentGateway"/> over the AsadAli.Checkout.Sdk
/// (PayPalServerSdk). Direct-card flow: create an order with intent=AUTHORIZE carrying the card, then
/// authorize it to place a hold — drivable without a browser. Every SDK / transport failure is translated
/// into a <see cref="PaymentGatewayException"/> so no SDK type leaks past this boundary.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private const string Representation = "return=representation";

    // PayPal transaction search bounds each request to a limited window; page each sub-window so an
    // arbitrarily long reconciliation range is still covered in full.
    private static readonly TimeSpan SearchWindow = TimeSpan.FromDays(30);

    // A per-process scope so that invoice ids and PayPal-Request-Id idempotency keys are unique across
    // runs (the sandbox merchant account rejects a reused invoice_id and replays a reused request id),
    // while remaining stable within a single run so a double-click is still idempotent.
    private static readonly string RunScope = Guid.NewGuid().ToString("N").Substring(0, 12);

    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IAppLogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string orderReference, string idempotencyKey, CancellationToken ct = default)
    {
        var source = new PaymentSource { Card = BuildCard(card) };
        return await CreateAndAuthorizeAsync(amount, currency, source, orderReference, idempotencyKey, ct);
    }

    public async Task<AuthorizationResult> AuthorizeWithVaultAsync(decimal amount, string currency, string vaultId,
        string orderReference, string idempotencyKey, CancellationToken ct = default)
    {
        var source = new PaymentSource { Card = new CardRequest { VaultId = vaultId } };
        return await CreateAndAuthorizeAsync(amount, currency, source, orderReference, idempotencyKey, ct);
    }

    private async Task<AuthorizationResult> CreateAndAuthorizeAsync(decimal amount, string currency,
        PaymentSource source, string orderReference, string idempotencyKey, CancellationToken ct)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PaymentSource = source,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = orderReference,
                    // invoice_id must be unique per merchant account; custom_id carries the plain order id
                    // so reconciliation can line PayPal's records up against the eShop order.
                    InvoiceId = $"{RunScope}-{orderReference}",
                    CustomId = orderReference,
                    Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = Format(amount) }
                }
            }
        };

        Order order;
        try
        {
            order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: Key(idempotencyKey + "-create"),
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: Representation,
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw Build("create the order", typed?.Name, typed?.Message, typed?.Details?.Select(d => d.Issue), raw, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (JsonException ex) { throw BadBody(ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }

        EnsureNoChallenge(order.Status, order.Links, "creating the payment");

        var payPalOrderId = order.Id
            ?? throw new PaymentGatewayException("PayPal did not return an order id.", 502);

        // With a direct card and intent=AUTHORIZE, the card is processed at create time, so the hold
        // already exists on the create response. Only when it does not (e.g. an approval-based flow) do
        // we place the hold with a separate AuthorizeOrder call.
        var authorization = ExtractAuthorization(order.PurchaseUnits);

        if (authorization?.Id is null)
        {
            OrderAuthorizeResponse authorized;
            try
            {
                authorized = await _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: Key(idempotencyKey + "-auth"),
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: Representation,
                    ct: ct);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                ex.Error.TryGetError(out var typed);
                ex.Error.TryGetRawError(out var raw);
                throw Build("authorize the payment", typed?.Name, typed?.Message, typed?.Details?.Select(d => d.Issue), raw, ex);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (JsonException ex) { throw BadBody(ex); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }

            EnsureNoChallenge(authorized.Status, authorized.Links, "authorizing the payment");
            authorization = ExtractAuthorization(authorized.PurchaseUnits);
        }

        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException(
                "PayPal accepted the order but returned no authorization to hold the funds.", 502);
        }

        return new AuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authorization.Id,
            Status = authorization.Status?.Value ?? string.Empty,
            ExpiresAt = ParseDate(authorization.ExpirationTime)
        };
    }

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .Select(pu => pu.Payments)
            .Where(p => p?.Authorizations != null)
            .SelectMany(p => p!.Authorizations!)
            .FirstOrDefault(a => !string.IsNullOrEmpty(a.Id));

    public async Task<AuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        PaymentAuthorization reauth;
        try
        {
            reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: Key(idempotencyKey),
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount) } },
                prefer: Representation,
                ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw Build("renew the authorization", typed?.Name, typed?.Message, typed?.Details?.Select(d => d.Issue), raw, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (JsonException ex) { throw BadBody(ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }

        return new AuthorizationState
        {
            AuthorizationId = reauth.Id ?? authorizationId,
            Status = reauth.Status?.Value ?? string.Empty,
            ExpiresAt = ParseDate(reauth.ExpirationTime)
        };
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: Key(idempotencyKey),
                payPalAuthAssertion: null,
                body: null,
                prefer: Representation,
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw Build("capture the payment", typed?.Name, typed?.Message, typed?.Details?.Select(d => d.Issue), raw, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (JsonException ex) { throw BadBody(ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }

        var breakdown = captured.SellerReceivableBreakdown;
        var gross = ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(captured.Amount) ?? 0m;
        var fee = ParseMoney(breakdown?.PaypalFee) ?? 0m;
        var net = ParseMoney(breakdown?.NetAmount) ?? gross - fee;
        var currency = breakdown?.GrossAmount?.CurrencyCode ?? captured.Amount?.CurrencyCode ?? string.Empty;

        return new CaptureResult
        {
            CaptureId = captured.Id ?? throw new PaymentGatewayException("PayPal captured the payment but returned no capture id.", 502),
            Status = captured.Status?.Value ?? string.Empty,
            Gross = gross,
            Fee = fee,
            Net = net,
            Currency = currency
        };
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            // NOTE: payPalRequestId is the 4th parameter on VoidPayment (after payPalAuthAssertion).
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: Key(idempotencyKey),
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw Build("release the authorization", typed?.Name, typed?.Message, typed?.Details?.Select(d => d.Issue), raw, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (JsonException)
        {
            // A successful void returns HTTP 204 No Content; the SDK models the return as a body and so
            // throws while deserializing the empty payload. An actual void failure surfaces as a typed
            // VoidPaymentError above, so reaching here means the hold was released successfully.
            _logger.LogInformation("Void succeeded (PayPal returned no content).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        var body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount.Value) } }
            : null; // full refund

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: Key(idempotencyKey),
                payPalAuthAssertion: null,
                body: body,
                prefer: Representation,
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw Build("refund the payment", typed?.Name, typed?.Message, typed?.Details?.Select(d => d.Issue), raw, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (JsonException ex) { throw BadBody(ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }

        return new RefundResult
        {
            RefundId = refund.Id ?? throw new PaymentGatewayException("PayPal accepted the refund but returned no refund id.", 502),
            Status = refund.Status?.Value ?? string.Empty,
            Amount = ParseMoney(refund.Amount) ?? amount ?? 0m,
            Currency = refund.Amount?.CurrencyCode ?? currency
        };
    }

    public async Task<VaultResult> VaultCardAsync(CardDetails card, string customerId, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = customerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card)
                }
            }
        };

        PaymentTokenResponse token;
        try
        {
            token = await _client.Vault.CreatePaymentToken(payPalRequestId: Key(idempotencyKey), body: body, ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            ex.Error.TryGetError1(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw Build("save the card", typed?.Name, typed?.Message, typed?.Details?.Select(d => d.Issue), raw, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (JsonException ex) { throw BadBody(ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }

        var entity = token.PaymentSource?.Card;
        var (month, year) = SplitExpiry(entity?.Expiry);

        return new VaultResult
        {
            VaultId = token.Id ?? throw new PaymentGatewayException("PayPal vaulted the card but returned no vault id.", 502),
            Brand = entity?.Brand?.Value,
            Last4 = entity?.LastDigits,
            ExpiryMonth = month,
            ExpiryYear = year
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            ex.Error.TryGetError1(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw Build("delete the saved card", typed?.Name, typed?.Message, typed?.Details?.Select(d => d.Issue), raw, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (JsonException ex) { throw BadBody(ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<GatewayTransaction>();

        for (var windowStart = from; windowStart < to; windowStart += SearchWindow)
        {
            var windowEnd = windowStart + SearchWindow;
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            int totalPages;
            do
            {
                SearchResponse resp;
                try
                {
                    resp = await _client.TransactionSearch.SearchTransactions(
                        startDate: FormatSearchDate(windowStart),
                        endDate: FormatSearchDate(windowEnd),
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
                catch (SdkException<RawError> ex) // Case B: SearchTransactions has no typed error model.
                {
                    var raw = ex.Error;
                    throw new PaymentGatewayException("PayPal transaction search failed.", (int)raw.StatusCode, inner: ex);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (JsonException ex) { throw BadBody(ex); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Transport(ex); }

                totalPages = resp.TotalPages ?? 1;

                if (resp.TransactionDetails != null)
                {
                    foreach (var detail in resp.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info == null) continue;

                        results.Add(new GatewayTransaction
                        {
                            TransactionId = info.TransactionId ?? string.Empty,
                            Status = info.TransactionStatus ?? string.Empty,
                            Amount = ParseMoney(info.TransactionAmount) ?? 0m,
                            Currency = info.TransactionAmount?.CurrencyCode ?? string.Empty,
                            InitiationDate = ParseDate(info.TransactionInitiationDate),
                            InvoiceId = info.InvoiceId,
                            CustomId = info.CustomField
                        });
                    }
                }

                page++;
            }
            while (page <= totalPages);
        }

        return results;
    }

    // --- helpers ---

    private static CardRequest BuildCard(CardDetails card) => new()
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = BuildAddress(card)
    };

    private static Address? BuildAddress(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.BillingCountryCode))
        {
            return null; // billing address is optional; omit when we have no country code (required field)
        }

        return new Address
        {
            AddressLine1 = card.BillingAddressLine1,
            AddressLine2 = card.BillingAddressLine2,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode,
            CountryCode = card.BillingCountryCode!
        };
    }

    private static void EnsureNoChallenge(OrderStatus? status, IReadOnlyList<LinkDescription>? links, string action)
    {
        if (status != null && status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentApprovalRequiredException(
                $"PayPal requires the shopper to approve this payment in a browser while {action} " +
                "(PAYER_ACTION_REQUIRED). This integration is direct-card only and does not perform a browser approval round-trip.");
        }
    }

    // Scope a caller idempotency key to this process run so it never collides with another run on the
    // same sandbox merchant account, while staying stable within the run.
    private static string Key(string idempotencyKey) => $"{RunScope}-{idempotencyKey}";

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money != null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d
            : null;

    private static (string? Month, string? Year) SplitExpiry(string? expiry)
    {
        // PayPal returns card expiry as "YYYY-MM".
        if (string.IsNullOrWhiteSpace(expiry)) return (null, null);
        var parts = expiry.Split('-');
        return parts.Length == 2 ? (parts[1], parts[0]) : (null, null);
    }

    private PaymentGatewayException Build(string action, string? name, string? message,
        IEnumerable<string?>? rawIssues, RawError? raw, Exception inner)
    {
        var issues = rawIssues?
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i!)
            .ToList() ?? new List<string>();

        int? status = raw != null ? (int)raw.StatusCode : null;

        var text = string.IsNullOrWhiteSpace(message) ? name : message;
        var issueSuffix = issues.Count > 0 ? $" (issues: {string.Join(", ", issues)})" : string.Empty;
        var caller = $"PayPal could not {action}: {text}{issueSuffix}";

        _logger.LogWarning($"PayPal error while trying to {action}. Status={status}, Name={name}, Issues={string.Join(",", issues)}");

        return new PaymentGatewayException(caller, status, issues, inner);
    }

    private PaymentGatewayException BadBody(JsonException ex)
    {
        _logger.LogWarning($"PayPal returned an unprocessable response body: {ex.Message}");
        return new PaymentGatewayException("PayPal returned a response that could not be processed.", 502, inner: ex);
    }

    private PaymentGatewayException Transport(Exception ex)
    {
        _logger.LogWarning($"PayPal was unreachable: {ex.Message}");
        return new PaymentGatewayException("PayPal is currently unreachable. Please try again.", 503, inner: ex);
    }
}
