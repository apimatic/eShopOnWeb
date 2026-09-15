using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Saves and removes a shopper's cards using PayPal Vault v3, per the spec in
/// <c>api-specs/paypal/vault_payment_tokens_v3</c>. Prefers a direct card vault
/// (<c>POST /v3/vault/payment-tokens</c>); if the account requires a setup token first, it falls
/// back to the setup-token exchange the same spec describes.
/// </summary>
public class PayPalVaultGateway : IPayPalVaultGateway
{
    private readonly PayPalApiClient _client;
    private readonly ILogger<PayPalVaultGateway> _logger;

    public PayPalVaultGateway(PayPalApiClient client, ILogger<PayPalVaultGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<VaultedCardResult> VaultCardAsync(string customerId, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        var cardModel = MapCard(card);
        var customer = new VaultCustomerModel { Id = customerId };

        try
        {
            var direct = new VaultPaymentTokenRequest
            {
                Customer = customer,
                PaymentSource = new VaultPaymentSourceModel { Card = cardModel }
            };
            var token = await _client.SendAsync<VaultPaymentTokenResponse>(HttpMethod.Post,
                "/v3/vault/payment-tokens", direct, Idem(idempotencyKey), cancellationToken);
            return Map(token, customerId);
        }
        catch (PayPalApiException ex) when (ex.PayPalStatusCode is 400 or 404 or 422)
        {
            // Some accounts require a setup token before a card can be vaulted; use that exchange.
            _logger.LogInformation(
                "Direct card vaulting was rejected ({Status}); retrying via the setup-token exchange.",
                ex.PayPalStatusCode);

            var setupRequest = new SetupTokenRequest
            {
                Customer = customer,
                PaymentSource = new VaultPaymentSourceModel { Card = cardModel }
            };
            var setup = await _client.SendAsync<SetupTokenResponse>(HttpMethod.Post,
                "/v3/vault/setup-tokens", setupRequest, Idem(idempotencyKey + "-setup"), cancellationToken);

            if (string.IsNullOrEmpty(setup.Id))
            {
                throw new PayPalApiException("PayPal setup-token response did not contain an id.");
            }

            var exchange = new VaultPaymentTokenRequest
            {
                PaymentSource = new VaultPaymentSourceModel
                {
                    Token = new TokenIdModel { Id = setup.Id, Type = "SETUP_TOKEN" }
                }
            };
            var token = await _client.SendAsync<VaultPaymentTokenResponse>(HttpMethod.Post,
                "/v3/vault/payment-tokens", exchange, Idem(idempotencyKey), cancellationToken);
            return Map(token, customerId);
        }
    }

    public Task DeletePaymentTokenAsync(string vaultId, CancellationToken cancellationToken)
        => _client.SendNoContentAsync(HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}",
            body: null, headers: null, cancellationToken);

    private static (string, string)[] Idem(string requestId) => new[] { ("PayPal-Request-Id", requestId) };

    private static CardRequestModel MapCard(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = card.BillingAddress == null ? null : new BillingAddressModel
        {
            AddressLine1 = card.BillingAddress.AddressLine1,
            AddressLine2 = card.BillingAddress.AddressLine2,
            AdminArea2 = card.BillingAddress.AdminArea2,
            AdminArea1 = card.BillingAddress.AdminArea1,
            PostalCode = card.BillingAddress.PostalCode,
            CountryCode = card.BillingAddress.CountryCode
        }
    };

    private static VaultedCardResult Map(VaultPaymentTokenResponse token, string fallbackCustomerId)
    {
        if (string.IsNullOrEmpty(token.Id))
        {
            throw new PayPalApiException("PayPal vault response did not contain a payment-token id.");
        }

        var respCard = token.PaymentSource?.Card;
        return new VaultedCardResult(
            token.Id,
            token.Customer?.Id ?? fallbackCustomerId,
            respCard?.Brand ?? "CARD",
            respCard?.LastDigits ?? string.Empty,
            respCard?.Name,
            respCard?.Expiry);
    }
}
