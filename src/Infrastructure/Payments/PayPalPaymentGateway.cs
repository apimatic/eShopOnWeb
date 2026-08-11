using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The one place that talks to PayPal, via the paypal-sdk (PayPalServerSdk) client. Translates the
/// SDK surface into the application-core <see cref="IPayPalPaymentGateway"/> abstraction and maps SDK
/// exceptions onto domain payment exceptions. Full card details flow straight to PayPal and are never
/// persisted or logged.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    private const string ReturnRepresentation = "return=representation";

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    private string Currency => _options.Currency;

    // ---- Authorize (hold) -------------------------------------------------------------------

    public async Task<CardAuthorizationResult> AuthorizeWithCardAsync(decimal amount, CardDetails card,
        bool storeInVault, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardRequest = new CardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = $"{card.ExpiryYear}-{card.ExpiryMonth}",
            SecurityCode = card.SecurityCode,
            BillingAddress = BuildAddress(card),
            Attributes = storeInVault
                ? new CardAttributes { Vault = new VaultInstructionBase { StoreInVault = StoreInVaultInstruction.OnSuccess } }
                : null
        };

        return await CreateAndAuthorizeAsync(amount, new PaymentSource { Card = cardRequest }, idempotencyKey,
            readVault: storeInVault, cancellationToken);
    }

    public async Task<CardAuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardRequest = new CardRequest { VaultId = vaultId };
        return await CreateAndAuthorizeAsync(amount, new PaymentSource { Card = cardRequest }, idempotencyKey,
            readVault: false, cancellationToken);
    }

    private async Task<CardAuthorizationResult> CreateAndAuthorizeAsync(decimal amount, PaymentSource paymentSource,
        string idempotencyKey, bool readVault, CancellationToken cancellationToken)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PaymentSource = paymentSource,
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

        // Step 1: create the order (intent = AUTHORIZE) with the card. Whether create-with-card already
        // produces an authorization is UNVERIFIED, so we read the auth from the create response first
        // and only call the explicit authorize step when the create response did not carry one.
        object createResponse = await CallAsync(
            () => _client.Orders.CreateOrder(null, idempotencyKey, null, null, null, orderRequest,
                prefer: ReturnRepresentation, ct: cancellationToken),
            "create order");

        GuardNoBrowserChallenge(ReadStatus(createResponse));

        var payPalOrderId = ReadString(createResponse, "Id")
            ?? throw new PaymentGatewayException("PayPal did not return an order id.");

        var auth = ExtractAuthorization(createResponse);
        object authoritativeResponse = createResponse;

        if (auth is null)
        {
            // Step 2: create did not auto-authorize — authorize the order explicitly to place the hold.
            object authorizeResponse = await CallAsync(
                () => _client.Orders.AuthorizeOrder(payPalOrderId, null, idempotencyKey + "-auth", null, null, null,
                    prefer: ReturnRepresentation, ct: cancellationToken),
                "authorize order");

            GuardNoBrowserChallenge(ReadStatus(authorizeResponse));
            authoritativeResponse = authorizeResponse;
            auth = ExtractAuthorization(authorizeResponse);
        }

        if (auth is null)
        {
            throw new PaymentGatewayException(
                "PayPal did not return an authorization for the order; the card may have been declined.");
        }

        string? vaultId = null, brand = null, last4 = null;
        if (readVault)
        {
            (vaultId, brand, last4) = ExtractVaultFromCard(authoritativeResponse) ?? ExtractVaultFromCard(createResponse) ?? (null, null, null);
        }

        return new CardAuthorizationResult(payPalOrderId, auth.Value.Id, auth.Value.Status,
            ParseDate(auth.Value.Expiry), vaultId, brand, last4);
    }

    // ---- Vault a card -----------------------------------------------------------------------

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default)
    {
        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = $"{card.ExpiryYear}-{card.ExpiryMonth}",
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card)
                }
            }
        };

        var requestId = $"vault-{Guid.NewGuid():N}";
        object response = await CallAsync(
            () => _client.Vault.CreatePaymentToken(requestId, body, ct: cancellationToken),
            "vault card");

        var vaultId = ReadString(response, "Id")
            ?? throw new PaymentGatewayException("PayPal did not return a vault id for the saved card.");

        var (brand, last4, expiry) = ExtractCardDescriptor(response);
        return new VaultCardResult(vaultId, brand, last4,
            ExpiryMonthOf(expiry) ?? card.ExpiryMonth, ExpiryYearOf(expiry) ?? card.ExpiryYear, card.CardholderName);
    }

    // ---- Capture (take money) ---------------------------------------------------------------

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        object response;
        try
        {
            // null body = full capture of the authorized amount.
            response = await _client.Payments.CaptureAuthorizedPayment(authorizationId, null, idempotencyKey, null, null,
                prefer: ReturnRepresentation, ct: cancellationToken);
        }
        catch (Exception ex) when (IsSdkException(ex))
        {
            if (IndicatesExpiredAuthorization(ex))
            {
                throw new AuthorizationExpiredException(
                    $"Authorization {authorizationId} has expired and must be renewed before capture.", ex);
            }
            throw new PaymentGatewayException($"PayPal capture failed: {DescribeSdkException(ex)}", ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException($"PayPal capture returned an unreadable response: {ex.Message}", ex);
        }

        var captureId = ReadString(response, "Id")
            ?? throw new PaymentGatewayException("PayPal did not return a capture id.");
        var status = ReadStatus(response) ?? "COMPLETED";
        var (grossAmount, currency) = ReadMoney(GetProperty(response, "Amount"));
        var (fee, net) = ReadSellerBreakdown(response);

        return new CaptureResult(captureId, status, grossAmount ?? 0m, currency ?? Currency, fee, net);
    }

    // ---- Reauthorize (renew a stale hold) ---------------------------------------------------

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        CancellationToken cancellationToken = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount) }
        };

        object response = await CallAsync(
            () => _client.Payments.ReauthorizePayment(authorizationId, $"reauth-{Guid.NewGuid():N}", null, body,
                prefer: ReturnRepresentation, ct: cancellationToken),
            "reauthorize");

        var newId = ReadString(response, "Id")
            ?? throw new PaymentGatewayException("PayPal did not return a renewed authorization id.");
        var status = ReadStatus(response) ?? "CREATED";
        var expiry = ParseDate(ReadString(response, "ExpirationTime"));
        return new AuthorizationResult(string.Empty, newId, status, expiry);
    }

    // ---- Void (cancel, release hold) --------------------------------------------------------

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Payments.VoidPayment(authorizationId, null, null, $"void-{Guid.NewGuid():N}",
                ct: cancellationToken);
        }
        catch (JsonException)
        {
            // A successful void returns HTTP 204 No Content; the SDK throws trying to deserialize the
            // empty body. The funds are released — treat it as success.
        }
        catch (Exception ex) when (IsSdkException(ex))
        {
            _logger.LogWarning(ex, "PayPal void authorization failed: {Detail}", DescribeSdkException(ex));
            throw new PaymentGatewayException($"PayPal void authorization failed: {DescribeSdkException(ex)}", ex);
        }
    }

    // ---- Refund -----------------------------------------------------------------------------

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        // null body = full refund; set Amount for a partial refund.
        RefundRequest? body = amount is decimal a
            ? new RefundRequest { Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(a) } }
            : null;

        // Scope the caller's idempotency key to the capture for the PayPal-Request-Id: the same key on
        // the same capture replays idempotently at PayPal, while the same key on a different capture
        // does not falsely collide in PayPal's global request-id namespace.
        var requestId = $"refund-{captureId}-{idempotencyKey}";
        object response = await CallAsync(
            () => _client.Payments.RefundCapturedPayment(captureId, null, requestId, null, body,
                prefer: ReturnRepresentation, ct: cancellationToken),
            "refund");

        var refundId = ReadString(response, "Id")
            ?? throw new PaymentGatewayException("PayPal did not return a refund id.");
        var status = ReadStatus(response) ?? "COMPLETED";
        var (refundAmount, currency) = ReadMoney(GetProperty(response, "Amount"));
        return new RefundResult(refundId, status, refundAmount ?? amount ?? 0m, currency ?? Currency);
    }

    // ---- Reconciliation (transaction search, all pages) -------------------------------------

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var start = FormatDate(from);
        var end = FormatDate(to);
        var results = new List<PayPalTransaction>();

        int page = 1;
        int totalPages;
        do
        {
            object response = await CallAsync(
                () => _client.TransactionSearch.SearchTransactions(start, end, null, null, null, null, null, null, null,
                    null, fields: "transaction_info", pageSize: 500, page: page, ct: cancellationToken),
                "transaction search");

            totalPages = ReadInt(response, "TotalPages") ?? 1;

            var details = GetProperty(response, "TransactionDetails") as IEnumerable;
            if (details is not null)
            {
                foreach (var detail in details)
                {
                    var info = GetProperty(detail, "TransactionInfo");
                    if (info is null) continue;

                    var (amount, currency) = ReadMoney(GetProperty(info, "TransactionAmount"));
                    results.Add(new PayPalTransaction(
                        ReadString(info, "TransactionId") ?? string.Empty,
                        ReadString(info, "TransactionStatus"),
                        amount,
                        currency,
                        ParseDate(ReadString(info, "TransactionInitiationDate")),
                        ReadString(info, "TransactionEventCode")));
                }
            }

            page++;
        } while (page <= totalPages);

        return results;
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private async Task<object> CallAsync<T>(Func<Task<T>> call, string operation) where T : class
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (IsSdkException(ex))
        {
            var detail = DescribeSdkException(ex);
            _logger.LogWarning(ex, "PayPal {Operation} failed: {Detail}", operation, detail);
            throw new PaymentGatewayException($"PayPal {operation} failed: {detail}", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "PayPal {Operation} returned an unreadable response.", operation);
            throw new PaymentGatewayException($"PayPal {operation} returned an unreadable response: {ex.Message}", ex);
        }
    }

    private static bool IsSdkException(Exception ex) =>
        ex.GetType().Namespace?.StartsWith("PayPalServerSdk", StringComparison.Ordinal) == true;

    /// <summary>
    /// Pulls PayPal's real error text (name / message / issues, or the raw body) out of an SDK
    /// exception, whose default Message is uninformative. Reflection keeps this independent of the
    /// exact generated error type for each operation.
    /// </summary>
    private static string DescribeSdkException(Exception ex)
    {
        var parts = new List<string>();
        var errorObj = GetProperty(ex, "Error");

        // Case B (transaction search) and the inherited RawError accessor: RawError with a body.
        var raw = TryInvokeTryGet(errorObj, "TryGetRawError") ?? (errorObj?.GetType().Name == "RawError" ? errorObj : null);
        if (raw is not null)
        {
            var status = ReadString(raw, "StatusCode");
            if (!string.IsNullOrEmpty(status)) parts.Add($"HTTP {status}");
            var body = InvokeNoArg(raw, "ReadAsString");
            if (!string.IsNullOrEmpty(body)) parts.Add(body!);
        }

        // Case A: the typed {Op}Error exposes a payload via TryGetError / TryGetError1 / TryGetDefaultError.
        foreach (var accessor in new[] { "TryGetError", "TryGetError1", "TryGetDefaultError", "TryGetNoContent" })
        {
            var payload = TryInvokeTryGet(errorObj, accessor);
            if (payload is null) continue;
            var name = ReadString(payload, "Name");
            var message = ReadString(payload, "Message");
            var debugId = ReadString(payload, "DebugId");
            var issues = ReadIssues(payload);
            var summary = string.Join(" | ", new[] { name, message, issues, debugId is null ? null : $"debug_id={debugId}" }
                .Where(s => !string.IsNullOrEmpty(s)));
            if (!string.IsNullOrEmpty(summary)) parts.Add(summary);
        }

        return parts.Count > 0 ? string.Join(" ; ", parts.Distinct()) : ex.Message;
    }

    private static object? TryInvokeTryGet(object? errorObj, string methodName)
    {
        if (errorObj is null) return null;
        var method = errorObj.GetType().GetMethod(methodName);
        if (method is null) return null;
        var args = new object?[] { null };
        try
        {
            var ok = method.Invoke(errorObj, args);
            return ok is true ? args[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? InvokeNoArg(object? target, string methodName)
    {
        if (target is null) return null;
        try { return target.GetType().GetMethod(methodName, Type.EmptyTypes)?.Invoke(target, null)?.ToString(); }
        catch { return null; }
    }

    private static string? ReadIssues(object? payload)
    {
        if (GetProperty(payload, "Details") is not IEnumerable details) return null;
        var issues = details.Cast<object?>()
            .Select(d => ReadString(d, "Issue") ?? ReadString(d, "Description"))
            .Where(s => !string.IsNullOrEmpty(s));
        var joined = string.Join(", ", issues);
        return string.IsNullOrEmpty(joined) ? null : joined;
    }

    private static bool IndicatesExpiredAuthorization(Exception ex)
    {
        var text = ex.ToString() + " " + DescribeSdkException(ex);
        return text.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AUTH_EXPIRED", StringComparison.OrdinalIgnoreCase)
            || text.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase);
    }

    private void GuardNoBrowserChallenge(string? status)
    {
        if (status is null) return;
        if (status.Contains("PAYER", StringComparison.OrdinalIgnoreCase)
            && status.Contains("ACTION", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This browser-less integration cannot complete such a payment.");
        }
    }

    private static Address? BuildAddress(CardDetails card)
    {
        if (string.IsNullOrEmpty(card.BillingAddressLine1) && string.IsNullOrEmpty(card.BillingPostalCode)
            && string.IsNullOrEmpty(card.BillingCountryCode))
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = card.BillingAddressLine1,
            AdminArea2 = card.BillingAdminArea2,
            AdminArea1 = card.BillingAdminArea1,
            PostalCode = card.BillingPostalCode,
            CountryCode = string.IsNullOrEmpty(card.BillingCountryCode) ? "US" : card.BillingCountryCode
        };
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt : null;

    private static string? ExpiryMonthOf(string? expiry) =>
        !string.IsNullOrEmpty(expiry) && expiry.Contains('-') ? expiry.Split('-')[1] : null;

    private static string? ExpiryYearOf(string? expiry) =>
        !string.IsNullOrEmpty(expiry) && expiry.Contains('-') ? expiry.Split('-')[0] : null;

    // Reflection-based readers keep this layer resilient to the generated model's exact nested types
    // (the contract flagged some response field population as UNVERIFIED). We read only names the
    // SDK map documents, and tolerate absence.

    private static object? GetProperty(object? source, string name)
    {
        if (source is null) return null;
        var prop = source.GetType().GetProperty(name);
        return prop?.GetValue(source);
    }

    private static string? ReadString(object? source, string name) => GetProperty(source, name)?.ToString();

    private static int? ReadInt(object? source, string name)
    {
        var value = GetProperty(source, name);
        return value switch
        {
            int i => i,
            null => null,
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
    }

    private static string? ReadStatus(object? source) => EnumToWire(GetProperty(source, "Status"));

    /// <summary>
    /// SDK enums are wrappers whose ToString() prints "Type { Value = WIRE }"; read the underlying
    /// wire value from the "Value" property when present.
    /// </summary>
    private static string? EnumToWire(object? enumObj)
    {
        if (enumObj is null) return null;
        var value = GetProperty(enumObj, "Value");
        return (value ?? enumObj).ToString();
    }

    private static (decimal? Amount, string? Currency) ReadMoney(object? money)
    {
        if (money is null) return (null, null);
        var value = ReadString(money, "Value");
        var currency = ReadString(money, "CurrencyCode");
        decimal? amount = decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
        return (amount, currency);
    }

    private static (decimal? Fee, decimal? Net) ReadSellerBreakdown(object? captureResponse)
    {
        var breakdown = GetProperty(captureResponse, "SellerReceivableBreakdown");
        if (breakdown is null) return (null, null);
        var (fee, _) = ReadMoney(GetProperty(breakdown, "PaypalFee"));
        var (net, _) = ReadMoney(GetProperty(breakdown, "NetAmount"));
        return (fee, net);
    }

    /// <summary>Extracts the first authorization (id/status/expiry) from an order/authorize response.</summary>
    private static (string Id, string Status, string? Expiry)? ExtractAuthorization(object response)
    {
        if (GetProperty(response, "PurchaseUnits") is not IEnumerable purchaseUnits) return null;

        foreach (var pu in purchaseUnits)
        {
            var payments = GetProperty(pu, "Payments");
            if (GetProperty(payments, "Authorizations") is not IEnumerable authorizations) continue;

            foreach (var authorization in authorizations)
            {
                var id = ReadString(authorization, "Id");
                if (string.IsNullOrEmpty(id)) continue;
                return (id, ReadStatus(authorization) ?? "CREATED", ReadString(authorization, "ExpirationTime"));
            }
        }
        return null;
    }

    /// <summary>Reads brand / last4 / expiry from a vaulted-card (payment token) response.</summary>
    private static (string? Brand, string? Last4, string? Expiry) ExtractCardDescriptor(object response)
    {
        var card = GetProperty(GetProperty(response, "PaymentSource"), "Card");
        return (EnumToWire(GetProperty(card, "Brand")), ReadString(card, "LastDigits"), ReadString(card, "Expiry"));
    }

    /// <summary>Reads a vault id (and brand/last4) back from an order response when a card was stored inline.</summary>
    private static (string? VaultId, string? Brand, string? Last4)? ExtractVaultFromCard(object response)
    {
        var card = GetProperty(GetProperty(response, "PaymentSource"), "Card");
        if (card is null) return null;
        var vault = GetProperty(GetProperty(card, "Attributes"), "Vault");
        var vaultId = ReadString(vault, "Id");
        if (string.IsNullOrEmpty(vaultId)) return null;
        return (vaultId, EnumToWire(GetProperty(card, "Brand")), ReadString(card, "LastDigits"));
    }
}
