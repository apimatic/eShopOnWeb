using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, IRepository<PaymentMethod> pmRepo, IPayPalService payPal, ClaimsPrincipal user) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                if (string.IsNullOrEmpty(request.Number) || string.IsNullOrEmpty(request.Expiry) || string.IsNullOrEmpty(request.SecurityCode))
                    return Results.BadRequest("Card number, expiry, and security code are required.");

                // Check if we already have a PayPal vault customer ID for this buyer
                var existingMethods = await pmRepo.ListAsync(new PaymentMethodsByBuyerSpec(buyerId));
                var vaultCustomerId = existingMethods.FirstOrDefault()?.VaultCustomerId
                    ?? GetVaultCustomerId(buyerId);

                var idempotencyKey = $"vault-{buyerId}-{Guid.NewGuid()}";

                var card = new CardVaultDetails(request.Number, request.Expiry, request.SecurityCode, request.Name);
                var vaultResult = await payPal.CreateVaultTokenAsync(vaultCustomerId, card, idempotencyKey);

                // If PayPal assigned a new customer ID, use it; otherwise keep existing
                var resolvedCustomerId = vaultResult.CustomerId ?? vaultCustomerId;
                var paymentMethod = new PaymentMethod(buyerId, resolvedCustomerId, vaultResult.TokenId, vaultResult.Last4, vaultResult.Brand, vaultResult.Expiry);
                await pmRepo.AddAsync(paymentMethod);

                return Results.Created($"api/payment-methods/{paymentMethod.Id}", new SavePaymentMethodResponse
                {
                    PaymentMethodId = paymentMethod.Id,
                    Last4 = vaultResult.Last4,
                    Brand = vaultResult.Brand,
                    Expiry = vaultResult.Expiry
                });
            })
            .Produces<SavePaymentMethodResponse>(201)
            .WithTags("PaymentMethodEndpoints");
    }

    private static string GetVaultCustomerId(string username)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(username));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..36];
    }
}
