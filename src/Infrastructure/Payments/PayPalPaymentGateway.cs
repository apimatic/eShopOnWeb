using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Enum;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal-backed implementation of <see cref="IPaymentGateway"/> over the AsadAli.Checkout.Sdk
/// (root namespace PayPalServerSdk). Every SDK type is fully namespaced via using-directives above;
/// the SDK splits its surface across several child namespaces (Models, Models.Enums, Errors,
/// Core.Exceptions, Core.ErrorResponse) which C# does not import transitively.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly IOptions<PayPalSettings> _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(
        PayPalServerSdkClient client,
        IOptions<PayPalSettings> settings,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public string CurrencyCode => _settings.Value.Currency;

    private string Currency => _settings.Value.Currency;

    // ---------------------------------------------------------------------------------------------
    // Authorize (raw card)
    // ---------------------------------------------------------------------------------------------
    public Task<AuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, CardPaymentDetails card, string idempotencyKey, CancellationToken cancellationToken)
    {
        // Never log card.Number or card.SecurityCode.
        _logger.LogInformation("Authorizing a PayPal card payment for {Amount} {Currency}.", amount, Currency);

        var cardRequest = new CardRequest
        {
            Number = card.Number,
            Expiry = ToPayPalExpiry(card.ExpiryMonth, card.ExpiryYear),
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = MapAddress(card.BillingAddress),
        };

        return AuthorizeInternalAsync(amount, cardRequest, idempotencyKey, cancellationToken);
    }

    // ---------------------------------------------------------------------------------------------
    // Authorize (vaulted card)
    // ---------------------------------------------------------------------------------------------
    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(
        decimal amount, string vaultId, string idempotencyKey, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Authorizing a PayPal payment against a vaulted card for {Amount} {Currency}.", amount, Currency);

        // A vaulted card is funded by vault id only — no raw PAN travels here.
        var cardRequest = new CardRequest { VaultId = vaultId };

        return AuthorizeInternalAsync(amount, cardRequest, idempotencyKey, cancellationToken);
    }

    private async Task<AuthorizationResult> AuthorizeInternalAsync(
        decimal amount, CardRequest cardRequest, string idempotencyKey, CancellationToken ct)
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
                        CurrencyCode = Currency,
                        Value = FormatAmount(amount),
                    },
                },
            },
            PaymentSource = new PaymentSource { Card = cardRequest },
        };

        Order order;
        try
        {
            order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                throw new PaymentException($"PayPal rejected the order: {DescribeError(e)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PaymentException(
                    $"PayPal rejected the order (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new PaymentException("PayPal rejected the order.", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw JsonBoundary(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw TimedOut(ex);
        }

        var orderId = order.Id ?? "";
        var status = order.Status;
        var cardResponse = order.PaymentSource?.Card;
        var links = order.Links;
        var auth = ReadAuthorization(order.PurchaseUnits);

        // If PayPal did not already produce an authorization and the order is not complete, ask for
        // an explicit authorization against the order.
        if (auth is null && status != OrderStatus.Completed)
        {
            OrderAuthorizeResponse authResponse;
            try
            {
                authResponse = await _client.Orders.AuthorizeOrder(
                    orderId,
                    payPalMockResponse: null,
                    payPalRequestId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                if (ex.Error.TryGetError(out var e))
                {
                    throw new PaymentException($"PayPal could not authorize the order: {DescribeError(e)}", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw new PaymentException(
                        $"PayPal could not authorize the order (HTTP {(int)raw.StatusCode}).", ex);
                }
                throw new PaymentException("PayPal could not authorize the order.", ex);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw JsonBoundary(ex);
            }
            catch (HttpRequestException ex)
            {
                throw Unreachable(ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw TimedOut(ex);
            }

            status = authResponse.Status ?? status;
            cardResponse = authResponse.PaymentSource?.Card ?? cardResponse;
            links = authResponse.Links ?? links;
            auth = ReadAuthorization(authResponse.PurchaseUnits);
        }

        var authId = auth?.Id;

        // STOP-on-challenge: this integration is server-only and does not build an approval round-trip.
        if (status == OrderStatus.PayerActionRequired
            || IsThreeDSecureChallenge(cardResponse)
            || HasPayerActionLink(links)
            || string.IsNullOrEmpty(authId))
        {
            throw new PaymentApprovalRequiredException(
                $"PayPal requires shopper approval (3-D Secure / payer action) before this card payment " +
                $"can proceed, or no authorization could be obtained. Order {orderId}, status " +
                $"{EnumWire(status) ?? "unknown"}.");
        }

        var (brand, last4, expMonth, expYear) = ReadCard(cardResponse);
        return new AuthorizationResult(
            orderId, authId!, EnumWire(auth!.Status) ?? "", brand, last4, expMonth, expYear);
    }

    // ---------------------------------------------------------------------------------------------
    // Capture
    // ---------------------------------------------------------------------------------------------
    public async Task<CaptureResult> CaptureAsync(
        string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                if (IndicatesExpiredOrReauth(IssueTokens(e)))
                {
                    throw new AuthorizationExpiredException(DescribeError(e));
                }
                throw new PaymentException($"PayPal could not capture the authorization: {DescribeError(e)}", ex);
            }
            if (ex.Error.TryGetNoContent(out var nc))
            {
                throw new PaymentException(
                    $"PayPal returned no content (HTTP {(int)nc.StatusCode}) capturing the authorization.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PaymentException(
                    $"PayPal could not capture the authorization (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new PaymentException("PayPal could not capture the authorization.", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw JsonBoundary(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(ex);
        }

        decimal gross, fee, net;
        var breakdown = captured.SellerReceivableBreakdown;
        if (breakdown != null)
        {
            gross = ParseAmount(breakdown.GrossAmount.Value);
            fee = breakdown.PaypalFee != null ? ParseAmount(breakdown.PaypalFee.Value) : 0m;
            net = breakdown.NetAmount != null ? ParseAmount(breakdown.NetAmount.Value) : gross;
        }
        else
        {
            gross = ParseAmount(captured.Amount?.Value);
            fee = 0m;
            net = gross;
        }

        return new CaptureResult(captured.Id ?? "", EnumWire(captured.Status) ?? "", gross, fee, net);
    }

    // ---------------------------------------------------------------------------------------------
    // Reauthorize
    // ---------------------------------------------------------------------------------------------
    public async Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, CancellationToken cancellationToken)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount) },
        };

        PaymentAuthorization result;
        try
        {
            result = await _client.Payments.ReauthorizePayment(
                authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                if (IndicatesReauthNotAllowed(IssueTokens(e)))
                {
                    throw new ReauthorizationNotAllowedException(
                        $"PayPal will not reauthorize {authorizationId}: {DescribeError(e)}");
                }
                throw new PaymentException($"PayPal could not reauthorize the payment: {DescribeError(e)}", ex);
            }
            if (ex.Error.TryGetNoContent(out var nc))
            {
                throw new PaymentException(
                    $"PayPal returned no content (HTTP {(int)nc.StatusCode}) reauthorizing the payment.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PaymentException(
                    $"PayPal could not reauthorize the payment (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new PaymentException("PayPal could not reauthorize the payment.", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw JsonBoundary(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(ex);
        }

        // The reauthorize response is a PaymentAuthorization; the originating order id is not carried here.
        return new AuthorizationResult(
            "", result.Id ?? "", EnumWire(result.Status) ?? "", null, null, null, null);
    }

    // ---------------------------------------------------------------------------------------------
    // Void
    // ---------------------------------------------------------------------------------------------
    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                prefer: "return=minimal",
                ct: cancellationToken);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                throw new PaymentException($"PayPal could not void the authorization: {DescribeError(e)}", ex);
            }
            if (ex.Error.TryGetNoContent(out var nc))
            {
                throw new PaymentException(
                    $"PayPal returned no content (HTTP {(int)nc.StatusCode}) voiding the authorization.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PaymentException(
                    $"PayPal could not void the authorization (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new PaymentException("PayPal could not void the authorization.", ex);
        }
        catch (System.Text.Json.JsonException)
        {
            // A successful void responds 204 with an EMPTY body (prefer=return=minimal), but VoidPayment
            // is typed to return PaymentAuthorization, so the SDK throws a JsonException trying to
            // deserialize nothing. That is SUCCESS, not an error — swallow it and return normally.
            // Genuine error statuses (4xx/5xx) arrive as SdkException<VoidPaymentError> (caught above)
            // and are still surfaced as PaymentException, so this narrow catch only affects the 204 case.
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(ex);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Refund
    // ---------------------------------------------------------------------------------------------
    public async Task<RefundResult> RefundAsync(
        string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        RefundRequest? body = amount is null
            ? null
            : new RefundRequest
            {
                Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount.Value) },
            };

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                throw new PaymentException($"PayPal could not refund the capture: {DescribeError(e)}", ex);
            }
            if (ex.Error.TryGetNoContent(out var nc))
            {
                throw new PaymentException(
                    $"PayPal returned no content (HTTP {(int)nc.StatusCode}) refunding the capture.", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PaymentException(
                    $"PayPal could not refund the capture (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new PaymentException("PayPal could not refund the capture.", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw JsonBoundary(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(ex);
        }

        var refundedAmount = amount ?? ParseAmount(refund.Amount?.Value);
        return new RefundResult(refund.Id ?? "", EnumWire(refund.Status) ?? "", refundedAmount);
    }

    // ---------------------------------------------------------------------------------------------
    // Vault a card
    // ---------------------------------------------------------------------------------------------
    public async Task<VaultCardResult> VaultCardAsync(CardPaymentDetails card, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Vaulting a card with PayPal.");

        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = ToPayPalExpiry(card.ExpiryMonth, card.ExpiryYear),
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = MapAddress(card.BillingAddress),
                },
            },
        };

        PaymentTokenResponse response;
        try
        {
            response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: body,
                ct: cancellationToken);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var e1))
            {
                throw new PaymentException($"PayPal could not vault the card: {DescribeError1(e1)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PaymentException(
                    $"PayPal could not vault the card (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new PaymentException("PayPal could not vault the card.", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw JsonBoundary(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(ex);
        }

        var vaultCard = response.PaymentSource?.Card;
        var (month, year) = ParseExpiry(vaultCard?.Expiry);

        return new VaultCardResult(
            response.Id ?? "",
            EnumWire(vaultCard?.Brand) ?? "",
            vaultCard?.LastDigits ?? "",
            month ?? card.ExpiryMonth,
            year ?? card.ExpiryYear);
    }

    // ---------------------------------------------------------------------------------------------
    // Delete a vaulted card
    // ---------------------------------------------------------------------------------------------
    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        // The SDK DOES expose a vault delete operation (DELETE /v3/vault/payment-tokens/{id}), so we
        // call it rather than treating this as a no-op.
        try
        {
            await _client.Vault.DeletePaymentToken(vaultId, ct: cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var e1))
            {
                throw new PaymentException($"PayPal could not delete the vaulted card: {DescribeError1(e1)}", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PaymentException(
                    $"PayPal could not delete the vaulted card (HTTP {(int)raw.StatusCode}).", ex);
            }
            throw new PaymentException("PayPal could not delete the vaulted card.", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw JsonBoundary(ex);
        }
        catch (HttpRequestException ex)
        {
            throw Unreachable(ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(ex);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // List transactions (walk every page)
    // ---------------------------------------------------------------------------------------------
    public async Task<IReadOnlyList<GatewayTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // PayPal's SearchTransactions rejects any range wider than 31 days, so tile the requested
        // [from, to] range into consecutive ~30-day sub-windows and page-walk each. Deduplicate by
        // transaction id across windows (keyed dictionary) in case a boundary overlaps a transaction.
        var transactions = new Dictionary<string, GatewayTransaction>();
        var slice = TimeSpan.FromDays(30);

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + slice;
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            var startDate = FormatSearchDate(windowStart);
            var endDate = FormatSearchDate(windowEnd);

            var page = 1;
            var totalPages = 1;

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
                        balanceAffectingRecordsOnly: "Y",
                        pageSize: 500,
                        page: page,
                        ct: cancellationToken);
                }
                catch (SdkException<RawError> ex)
                {
                    // SearchTransactions is Case B — the error IS a RawError (no typed accessors).
                    throw new PaymentException(
                        $"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}", ex);
                }
                catch (System.Text.Json.JsonException ex)
                {
                    throw JsonBoundary(ex);
                }
                catch (HttpRequestException ex)
                {
                    throw Unreachable(ex);
                }
                catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    throw TimedOut(ex);
                }

                if (page == 1)
                {
                    totalPages = response.TotalPages ?? 1;
                }

                if (response.TransactionDetails != null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info?.TransactionId is null)
                        {
                            continue;
                        }

                        var date = DateTimeOffset.TryParse(
                            info.TransactionInitiationDate,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out var parsedDate)
                            ? parsedDate
                            : to;

                        transactions[info.TransactionId] = new GatewayTransaction(
                            info.TransactionId,
                            info.TransactionEventCode ?? "",
                            info.TransactionStatus ?? "",
                            ParseAmount(info.TransactionAmount?.Value),
                            info.TransactionAmount?.CurrencyCode ?? Currency,
                            date);
                    }
                }

                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        return transactions.Values.ToList();
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Render an SDK <c>StringEnum&lt;T&gt;</c> as its plain wire string (e.g. "VISA", "COMPLETED") via
    /// the <c>.Value</c> accessor. The generated record's own ToString yields the wrapper form
    /// (e.g. "CardBrand { Value = VISA }"), so never use ToString for wire rendering. Returns null when
    /// the enum itself is null.
    /// </summary>
    private static string? EnumWire<TEnum>(StringEnum<TEnum>? value) where TEnum : StringEnum<TEnum> =>
        value?.Value;

    /// <summary>Fixed 2-decimal invariant string, as PayPal money values require.</summary>
    private static string FormatAmount(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>Parse a PayPal money string invariantly; 0 on null/blank/parse-fail.</summary>
    private static decimal ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;

    /// <summary>Convert the interface's "MM"/"YYYY" pair into PayPal's card expiry format "YYYY-MM".</summary>
    private static string ToPayPalExpiry(string expiryMonth, string expiryYear)
    {
        var month = (expiryMonth ?? "").Trim().PadLeft(2, '0');
        var year = (expiryYear ?? "").Trim();
        return $"{year}-{month}";
    }

    /// <summary>Split PayPal's "YYYY-MM" expiry back into (month, year); nulls when it can't be read.</summary>
    private static (string? Month, string? Year) ParseExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            return (null, null);
        }

        var parts = expiry.Split('-');
        if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
        {
            return (parts[1], parts[0]);
        }

        return (null, null);
    }

    /// <summary>
    /// Map the interface billing address to PayPal's <c>Address</c>. AdminArea1=state, AdminArea2=city.
    /// PayPal requires CountryCode, so default to "US" when the address or country is missing.
    /// </summary>
    private static Address MapAddress(CardBillingAddress? billingAddress)
    {
        var country = billingAddress?.CountryCode;
        if (string.IsNullOrWhiteSpace(country))
        {
            country = "US";
        }

        return new Address
        {
            AddressLine1 = billingAddress?.AddressLine1,
            AddressLine2 = billingAddress?.AddressLine2,
            AdminArea1 = billingAddress?.AdminArea1,
            AdminArea2 = billingAddress?.AdminArea2,
            PostalCode = billingAddress?.PostalCode,
            CountryCode = country,
        };
    }

    private static AuthorizationWithAdditionalData? ReadAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        var authorizations = purchaseUnits is { Count: > 0 }
            ? purchaseUnits[0].Payments?.Authorizations
            : null;
        return authorizations is { Count: > 0 } ? authorizations[0] : null;
    }

    private static (string? Brand, string? Last4, string? Month, string? Year) ReadCard(CardResponse? card)
    {
        if (card is null)
        {
            return (null, null, null, null);
        }

        var (month, year) = ParseExpiry(card.Expiry);
        return (EnumWire(card.Brand), card.LastDigits, month, year);
    }

    /// <summary>
    /// Heuristic detection of a 3-D Secure challenge that the shopper must complete in a browser.
    /// UNVERIFIED against live traffic: the precise enrollment/authentication-status combinations
    /// PayPal returns for a pending challenge can only be confirmed on the wire. Erring toward
    /// stopping (surfacing an approval-required signal) is the safe default for a server-only flow.
    /// </summary>
    private static bool IsThreeDSecureChallenge(CardResponse? card)
    {
        var threeDSecure = card?.AuthenticationResult?.ThreeDSecure;
        if (threeDSecure is null)
        {
            return false;
        }

        var enrollment = EnumWire(threeDSecure.EnrollmentStatus);
        var authStatus = EnumWire(threeDSecure.AuthenticationStatus);

        // "C" = challenge required (a browser step is pending).
        if (string.Equals(authStatus, "C", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Enrolled in 3-D Secure but not (yet) authenticated ("Y") or attempted ("A") => pending.
        if (string.Equals(enrollment, "Y", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(authStatus, "Y", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(authStatus, "A", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool HasPayerActionLink(IReadOnlyList<LinkDescription>? links) =>
        links != null && links.Any(l =>
            !string.IsNullOrEmpty(l.Rel)
            && (l.Rel.Equals("payer-action", StringComparison.OrdinalIgnoreCase)
                || l.Rel.Equals("approve", StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<string> IssueTokens(Error error)
    {
        if (!string.IsNullOrEmpty(error.Name))
        {
            yield return error.Name;
        }

        if (error.Details != null)
        {
            foreach (var detail in error.Details)
            {
                if (!string.IsNullOrEmpty(detail.Issue))
                {
                    yield return detail.Issue;
                }
            }
        }
    }

    // Expiry / reauthorization needed (for capture): only expiry/reauth tokens — NOT currency mismatch.
    private static bool IndicatesExpiredOrReauth(IEnumerable<string> tokens) =>
        tokens.Any(t =>
            t.IndexOf("EXPIRED", StringComparison.OrdinalIgnoreCase) >= 0
            || t.IndexOf("REAUTHORIZ", StringComparison.OrdinalIgnoreCase) >= 0);

    // The authorization can no longer be reauthorized.
    private static readonly string[] _reauthNotAllowedTokens =
    {
        "AUTHORIZATION_EXPIRED",
        "REAUTHORIZATION_NOT_ALLOWED",
        "MAX_NUMBER_OF_REAUTHORIZATION_EXCEEDED",
        "AUTHORIZATION_ALREADY_CAPTURED",
    };

    private static bool IndicatesReauthNotAllowed(IEnumerable<string> tokens) =>
        tokens.Any(t => _reauthNotAllowedTokens.Any(
            token => t.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));

    private static string DescribeError(Error error)
    {
        var issues = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(d => d.Issue))
            : null;
        return Compose(error.Name, error.Message, issues);
    }

    private static string DescribeError1(Error1 error)
    {
        var issues = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(d => d.Issue))
            : null;
        return Compose(error.Name, error.Message, issues);
    }

    private static string Compose(string? name, string? message, string? issues)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(name))
        {
            parts.Add(name!);
        }
        if (!string.IsNullOrWhiteSpace(issues))
        {
            parts.Add(issues!);
        }
        if (!string.IsNullOrWhiteSpace(message))
        {
            parts.Add(message!);
        }
        return parts.Count > 0 ? string.Join(" - ", parts) : "unknown error";
    }

    /// <summary>
    /// ISO-8601 / RFC-3339 timestamp with offset, as PayPal's reporting API requires.
    /// UNVERIFIED against live traffic: the exact offset formatting PayPal accepts can only be
    /// confirmed on the wire; this emits an offset-bearing RFC-3339 value.
    /// </summary>
    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    // A 2xx or error body that could not be deserialized to the generated model. Surface a
    // caller-safe message rather than leaking System.Text.Json detail; do NOT map to success.
    private static PaymentException JsonBoundary(System.Text.Json.JsonException ex) =>
        new PaymentException("PayPal returned a response that could not be processed.", ex);

    private static PaymentException Unreachable(HttpRequestException ex) =>
        new PaymentException("PayPal is currently unreachable.", ex);

    private static PaymentException TimedOut(TaskCanceledException ex) =>
        new PaymentException("The PayPal request timed out.", ex);
}
