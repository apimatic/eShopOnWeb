using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IRepository<SavedPaymentMethod>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request,
                   IRepository<SavedPaymentMethod> pmRepo,
                   PayPalPaymentService paypal,
                   ClaimsPrincipal user,
                   CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                if (string.IsNullOrEmpty(request.CardNumber) || string.IsNullOrEmpty(request.Expiry))
                    return Results.BadRequest(new { error = "Card number and expiry are required." });

                // Get existing PayPal customer ID for this shopper if any
                var existingSpec = new SavedPaymentMethodsByBuyerSpec(buyerId);
                var existingMethods = await pmRepo.ListAsync(existingSpec, ct);
                var existingCustomerId = existingMethods.FirstOrDefault(m => m.PayPalCustomerId != null)?.PayPalCustomerId;

                VaultResult vaultResult;
                try
                {
                    vaultResult = await paypal.SaveCardAsync(
                        cardNumber: request.CardNumber,
                        expiry: request.Expiry,
                        cvv: request.SecurityCode ?? string.Empty,
                        cardName: request.CardholderName,
                        existingCustomerId: existingCustomerId,
                        ct: ct);
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.HttpStatusCode);
                }

                var savedMethod = new SavedPaymentMethod(
                    buyerId: buyerId,
                    vaultTokenId: vaultResult.VaultTokenId,
                    last4: vaultResult.Last4,
                    cardBrand: vaultResult.CardBrand,
                    payPalCustomerId: vaultResult.PayPalCustomerId ?? existingCustomerId);

                savedMethod = await pmRepo.AddAsync(savedMethod, ct);

                return Results.Created($"/api/payment-methods/{savedMethod.Id}", new SavePaymentMethodResponse
                {
                    PaymentMethodId = savedMethod.Id,
                    Last4 = savedMethod.Last4,
                    CardBrand = savedMethod.CardBrand
                });
            })
            .Produces<SavePaymentMethodResponse>(201)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, IRepository<SavedPaymentMethod> service)
        => throw new System.NotSupportedException();
}
