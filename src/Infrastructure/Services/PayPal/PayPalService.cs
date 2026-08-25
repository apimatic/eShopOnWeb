using System.Web;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

public class PayPalService : IPayPalService
{
    private readonly PayPalHttpClient _client;
    private readonly PayPalSettings _settings;

    public PayPalService(PayPalHttpClient client, IOptions<PayPalSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    // ── Authorize with new card ────────────────────────────────────────────

    public async Task<AuthorizeResult> AuthorizeWithCardAsync(
        string idempotencyKey, decimal amount, string currency,
        CardDetails card, string merchantCustomerId, CancellationToken ct = default)
    {
        var createReq = new CreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new Money { CurrencyCode = currency, Value = PayPalHttpClient.FormatAmount(amount) },
                    CustomId = merchantCustomerId
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = new CardRequest
                {
                    Name = card.CardholderName,
                    Number = card.CardNumber,
                    Expiry = $"{card.ExpiryYear:D4}-{card.ExpiryMonth:D2}",
                    SecurityCode = card.Cvv,
                    BillingAddress = BuildCardAddress(card),
                    Attributes = new CardAttributes
                    {
                        Customer = new CardCustomer { MerchantCustomerId = merchantCustomerId }
                    }
                }
            }
        };

        var orderResp = await _client.PostAsync<CreateOrderRequest, OrderResponse>(
            "v2/checkout/orders", createReq, idempotencyKey + "-create", ct);

        if (orderResp.Status == "PAYER_ACTION_REQUIRED")
            throw new PayPalException(
                "PayPal requires payer action (3DS challenge). Direct card authorization not available for this card.",
                "PAYER_ACTION_REQUIRED");

