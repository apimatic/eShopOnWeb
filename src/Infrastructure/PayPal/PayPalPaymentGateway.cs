using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Requests.Orders;
using PayPalServerSdk.Requests.Payments;
using PayPalServerSdk.Requests.TransactionSearch;
using PayPalServerSdk.Requests.Vault;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single seam over PayPal. Every PayPal call goes through here; the SDK types never leak past
/// this class. Provider failures are translated into <see cref="PaymentGatewayException"/>.
/// </summary>
public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly HashSet<string> ZeroDecimalCurrencies =
        new(StringComparer.OrdinalIgnoreCase) { "JPY", "HUF", "TWD" };

    // Per-call budget (whole call, across any retries). The only thing that bounds a whole call.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    // PayPal's TransactionSearch supports at most a 31-day window.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int SearchPageSize = 500;
    private const int MaxPagesPerWindow = 2000; // safety backstop against an unbounded page loop

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ---------------------------------------------------------------- Authorize

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeCommand command, CancellationToken cancellationToken)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = command.CurrencyCode,
                        Value = FormatMoney(command.Amount, command.CurrencyCode)
                    },
                    CustomId = command.ReconciliationReference,
                    InvoiceId = command.ReconciliationReference,
                    Description = command.Description
                }
            },
            PaymentSource = BuildPaymentSource(command)
        };

        var request = new CreateOrderRequest
        {
            Body = body,
            PayPalRequestId = command.IdempotencyKey,
            Prefer = "return=representation"
        };

        Order order;
        try
        {
            order = await Bounded(t => _client.Orders.CreateOrder(request, cancellationToken: t), "authorize", cancellationToken);
        }
        catch (ApiException<CreateOrderError> ex)
        {
            throw MapTyped(ex.StatusCode, ex.Error.TryGetError(out var e) ? e : null, "authorize", ex);
        }
        catch (Exception ex)
        {
            throw MapTransport(ex, "authorize");
        }

        return InterpretAuthorization(order);
    }

    private AuthorizationResult InterpretAuthorization(Order order)
    {
        // A card challenge (3DS / payer approval) is out of scope by mandate — stop and report.
        if (order.Status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (payer action / 3DS). " +
                "This integration does not support an approval round-trip.");
        }

        var authorization = order.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException(
                $"PayPal authorize completed with status '{order.Status?.Value ?? "unknown"}' but returned no authorization to hold funds.",
                PaymentGatewayFailureKind.CallerError, 422, order.Status?.Value);
        }

        if (authorization.Status == AuthorizationStatus.Denied)
        {
            throw new PaymentGatewayException(
                "The card authorization was denied by PayPal.",
                PaymentGatewayFailureKind.CallerError, 422, "DENIED");
        }

        return new AuthorizationResult
        {
            PayPalOrderId = order.Id!,
            AuthorizationId = authorization.Id!,
            AuthorizationStatus = authorization.Status?.Value,
            ExpiresAt = ParseTime(authorization.ExpirationTime),
            OrderStatus = order.Status?.Value
        };
    }

    private static PaymentSource BuildPaymentSource(AuthorizeCommand command)
    {
        if (!string.IsNullOrEmpty(command.VaultId))
        {
            // Pay with a saved (vaulted) card — reference the vault token only.
            return new PaymentSource { Card = new CardRequest { VaultId = command.VaultId } };
        }

        var card = command.Card
            ?? throw new PaymentGatewayException("No card or saved card was supplied to authorize the order.",
                PaymentGatewayFailureKind.CallerError, 400);

        return new PaymentSource
        {
            Card = new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = BuildAddress(card)
            }
        };
    }

    private static Address? BuildAddress(CardDetails card)
    {
        if (string.IsNullOrEmpty(card.BillingCountryCode)) return null;
        return new Address
        {
            CountryCode = card.BillingCountryCode!,
            AddressLine1 = card.BillingAddressLine1,
            AddressLine2 = card.BillingAddressLine2,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode
        };
    }

    // ---------------------------------------------------------------- Get / Reauthorize

    public async Task<AuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        var request = new GetAuthorizedPaymentRequest { AuthorizationId = authorizationId };
        try
        {
            var auth = await Bounded(t => _client.Payments.GetAuthorizedPayment(request, cancellationToken: t), "get-authorization", cancellationToken);
            return new AuthorizationInfo
            {
                AuthorizationId = auth.Id ?? authorizationId,
                Status = auth.Status?.Value,
                ExpiresAt = ParseTime(auth.ExpirationTime)
            };
        }
        catch (ApiException<GetAuthorizedPaymentError> ex)
        {
            throw MapTyped(ex.StatusCode, ex.Error.TryGetError(out var e) ? e : null, "get-authorization", ex);
        }
        catch (Exception ex)
        {
            throw MapTransport(ex, "get-authorization");
        }
    }

    public async Task<AuthorizationInfo> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken)
    {
        var request = new ReauthorizePaymentRequest
        {
            AuthorizationId = authorizationId,
            PayPalRequestId = idempotencyKey,
            Prefer = "return=representation",
            Body = new ReauthorizeRequest
            {
                Amount = new Money { CurrencyCode = currencyCode, Value = FormatMoney(amount, currencyCode) }
            }
        };
        try
        {
            var auth = await Bounded(t => _client.Payments.ReauthorizePayment(request, cancellationToken: t), "reauthorize", cancellationToken);
            return new AuthorizationInfo
            {
                AuthorizationId = auth.Id ?? authorizationId,
                Status = auth.Status?.Value,
                ExpiresAt = ParseTime(auth.ExpirationTime)
            };
        }
        catch (ApiException<ReauthorizePaymentError> ex)
        {
            throw MapTyped(ex.StatusCode, ex.Error.TryGetError(out var e) ? e : null, "reauthorize", ex);
        }
        catch (Exception ex)
        {
            throw MapTransport(ex, "reauthorize");
        }
    }

    // ---------------------------------------------------------------- Capture

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken)
    {
        var request = new CaptureAuthorizedPaymentRequest
        {
            AuthorizationId = authorizationId,
            PayPalRequestId = idempotencyKey,
            Prefer = "return=representation",
            Body = new CaptureRequest
            {
                Amount = new Money { CurrencyCode = currencyCode, Value = FormatMoney(amount, currencyCode) },
                FinalCapture = true
            }
        };

        CapturedPayment captured;
        try
        {
            captured = await Bounded(t => _client.Payments.CaptureAuthorizedPayment(request, cancellationToken: t), "capture", cancellationToken);
        }
        catch (ApiException<CaptureAuthorizedPaymentError> ex)
        {
            throw MapTyped(ex.StatusCode, ex.Error.TryGetError(out var e) ? e : null, "capture", ex);
        }
        catch (Exception ex)
        {
            throw MapTransport(ex, "capture");
        }

        if (captured.Id is null)
        {
            throw new PaymentGatewayException("PayPal capture returned no capture id.",
                PaymentGatewayFailureKind.Unknown);
        }

        var breakdown = captured.SellerReceivableBreakdown;
        return new CaptureResult
        {
            CaptureId = captured.Id!,
            Status = captured.Status?.Value,
            CapturedAmount = ParseMoney(captured.Amount?.Value) ?? amount,
            PayPalFee = ParseMoney(breakdown?.PaypalFee?.Value),
            NetAmount = ParseMoney(breakdown?.NetAmount?.Value)
        };
    }

    // ---------------------------------------------------------------- Void

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var request = new VoidPaymentRequest
        {
            AuthorizationId = authorizationId,
            PayPalRequestId = idempotencyKey
        };
        try
        {
            await Bounded(t => _client.Payments.VoidPayment(request, cancellationToken: t), "void", cancellationToken);
        }
        catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300)
        {
            // A successful void returns 204 No Content; the SDK cannot deserialize the (empty) body into
            // PaymentAuthorization. The 2xx status means the hold was released — treat it as success.
            _logger.LogInformation("Void returned {Status} with no body (success).", (int)ex.StatusCode);
        }
        catch (ApiException<VoidPaymentError> ex)
        {
            throw MapTyped(ex.StatusCode, ex.Error.TryGetError(out var e) ? e : null, "void", ex);
        }
        catch (Exception ex)
        {
            throw MapTransport(ex, "void");
        }
    }

    // ---------------------------------------------------------------- Refund

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken)
    {
        var refundBody = amount is null
            ? new RefundRequest()
            : new RefundRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatMoney(amount.Value, currencyCode) } };

        var request = new RefundCapturedPaymentRequest
        {
            CaptureId = captureId,
            PayPalRequestId = idempotencyKey,
            Prefer = "return=representation",
            Body = refundBody
        };

        Refund refund;
        try
        {
            refund = await Bounded(t => _client.Payments.RefundCapturedPayment(request, cancellationToken: t), "refund", cancellationToken);
        }
        catch (ApiException<RefundCapturedPaymentError> ex)
        {
            throw MapTyped(ex.StatusCode, ex.Error.TryGetError(out var e) ? e : null, "refund", ex);
        }
        catch (Exception ex)
        {
            throw MapTransport(ex, "refund");
        }

        if (refund.Id is null)
        {
            throw new PaymentGatewayException("PayPal refund returned no refund id.", PaymentGatewayFailureKind.Unknown);
        }

        return new RefundResult
        {
            RefundId = refund.Id!,
            Status = refund.Status?.Value,
            Amount = ParseMoney(refund.Amount?.Value) ?? amount ?? 0m
        };
    }

    // ---------------------------------------------------------------- Vault

    public async Task<VaultCardResult> VaultCardAsync(VaultCardCommand command, CancellationToken cancellationToken)
    {
        var customer = string.IsNullOrEmpty(command.PayPalCustomerId)
            ? new Customer { MerchantCustomerId = command.MerchantCustomerId }
            : new Customer { Id = command.PayPalCustomerId };

        var request = new CreatePaymentTokenRequest
        {
            PayPalRequestId = command.IdempotencyKey,
            Body = new PaymentTokenRequest
            {
                Customer = customer,
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Number = command.Card.Number,
                        Expiry = command.Card.Expiry,
                        SecurityCode = command.Card.SecurityCode,
                        Name = command.Card.CardholderName,
                        BillingAddress = BuildAddress(command.Card)
                    }
                }
            }
        };

        PaymentTokenResponse response;
        try
        {
            response = await Bounded(t => _client.Vault.CreatePaymentToken(request, cancellationToken: t), "vault-card", cancellationToken);
        }
        catch (ApiException<CreatePaymentTokenError> ex)
        {
            throw MapTyped(ex.StatusCode, ex.Error.TryGetError(out var e) ? e : null, "vault-card", ex);
        }
        catch (Exception ex)
        {
            throw MapTransport(ex, "vault-card");
        }

        if (response.Id is null)
        {
            throw new PaymentGatewayException("PayPal returned no vault id for the saved card.", PaymentGatewayFailureKind.Unknown);
        }

        var card = response.PaymentSource?.Card;
        return new VaultCardResult
        {
            VaultId = response.Id!,
            PayPalCustomerId = response.Customer?.Id ?? command.PayPalCustomerId ?? command.MerchantCustomerId,
            Brand = card?.Brand?.Value,
            LastDigits = card?.LastDigits,
            Expiry = card?.Expiry,
            CardholderName = card?.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        var request = new DeletePaymentTokenRequest { Id = vaultId };
        try
        {
            await Bounded(async t =>
            {
                await _client.Vault.DeletePaymentToken(request, cancellationToken: t);
                return true;
            }, "delete-vault-card", cancellationToken);
        }
        catch (ApiException<DeletePaymentTokenError> ex)
        {
            throw MapTyped(ex.StatusCode, ex.Error.TryGetError(out var e) ? e : null, "delete-vault-card", ex);
        }
        catch (Exception ex)
        {
            throw MapTransport(ex, "delete-vault-card");
        }
    }

    // ---------------------------------------------------------------- Reconciliation

    public async Task<ReconciliationReport> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var transactions = new List<ReconciliationTransaction>();
        var pagesFetched = 0;
        var complete = true;
        int? truncatedAtPage = null;

        foreach (var (windowStart, windowEnd) in SplitIntoWindows(from, to))
        {
            var page = 1;
            var totalPages = 1;

            do
            {
                var request = new SearchTransactionsRequest
                {
                    StartDate = FormatTimestamp(windowStart),
                    EndDate = FormatTimestamp(windowEnd),
                    Fields = "all",
                    BalanceAffectingRecordsOnly = "N",
                    PageSize = SearchPageSize,
                    Page = page
                };

                SearchResponse response;
                try
                {
                    response = await Bounded(t => _client.TransactionSearch.SearchTransactions(request, cancellationToken: t), "reconciliation", cancellationToken);
                }
                catch (ApiException<RawError> ex)
                {
                    throw MapRaw(ex.StatusCode, "reconciliation", ex);
                }
                catch (Exception ex)
                {
                    throw MapTransport(ex, "reconciliation");
                }

                pagesFetched++;
                totalPages = response.TotalPages ?? 1;

                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    transactions.Add(new ReconciliationTransaction
                    {
                        TransactionId = info?.TransactionId,
                        Status = info?.TransactionStatus,
                        EventCode = info?.TransactionEventCode,
                        Amount = ParseMoney(info?.TransactionAmount?.Value),
                        CurrencyCode = info?.TransactionAmount?.CurrencyCode,
                        FeeAmount = ParseMoney(info?.FeeAmount?.Value),
                        CustomField = info?.CustomField,
                        InvoiceId = info?.InvoiceId,
                        InitiationDate = ParseTime(info?.TransactionInitiationDate)
                    });
                }

                page++;

                if (page > MaxPagesPerWindow)
                {
                    complete = false;
                    truncatedAtPage = page - 1;
                    _logger.LogWarning("Reconciliation truncated at page {Page} for window {Start}..{End}", page - 1, windowStart, windowEnd);
                    break;
                }
            }
            while (page <= totalPages);
        }

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Transactions = transactions,
            PagesFetched = pagesFetched,
            TotalItems = transactions.Count,
            Complete = complete,
            TruncatedAtPage = truncatedAtPage
        };
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitIntoWindows(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            yield return (from, to);
            yield break;
        }

        var cursor = from;
        while (cursor < to)
        {
            var next = cursor + MaxSearchWindow;
            if (next > to) next = to;
            yield return (cursor, next);
            cursor = next;
        }
    }

    // ---------------------------------------------------------------- Call bounding

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, string operation, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Our own budget elapsed (not the caller's cancellation). The write may still have landed.
            throw new PaymentGatewayException(
                $"PayPal {operation} did not complete within {CallBudget.TotalSeconds:0}s; outcome unknown.",
                PaymentGatewayFailureKind.Unknown);
        }
    }

    // ---------------------------------------------------------------- Error translation

    private PaymentGatewayException MapTyped(HttpStatusCode status, Error? error, string operation, Exception inner)
    {
        var s = (int)status;
        var kind = ClassifyStatus(s);
        var issue = error?.Details?.FirstOrDefault();
        var detail = issue is null ? null : $"{issue.Issue}{(string.IsNullOrEmpty(issue.Description) ? "" : $": {issue.Description}")}";
        var message = error is null
            ? $"PayPal {operation} failed with HTTP {s}."
            : $"PayPal {operation} failed: {error.Name}{(detail is null ? "" : $" ({detail})")} — {error.Message}";

        _logger.LogError("PayPal {Operation} error: status={Status} name={Name} issue={Issue} debugId={DebugId}",
            operation, s, error?.Name, issue?.Issue, error?.DebugId);

        return new PaymentGatewayException(message, kind, s, error?.Name, error?.DebugId, inner);
    }

    private PaymentGatewayException MapRaw(HttpStatusCode status, string operation, ApiException<RawError> ex)
    {
        var s = (int)status;
        _logger.LogError("PayPal {Operation} error: status={Status} body={Body}", operation, s, SafeReadRaw(ex.Error));
        return new PaymentGatewayException($"PayPal {operation} failed with HTTP {s}.", ClassifyStatus(s), s, innerException: ex);
    }

    private Exception MapTransport(Exception ex, string operation)
    {
        switch (ex)
        {
            case PaymentGatewayException pge:
                return pge;
            case OperationCanceledException:
                return ex; // caller cancellation — never wrapped
            case ResponseDeserializationException rde:
            {
                var s = (int)rde.StatusCode;
                var kind = (s >= 200 && s < 300) ? PaymentGatewayFailureKind.Unknown : ClassifyStatus(s);
                _logger.LogError(rde, "PayPal {Operation}: undeserializable response, status={Status}", operation, s);
                return new PaymentGatewayException(
                    "The payment provider returned a response that could not be processed.", kind, s, innerException: rde);
            }
            case SdkTimeoutException:
                _logger.LogError(ex, "PayPal {Operation} timed out", operation);
                return new PaymentGatewayException($"PayPal {operation} timed out; outcome unknown.", PaymentGatewayFailureKind.Unknown, innerException: ex);
            case SdkConnectionException:
                _logger.LogError(ex, "PayPal {Operation} could not reach the provider", operation);
                return new PaymentGatewayException($"PayPal {operation} could not reach the provider; outcome unknown.", PaymentGatewayFailureKind.Unknown, innerException: ex);
            case AuthSchemeException:
                _logger.LogError(ex, "PayPal {Operation}: credentials could not be applied", operation);
                return new PaymentGatewayException("PayPal credentials could not be applied.", PaymentGatewayFailureKind.ProviderUnavailable, innerException: ex);
            case SdkException:
                _logger.LogError(ex, "PayPal {Operation} failed", operation);
                return new PaymentGatewayException($"PayPal {operation} failed.", PaymentGatewayFailureKind.ProviderUnavailable, innerException: ex);
            default:
                return ex;
        }
    }

    private static PaymentGatewayFailureKind ClassifyStatus(int status) =>
        status is 401 or 403 or 429 || status >= 500
            ? PaymentGatewayFailureKind.ProviderUnavailable
            : (status >= 400 && status < 500 ? PaymentGatewayFailureKind.CallerError : PaymentGatewayFailureKind.ProviderUnavailable);

    private static string SafeReadRaw(RawError raw)
    {
        try { return raw.ReadAsString(); }
        catch { return "(unreadable body)"; }
    }

    // ---------------------------------------------------------------- Formatting helpers

    private static string FormatMoney(decimal amount, string currency)
    {
        var decimals = ZeroDecimalCurrencies.Contains(currency) ? 0 : 2;
        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static decimal? ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
}
