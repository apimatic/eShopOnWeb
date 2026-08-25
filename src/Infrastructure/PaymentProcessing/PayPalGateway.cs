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
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

/// <summary>
/// Talks to PayPal for direct-card authorize/capture/void/refund, card vaulting, and transaction
/// reporting. Every write call carries a caller-supplied idempotency key as PayPal's
/// <c>PayPal-Request-Id</c> header. Every call requests <c>return=representation</c> where the
/// caller needs more than id/status back, since PayPal's default <c>return=minimal</c> omits
/// nested resources such as an order's authorizations.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(string requestId, decimal amount, string currency,
        CardDetails? card, string? vaultId, CancellationToken ct)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = FormatAmount(amount)
                    }
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = BuildCardRequest(card, vaultId)
            }
        };

        var order = await ExecuteAsync<Order, CreateOrderError>(
            () => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct),
            err =>
            {
                err.TryGetError(out var typed);
                err.TryGetRawError(out var raw);
                return ClassifyTwoWay(typed, raw, "authorize the payment");
            },
            "authorize the payment");

        if (order.Status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentActionRequiredException(
                $"PayPal requires additional shopper verification for order {order.Id} which this server-to-server integration does not support.");
        }

        var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException(
                $"PayPal did not return an authorization for order {order.Id} (status: {order.Status?.Value}).");
        }

        return new AuthorizationResult(
            order.Id ?? string.Empty,
            authorization.Id,
            authorization.Status?.Value ?? "UNKNOWN",
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(string requestId, string authorizationId, decimal amount,
        string currency, CancellationToken ct)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
        };

        var result = await ExecuteAsync<PaymentAuthorization, ReauthorizePaymentError>(
            () => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct),
            err =>
            {
                err.TryGetError(out var typed);
                err.TryGetNoContent(out var noContent);
                err.TryGetRawError(out var raw);
                return ClassifyThreeWay(typed, noContent, raw, "renew the payment authorization");
            },
            "renew the payment authorization");

        return new ReauthorizationResult(result.Id ?? authorizationId, result.Status?.Value ?? "UNKNOWN",
            ParseDate(result.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAsync(string requestId, string authorizationId, CancellationToken ct)
    {
        var capture = await ExecuteAsync<CapturedPayment, CaptureAuthorizedPaymentError>(
            () => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct),
            err =>
            {
                err.TryGetError(out var typed);
                err.TryGetNoContent(out var noContent);
                err.TryGetRawError(out var raw);
                return ClassifyThreeWay(typed, noContent, raw, "capture the authorized payment");
            },
            "capture the authorized payment");

        var breakdown = capture.SellerReceivableBreakdown;
        return new CaptureResult(
            capture.Id ?? string.Empty,
            capture.Status?.Value ?? "UNKNOWN",
            ParseMoneyOrThrow(capture.Amount, "captured amount"),
            capture.Amount?.CurrencyCode ?? string.Empty,
            ParseMoney(breakdown?.PaypalFee),
            ParseMoney(breakdown?.NetAmount));
    }

    public async Task VoidAsync(string requestId, string authorizationId, CancellationToken ct)
    {
        await ExecuteAsync<PaymentAuthorization, VoidPaymentError>(
            () => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: requestId,
                prefer: "return=representation",
                ct: ct),
            err =>
            {
                err.TryGetError(out var typed);
                err.TryGetNoContent(out var noContent);
                err.TryGetRawError(out var raw);
                return ClassifyThreeWay(typed, noContent, raw, "void the payment authorization");
            },
            "void the payment authorization");
    }

    public async Task<RefundResult> RefundAsync(string idempotencyKey, string captureId, decimal amount,
        string currency, CancellationToken ct)
    {
        var body = new RefundRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
        };

        var refund = await ExecuteAsync<Refund, RefundCapturedPaymentError>(
            () => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct),
            err =>
            {
                err.TryGetError(out var typed);
                err.TryGetNoContent(out var noContent);
                err.TryGetRawError(out var raw);
                return ClassifyThreeWay(typed, noContent, raw, "refund the captured payment");
            },
            "refund the captured payment");

        return new RefundResult(
            refund.Id ?? string.Empty,
            refund.Status?.Value ?? "UNKNOWN",
            ParseMoneyOrThrow(refund.Amount, "refund amount"),
            refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<VaultTokenResult> CreateVaultTokenAsync(string requestId, CardDetails card, CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = BuildBillingAddress(card)
                }
            }
        };

        var response = await ExecuteAsync<PaymentTokenResponse, CreatePaymentTokenError>(
            () => _client.Vault.CreatePaymentToken(requestId, body, ct: ct),
            err =>
            {
                err.TryGetError1(out var typed);
                err.TryGetRawError(out var raw);
                return ClassifyTwoWay(typed, raw, "save the card");
            },
            "save the card");

        if (response.Id is null)
        {
            throw new PaymentGatewayException("PayPal did not return a vault token id for the saved card.");
        }

        var cardEntity = response.PaymentSource?.Card;
        return new VaultTokenResult(response.Id, cardEntity?.Brand?.Value, cardEntity?.LastDigits, cardEntity?.Expiry);
    }

    public async Task DeleteVaultTokenAsync(string vaultId, CancellationToken ct)
    {
        await ExecuteAsync<DeletePaymentTokenError>(
            () => _client.Vault.DeletePaymentToken(vaultId, ct: ct),
            err =>
            {
                err.TryGetError1(out var typed);
                err.TryGetRawError(out var raw);
                return ClassifyTwoWay(typed, raw, "delete the saved card");
            },
            "delete the saved card");
    }

    public async Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        var transactions = new List<TransactionRecord>();
        var warnings = new List<string>();

        var chunkStart = from;
        while (chunkStart < to)
        {
            var chunkEnd = chunkStart.AddDays(31) < to ? chunkStart.AddDays(31) : to;
            await SearchChunkAsync(chunkStart, chunkEnd, transactions, warnings, ct, depth: 0);
            chunkStart = chunkEnd;
        }

        return new TransactionSearchResult(transactions, warnings);
    }

    /// <summary>
    /// PayPal's actual max date-range span per <c>SearchTransactions</c> call is not documented by
    /// the SDK (see paypal-plan.md, Area 7). We chunk defensively at 31 days and, if a chunk still
    /// fails, halve it and retry rather than failing the whole report — a partial, logged result
    /// beats none for a reconciliation pass.
    /// </summary>
    private async Task SearchChunkAsync(DateTimeOffset from, DateTimeOffset to, List<TransactionRecord> transactions,
        List<string> warnings, CancellationToken ct, int depth)
    {
        var page = 1;
        while (true)
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: FormatDate(from),
                    endDate: FormatDate(to),
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    fields: "all",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: page,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                if (depth < 3 && to - from > TimeSpan.FromHours(1))
                {
                    var mid = from + (to - from) / 2;
                    warnings.Add(
                        $"Transaction search for {from:O}..{to:O} failed (HTTP {(int)ex.Error.StatusCode}); retrying as two smaller windows.");
                    await SearchChunkAsync(from, mid, transactions, warnings, ct, depth + 1);
                    await SearchChunkAsync(mid, to, transactions, warnings, ct, depth + 1);
                    return;
                }

                warnings.Add($"Transaction search for {from:O}..{to:O} failed (HTTP {(int)ex.Error.StatusCode}) and was skipped.");
                return;
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException("PayPal returned a response that could not be processed while searching transactions.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new PaymentGatewayException("Unable to reach PayPal while searching transactions.", ex);
            }

            foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info?.TransactionId is null) continue;

                transactions.Add(new TransactionRecord(
                    info.TransactionId,
                    ParseMoney(info.TransactionAmount),
                    info.TransactionAmount?.CurrencyCode,
                    info.TransactionStatus,
                    info.TransactionInitiationDate));
            }

            if (response.TransactionDetails is null || response.TransactionDetails.Count == 0) break;
            if (response.TotalPages is null || page >= response.TotalPages) break;
            page++;
        }
    }

    private static CardRequest BuildCardRequest(CardDetails? card, string? vaultId)
    {
        if (vaultId is not null)
        {
            return new CardRequest { VaultId = vaultId };
        }

        if (card is null)
        {
            throw new PaymentOperationNotAllowedException("Either card details or a vaulted card id must be supplied.");
        }

        return new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = BuildBillingAddress(card)
        };
    }

    private static Address BuildBillingAddress(CardDetails card) => new()
    {
        AddressLine1 = card.AddressLine1,
        AddressLine2 = card.AddressLine2,
        AdminArea2 = card.City,
        AdminArea1 = card.State,
        PostalCode = card.PostalCode,
        CountryCode = card.CountryCode
    };

    private static string Describe(Error error)
    {
        var detail = error.Details?.FirstOrDefault();
        return detail is null ? $"{error.Name}: {error.Message}" : $"{error.Name}: {error.Message} ({detail.Issue}: {detail.Description})";
    }

    private static string Describe(Error1 error)
    {
        var detail = error.Details?.FirstOrDefault();
        return detail is null ? $"{error.Name}: {error.Message}" : $"{error.Name}: {error.Message} ({detail.Issue}: {detail.Description})";
    }

    private static Exception ClassifyTwoWay(Error? businessError, RawError? rawError, string action)
    {
        if (businessError is not null)
            return new PaymentOperationNotAllowedException($"PayPal rejected the request to {action}: {Describe(businessError)}");
        if (rawError is not null)
            return new PaymentGatewayException($"PayPal returned an unexpected error (HTTP {(int)rawError.StatusCode}) while trying to {action}.");
        return new PaymentGatewayException($"PayPal returned an unrecognized error while trying to {action}.");
    }

    private static Exception ClassifyTwoWay(Error1? businessError, RawError? rawError, string action)
    {
        if (businessError is not null)
            return new PaymentOperationNotAllowedException($"PayPal rejected the request to {action}: {Describe(businessError)}");
        if (rawError is not null)
            return new PaymentGatewayException($"PayPal returned an unexpected error (HTTP {(int)rawError.StatusCode}) while trying to {action}.");
        return new PaymentGatewayException($"PayPal returned an unrecognized error while trying to {action}.");
    }

    private static Exception ClassifyThreeWay(Error? businessError, RawError? noContent, RawError? rawError, string action)
    {
        if (businessError is not null)
            return new PaymentOperationNotAllowedException($"PayPal rejected the request to {action}: {Describe(businessError)}");
        if (noContent is not null)
            return new PaymentGatewayException($"PayPal returned an internal error (HTTP {(int)noContent.StatusCode}) while trying to {action}.");
        if (rawError is not null)
            return new PaymentGatewayException($"PayPal returned an unexpected error (HTTP {(int)rawError.StatusCode}) while trying to {action}.");
        return new PaymentGatewayException($"PayPal returned an unrecognized error while trying to {action}.");
    }

    private static async Task<T> ExecuteAsync<T, TError>(Func<Task<T>> call, Func<TError, Exception> classify, string action)
    {
        try
        {
            return await call();
        }
        catch (SdkException<TError> ex)
        {
            throw classify(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException($"PayPal returned a response that could not be processed while trying to {action}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException($"Unable to reach PayPal while trying to {action}.", ex);
        }
    }

    private static async Task ExecuteAsync<TError>(Func<Task> call, Func<TError, Exception> classify, string action)
    {
        try
        {
            await call();
        }
        catch (SdkException<TError> ex)
        {
            throw classify(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException($"PayPal returned a response that could not be processed while trying to {action}.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException($"Unable to reach PayPal while trying to {action}.", ex);
        }
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) => value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value is null) return null;
        return decimal.TryParse(money.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static decimal ParseMoneyOrThrow(Money? money, string what) =>
        ParseMoney(money) ?? throw new PaymentGatewayException($"PayPal did not return a valid {what}.");

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
}
