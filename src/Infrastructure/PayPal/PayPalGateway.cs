using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal Server SDK implementation of <see cref="IPayPalGateway"/>. Maps domain requests onto SDK calls
/// and every SDK failure onto <see cref="PaymentGatewayException"/>. Full card details flow only into SDK
/// request models; they are never persisted or logged here.
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, IOptions<PayPalSettings> settings, ILogger<PayPalGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string Currency => _settings.Currency;

    // ----------------------------------------------------------------------------------------------------
    // Authorize (place the hold)
    // ----------------------------------------------------------------------------------------------------
    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeRequest request, CancellationToken ct)
    {
        if (request.Card is null && string.IsNullOrWhiteSpace(request.VaultId))
        {
            throw new PaymentOperationException("A card or a saved-card id is required to pay.");
        }

        var currency = _settings.Currency;
        var amountValue = PayPalMoney.Format(request.Amount, currency);

        var card = request.VaultId is not null
            ? new CardRequest { VaultId = request.VaultId }
            : new CardRequest
            {
                Number = request.Card!.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                Name = request.Card.CardHolderName,
                BillingAddress = MapBillingAddress(request.Card.BillingAddress),
            };

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = amountValue },
                    CustomId = request.CustomId,
                    InvoiceId = request.InvoiceId,
                    Description = request.Description,
                },
            },
            PaymentSource = new PaymentSource { Card = card },
        };

        try
        {
            var created = await Invoke(
                token => _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: $"create-{request.RequestIdSeed}",
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: orderRequest,
                    prefer: "return=representation",
                    ct: token),
                "create the PayPal order", ct);

            GuardNoChallenge(created.Status, "authorize the payment");

            var auth = FirstAuthorization(created.PurchaseUnits);
            var payPalOrderId = created.Id
                ?? throw new PaymentGatewayException("PayPal did not return an order id.");

            if (auth is null)
            {
                var authResp = await Invoke(
                    token => _client.Orders.AuthorizeOrder(
                        id: payPalOrderId,
                        payPalMockResponse: null,
                        payPalRequestId: $"authorize-{request.RequestIdSeed}",
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: null,
                        prefer: "return=representation",
                        ct: token),
                    "authorize the PayPal order", ct);

                GuardNoChallenge(authResp.Status, "authorize the payment");
                auth = FirstAuthorization(authResp.PurchaseUnits);
            }

            if (auth?.Id is null)
            {
                throw new PaymentGatewayException(
                    "PayPal did not create an authorization; the card may have been declined.");
            }

            var authorizedAmount = PayPalMoney.Parse(auth.Amount?.Value) ?? request.Amount;
            var expiresAt = ParseDate(auth.ExpirationTime);

            _logger.LogInformation(
                "PayPal authorized order {PayPalOrderId} → authorization {AuthorizationId} ({Status}) for {Amount} {Currency}.",
                payPalOrderId, auth.Id, auth.Status?.Value, amountValue, currency);

            return new AuthorizationResult(
                payPalOrderId, auth.Id, auth.Status?.Value ?? "CREATED", authorizedAmount, currency, expiresAt);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw FromCreateOrderError(ex);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw Build("authorize the PayPal order", typed, raw, ex);
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Capture (take the money at fulfilment)
    // ----------------------------------------------------------------------------------------------------
    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        var currency = _settings.Currency;
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = PayPalMoney.Format(amount, currency) },
            FinalCapture = true,
        };

        try
        {
            var cap = await Invoke(
                token => _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: token),
                "capture the payment", ct);

            var breakdown = cap.SellerReceivableBreakdown;
            var captured = PayPalMoney.Parse(cap.Amount?.Value) ?? amount;
            var fee = PayPalMoney.Parse(breakdown?.PaypalFee?.Value);
            var net = PayPalMoney.Parse(breakdown?.NetAmount?.Value);

            _logger.LogInformation(
                "PayPal captured authorization {AuthorizationId} → capture {CaptureId} ({Status}); gross {Gross}, fee {Fee}, net {Net} {Currency}.",
                authorizationId, cap.Id, cap.Status?.Value, captured, fee, net, currency);

            return new CaptureResult(
                cap.Id ?? throw new PaymentGatewayException("PayPal did not return a capture id."),
                cap.Status?.Value ?? "COMPLETED", captured, fee, net, currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetNoContent(out var noContent);
            ex.Error.TryGetRawError(out var raw);
            var built = Build("capture the payment", typed, raw ?? noContent, ex);
            var issue = FirstIssue(typed);
            if (IsExpiredAuthorization(issue, typed))
            {
                throw new PaymentGatewayException(built.Message, built.StatusCode, built.DebugId, ex)
                {
                    AuthorizationExpired = true,
                    Issue = issue,
                };
            }
            throw built;
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Reauthorize (renew a stale hold before fulfilment)
    // ----------------------------------------------------------------------------------------------------
    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        var currency = _settings.Currency;
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = PayPalMoney.Format(amount, currency) },
        };

        try
        {
            var reauth = await Invoke(
                token => _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: token),
                "re-authorize the payment", ct);

            var newId = reauth.Id ?? throw new PaymentGatewayException("PayPal did not return a re-authorization id.");
            _logger.LogInformation(
                "PayPal re-authorized {OldAuthorizationId} → {NewAuthorizationId} ({Status}).",
                authorizationId, newId, reauth.Status?.Value);

            return new ReauthorizeResult(
                newId, reauth.Status?.Value ?? "CREATED",
                PayPalMoney.Parse(reauth.Amount?.Value) ?? amount, currency, ParseDate(reauth.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetNoContent(out var noContent);
            ex.Error.TryGetRawError(out var raw);
            var built = Build("re-authorize the payment", typed, raw ?? noContent, ex);
            // The reauthorization attempt failed — from fulfilment's perspective the hold can no longer be
            // renewed and needs operator action (e.g. re-collect payment from the shopper).
            throw new PaymentGatewayException(built.Message, built.StatusCode, built.DebugId, ex)
            {
                AuthorizationUnrenewable = true,
                Issue = FirstIssue(typed),
            };
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Void (release the hold on cancel)
    // ----------------------------------------------------------------------------------------------------
    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            await Invoke(
                token => _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey,
                    prefer: "return=representation",
                    ct: token),
                "void the authorization", ct);

            _logger.LogInformation("PayPal voided authorization {AuthorizationId}.", authorizationId);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetNoContent(out var noContent);
            ex.Error.TryGetRawError(out var raw);
            throw Build("void the authorization", typed, raw ?? noContent, ex);
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Refund (return money after fulfilment)
    // ----------------------------------------------------------------------------------------------------
    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        var currency = _settings.Currency;
        var body = new RefundRequest
        {
            Amount = amount.HasValue
                ? new Money { CurrencyCode = currency, Value = PayPalMoney.Format(amount.Value, currency) }
                : null,
            NoteToPayer = "Refund for your eShopOnWeb order.",
        };

        try
        {
            var refund = await Invoke(
                token => _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: token),
                "refund the payment", ct);

            var refundedAmount = PayPalMoney.Parse(refund.Amount?.Value) ?? amount ?? 0m;
            _logger.LogInformation(
                "PayPal refunded capture {CaptureId} → refund {RefundId} ({Status}) for {Amount} {Currency}.",
                captureId, refund.Id, refund.Status?.Value, refundedAmount, currency);

            return new RefundResult(
                refund.Id ?? throw new PaymentGatewayException("PayPal did not return a refund id."),
                refund.Status?.Value ?? "COMPLETED", refundedAmount, currency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetNoContent(out var noContent);
            ex.Error.TryGetRawError(out var raw);
            throw Build("refund the payment", typed, raw ?? noContent, ex);
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Vault a card (save) — setup token → payment token
    // ----------------------------------------------------------------------------------------------------
    public async Task<VaultedCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken ct)
    {
        var card = request.Card;
        var setupBody = new SetupTokenRequest
        {
            PaymentSource = new SetupTokenRequestPaymentSource
            {
                Card = new SetupTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardHolderName,
                    BillingAddress = MapBillingAddress(card.BillingAddress),
                },
            },
        };

        SetupTokenResponse setup;
        try
        {
            setup = await Invoke(
                token => _client.Vault.CreateSetupToken(payPalRequestId: null, body: setupBody, ct: token),
                "create the setup token", ct);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw Build("save the card (setup token)", typed, raw, ex);
        }

        if (setup.Status == PaymentTokenStatus.PayerActionRequired)
        {
            throw new PaymentGatewayException(
                "PayPal requires the shopper to approve saving this card in a browser (a challenge such as 3-D Secure). " +
                "This app does not perform browser approval; saving cannot proceed unattended.");
        }

        var setupId = setup.Id ?? throw new PaymentGatewayException("PayPal did not return a setup token id.");

        var tokenBody = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = SanitizeCustomerId(request.MerchantCustomerId) },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Token = new VaultTokenRequest { Id = setupId, Type = VaultTokenRequestType.SetupToken },
            },
        };

        try
        {
            var tokenResp = await Invoke(
                token => _client.Vault.CreatePaymentToken(payPalRequestId: null, body: tokenBody, ct: token),
                "save the card (payment token)", ct);

            var vaultId = tokenResp.Id ?? throw new PaymentGatewayException("PayPal did not return a vault payment-token id.");
            var cardEntity = tokenResp.PaymentSource?.Card;

            _logger.LogInformation("PayPal vaulted a card → token {VaultId} ({Brand} ****{Last4}).",
                vaultId, cardEntity?.Brand?.Value, cardEntity?.LastDigits);

            return new VaultedCardResult(
                vaultId,
                cardEntity?.Brand?.Value,
                cardEntity?.LastDigits,
                cardEntity?.Expiry,
                cardEntity?.Name,
                tokenResp.Customer?.Id);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw Build("save the card (payment token)", typed, raw, ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await InvokeVoid(
                token => _client.Vault.DeletePaymentToken(id: vaultId, ct: token),
                "delete the saved card", ct);
            _logger.LogInformation("PayPal deleted vault token {VaultId}.", vaultId);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            ex.Error.TryGetError(out var typed);
            ex.Error.TryGetRawError(out var raw);
            throw Build("delete the saved card", typed, raw, ex);
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Reconciliation — list PayPal's own transactions across the whole range (all pages, windowed)
    // ----------------------------------------------------------------------------------------------------
    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<PayPalTransaction>();
        // Walk the range in <=30-day windows so a long range still returns the whole range (PayPal's
        // transaction search bounds a single query's window); each window is fully paged.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(30);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await SearchWindowAsync(windowStart, windowEnd, results, ct);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task SearchWindowAsync(DateTimeOffset from, DateTimeOffset to, List<PayPalTransaction> sink, CancellationToken ct)
    {
        var start = FormatSearchDate(from);
        var end = FormatSearchDate(to);
        const int MaxPages = 1000; // safety bound — never trust the provider's own stop condition alone
        var page = 1;
        int totalPages;

        do
        {
            SearchResponse resp;
            try
            {
                resp = await Invoke(
                    token => _client.TransactionSearch.SearchTransactions(
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
                        balanceAffectingRecordsOnly: "Y",
                        pageSize: 100,
                        page: page,
                        ct: token),
                    "search transactions", ct);
            }
            catch (SdkException<RawError> ex)
            {
                // TransactionSearch is Case B — RawError carries the status directly.
                string body;
                try { body = ex.Error.ReadAsString(); } catch { body = "<unreadable>"; }

                // PayPal's transaction reporting lags live activity: a window whose start date is too recent
                // has no reportable data yet, which PayPal signals as 404 "Data for the given start date is
                // not available." That is the documented lag, not a failure — treat the window as empty.
                if (ex.Error.StatusCode == System.Net.HttpStatusCode.NotFound &&
                    body.IndexOf("start date is not available", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _logger.LogWarning(
                        "Transaction search window {Start}..{End} has no reportable data yet (PayPal reporting lag); treating as empty.",
                        start, end);
                    return;
                }

                throw new PaymentGatewayException(
                    $"PayPal could not search transactions (HTTP {(int)ex.Error.StatusCode}): {Truncate(body, 300)}",
                    ex.Error.StatusCode, null, ex);
            }

            totalPages = resp.TotalPages ?? 1;
            if (resp.TransactionDetails is not null)
            {
                foreach (var detail in resp.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }
                    sink.Add(new PayPalTransaction(
                        info.TransactionId,
                        info.TransactionStatus,
                        PayPalMoney.Parse(info.TransactionAmount?.Value),
                        PayPalMoney.Parse(info.FeeAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        info.InvoiceId,
                        info.CustomField,
                        ParseDate(info.TransactionInitiationDate),
                        info.TransactionEventCode));
                }
            }

            page++;
        }
        while (page <= totalPages && page <= MaxPages);

        if (totalPages > MaxPages)
        {
            _logger.LogWarning(
                "Transaction search window {Start}..{End} reported {TotalPages} pages; capped at {MaxPages}. Results may be truncated.",
                start, end, totalPages, MaxPages);
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------------------

    /// <summary>Runs an SDK call under a total-call deadline and converts non-SDK failures to the gateway type.
    /// SDK typed exceptions propagate to the per-operation catch that knows their accessors.</summary>
    private async Task<T> Invoke<T>(Func<CancellationToken, Task<T>> call, string operation, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
        try
        {
            return await call(cts.Token);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException(
                $"PayPal returned a response that could not be processed while trying to {operation}.", null, null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PaymentGatewayException($"PayPal was unreachable while trying to {operation}.", null, null, ex);
        }
        catch (OperationCanceledException ex) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new PaymentGatewayException(
                $"The PayPal request to {operation} timed out after {_settings.TimeoutSeconds}s.", null, null, ex);
        }
    }

    private async Task InvokeVoid(Func<CancellationToken, Task> call, string operation, CancellationToken ct)
    {
        await Invoke<bool>(async token => { await call(token); return true; }, operation, ct);
    }

    private static Address? MapBillingAddress(CardBillingAddress? address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.CountryCode))
        {
            return null;
        }
        return new Address
        {
            CountryCode = address.CountryCode!,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode,
        };
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? units)
    {
        if (units is null)
        {
            return null;
        }
        foreach (var unit in units)
        {
            var auths = unit.Payments?.Authorizations;
            if (auths is { Count: > 0 })
            {
                return auths[0];
            }
        }
        return null;
    }

    /// <summary>If PayPal signals the payer must approve in a browser (e.g. 3-D Secure), stop — this app
    /// does not build a browser approval round-trip.</summary>
    private static void GuardNoChallenge(OrderStatus? status, string action)
    {
        if (status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentGatewayException(
                $"PayPal requires the shopper to approve this payment in a browser (a challenge such as 3-D Secure) before it can {action}. " +
                "This app does not perform browser approval.");
        }
    }

    private static string? FirstIssue(Error? typed) =>
        typed?.Details is { Count: > 0 } ? typed.Details[0].Issue : null;

    private static bool IsExpiredAuthorization(string? issue, Error? typed)
    {
        if (!string.IsNullOrEmpty(issue) && issue!.IndexOf("EXPIRED", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
        var name = typed?.Name;
        return !string.IsNullOrEmpty(name) && name!.IndexOf("EXPIRED", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static PaymentGatewayException FromCreateOrderError(SdkException<CreateOrderError> ex)
    {
        ex.Error.TryGetError(out var typed);
        ex.Error.TryGetRawError(out var raw);
        return Build("create the PayPal order", typed, raw, ex);
    }

    /// <summary>Builds the caller-safe gateway exception from a typed <see cref="Error"/> body (identity
    /// fields: name, message, debug_id, first issue) or a raw fallback, never leaking SDK/JSON internals.</summary>
    private static PaymentGatewayException Build(string operation, Error? typed, RawError? raw, Exception inner)
    {
        if (typed is not null)
        {
            var issue = FirstIssue(typed);
            var description = typed.Details is { Count: > 0 } ? typed.Details[0].Description : null;
            var detail = issue is null ? string.Empty : $" (issue: {issue}{(description is null ? string.Empty : $" — {description}")})";
            var message = $"PayPal could not {operation}: {typed.Name} — {typed.Message}{detail}";
            return new PaymentGatewayException(message, raw?.StatusCode, typed.DebugId, inner) { Issue = issue };
        }
        if (raw is not null)
        {
            return new PaymentGatewayException(
                $"PayPal could not {operation} (HTTP {(int)raw.StatusCode}).", raw.StatusCode, null, inner);
        }
        return new PaymentGatewayException($"PayPal could not {operation}.", null, null, inner);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static string SanitizeCustomerId(string buyerId)
    {
        var builder = new StringBuilder(buyerId.Length);
        foreach (var ch in buyerId)
        {
            var allowed = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')
                || ch is '-' or '_' or '.' or '^' or '*' or '$' or '@' or '#';
            builder.Append(allowed ? ch : '_');
        }
        var sanitized = builder.ToString();
        return sanitized.Length > 64 ? sanitized.Substring(0, 64) : sanitized;
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);
}
