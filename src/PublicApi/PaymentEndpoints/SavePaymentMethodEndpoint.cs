using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Saves (vaults) a card for the signed-in shopper. Returns a safe descriptor, never card details.</summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, CardDto, ISavedCardService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CardDto request, ISavedCardService service, HttpContext http) =>
                await HandleAsync(request, service, http))
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CardDto request, ISavedCardService service, HttpContext http)
    {
        var buyerId = http.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var view = await service.SaveCardAsync(buyerId, request.ToCardDetails(), http.RequestAborted);
        return Results.Created($"api/payment-methods/{view.PaymentMethodId}", new SavePaymentMethodResponse
        {
            PaymentMethodId = view.PaymentMethodId,
            Brand = view.Brand,
            Last4 = view.Last4,
            Expiry = view.Expiry
        });
    }
}
