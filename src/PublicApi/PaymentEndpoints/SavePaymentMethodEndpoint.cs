using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/payment-methods — saves (vaults) a card for the signed-in shopper. The response identifies
/// the saved card and describes it safely (brand + last four); full card details are never returned.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentMethodService service) =>
            {
                request.Caller = CallerContext.From(user);
                return await HandleAsync(request, service);
            })
            .Produces<SavedCardView>(StatusCodes.Status201Created)
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentMethodService service)
    {
        var saved = await service.SaveCardAsync(request.Caller.Username, request.Card.ToCardDetails());
        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", saved);
    }
}
