using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's contact numbers. Afterwards it no longer appears among the caller's numbers
/// and nothing is sent to it again. One shopper can never delete another's number.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IContactNumberService service) =>
            {
                return await HandleAsync(
                    new DeleteContactNumberRequest { BuyerId = user.GetBuyerId(), ContactNumberId = contactNumberId },
                    service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId)) return Results.Unauthorized();

        var deleted = await service.DeleteAsync(request.BuyerId, request.ContactNumberId);
        // Not found and not-yours are indistinguishable, so another shopper's number is never revealed.
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
