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
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// The one place the PayPal SDK is used. Translates the app's payment-gateway contract onto the
/// PayPal Orders/Payments/Vault/Transaction-search operations, and converts SDK failures into the
/// application's own exception types so nothing PayPal-specific leaks past this boundary.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, PayPalSettings settings, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardRequest = new CardRequest
        {
            Number = card.Number,
            Expiry = FormatExpiry(card.ExpiryYear, card.ExpiryMonth),
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = ToAddress(card.BillingAddress)
        };
        return await CreateAndAuthorizeAsync(amount, currency, cardRequest, idempotencyKey, cancellationToken);
    }

    public async Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardRequest = new CardRequest { VaultId = vaultId };
        return await CreateAndAuthorizeAsync(amount, currency, cardRequest, idempotencyKey, cancellationToken);
    }

    private async Task<AuthorizationResult> CreateAndAuthorizeAsync(decimal amount, string currency,
        CardRequest cardRequest, string idempotencyKey, CancellationToken cancellationToken)
    {
        // Unbranded/direct-card (ACDC) flow: create the order (intent = AUTHORIZE) with the card in the
        // payment source. PayPal processes the card and, in this flow, returns the authorization on the
        // create response itself — no browser approval. If (and only if) the authorization is not present
        // yet, fall back to an explicit authorize call.
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
                        Value = FormatAmount(amount)
                    }
                }
            },
            PaymentSource = new PaymentSource { Card = cardRequest }
        };

        var order = await CallAsync<Order, PayPalServerSdk.Errors.CreateOrderError>(
            "authorize the payment",
            ct => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct),
            e => e.TryGetError(out var err) ? DescribePayPalError(err.Name, err.Message, (err.Details?.FirstOrDefault() is {} d ? (d.Description is null ? d.Issue : d.Issue + ": " + d.Description) : null)) : null,
            cancellationToken);

        var orderId = order.Id ?? throw new PaymentProviderUnavailableException("PayPal did not return an order id.");
        EnsureNoChallenge(order.Status, order.Links, "creating the payment");

        var authorization = order.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        // Fallback: the card was accepted but the authorization was not returned inline — authorize once.
        if (authorization?.Id is null)
        {
            var authResponse = await CallAsync<OrderAuthorizeResponse, PayPalServerSdk.Errors.AuthorizeOrderError>(
                "authorize the payment",
                ct => _client.Orders.AuthorizeOrder(
                    id: orderId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey + ":authorize",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct),
                e => e.TryGetError(out var err) ? DescribePayPalError(err.Name, err.Message, (err.Details?.FirstOrDefault() is {} d ? (d.Description is null ? d.Issue : d.Issue + ": " + d.Description) : null)) : null,
                cancellationToken);

            EnsureNoChallenge(authResponse.Status, authResponse.Links, "authorizing the payment");
            authorization = authResponse.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        }

        if (authorization?.Id is null)
        {
            throw new PaymentProviderUnavailableException(
                "PayPal accepted the order but returned no authorization to hold the funds.");
        }

        return new AuthorizationResult(
            orderId,
            authorization.Id,
            authorization.Status?.Value ?? "CREATED",
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var capture = await CallAsync<CapturedPayment, PayPalServerSdk.Errors.CaptureAuthorizedPaymentError>(
            "capture the payment",
            ct => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct),
            e => e.TryGetError(out var err) ? DescribePayPalError(err.Name, err.Message, (err.Details?.FirstOrDefault() is {} d ? (d.Description is null ? d.Issue : d.Issue + ": " + d.Description) : null)) : null,
            cancellationToken);

        var captureId = capture.Id ?? throw new PaymentProviderUnavailableException("PayPal did not return a capture id.");
        var breakdown = capture.SellerReceivableBreakdown;

        var gross = ParseMoney(breakdown?.GrossAmount);
        var fee = ParseMoney(breakdown?.PaypalFee);
        var net = ParseMoney(breakdown?.NetAmount);

        return new CaptureResult(
            captureId,
            capture.Status?.Value ?? "COMPLETED",
            gross,
            fee,
            net);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default)
    {
        var reauth = await CallAsync<PaymentAuthorization, PayPalServerSdk.Errors.ReauthorizePaymentError>(
            "renew the authorization",
            ct => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
                },
                prefer: "return=representation",
                ct: ct),
            e => e.TryGetError(out var err) ? DescribePayPalError(err.Name, err.Message, (err.Details?.FirstOrDefault() is {} d ? (d.Description is null ? d.Issue : d.Issue + ": " + d.Description) : null)) : null,
            cancellationToken);

        var newAuthId = reauth.Id ?? throw new PaymentProviderUnavailableException("PayPal did not return a renewed authorization id.");
        return new AuthorizationResult(
            string.Empty,
            newAuthId,
            reauth.Status?.Value ?? "CREATED",
            ParseDate(reauth.ExpirationTime));
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await CallAsync<PaymentAuthorization, PayPalServerSdk.Errors.VoidPaymentError>(
            "release the authorization hold",
            ct => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                ct: ct),
            e => e.TryGetError(out var err) ? DescribePayPalError(err.Name, err.Message, (err.Details?.FirstOrDefault() is {} d ? (d.Description is null ? d.Issue : d.Issue + ": " + d.Description) : null)) : null,
            cancellationToken);
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) } }
            : null;

        var refund = await CallAsync<Refund, PayPalServerSdk.Errors.RefundCapturedPaymentError>(
            "refund the payment",
            ct => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct),
            e => e.TryGetError(out var err) ? DescribePayPalError(err.Name, err.Message, (err.Details?.FirstOrDefault() is {} d ? (d.Description is null ? d.Issue : d.Issue + ": " + d.Description) : null)) : null,
            cancellationToken);

        var refundId = refund.Id ?? throw new PaymentProviderUnavailableException("PayPal did not return a refund id.");
        return new RefundResult(refundId, refund.Status?.Value ?? "PENDING");
    }

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default)
    {
        var request = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = FormatExpiry(card.ExpiryYear, card.ExpiryMonth),
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = ToAddress(card.BillingAddress)
                }
            }
        };

        var token = await CallAsync<PaymentTokenResponse, PayPalServerSdk.Errors.CreatePaymentTokenError>(
            "save the card",
            ct => _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: request,
                ct: ct),
            e => e.TryGetError1(out var err) ? DescribePayPalError(err.Name, err.Message, (err.Details?.FirstOrDefault() is {} d ? (d.Description is null ? d.Issue : d.Issue + ": " + d.Description) : null)) : null,
            cancellationToken);

        var vaultId = token.Id ?? throw new PaymentProviderUnavailableException("PayPal did not return a vault token id for the saved card.");
        var storedCard = token.PaymentSource?.Card;
        var (expiryMonth, expiryYear) = SplitExpiry(storedCard?.Expiry, card.ExpiryMonth, card.ExpiryYear);

        return new VaultedCard(
            vaultId,
            storedCard?.Brand?.Value ?? "UNKNOWN",
            storedCard?.LastDigits ?? Last4(card.Number),
            expiryMonth,
            expiryYear,
            storedCard?.Name ?? card.CardholderName);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await CallAsync<bool, PayPalServerSdk.Errors.DeletePaymentTokenError>(
                "remove the saved card",
                async ct =>
                {
                    await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
                    return true;
                },
                e => e.TryGetError1(out var err) ? DescribePayPalError(err.Name, err.Message, (err.Details?.FirstOrDefault() is {} d ? (d.Description is null ? d.Issue : d.Issue + ": " + d.Description) : null)) : null,
                cancellationToken);
        }
        catch (PaymentException ex)
        {
            // Deleting a token that PayPal no longer has is not a failure for the shopper — the card
            // is being forgotten either way. Log and continue so the local record is still removed.
            _logger.LogWarning(ex, "PayPal vault token {VaultId} could not be deleted; removing local record anyway.", vaultId);
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<GatewayTransaction>();
        var startDate = FormatSearchDate(from);
        var endDate = FormatSearchDate(to);

        int page = 1;
        int totalPages = 1;

        do
        {
            SearchResponse response;
            try
            {
                var currentPage = page;
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
                    page: currentPage,
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                var status = (int)ex.Error.StatusCode;
                if (status >= 500)
                {
                    throw new PaymentProviderUnavailableException(
                        $"The payment provider failed to return the transaction report (HTTP {status}).", ex);
                }
                throw new PaymentException($"Could not read the transaction report (HTTP {status}): {SafeBody(ex.Error)}", ex);
            }
            catch (JsonException ex)
            {
                throw new PaymentProviderUnavailableException(
                    "The payment provider returned a transaction report that could not be processed.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new PaymentProviderUnavailableException("The payment provider was unreachable while reading the transaction report.", ex);
            }

            totalPages = response.TotalPages ?? 1;

            foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info?.TransactionId is null)
                {
                    continue;
                }
                results.Add(new GatewayTransaction(
                    info.TransactionId,
                    info.TransactionStatus ?? string.Empty,
                    ParseMoneyNullable(info.TransactionAmount),
                    info.TransactionAmount?.CurrencyCode,
                    ParseDate(info.TransactionInitiationDate)));
            }

            page++;
        }
        while (page <= totalPages);

        return results;
    }

    // --- SDK-call boundary: converts every SDK failure into an application exception ---

    private async Task<TResp> CallAsync<TResp, TError>(
        string action,
        Func<CancellationToken, Task<TResp>> call,
        Func<TError, string?> describe,
        CancellationToken cancellationToken)
        where TError : ApiError
    {
        try
        {
            return await call(cancellationToken);
        }
        catch (SdkException<TError> ex)
        {
            var message = describe(ex.Error);
            if (message is not null)
            {
                throw new PaymentException($"Could not {action}: {message}", ex);
            }
            if (ex.Error.TryGetRawError(out RawError raw))
            {
                var status = (int)raw.StatusCode;
                if (status >= 500)
                {
                    throw new PaymentProviderUnavailableException($"The payment provider failed while trying to {action} (HTTP {status}).", ex);
                }
                throw new PaymentException($"Could not {action} (HTTP {status}): {SafeBody(raw)}", ex);
            }
            throw new PaymentProviderUnavailableException($"The payment provider returned an unreadable error while trying to {action}.", ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderUnavailableException($"The payment provider returned a response that could not be processed while trying to {action}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentProviderUnavailableException($"The payment provider was unreachable while trying to {action}.", ex);
        }
    }

    private void EnsureNoChallenge(OrderStatus? status, IReadOnlyList<LinkDescription>? links, string context)
    {
        var needsApproval =
            (status is not null && string.Equals(status.Value, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)) ||
            (links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false);

        if (needsApproval)
        {
            throw new PaymentChallengeRequiredException(
                $"PayPal requires the shopper to approve this card in a browser (3-D Secure challenge) while {context}. " +
                "This integration does not perform a browser approval round-trip; use a card that authorizes directly.");
        }
    }

    private static string DescribePayPalError(string? name, string? message, string? issue)
    {
        var head = string.IsNullOrWhiteSpace(name) ? (message ?? "rejected") : $"{name} - {message}";
        if (!string.IsNullOrWhiteSpace(issue))
        {
            head += $" ({issue})";
        }
        return head;
    }

    private static string SafeBody(RawError raw)
    {
        try { return raw.ReadAsString() ?? "(no body)"; }
        catch { return "(unreadable body)"; }
    }

    private static Address? ToAddress(CardBillingAddress? billing)
    {
        if (billing is null || string.IsNullOrWhiteSpace(billing.CountryCode))
        {
            return null;
        }
        return new Address
        {
            AddressLine1 = billing.Line1,
            AddressLine2 = billing.Line2,
            AdminArea2 = billing.City,
            AdminArea1 = billing.State,
            PostalCode = billing.PostalCode,
            CountryCode = billing.CountryCode
        };
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatExpiry(string year, string month) => $"{PadYear(year)}-{PadMonth(month)}";

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(Money? money) => ParseMoneyNullable(money) ?? 0m;

    private static decimal? ParseMoneyNullable(Money? money)
    {
        if (money?.Value is null || !decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }
        return value;
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static (string month, string year) SplitExpiry(string? expiry, string fallbackMonth, string fallbackYear)
    {
        // PayPal returns expiry as "YYYY-MM".
        if (!string.IsNullOrWhiteSpace(expiry) && expiry.Contains('-'))
        {
            var parts = expiry.Split('-');
            if (parts.Length == 2)
            {
                return (PadMonth(parts[1]), PadYear(parts[0]));
            }
        }
        return (PadMonth(fallbackMonth), PadYear(fallbackYear));
    }

    private static string PadMonth(string month) =>
        int.TryParse(month, out var m) ? m.ToString("00", CultureInfo.InvariantCulture) : month;

    private static string PadYear(string year) => year;

    private static string Last4(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }
}
