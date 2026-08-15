using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Implements saving and removing cards against the PayPal Vault v3 API. Raw cards are vaulted
/// directly (POST /v3/vault/payment-tokens); only the returned vault id and a safe descriptor leave
/// this method.
/// </summary>
public class PayPalVaultGateway : IPayPalVaultGateway
{
    private readonly PayPalApiClient _client;

    public PayPalVaultGateway(PayPalApiClient client)
    {
        _client = client;
    }

    public async Task<VaultedCard> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default)
    {
        var c = request.Card;

        VaultCustomer? customer = null;
        if (!string.IsNullOrEmpty(request.PayPalCustomerId))
        {
            customer = new VaultCustomer { Id = request.PayPalCustomerId };
        }
        else if (!string.IsNullOrEmpty(request.MerchantCustomerId))
        {
            customer = new VaultCustomer { MerchantCustomerId = request.MerchantCustomerId };
        }

        var body = new PaymentTokenRequest
        {
            Customer = customer,
            PaymentSource = new VaultPaymentSource
            {
                Card = new VaultCard
                {
                    Name = c.CardholderName,
                    Number = c.Number,
                    Expiry = c.Expiry,
                    SecurityCode = c.SecurityCode,
                    BillingAddress = new AddressPortable
                    {
                        AddressLine1 = c.Line1,
                        AddressLine2 = c.Line2,
                        AdminArea2 = c.City,
                        AdminArea1 = c.State,
                        PostalCode = c.PostalCode,
                        CountryCode = c.CountryCode,
                    },
                },
            },
        };

        var headers = new PayPalRequestHeaders { RequestId = request.IdempotencyKey };

        var token = await _client.SendAsync<PaymentTokenResponse>(
            HttpMethod.Post, "/v3/vault/payment-tokens", body, headers, cancellationToken);

        if (token?.Id is null)
        {
            throw new PaymentGatewayException("PayPal vault response did not contain a token id.", 502, "VAULT_ID_MISSING");
        }

        var savedCard = token.PaymentSource?.Card;
        return new VaultedCard(
            VaultId: token.Id,
            PayPalCustomerId: token.Customer?.Id,
            Brand: savedCard?.Brand ?? "UNKNOWN",
            Last4: savedCard?.LastDigits ?? "****",
            Expiry: savedCard?.Expiry,
            CardType: savedCard?.Type,
            CardholderName: savedCard?.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        await _client.SendNoContentAsync(
            HttpMethod.Delete, $"/v3/vault/payment-tokens/{vaultId}", body: null, headers: null, cancellationToken);
    }
}
