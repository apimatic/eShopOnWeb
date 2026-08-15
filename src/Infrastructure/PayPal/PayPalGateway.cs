using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal SDK implementation of <see cref="IPayPalGateway"/>. All signatures, wire names, response
/// envelopes and error accessors follow the grounded contract sheet (paypal-plan.md). Raw card data is
/// only ever placed on outbound request models — never logged or persisted here.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;

    public PayPalGateway(PayPalServerSdkClient client, IOptions<PayPalSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeCommand command, CancellationToken cancellationToken = default)
    {
        var order = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = command.ReferenceId,
                    CustomId = command.ReferenceId,
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = _settings.Currency,
                        Value = FormatAmount(command.Amount),
                    },
                },
            },
            PaymentSource = new PaymentSource { Card = BuildCardRequest(command) },
        };

        try
        {
            var created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: command.RequestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: order,
                prefer: "return=representation",
                ct: cancellationToken);

            EnsureNoChallenge(created.Status, created.Links);
            var orderId = created.Id ?? string.Empty;

            var authorized = await _client.Orders.AuthorizeOrder(
                id: orderId,
                payPalMockResponse: null,
                payPalRequestId: command.RequestId,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cancellationToken);

            EnsureNoChallenge(authorized.Status, authorized.Links);

            var authorization = FirstAuthorization(authorized)
                ?? throw new PayPalGatewayException("PayPal did not return an authorization for the order.");

            return new PayPalAuthorizationResult(
                PayPalOrderId: authorized.Id ?? orderId,
                AuthorizationId: authorization.Id ?? string.Empty,
                Status: authorization.Status?.Value ?? authorized.Status?.Value ?? "UNKNOWN",
                Currency: _settings.Currency,
                ExpiresAt: ParseDate(authorization.ExpirationTime));
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw new PayPalGatewayException(ex.Error.TryGetError(out var e) ? Format(Read(e)) : DescribeRaw(ex.Error), ex);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw new PayPalGatewayException(ex.Error.TryGetError(out var e) ? Format(Read(e)) : DescribeRaw(ex.Error), ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = _settings.Currency, Value = FormatAmount(amount) },
        };

        try
        {
            var authorization = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);

            return new PayPalAuthorizationResult(
                PayPalOrderId: string.Empty,
                AuthorizationId: authorization.Id ?? string.Empty,
                Status: authorization.Status?.Value ?? "UNKNOWN",
                Currency: _settings.Currency,
                ExpiresAt: ParseDate(authorization.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw new PayPalGatewayException(ex.Error.TryGetError(out var e) ? Format(Read(e)) : DescribeRaw(ex.Error), ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        try
        {
            var captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: cancellationToken);

            var breakdown = captured.SellerReceivableBreakdown;
            return new PayPalCaptureResult(
                CaptureId: captured.Id ?? string.Empty,
                Status: captured.Status?.Value ?? "UNKNOWN",
                GrossAmount: ParseMoney(breakdown?.GrossAmount) ?? 0m,
                PayPalFee: ParseMoney(breakdown?.PaypalFee),
                NetAmount: ParseMoney(breakdown?.NetAmount),
                Currency: breakdown?.GrossAmount?.CurrencyCode ?? _settings.Currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                var payPalError = Read(e);
                if (IsExpiry(payPalError))
                {
                    throw new AuthorizationExpiredException(Format(payPalError));
                }

                throw new PayPalGatewayException(Format(payPalError), ex);
            }

            throw new PayPalGatewayException(DescribeRaw(ex.Error), ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Param-order trap: (authorizationId, payPalMockResponse, payPalAuthAssertion, payPalRequestId, ...)
            // — the request id is the 4th parameter. Named args keep it correct.
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: requestId,
                prefer: "return=representation",
                ct: cancellationToken);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw new PayPalGatewayException(ex.Error.TryGetError(out var e) ? Format(Read(e)) : DescribeRaw(ex.Error), ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        RefundRequest? body = amount is null
            ? null
            : new RefundRequest
            {
                Amount = new Money { CurrencyCode = _settings.Currency, Value = FormatAmount(amount.Value) },
            };

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: cancellationToken);

            return new PayPalRefundResult(
                RefundId: refund.Id ?? string.Empty,
                Status: refund.Status?.Value ?? "UNKNOWN",
                Amount: ParseMoney(refund.Amount) ?? amount ?? 0m,
                Currency: refund.Amount?.CurrencyCode ?? _settings.Currency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw new PayPalGatewayException(ex.Error.TryGetError(out var e) ? Format(Read(e)) : DescribeRaw(ex.Error), ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<PayPalVaultedCardResult> VaultCardAsync(PayPalCardInput card, CancellationToken cancellationToken = default)
    {
        var request = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = card.ExpiryYearMonth,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card),
                },
            },
        };

        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: request,
                ct: cancellationToken);

            var cardEntity = token.PaymentSource?.Card;
            return new PayPalVaultedCardResult(
                VaultId: token.Id ?? string.Empty,
                CustomerId: token.Customer?.Id,
                Brand: cardEntity?.Brand?.Value ?? "UNKNOWN",
                LastFourDigits: cardEntity?.LastDigits ?? LastFour(card.Number),
                ExpiryYearMonth: card.ExpiryYearMonth,
                CardholderName: card.CardholderName);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            // Vault operations expose the typed body via TryGetError1(out Error1), not TryGetError.
            throw new PayPalGatewayException(ex.Error.TryGetError1(out var e) ? Format(Read(e)) : DescribeRaw(ex.Error), ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw new PayPalGatewayException(ex.Error.TryGetError1(out var e) ? Format(Read(e)) : DescribeRaw(ex.Error), ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }
    }

    public async Task<IReadOnlyList<PayPalLedgerEntry>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<PayPalLedgerEntry>();
        var startDate = from.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var endDate = to.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var page = 1;

        try
        {
            while (true)
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

                var details = response.TransactionDetails;
                if (details is not null)
                {
                    foreach (var detail in details)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null)
                        {
                            continue;
                        }

                        results.Add(new PayPalLedgerEntry(
                            TransactionId: info.TransactionId ?? string.Empty,
                            Status: info.TransactionStatus ?? "UNKNOWN",
                            Amount: ParseMoney(info.TransactionAmount),
                            Currency: info.TransactionAmount?.CurrencyCode,
                            Date: ParseDate(info.TransactionInitiationDate ?? info.TransactionUpdatedDate)));
                    }
                }

                var totalPages = response.TotalPages ?? 0;
                if (details is null || details.Count == 0 || page >= totalPages)
                {
                    break;
                }

                page++;
            }
        }
        catch (SdkException<RawError> ex)
        {
            // SearchTransactions is the SDK's only Case-B operation — the error IS a RawError.
            throw new PayPalGatewayException($"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}).", ex);
        }
        catch (Exception ex) when (IsTransport(ex, cancellationToken))
        {
            throw Unreachable(ex);
        }
        catch (JsonException ex)
        {
            throw Unprocessable(ex);
        }

        return results;
    }

    // ----- request builders -----

    private static CardRequest BuildCardRequest(PayPalAuthorizeCommand command)
    {
        if (!string.IsNullOrEmpty(command.VaultId))
        {
            return new CardRequest { VaultId = command.VaultId };
        }

        var card = command.Card
            ?? throw new PayPalGatewayException("A card or a saved-card vault id is required to authorize a payment.");

        return new CardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.ExpiryYearMonth,
            SecurityCode = card.SecurityCode,
            BillingAddress = BuildAddress(card),
        };
    }

    private static Address BuildAddress(PayPalCardInput card) => new()
    {
        CountryCode = card.BillingCountryCode,
        AddressLine1 = card.BillingLine1,
        AdminArea2 = card.BillingCity,
        AdminArea1 = card.BillingState,
        PostalCode = card.BillingPostalCode,
    };

    // ----- response helpers -----

    private static AuthorizationWithAdditionalData? FirstAuthorization(OrderAuthorizeResponse authorized)
    {
        var units = authorized.PurchaseUnits;
        if (units is null)
        {
            return null;
        }

        foreach (var unit in units)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is not null)
            {
                foreach (var authorization in authorizations)
                {
                    return authorization;
                }
            }
        }

        return null;
    }

    private static void EnsureNoChallenge(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        if (status is not null && status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentChallengeRequiredException("PayPal requires the shopper to approve this payment in a browser.");
        }

        if (links is not null)
        {
            foreach (var link in links)
            {
                if (string.Equals(link.Rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PaymentChallengeRequiredException("PayPal requires the shopper to approve this payment in a browser.");
                }
            }
        }
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
        => money is not null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string LastFour(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return string.Empty;
        }

        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    // ----- error translation -----

    private readonly record struct PayPalError(string? Name, string? Message, string? Issue, string? DebugId);

    private static PayPalError Read(Error error)
        => new(error.Name, error.Message, FirstIssue(error.Details), error.DebugId);

    private static PayPalError Read(Error1 error)
        => new(error.Name, error.Message, FirstIssue(error.Details), error.DebugId);

    private static string? FirstIssue(IReadOnlyList<ErrorDetails>? details)
        => details is { Count: > 0 } ? details[0].Issue : null;

    private static string? FirstIssue(IReadOnlyList<ErrorDetails1>? details)
        => details is { Count: > 0 } ? details[0].Issue : null;

    private static string Format(PayPalError error)
        => $"{error.Name}: {error.Message} (issue: {error.Issue}, debug_id: {error.DebugId})";

    private static string DescribeRaw(ApiError error)
        => error.TryGetRawError(out var raw)
            ? $"PayPal returned an error (HTTP {(int)raw.StatusCode})."
            : "PayPal returned an unexpected error.";

    private static bool IsExpiry(PayPalError error)
        => (((error.Name ?? string.Empty) + " " + (error.Issue ?? string.Empty)).ToUpperInvariant()).Contains("EXPIRED");

    private static bool IsTransport(Exception ex, CancellationToken ct)
        => ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested);

    private static PayPalGatewayException Unreachable(Exception ex)
        => new("PayPal could not be reached.", ex);

    private static PayPalGatewayException Unprocessable(Exception ex)
        => new("PayPal returned a response that could not be processed.", ex);
}
