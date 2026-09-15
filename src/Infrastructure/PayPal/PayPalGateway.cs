using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using AppLogger = Microsoft.eShopWeb.ApplicationCore.Interfaces.IAppLogger<Microsoft.eShopWeb.Infrastructure.PayPal.PayPalGateway>;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The only class that talks to the PayPal SDK. It translates eShop's domain requests into SDK calls and
/// SDK failures into <see cref="PayPalApiException"/> (carrying the provider status), and never lets a raw
/// SDK/framework exception reach the caller.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    // Currencies with no minor unit — everything else is formatted to 2 decimals.
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "VND", "CLP", "XAF", "XOF", "BIF", "DJF", "GNF", "KMF", "MGA", "PYG", "RWF", "UGX", "VUV", "XPF"
    };

    // PayPal's transaction search accepts at most a 31-day window per request.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);

    private readonly PayPalServerSdkClient _client;
    private readonly AppLogger _logger;

    public PayPalGateway(PayPalServerSdkClient client, string currency, AppLogger logger)
    {
        _client = client;
        Currency = Guard.Against.NullOrEmpty(currency, nameof(currency));
        _logger = logger;
    }

    public string Currency { get; }

    public async Task<AuthorizationResult> AuthorizeAsync(decimal amount, CardPaymentInstrument instrument,
        string idempotencyKeyPrefix, CancellationToken ct)
    {
        var card = BuildCardRequest(instrument);

        // Create the order with just the amount and intent — no payment source. Presenting the card only at
        // the authorize step keeps order creation clean and matches the sandbox's card-processing flow.
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = Currency,
                        Value = FormatAmount(amount)
                    }
                }
            }
        };

        var order = await InvokeAsync<Order, CreateOrderError>(
            token => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKeyPrefix + "-c",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=minimal",
                ct: token),
            e => e.TryGetError(out var err) ? Describe(err) : null,
            "create order", ct);

        var payPalOrderId = order.Id;
        if (string.IsNullOrEmpty(payPalOrderId))
        {
            throw new PayPalApiException("PayPal did not return an order id.", null, false);
        }

        var authorizeBody = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = card }
        };

        var authResponse = await InvokeAsync<OrderAuthorizeResponse, AuthorizeOrderError>(
            token => _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKeyPrefix + "-a",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: authorizeBody,
                prefer: "return=representation",
                ct: token),
            e => e.TryGetError(out var err) ? Describe(err) : null,
            "authorize order", ct);

        // If PayPal wants the shopper to approve in a browser, STOP and report — no approval round-trip.
        ThrowIfChallenge(authResponse.Status, authResponse.Links);

        var authorization = authResponse.PurchaseUnits?
            .Where(pu => pu.Payments?.Authorizations is not null)
            .SelectMany(pu => pu.Payments!.Authorizations!)
            .FirstOrDefault();

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PayPalApiException("PayPal did not return an authorization for the order.", null, false);
        }

        return new AuthorizationResult(
            payPalOrderId!,
            authorization.Id!,
            authorization.Status?.Value ?? string.Empty,
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<AuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        var auth = await InvokeAsync<PaymentAuthorization, GetAuthorizedPaymentError>(
            token => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: token),
            e => e.TryGetError(out var err) ? Describe(err) : null,
            "get authorization", ct);

        return new AuthorizationDetails(auth.Status?.Value ?? string.Empty, ParseDate(auth.ExpirationTime));
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken ct)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount) }
        };

        var reauth = await InvokeAsync<PaymentAuthorization, ReauthorizePaymentError>(
            token => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: token),
            e => e.TryGetError(out var err) ? Describe(err) : null,
            "reauthorize", ct);

        if (string.IsNullOrEmpty(reauth.Id))
        {
            throw new PayPalApiException("PayPal did not return a renewed authorization.", null, false);
        }

        return new AuthorizationResult(
            string.Empty,
            reauth.Id!,
            reauth.Status?.Value ?? string.Empty,
            ParseDate(reauth.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        var captured = await InvokeAsync<CapturedPayment, CaptureAuthorizedPaymentError>(
            token => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: token),
            e => e.TryGetError(out var err) ? Describe(err) : null,
            "capture", ct);

        if (string.IsNullOrEmpty(captured.Id))
        {
            throw new PayPalApiException("PayPal did not return a capture id.", null, false);
        }

        var breakdown = captured.SellerReceivableBreakdown;
        return new CaptureResult(
            captured.Id!,
            captured.Status?.Value ?? string.Empty,
            ParseMoney(captured.Amount) ?? 0m,
            ParseMoney(breakdown?.PaypalFee),
            ParseMoney(breakdown?.NetAmount));
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            await InvokeAsync<PaymentAuthorization, VoidPaymentError>(
                token => _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: null,
                    prefer: "return=representation",
                    ct: token),
                e => e.TryGetError(out var err) ? Describe(err) : null,
                "void authorization", ct);
        }
        catch (PayPalApiException ex) when (ex.ProviderStatusCode is >= 200 and < 300)
        {
            // A successful void returns 204 No Content; the SDK then throws deserializing the empty body.
            // The void itself succeeded and we don't read its body, so treat a 2xx as success.
        }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey,
        CancellationToken ct)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount.Value) } }
            : null; // full refund

        var refund = await InvokeAsync<Refund, RefundCapturedPaymentError>(
            token => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: token),
            e => e.TryGetError(out var err) ? Describe(err) : null,
            "refund", ct);

        if (string.IsNullOrEmpty(refund.Id))
        {
            throw new PayPalApiException("PayPal did not return a refund id.", null, false);
        }

        return new RefundResult(
            refund.Id!,
            refund.Status?.Value ?? string.Empty,
            ParseMoney(refund.Amount) ?? amount ?? 0m);
    }

    public async Task<VaultCardResult> VaultCardAsync(string customerId, CardDetails card, CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { Id = customerId },
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

        var response = await InvokeAsync<PaymentTokenResponse, CreatePaymentTokenError>(
            token => _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: body,
                ct: token),
            e => e.TryGetError1(out var err) ? Describe(err) : null,
            "vault card", ct);

        if (string.IsNullOrEmpty(response.Id))
        {
            throw new PayPalApiException("PayPal did not return a vault token id.", null, false);
        }

        var vaultedCard = response.PaymentSource?.Card;
        return new VaultCardResult(
            response.Id!,
            vaultedCard?.Brand?.Value ?? "CARD",
            vaultedCard?.LastDigits ?? "****",
            vaultedCard?.Expiry);
    }

    public Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct)
    {
        return InvokeVoidAsync<DeletePaymentTokenError>(
            token => _client.Vault.DeletePaymentToken(id: vaultTokenId, ct: token),
            e => e.TryGetError1(out var err) ? Describe(err) : null,
            "delete vaulted card", ct);
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<PayPalTransaction>();

        // Cover the whole range: chunk into <=31-day windows, and page each window to the end.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var page = 1;
            int totalPages;
            do
            {
                var startLocal = windowStart;
                var endLocal = windowEnd;
                var pageLocal = page;

                var response = await InvokeAsync<SearchResponse, RawError>(
                    token => _client.TransactionSearch.SearchTransactions(
                        startDate: FormatSearchDate(startLocal),
                        endDate: FormatSearchDate(endLocal),
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
                        page: pageLocal,
                        ct: token),
                    raw => SafeReadRaw(raw),
                    "search transactions", ct);

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null || string.IsNullOrEmpty(info.TransactionId))
                        {
                            continue;
                        }

                        results.Add(new PayPalTransaction(
                            info.TransactionId!,
                            info.TransactionStatus ?? string.Empty,
                            ParseMoney(info.TransactionAmount),
                            info.TransactionAmount?.CurrencyCode,
                            ParseMoney(info.FeeAmount),
                            null));
                    }
                }

                totalPages = response.TotalPages ?? 1;
                page++;
            }
            while (page <= totalPages);

            // Advance past this window (avoid re-counting the boundary second).
            windowStart = windowEnd == to ? to : windowEnd.AddSeconds(1);
        }

        return results;
    }

    // ---- SDK call boundary: one place that translates every failure into PayPalApiException ----

    private async Task<TResult> InvokeAsync<TResult, TError>(
        Func<CancellationToken, Task<TResult>> operation,
        Func<TError, string?> describe,
        string action,
        CancellationToken ct)
    {
        using (PayPalResponseContext.BeginScope())
        {
            try
            {
                return await operation(ct);
            }
            catch (SdkException<TError> ex)
            {
                var status = PayPalResponseContext.LastStatusCode;
                var message = SafeDescribe(() => describe(ex.Error)) ?? $"PayPal rejected the {action} request.";
                _logger.LogWarning($"PayPal {action} failed (HTTP {status}): {message} | body={PayPalResponseContext.LastErrorBody}");
                throw new PayPalApiException(message, status, IsClientError(status), ex);
            }
            catch (JsonException ex)
            {
                // A drifted body. On a 2xx it means "outcome unknown" (5xx); on an error status it means the
                // provider rejected us and only the detail was lost — surface it as that same 4xx.
                var status = PayPalResponseContext.LastStatusCode;
                var isClient = IsClientError(status);
                var message = isClient
                    ? $"PayPal rejected the {action} request."
                    : $"PayPal returned a response to the {action} request that could not be processed.";
                throw new PayPalApiException(message, status, isClient, ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                throw new PayPalApiException($"PayPal is unreachable while processing the {action} request.", null, false, ex);
            }
        }
    }

    private async Task InvokeVoidAsync<TError>(
        Func<CancellationToken, Task> operation,
        Func<TError, string?> describe,
        string action,
        CancellationToken ct)
    {
        await InvokeAsync<bool, TError>(async token =>
        {
            await operation(token);
            return true;
        }, describe, action, ct);
    }

    // ---- helpers ----

    private CardRequest BuildCardRequest(CardPaymentInstrument instrument)
    {
        if (!string.IsNullOrEmpty(instrument.VaultId))
        {
            return new CardRequest { VaultId = instrument.VaultId };
        }

        var c = instrument.Card
            ?? throw new PaymentValidationException("No card details or saved card supplied.");

        return new CardRequest
        {
            Name = c.Name,
            Number = c.Number,
            Expiry = c.Expiry,
            SecurityCode = c.SecurityCode,
            BillingAddress = BuildAddress(c)
        };
    }

    // Only send a billing address when the caller actually supplied one — an address with just a defaulted
    // country and otherwise-empty fields can trip AVS and get the card refused.
    private static Address? BuildAddress(CardDetails c)
    {
        var hasAny = !string.IsNullOrWhiteSpace(c.AddressLine1)
            || !string.IsNullOrWhiteSpace(c.City)
            || !string.IsNullOrWhiteSpace(c.State)
            || !string.IsNullOrWhiteSpace(c.PostalCode)
            || !string.IsNullOrWhiteSpace(c.CountryCode);
        if (!hasAny)
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = c.AddressLine1,
            AddressLine2 = c.AddressLine2,
            AdminArea2 = c.City,
            AdminArea1 = c.State,
            PostalCode = c.PostalCode,
            CountryCode = string.IsNullOrWhiteSpace(c.CountryCode) ? "US" : c.CountryCode!
        };
    }

    private static void ThrowIfChallenge(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        var challenge = string.Equals(status?.Value, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase);
        string? approveHref = null;

        if (links is not null)
        {
            foreach (var link in links)
            {
                var rel = link.Rel;
                if (!string.IsNullOrEmpty(rel) &&
                    (rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase) ||
                     rel.Equals("approve", StringComparison.OrdinalIgnoreCase)))
                {
                    challenge = true;
                    approveHref = link.Href;
                }
            }
        }

        if (challenge)
        {
            throw new PaymentApprovalRequiredException(
                "PayPal requires the shopper to approve this payment in a browser (a 3-D Secure / payer-action " +
                "challenge). This integration stops and reports rather than performing a browser approval round-trip.",
                approveHref);
        }
    }

    private string FormatAmount(decimal amount)
    {
        var scale = ZeroDecimalCurrencies.Contains(Currency) ? 0 : 2;
        return Math.Round(amount, scale, MidpointRounding.AwayFromZero)
            .ToString("F" + scale, CultureInfo.InvariantCulture);
    }

    private static decimal? ParseMoney(Money? money)
        => money?.Value is { Length: > 0 } v && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;

    private static DateTimeOffset? ParseDate(string? value)
        => !string.IsNullOrEmpty(value) &&
           DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto)
            ? dto
            : null;

    private static string FormatSearchDate(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static bool IsClientError(int? status) => status is >= 400 and < 500;

    private static string? Describe(Error? error)
    {
        if (error is null)
        {
            return null;
        }
        var issues = error.Details is null
            ? null
            : string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}"));
        var parts = new[] { error.Message, issues }.Where(s => !string.IsNullOrEmpty(s));
        var combined = string.Join(" — ", parts);
        return string.IsNullOrEmpty(combined) ? error.Name : combined;
    }

    private static string? Describe(Error1? error)
    {
        if (error is null)
        {
            return null;
        }
        var issues = error.Details is null
            ? null
            : string.Join("; ", error.Details.Select(d => $"{d.Issue}: {d.Description}"));
        var parts = new[] { error.Message, issues }.Where(s => !string.IsNullOrEmpty(s));
        var combined = string.Join(" — ", parts);
        return string.IsNullOrEmpty(combined) ? error.Name : combined;
    }

    private static string? SafeReadRaw(RawError raw)
    {
        try
        {
            var body = raw.ReadAsString();
            return string.IsNullOrWhiteSpace(body) ? null : Truncate(body, 500);
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeDescribe(Func<string?> describe)
    {
        try
        {
            return describe();
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max);
}
