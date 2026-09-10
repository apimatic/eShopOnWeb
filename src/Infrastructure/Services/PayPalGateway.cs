using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.Hooks;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The one place that talks to the PayPal SDK. Maps SDK models onto the application's SDK-free gateway
/// DTOs and translates every SDK, JSON and transport failure into a
/// <see cref="PaymentGatewayException"/> (or <see cref="PaymentApprovalRequiredException"/> for a browser
/// challenge). Card details reaching this class are used only to build the outgoing request; nothing here
/// persists a PAN/CVC or logs a request body.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;

    public PayPalGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options)
    {
        _client = client;
        _currency = options.Value.Currency;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeCommand command, CancellationToken ct)
    {
        var card = command.VaultId is not null
            ? new CardRequest { VaultId = command.VaultId }
            : BuildCardRequest(command.Card!);

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = _currency,
                        Value = Money2(command.Amount)
                    },
                    InvoiceId = command.InvoiceId,
                    CustomId = command.CustomId,
                    Description = command.Description
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: command.IdempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            var auth = FirstAuthorization(order.PurchaseUnits);

            // If a card-bearing create did not itself authorize but the order is approved, authorize it.
            if (auth is null && order.Status == OrderStatus.Approved && order.Id is not null)
            {
                var authResp = await _client.Orders.AuthorizeOrder(
                    order.Id, null, command.IdempotencyKey + "-authorize", null, null,
                    body: null, prefer: "return=representation", ct: ct);
                auth = FirstAuthorization(authResp.PurchaseUnits);
            }

            if (auth is null)
            {
                if (HasApprovalLink(order.Links))
                {
                    throw new PaymentApprovalRequiredException(
                        "PayPal requires the shopper to approve this card payment in a browser " +
                        "(e.g. 3-D Secure). This browser-free integration cannot complete it.");
                }
                throw new PaymentGatewayException(
                    $"PayPal did not return an authorization for the order (status '{order.Status?.Value}').");
            }

            var description = DescribeCard(order.PaymentSource?.Card, command.Card);
            return new PayPalAuthorizationResult(
                order.Id ?? string.Empty,
                auth.Id ?? throw new PaymentGatewayException("PayPal authorization is missing an id."),
                auth.Status?.Value ?? string.Empty,
                ParseTime(auth.ExpirationTime),
                description);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw FromCaseA(ex.Error.TryGetError(out var e) ? e : null, ex.Error.TryGetRawError(out var r) ? r : null, "authorize the order", ex);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw FromCaseA(ex.Error.TryGetError(out var e) ? e : null, ex.Error.TryGetRawError(out var r) ? r : null, "authorize the order", ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw FromTransport(ex, "authorize the order");
        }
    }

    public async Task<PayPalAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var pa = await _client.Payments.GetAuthorizedPayment(authorizationId, null, null, ct: ct);
            return new PayPalAuthorizationInfo(pa.Status?.Value ?? string.Empty, ParseTime(pa.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw FromCaseA(ex.Error.TryGetError(out var e) ? e : null, ex.Error.TryGetRawError(out var r) ? r : null, "read the authorization", ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw FromTransport(ex, "read the authorization");
        }
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken ct)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = _currency, Value = Money2(amount) }
        };
        try
        {
            var pa = await _client.Payments.ReauthorizePayment(
                authorizationId, payPalRequestId: idempotencyKey, payPalAuthAssertion: null,
                body: body, prefer: "return=representation", ct: ct);
            return new PayPalAuthorizationResult(
                string.Empty,
                pa.Id ?? throw new PaymentGatewayException("PayPal reauthorization is missing an id."),
                pa.Status?.Value ?? string.Empty,
                ParseTime(pa.ExpirationTime),
                null);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw FromCaseA(ex.Error.TryGetError(out var e) ? e : null, ex.Error.TryGetRawError(out var r) ? r : null, "renew the authorization", ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw FromTransport(ex, "renew the authorization");
        }
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken ct)
    {
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = _currency, Value = Money2(amount) },
            FinalCapture = true
        };
        try
        {
            var cap = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId, payPalMockResponse: null, payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null, body: body, prefer: "return=representation", ct: ct);

            var srb = cap.SellerReceivableBreakdown;
            return new PayPalCaptureResult(
                cap.Id ?? throw new PaymentGatewayException("PayPal capture is missing an id."),
                cap.Status?.Value ?? string.Empty,
                ParseMoney(srb?.GrossAmount) ?? amount,
                ParseMoney(srb?.PaypalFee),
                ParseMoney(srb?.NetAmount),
                srb?.GrossAmount?.CurrencyCode ?? _currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw FromCaseA(ex.Error.TryGetError(out var e) ? e : null, ex.Error.TryGetRawError(out var r) ? r : null, "capture the payment", ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw FromTransport(ex, "capture the payment");
        }
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        // A successful void returns HTTP 204 with no body, but the SDK types the operation as returning
        // PaymentAuthorization and throws JsonException trying to deserialize the empty body. Capture the
        // transport status via a response hook so a 2xx-with-empty-body is recognised as success rather
        // than mistaken for an unprocessable response.
        System.Net.HttpStatusCode? status = null;
        var options = new RequestOptions
        {
            Hooks = new List<SdkHook> { SdkHook.OnResponse((res, _) => status = res.StatusCode) }
        };
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId, payPalMockResponse: null, payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey, prefer: "return=minimal", requestOptions: options, ct: ct);
        }
        catch (System.Text.Json.JsonException) when (status is { } s && (int)s is >= 200 and < 300)
        {
            // Void succeeded (empty 2xx body); nothing to deserialize.
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw FromCaseA(ex.Error.TryGetError(out var e) ? e : null, ex.Error.TryGetRawError(out var r) ? r : null, "void the authorization", ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw FromTransport(ex, "void the authorization");
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount,
        string idempotencyKey, CancellationToken ct)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = _currency, Value = Money2(amount.Value) } }
            : null;
        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId, payPalMockResponse: null, payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null, body: body, prefer: "return=representation", ct: ct);
            return new PayPalRefundResult(
                refund.Id ?? throw new PaymentGatewayException("PayPal refund is missing an id."),
                refund.Status?.Value ?? string.Empty,
                ParseMoney(refund.Amount) ?? amount ?? 0m,
                refund.Amount?.CurrencyCode ?? _currency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw FromCaseA(ex.Error.TryGetError(out var e) ? e : null, ex.Error.TryGetRawError(out var r) ? r : null, "refund the payment", ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw FromTransport(ex, "refund the payment");
        }
    }

    public async Task<PayPalVaultedCard> VaultCardAsync(PayPalVaultCardCommand command, CancellationToken ct)
    {
        var address = BuildAddress(command.CountryCode, command.AddressLine1, command.AddressLine2,
            command.AdminArea1, command.AdminArea2, command.PostalCode);

        // Two-step vault: a setup token holds the raw card, then it is exchanged for a permanent payment
        // token. This is the merchant-initiated "save a card" flow; the SCA_WHEN_REQUIRED verification
        // method keeps it browser-free for cards that do not mandate a challenge.
        var setupBody = new SetupTokenRequest
        {
            PaymentSource = new SetupTokenRequestPaymentSource
            {
                Card = new SetupTokenRequestCard
                {
                    Number = command.Number,
                    Expiry = command.Expiry,
                    SecurityCode = command.SecurityCode,
                    Name = command.Name,
                    BillingAddress = address
                }
            }
        };

        string setupTokenId;
        try
        {
            var setup = await _client.Vault.CreateSetupToken(
                payPalRequestId: Guid.NewGuid().ToString("N"), body: setupBody, ct: ct);
            setupTokenId = setup.Id ?? throw new PaymentGatewayException("PayPal setup token is missing an id.");
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            throw FromCaseA(ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetRawError(out var r) ? r : null, "save the card", ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw FromTransport(ex, "save the card");
        }

        var tokenBody = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Token = new VaultTokenRequest
                {
                    Id = setupTokenId,
                    Type = VaultTokenRequestType.SetupToken
                }
            }
        };
        try
        {
            var resp = await _client.Vault.CreatePaymentToken(
                payPalRequestId: Guid.NewGuid().ToString("N"), body: tokenBody, ct: ct);

            var cardEnt = resp.PaymentSource?.Card;
            return new PayPalVaultedCard(
                resp.Id ?? throw new PaymentGatewayException("PayPal vault token is missing an id."),
                cardEnt?.Brand?.Value,
                cardEnt?.LastDigits,
                cardEnt?.Expiry,
                cardEnt?.Name,
                resp.Customer?.Id);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw FromCaseA(ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetRawError(out var r) ? r : null, "save the card", ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw FromTransport(ex, "save the card");
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw FromCaseA(ex.Error.TryGetError(out var e) ? e : null, ex.Error.TryGetRawError(out var r) ? r : null, "delete the saved card", ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw FromTransport(ex, "delete the saved card");
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        var start = FormatPayPalDate(from);
        var end = FormatPayPalDate(to);
        var all = new List<PayPalTransaction>();
        const int MaxPages = 1000; // provider-independent bound on the page loop
        var page = 1;

        try
        {
            while (true)
            {
                var resp = await _client.TransactionSearch.SearchTransactions(
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
                    ct: ct);

                if (resp.TransactionDetails is not null)
                {
                    foreach (var detail in resp.TransactionDetails)
                    {
                        var ti = detail.TransactionInfo;
                        if (ti is null) continue;
                        all.Add(new PayPalTransaction(
                            ti.TransactionId,
                            ti.InvoiceId,
                            ParseMoney(ti.TransactionAmount),
                            ti.TransactionAmount?.CurrencyCode,
                            ti.TransactionStatus,
                            ParseTime(ti.TransactionInitiationDate),
                            ParseMoney(ti.FeeAmount)));
                    }
                }

                var totalPages = resp.TotalPages ?? 1;
                if (page >= totalPages || page >= MaxPages) break;
                page++;
            }
            return all;
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "search transactions", ex);
        }
        catch (Exception ex) when (IsTransportOrJson(ex))
        {
            throw FromTransport(ex, "search transactions");
        }
    }

    // ---------- mapping helpers ----------

    private CardRequest BuildCardRequest(PayPalCardDetails d) => new()
    {
        Number = d.Number,
        Expiry = d.Expiry,
        SecurityCode = d.SecurityCode,
        Name = d.Name,
        BillingAddress = BuildAddress(d.CountryCode, d.AddressLine1, d.AddressLine2,
            d.AdminArea1, d.AdminArea2, d.PostalCode)
    };

    private static Address? BuildAddress(string? countryCode, string? line1, string? line2,
        string? adminArea1, string? adminArea2, string? postalCode)
    {
        if (countryCode is null && line1 is null && line2 is null && adminArea1 is null
            && adminArea2 is null && postalCode is null)
        {
            return null;
        }
        return new Address
        {
            CountryCode = string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode!,
            AddressLine1 = line1,
            AddressLine2 = line2,
            AdminArea1 = adminArea1,
            AdminArea2 = adminArea2,
            PostalCode = postalCode
        };
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? units) =>
        units?.SelectMany(u => u.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

    private static bool HasApprovalLink(IReadOnlyList<LinkDescription>? links) =>
        links is not null && links.Any(l =>
            l.Rel.Equals("approve", StringComparison.OrdinalIgnoreCase) ||
            l.Rel.Equals("payer-action", StringComparison.OrdinalIgnoreCase));

    private static string? DescribeCard(CardResponse? card, PayPalCardDetails? raw)
    {
        if (card?.LastDigits is { } last)
        {
            var brand = card.Brand?.Value ?? "Card";
            return $"{brand} ending {last}";
        }
        if (raw is not null && raw.Number.Length >= 4)
        {
            return $"Card ending {raw.Number[^4..]}";
        }
        return null;
    }

    private static string Money2(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money?.Value is { } v && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;

    private static DateTimeOffset? ParseTime(string? value) =>
        value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt
            : null;

    private static string FormatPayPalDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    // ---------- error translation ----------

    private static PaymentGatewayException FromCaseA(Error? typed, RawError? raw, string action, Exception inner)
    {
        if (typed is not null)
        {
            var issue = typed.Details?.FirstOrDefault()?.Issue;
            var message = WithDebugId(BuildMessage(action, typed.Message, issue), typed.DebugId);
            return new PaymentGatewayException(message, statusCode: null, debugId: typed.DebugId,
                issueCode: issue, inner: inner);
        }
        if (raw is not null)
        {
            return FromRaw(raw, action, inner);
        }
        return new PaymentGatewayException(
            $"PayPal could not {action}. The provider returned an unrecognised error.", inner: inner);
    }

    private static PaymentGatewayException FromRaw(RawError raw, string action, Exception inner)
    {
        string? issue = null;
        string? debugId = null;
        try
        {
            var parsed = raw.ReadAsJson<Error>();
            issue = parsed?.Details?.FirstOrDefault()?.Issue;
            debugId = parsed?.DebugId;
        }
        catch (JsonException)
        {
            // Non-JSON body (gateway/proxy HTML/text) — fall back to status only.
        }
        var message = WithDebugId($"PayPal could not {action} (HTTP {(int)raw.StatusCode})" +
                      (issue is not null ? $": {issue}." : "."), debugId);
        return new PaymentGatewayException(message, statusCode: raw.StatusCode, debugId: debugId,
            issueCode: issue, inner: inner);
    }

    private static PaymentGatewayException FromTransport(Exception ex, string action)
    {
        return ex switch
        {
            JsonException => new PaymentGatewayException(
                $"PayPal returned a response that could not be processed while trying to {action}.", inner: ex),
            TaskCanceledException => new PaymentGatewayException(
                $"The request to {action} with PayPal timed out.", inner: ex),
            AuthSchemeException => new PaymentGatewayException(
                $"PayPal credentials could not be applied while trying to {action}.", inner: ex),
            _ => new PaymentGatewayException($"PayPal was unreachable while trying to {action}.", inner: ex)
        };
    }

    private static bool IsTransportOrJson(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException or AuthSchemeException;

    private static string WithDebugId(string message, string? debugId) =>
        string.IsNullOrEmpty(debugId) ? message : $"{message} (PayPal debug id: {debugId})";

    private static string BuildMessage(string action, string? providerMessage, string? issue)
    {
        var detail = issue ?? providerMessage;
        return detail is not null
            ? $"PayPal could not {action}: {detail}."
            : $"PayPal could not {action}.";
    }
}
