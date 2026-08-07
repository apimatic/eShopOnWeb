using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves (vaults) a card for the signed-in shopper for reuse on later orders.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, savedCardService);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("PaymentMethodEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Saves a card for the signed-in shopper", "Vaults a card with PayPal and returns a safe descriptor."));
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedCardService savedCardService)
    {
        var response = new SavePaymentMethodResponse();

        var saved = await savedCardService.SaveCardAsync(request.BuyerId, request.ToCardDetails());

        response.PaymentMethodId = saved.Id;
        response.PaymentMethod = PaymentMethodDto.FromEntity(saved);

        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
