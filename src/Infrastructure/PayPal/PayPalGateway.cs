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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The only type that talks to the PayPal SDK. Maps PayPal's models onto the domain DTOs and translates
/// SDK/transport failures into <see cref="PaymentGatewayException"/> / <see cref="PaymentChallengeException"/>.
/// Card numbers/CVVs pass through transiently and are never logged or persisted here.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private const string Representation = "return=representation";
    private const int MaxReconciliationPages = 1000;

    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, IAppLogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AuthorizationOutcome> AuthorizeAsync(string invoiceId, decimal amount, string currency,
        string description, CardDetails? card, string? vaultId, string idempotencyKey, CancellationToken ct)
    {
        CardRequest cardRequest = vaultId is not null
            ? new CardRequest { VaultId = vaultId }
            : new CardRequest
            {
                Number = card!.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = MapAddress(card.BillingAddress),
            };

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits =
            [
                new PurchaseUnitRequest
                {
                    ReferenceId = "default",
                    InvoiceId = invoiceId,
                    CustomId = invoiceId,
                    Description = Truncate(description, 127),
                    Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = Format(amount) },
                }
            ],
            PaymentSource = new PaymentSource { Card = cardRequest },
        };

        var order = await RunAsync<Order, CreateOrderError>(
            () => _client.Orders.CreateOrder(null, idempotencyKey, null, null, null, body, Representation, ct: ct),
            "authorize order", e => e.TryGetError(out var err) ? err : null);

        var auth = ExtractAuthorization(order.PurchaseUnits);

        if (auth is null && string.Equals(order.Status?.Value, "APPROVED", StringComparison.OrdinalIgnoreCase))
        {
            var authResponse = await RunAsync<OrderAuthorizeResponse, AuthorizeOrderError>(
                () => _client.Orders.AuthorizeOrder(order.Id!, null, idempotencyKey, null, null, null,
                    Representation, ct: ct),
                "authorize order", e => e.TryGetError(out var err) ? err : null);
            auth = ExtractAuthorization(authResponse.PurchaseUnits);
        }

        if (auth is null)
        {
            if (RequiresPayerApproval(order.Status?.Value, order.Links))
                throw new PaymentChallengeException(
                    "This card requires the shopper to approve the payment in a browser (e.g. 3-D Secure), " +
                    "which this integration does not support.");
            throw new PaymentGatewayException(
                $"PayPal did not return an authorization for the order (status {order.Status?.Value ?? "unknown"}).");
        }

        return new AuthorizationOutcome(order.Id!, auth.Id!, auth.ExpirationTime, auth.Status?.Value ?? "CREATED");
    }

    public async Task<CaptureOutcome> CaptureAsync(string authorizationId, decimal amount, string currency,
        string invoiceId, string idempotencyKey, CancellationToken ct)
    {
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = Format(amount) },
            FinalCapture = true,
            InvoiceId = invoiceId,
        };

        var capture = await RunAsync<CapturedPayment, CaptureAuthorizedPaymentError>(
            () => _client.Payments.CaptureAuthorizedPayment(authorizationId, null, idempotencyKey, null, body,
                Representation, ct: ct),
            "capture payment", e => e.TryGetError(out var err) ? err : null);

        var breakdown = capture.SellerReceivableBreakdown;
        var captured = ParseMoney(capture.Amount) ?? ParseMoney(breakdown?.GrossAmount) ?? amount;
        return new CaptureOutcome(
            capture.Id!, captured,
            ParseMoney(breakdown?.PaypalFee),
            ParseMoney(breakdown?.NetAmount),
            capture.Status?.Value ?? "COMPLETED");
    }

    public async Task<ReauthorizeOutcome> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct)
    {
        var body = new ReauthorizeRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount) } };

        var auth = await RunAsync<PaymentAuthorization, ReauthorizePaymentError>(
            () => _client.Payments.ReauthorizePayment(authorizationId, idempotencyKey, null, body, Representation, ct: ct),
            "re-authorize payment", e => e.TryGetError(out var err) ? err : null);

        return new ReauthorizeOutcome(auth.Id!, auth.ExpirationTime, auth.Status?.Value ?? "CREATED");
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            await _client.Payments.VoidPayment(authorizationId, null, null, idempotencyKey, "return=minimal", ct: ct);
        }
        catch (JsonException)
        {
            // Void succeeds with HTTP 204 (No Content); the SDK cannot deserialize the empty body into
            // PaymentAuthorization and throws. An empty 2xx body here means the authorization was released.
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw Translate(ex.Error, e => e.TryGetError(out var err) ? err : null, "void authorization");
        }
        catch (AuthSchemeException ex)
        {
            throw new PaymentGatewayException("PayPal authentication failed for void authorization.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable (void authorization).", inner: ex);
        }
    }

    public async Task<RefundOutcome> RefundAsync(string captureId, decimal? amount, string currency, string invoiceId,
        string idempotencyKey, CancellationToken ct)
    {
        var body = new RefundRequest
        {
            InvoiceId = invoiceId,
            Amount = amount.HasValue ? new Money { CurrencyCode = currency, Value = Format(amount.Value) } : null,
        };

        var refund = await RunAsync<Refund, RefundCapturedPaymentError>(
            () => _client.Payments.RefundCapturedPayment(captureId, null, idempotencyKey, null, body, Representation, ct: ct),
            "refund payment", e => e.TryGetError(out var err) ? err : null);

        var refunded = ParseMoney(refund.Amount) ?? amount ?? 0m;
        return new RefundOutcome(refund.Id!, refunded, refund.Status?.Value ?? "COMPLETED");
    }

    public async Task<VaultedCard> VaultCardAsync(string? existingCustomerId, string merchantCustomerId,
        CardDetails card, string currency, CancellationToken ct)
    {
        // Vault the card by running a nominal authorization that stores it on success, then releasing the
        // hold — the card is saved without any money being taken. (The standalone v3 vault-without-purchase
        // endpoint is not available on this account; vaulting on a transaction is the supported path.)
        var invoiceId = $"eshop-vault-{Guid.NewGuid():N}";
        var requestId = Guid.NewGuid().ToString("N");

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits =
            [
                new PurchaseUnitRequest
                {
                    ReferenceId = "default",
                    InvoiceId = invoiceId,
                    CustomId = invoiceId,
                    Description = "Save card (verification)",
                    Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = "1.00" },
                }
            ],
            PaymentSource = new PaymentSource
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = MapAddress(card.BillingAddress),
                    Attributes = new CardAttributes
                    {
                        Vault = new VaultInstructionBase { StoreInVault = StoreInVaultInstruction.OnSuccess },
                        Customer = existingCustomerId is not null
                            ? new CardCustomerInformation { Id = existingCustomerId }
                            : null,
                    },
                },
            },
        };

        var order = await RunAsync<Order, CreateOrderError>(
            () => _client.Orders.CreateOrder(null, requestId, null, null, null, body, Representation, ct: ct),
            "save card", e => e.TryGetError(out var err) ? err : null);

        if (RequiresPayerApproval(order.Status?.Value, order.Links))
            throw new PaymentChallengeException(
                "Saving this card requires the shopper to approve it in a browser (e.g. 3-D Secure), " +
                "which this integration does not support.");

        var responseCard = order.PaymentSource?.Card;
        var vault = responseCard?.Attributes?.Vault;
        if (vault?.Id is null)
            throw new PaymentGatewayException(
                "PayPal accepted the card but did not return a vault id; the card was not saved.");

        // Release the verification hold best-effort — the card stays vaulted regardless.
        var auth = ExtractAuthorization(order.PurchaseUnits);
        if (auth?.Id is not null)
        {
            try
            {
                await VoidAsync(auth.Id, Guid.NewGuid().ToString("N"), ct);
            }
            catch (PaymentGatewayException ex)
            {
                _logger.LogWarning("Vaulted card {0} but could not release the verification hold: {1}",
                    vault.Id, ex.Message);
            }
        }

        var lastDigits = responseCard?.LastDigits ?? LastFour(card.Number);
        var brand = responseCard?.Brand?.Value ?? "CARD";
        return new VaultedCard(vault.Id, vault.Customer?.Id ?? existingCustomerId, brand, lastDigits,
            responseCard?.Expiry ?? card.Expiry, responseCard?.Name ?? card.CardholderName);
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
        => RunAsync<DeletePaymentTokenError>(
            () => _client.Vault.DeletePaymentToken(vaultId, ct: ct),
            "delete payment token", e => e.TryGetError(out var err) ? err : null);

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, string? currency, CancellationToken ct)
    {
        var results = new List<PayPalTransaction>();
        int page = 1;
        int totalPages = 1;

        do
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: Rfc3339(from),
                    endDate: Rfc3339(to),
                    transactionId: null, transactionType: null, transactionStatus: null, transactionAmount: null,
                    transactionCurrency: currency, paymentInstrumentType: null, storeId: null, terminalId: null,
                    fields: "transaction_info", balanceAffectingRecordsOnly: "Y", pageSize: 500, page: page, ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw FromRaw(ex.Error, "search transactions");
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException(
                    "PayPal returned a transaction-search response that could not be processed.", inner: ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new PaymentGatewayException("PayPal is unreachable (search transactions).", inner: ex);
            }

            totalPages = response.TotalPages ?? 1;
            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    results.Add(new PayPalTransaction(
                        info.TransactionId,
                        info.TransactionStatus,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        info.InvoiceId,
                        info.TransactionInitiationDate,
                        ParseMoney(info.FeeAmount)));
                }
            }

            page++;
        }
        while (page <= totalPages && page <= MaxReconciliationPages);

        return results;
    }

    // --- translation helpers ---

    private async Task<T> RunAsync<T, TError>(Func<Task<T>> call, string op, Func<TError, Error?> getError)
        where TError : PayPalServerSdk.Core.ErrorResponse.ApiError
    {
        try
        {
            return await call();
        }
        catch (SdkException<TError> ex)
        {
            throw Translate(ex.Error, getError, op);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException($"PayPal returned a response for {op} that could not be processed.",
                inner: ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new PaymentGatewayException($"PayPal authentication failed for {op}.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException($"PayPal is unreachable ({op}).", inner: ex);
        }
    }

    // Void/Delete return Task (no body) — a non-generic runner.
    private async Task RunAsync<TError>(Func<Task> call, string op, Func<TError, Error?> getError)
        where TError : PayPalServerSdk.Core.ErrorResponse.ApiError
    {
        try
        {
            await call();
        }
        catch (SdkException<TError> ex)
        {
            throw Translate(ex.Error, getError, op);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException($"PayPal returned a response for {op} that could not be processed.",
                inner: ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new PaymentGatewayException($"PayPal authentication failed for {op}.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException($"PayPal is unreachable ({op}).", inner: ex);
        }
    }

    private PaymentGatewayException Translate<TError>(TError error, Func<TError, Error?> getError, string op)
        where TError : PayPalServerSdk.Core.ErrorResponse.ApiError
    {
        var typed = getError(error);
        if (typed is not null)
        {
            var detail = typed.Details?.FirstOrDefault();
            var ex = new PaymentGatewayException(
                $"PayPal {op} failed: {typed.Name}" +
                (detail?.Issue is { } issue ? $" ({issue})" : "") +
                $" - {detail?.Description ?? typed.Message}",
                detail?.Issue, typed.DebugId);
            _logger.LogWarning("PayPal {0} failed: name={1} issue={2} debug_id={3}", op, typed.Name,
                detail?.Issue, typed.DebugId);
            return ex;
        }

        if (error.TryGetRawError(out var raw))
            return FromRaw(raw, op);

        return new PaymentGatewayException($"PayPal {op} failed with an unrecognised error shape.");
    }

    private PaymentGatewayException FromRaw(RawError raw, string op)
    {
        var status = (int)raw.StatusCode;
        _logger.LogWarning("PayPal {0} failed: HTTP {1}", op, status);
        return new PaymentGatewayException($"PayPal {op} failed (HTTP {status}).", statusCode: status);
    }

    // --- small mapping helpers ---

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
        => purchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
            .FirstOrDefault(a => a.Id is not null);

    private static bool RequiresPayerApproval(string? status, IReadOnlyList<LinkDescription>? links)
        => string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
           || (links?.Any(l => string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase)
                               || string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false);

    private static Address? MapAddress(CardBillingAddress? address)
    {
        if (address is null) return null;
        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode,
            CountryCode = string.IsNullOrWhiteSpace(address.CountryCode) ? "US" : address.CountryCode!,
        };
    }

    private static string Format(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
        => money is not null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;

    private static string Rfc3339(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string LastFour(string number)
        => number.Length >= 4 ? number[^4..] : number;

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value.Substring(0, max);
}