        var authId = await ExtractOrAuthorizeAsync(orderResp, idempotencyKey + "-auth", ct);
        return new AuthorizeResult(orderResp.Id, authId);
    }

    // ── Authorize with vaulted card ────────────────────────────────────────

    public async Task<AuthorizeResult> AuthorizeWithVaultAsync(
        string idempotencyKey, decimal amount, string currency,
        string vaultId, CancellationToken ct = default)
    {
        var createReq = new CreateOrderRequest
        {
            Intent = "AUTHORIZE",
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new Money { CurrencyCode = currency, Value = PayPalHttpClient.FormatAmount(amount) }
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = new CardRequest
                {
                    VaultId = vaultId,
                    StoredCredential = new StoredCredential
                    {
                        PaymentInitiator = "CUSTOMER",
                        PaymentType = "UNSCHEDULED",
                        Usage = "SUBSEQUENT"
                    }
                }
            }
        };

        var orderResp = await _client.PostAsync<CreateOrderRequest, OrderResponse>(
            "v2/checkout/orders", createReq, idempotencyKey + "-create", ct);

        if (orderResp.Status == "PAYER_ACTION_REQUIRED")
            throw new PayPalException(
                "PayPal requires payer action (3DS challenge). Vaulted card authorization not available.",
                "PAYER_ACTION_REQUIRED");

        var authId = await ExtractOrAuthorizeAsync(orderResp, idempotencyKey + "-auth", ct);
        return new AuthorizeResult(orderResp.Id, authId);
    }

    // ── Shared: extract auth ID from completed order or call /authorize ────

    private async Task<string> ExtractOrAuthorizeAsync(
        OrderResponse orderResp, string idempotencyKey, CancellationToken ct)
    {
        // When intent=AUTHORIZE with a direct card, PayPal may authorize inline
        // and return status=COMPLETED with the authorization already in the response.
        var existingAuthId = orderResp.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault()?.Id;

        if (!string.IsNullOrEmpty(existingAuthId))
            return existingAuthId;

        // Otherwise (status=APPROVED), call /authorize explicitly.
        var authResp = await _client.PostEmptyAsync<OrderResponse>(
            $"v2/checkout/orders/{orderResp.Id}/authorize", idempotencyKey, ct);

        var authId = authResp.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault()?.Id;

        if (string.IsNullOrEmpty(authId))
            throw new PayPalException(
                $"PayPal returned no authorization ID after authorizing order {orderResp.Id}.");

        return authId;
    }

    // ── Get authorization details ──────────────────────────────────────────

    public async Task<(string Status, DateTimeOffset? ExpirationTime)> GetAuthorizationAsync(
        string authorizationId, CancellationToken ct = default)
    {
        var resp = await _client.GetAsync<AuthorizationDetailsResponse>(
            $"v2/payments/authorizations/{authorizationId}", ct);

        DateTimeOffset? expiry = null;
        if (!string.IsNullOrEmpty(resp.ExpirationTime) &&
            DateTimeOffset.TryParse(resp.ExpirationTime, out var parsed))
            expiry = parsed;

        return (resp.Status, expiry);
    }

    // ── Capture ───────────────────────────────────────────────────────────

    public async Task<CaptureResult> CaptureAsync(
        string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new CaptureAuthorizationRequest { FinalCapture = true };
        var resp = await _client.PostAsync<CaptureAuthorizationRequest, CaptureResponse>(
            $"v2/payments/authorizations/{authorizationId}/capture", body, idempotencyKey, ct);

        var capturedAmount = PayPalHttpClient.ParseAmount(resp.SellerReceivableBreakdown?.GrossAmount?.Value ?? resp.Amount?.Value);
        var fee = PayPalHttpClient.ParseAmount(resp.SellerReceivableBreakdown?.PayPalFee?.Value);
        var net = PayPalHttpClient.ParseAmount(resp.SellerReceivableBreakdown?.NetAmount?.Value);

        return new CaptureResult(resp.Id, capturedAmount, fee, net);
    }

    // ── Reauthorize ───────────────────────────────────────────────────────

    public async Task<string> ReauthorizeAsync(
        string authorizationId, CancellationToken ct = default)
    {
        var resp = await _client.PostEmptyAsync<ReauthorizeResponse>(
            $"v2/payments/authorizations/{authorizationId}/reauthorize", null, ct);

        if (string.IsNullOrEmpty(resp.Id))
            throw new PayPalException(
                $"Reauthorization of {authorizationId} succeeded but returned no new authorization ID.");

        return resp.Id;
    }

    // ── Void ──────────────────────────────────────────────────────────────

    public async Task VoidAsync(string authorizationId, CancellationToken ct = default)
    {
        await _client.PostVoidAsync(
            $"v2/payments/authorizations/{authorizationId}/void", null, ct);
    }

    // ── Refund ────────────────────────────────────────────────────────────

    public async Task<RefundResult> RefundAsync(
        string captureId, decimal? amount, string currency,
        string idempotencyKey, string? note, CancellationToken ct = default)
    {
        var body = new RefundRequest
        {
            NoteToPayer = note,
            Amount = amount.HasValue
                ? new Money { CurrencyCode = currency, Value = PayPalHttpClient.FormatAmount(amount.Value) }
                : null
        };

        var resp = await _client.PostAsync<RefundRequest, RefundResponse>(
            $"v2/payments/captures/{captureId}/refund", body, idempotencyKey, ct);

        var refundAmount = PayPalHttpClient.ParseAmount(resp.Amount?.Value);
        return new RefundResult(resp.Id, refundAmount);
    }

    // ── Vault card ────────────────────────────────────────────────────────

    public async Task<VaultResult> VaultCardAsync(
        string merchantCustomerId, CardDetails card, CancellationToken ct = default)
    {
        // Step 1: create setup token
        var setupReq = new SetupTokenRequest
        {
            Customer = new VaultCustomer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new VaultPaymentSource
            {
                Card = new VaultCard
                {
                    Name = card.CardholderName,
                    Number = card.CardNumber,
                    Expiry = $"{card.ExpiryYear:D4}-{card.ExpiryMonth:D2}",
                    SecurityCode = card.Cvv,
                    BillingAddress = BuildVaultAddress(card)
                }
            }
        };

        var setupResp = await _client.PostAsync<SetupTokenRequest, SetupTokenResponse>(
            "v3/vault/setup-tokens", setupReq, null, ct);

        if (setupResp.Status == "PAYER_ACTION_REQUIRED")
            throw new PayPalException(
                "PayPal requires payer action (3DS challenge) to vault this card. " +
                "Direct card vaulting is not available for this card.",
                "PAYER_ACTION_REQUIRED");

        // Step 2: convert setup token to payment token
        var tokenReq = new PaymentTokenRequest
        {
            Customer = new VaultCustomer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new VaultPaymentSource
            {
                Token = new VaultToken { Id = setupResp.Id, Type = "SETUP_TOKEN" }
            }
        };

        var tokenResp = await _client.PostAsync<PaymentTokenRequest, PaymentTokenResponse>(
            "v3/vault/payment-tokens", tokenReq, null, ct);

        var cardInfo = tokenResp.PaymentSource?.Card;
        var last4 = cardInfo?.LastDigits ?? "????";
        var brand = cardInfo?.Brand ?? "UNKNOWN";

        // Parse expiry "YYYY-MM" from vault response
        var expYear = card.ExpiryYear;
        var expMonth = card.ExpiryMonth;
        if (!string.IsNullOrEmpty(cardInfo?.Expiry) && cardInfo.Expiry.Contains('-'))
        {
            var parts = cardInfo.Expiry.Split('-');
            if (parts.Length == 2)
            {
                _ = int.TryParse(parts[0], out expYear);
                _ = int.TryParse(parts[1], out expMonth);
            }
        }

        var paypalCustomerId = tokenResp.Customer?.Id
            ?? setupResp.Customer?.Id
            ?? merchantCustomerId;

        return new VaultResult(tokenResp.Id, paypalCustomerId, last4, brand, expYear, expMonth);
    }

    // ── Delete vaulted card ───────────────────────────────────────────────

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        await _client.DeleteAsync($"v3/vault/payment-tokens/{vaultId}", ct);
    }

    // ── Transaction search (handles pagination + chunking for >31-day ranges) ──

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();
        var chunkStart = from;

        while (chunkStart < to)
        {
            // Max window is 31 days per PayPal spec
            var chunkEnd = chunkStart.AddDays(31) < to ? chunkStart.AddDays(31) : to;
            await FetchTransactionChunkAsync(chunkStart, chunkEnd, results, ct);
            chunkStart = chunkEnd;
        }

        return results.AsReadOnly();
    }

    private async Task FetchTransactionChunkAsync(
        DateTimeOffset from, DateTimeOffset to,
        List<PayPalTransaction> accumulator, CancellationToken ct)
    {
        int page = 1;
        int totalPages = 1;

        do
        {
            var startEnc = HttpUtility.UrlEncode(from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
            var endEnc = HttpUtility.UrlEncode(to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
            var path = $"v1/reporting/transactions" +
                       $"?start_date={startEnc}&end_date={endEnc}" +
                       $"&fields=all&page_size=500&page={page}&total_required=true";

            var resp = await _client.GetAsync<TransactionSearchResponse>(path, ct);
            totalPages = resp.TotalPages > 0 ? resp.TotalPages : 1;

            if (resp.TransactionDetails != null)
            {
                foreach (var t in resp.TransactionDetails)
                {
                    var info = t.TransactionInfo;
                    if (info == null) continue;

                    DateTimeOffset? initDate = null;
                    if (!string.IsNullOrEmpty(info.TransactionInitiationDate) &&
                        DateTimeOffset.TryParse(info.TransactionInitiationDate, out var d))
                        initDate = d;

                    accumulator.Add(new PayPalTransaction(
                        TransactionId: info.TransactionId ?? "",
                        PayPalReferenceId: info.PayPalReferenceId,
                        Status: info.TransactionStatus,
                        Amount: PayPalHttpClient.ParseAmount(info.TransactionAmount?.Value),
                        Currency: info.TransactionAmount?.CurrencyCode ?? _settings.Currency,
                        CustomField: info.CustomField,
                        EventCode: info.TransactionEventCode,
                        InitiationDate: initDate));
                }
            }

            page++;
        }
        while (page <= totalPages);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static CardAddress? BuildCardAddress(CardDetails card)
    {
        if (card.Street == null && card.City == null) return null;
        return new CardAddress
        {
            AddressLine1 = card.Street,
            City = card.City,
            State = card.State,
            PostalCode = card.ZipCode,
            CountryCode = card.Country
        };
    }

    private static VaultAddress? BuildVaultAddress(CardDetails card)
    {
        if (card.Street == null && card.City == null) return null;
        return new VaultAddress
        {
            AddressLine1 = card.Street,
            City = card.City,
            State = card.State,
            PostalCode = card.ZipCode,
            CountryCode = card.Country
        };
    }
}
