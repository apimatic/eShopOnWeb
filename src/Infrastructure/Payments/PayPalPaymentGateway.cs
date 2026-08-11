using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The PayPal implementation of <see cref="IPaymentGateway"/>. It is the single place
/// that talks to PayPal (via the paypal-sdk client) and the single place PayPal errors
/// are translated into caller-safe <see cref="PaymentException"/>s. No card data or raw
/// provider payload ever crosses this boundary or reaches a log.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<GatewayAuthorization> AuthorizeAsync(string idempotencyKey, decimal amount, string currency,
        CardDetails? card, string? vaultId, CancellationToken cancellationToken = default)
    {
        var paymentSource = new PaymentSource
        {
            Card = vaultId is not null
                ? new CardRequest { VaultId = vaultId }
                : ToCardRequest(card!)
        };

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = Format(amount)
                    }
                }
            },
            PaymentSource = paymentSource
        };

        return await ExecuteAsync(async () =>
        {
            var order = await _client.Orders.CreateOrder(
                null, idempotencyKey, null, null, null, body,
                prefer: "return=representation", ct: cancellationToken);

            var auth = FindAuthorization(order?.PurchaseUnits);

            // Some card flows authorize inline on create; others need an explicit authorize call.
            if (auth is null && order?.Id is not null)
            {
                var authorized = await _client.Orders.AuthorizeOrder(
                    order.Id, null, idempotencyKey + "-auth", null, null, null,
                    prefer: "return=representation", ct: cancellationToken);
                auth = FindAuthorization(authorized?.PurchaseUnits);
            }

            if (order?.Id is null || auth?.Id is null)
            {
                throw new PaymentException("PayPal did not return an authorization for the order.", 502);
            }

            return new GatewayAuthorization(order.Id, auth.Id, auth.Status?.Value ?? "CREATED");
        }, "authorize the payment");
    }

    public async Task<GatewayCapture> CaptureAsync(string idempotencyKey, string authorizationId, decimal amount,
        string currency, bool finalCapture, CancellationToken cancellationToken = default)
    {
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = Format(amount) },
            FinalCapture = finalCapture
        };

        return await ExecuteAsync(async () =>
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId, null, idempotencyKey, null, body,
                prefer: "return=representation", ct: cancellationToken);

            if (capture?.Id is null)
            {
                throw new PaymentException("PayPal did not return a capture.", 502);
            }

            var breakdown = capture.SellerReceivableBreakdown;
            return new GatewayCapture(
                capture.Id,
                capture.Status?.Value ?? "COMPLETED",
                ParseAmount(capture.Amount) ?? amount,
                ParseAmount(breakdown?.PaypalFee),
                ParseAmount(breakdown?.NetAmount),
                capture.Amount?.CurrencyCode ?? currency);
        }, "capture the payment", detectStaleAuthorization: true);
    }

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = Format(amount) }
        };

        return await ExecuteAsync(async () =>
        {
            var auth = await _client.Payments.ReauthorizePayment(
                authorizationId, null, null, body, prefer: "return=representation", ct: cancellationToken);

            if (auth?.Id is null)
            {
                throw new PaymentException(
                    "The authorization could not be renewed; a fresh authorization is required.", 409);
            }

            return new GatewayAuthorization(string.Empty, auth.Id, auth.Status?.Value ?? "CREATED");
        }, "renew the authorization");
    }

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Payments.VoidPayment(authorizationId, null, null, null,
                prefer: "return=representation", ct: cancellationToken);
        }
        catch (JsonException)
        {
            // PayPal answers a successful void with 204 No Content; the SDK cannot deserialize
            // the empty body. The void itself succeeded, so the empty body is the success signal.
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw Translate(ex.Error.TryGetError(out var e) ? e : null, "release the authorization");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal was unreachable while trying to release the authorization.", 502, ex);
        }
    }

    public async Task<GatewayRefund> RefundAsync(string idempotencyKey, string captureId, decimal? amount,
        string currency, CancellationToken cancellationToken = default)
    {
        var body = amount is decimal value
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = Format(value) } }
            : new RefundRequest();

        return await ExecuteAsync(async () =>
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId, null, idempotencyKey, null, body,
                prefer: "return=representation", ct: cancellationToken);

            if (refund?.Id is null)
            {
                throw new PaymentException("PayPal did not return a refund.", 502);
            }

            return new GatewayRefund(
                refund.Id,
                refund.Status?.Value ?? "COMPLETED",
                ParseAmount(refund.Amount) ?? amount ?? 0m,
                refund.Amount?.CurrencyCode ?? currency);
        }, "refund the payment");
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(string idempotencyKey, string customerId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToAddress(card.BillingAddress)
                }
            }
        };

        return await ExecuteAsync(async () =>
        {
            var token = await _client.Vault.CreatePaymentToken(idempotencyKey, body, ct: cancellationToken);

            if (token?.Id is null)
            {
                throw new PaymentException("PayPal did not return a vaulted card token.", 502);
            }

            var savedCard = token.PaymentSource?.Card;
            return new GatewayVaultedCard(
                token.Id,
                savedCard?.Brand?.Value,
                savedCard?.LastDigits,
                savedCard?.Expiry);
        }, "save the card");
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(async () =>
        {
            await _client.Vault.DeletePaymentToken(vaultId, ct: cancellationToken);
            return true;
        }, "remove the saved card");
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var startDate = FormatDate(from);
        var endDate = FormatDate(to);
        var results = new List<GatewayTransaction>();

        try
        {
            int page = 1;
            int totalPages;
            do
            {
                var response = await _client.TransactionSearch.SearchTransactions(
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
                    page: page,
                    ct: cancellationToken);

                foreach (var detail in response?.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    results.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.TransactionStatus,
                        ParseAmount(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        ParseAmount(info.FeeAmount),
                        ParseDate(info.TransactionInitiationDate)));
                }

                totalPages = response?.TotalPages ?? 1;
                page++;
            }
            while (page <= totalPages);
        }
        // SearchTransactions is the SDK's one raw-error operation.
        catch (SdkException<RawError> ex)
        {
            throw new PaymentException(
                $"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}).", 502, ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal was unreachable while searching transactions.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentException("PayPal returned a transaction report that could not be processed.", 502, ex);
        }

        return results;
    }

    // --- helpers ---

    private static CardRequest ToCardRequest(CardDetails card) => new CardRequest
    {
        Name = card.CardholderName,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = ToAddress(card.BillingAddress),
        // Process the raw card directly, only stepping up to 3-D Secure if the issuer
        // actually requires it — never forcing a browser challenge for a server-side flow.
        Attributes = new CardAttributes
        {
            Verification = new CardVerification
            {
                Method = OrdersCardVerificationMethod.ScaWhenRequired
            }
        }
    };

    private static Address? ToAddress(BillingAddress? a)
    {
        if (a is null)
        {
            return null;
        }
        return new Address
        {
            AddressLine1 = a.AddressLine1,
            AddressLine2 = a.AddressLine2,
            AdminArea2 = a.City,
            AdminArea1 = a.State,
            PostalCode = a.PostalCode,
            CountryCode = a.CountryCode
        };
    }

    private static AuthorizationWithAdditionalData? FindAuthorization(IEnumerable<PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null)
        {
            return null;
        }
        return purchaseUnits
            .SelectMany(pu => pu.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault(a => a?.Id is not null);
    }

    private static string Format(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;

    private static decimal? ParseAmount(Money? money)
    {
        if (money?.Value is null)
        {
            return null;
        }
        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    /// <summary>
    /// Runs an SDK call and converts every failure into a caller-safe <see cref="PaymentException"/>.
    /// The typed error accessors differ per operation family, so each family is caught explicitly.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string action,
        bool detectStaleAuthorization = false)
    {
        try
        {
            return await operation();
        }
        catch (PaymentException)
        {
            throw; // already translated (e.g. a null-field guard inside the operation)
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw Translate(ex.Error.TryGetError(out var e) ? e : null, action, detectStaleAuthorization);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw Translate(ex.Error.TryGetError(out var e) ? e : null, action, detectStaleAuthorization);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw Translate(ex.Error.TryGetError(out var e) ? e : null, action, detectStaleAuthorization);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw Translate(ex.Error.TryGetError(out var e) ? e : null, action, detectStaleAuthorization);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw Translate(ex.Error.TryGetError(out var e) ? e : null, action, detectStaleAuthorization);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw Translate(ex.Error.TryGetError(out var e) ? e : null, action, detectStaleAuthorization);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw Translate(ex.Error.TryGetError1(out var e) ? e : null, action);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw Translate(ex.Error.TryGetError1(out var e) ? e : null, action);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException($"PayPal was unreachable while trying to {action}.", 502, ex);
        }
        catch (JsonException ex)
        {
            // A broken success body — outcome genuinely unknown.
            throw new PaymentException(
                $"PayPal returned a response that could not be processed while trying to {action}.", 502, ex);
        }
    }

    private static PaymentException Translate(Error? error, string action, bool detectStaleAuthorization = false)
    {
        var issues = error?.Details?.Select(d => d.Issue).Where(i => !string.IsNullOrWhiteSpace(i)).ToArray()
                     ?? Array.Empty<string>();

        if (detectStaleAuthorization && issues.Any(IsStaleAuthorizationIssue))
        {
            return new StaleAuthorizationException(
                "The authorization has expired and must be renewed before the payment can be captured.");
        }

        var message = BuildMessage(error?.Name, error?.Message, issues);
        // A rejection PayPal describes is something the caller/operator can act on -> 4xx.
        return new PaymentException($"PayPal could not {action}: {message}", 400);
    }

    private static PaymentException Translate(Error1? error, string action)
    {
        var issues = error?.Details?.Select(d => d.Issue).Where(i => !string.IsNullOrWhiteSpace(i)).ToArray()
                     ?? Array.Empty<string>();
        var message = BuildMessage(error?.Name, error?.Message, issues);
        return new PaymentException($"PayPal could not {action}: {message}", 400);
    }

    private static bool IsStaleAuthorizationIssue(string issue) =>
        issue.Contains("EXPIR", StringComparison.OrdinalIgnoreCase) ||
        issue.Contains("REAUTH", StringComparison.OrdinalIgnoreCase);

    private static string BuildMessage(string? name, string? message, IReadOnlyCollection<string> issues)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(name)) parts.Add(name!);
        if (issues.Count > 0) parts.Add(string.Join("; ", issues));
        else if (!string.IsNullOrWhiteSpace(message)) parts.Add(message!);

        return parts.Count > 0 ? string.Join(" - ", parts) : "the request was rejected.";
    }
}
