using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Afterwards nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IContactNumberService contactNumberService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(
                    new DeleteContactNumberRequest(contactNumberId) { BuyerId = user.GetBuyerId(), CancellationToken = cancellationToken },
                    contactNumberService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService contactNumberService)
    {
        if (request.BuyerId is null)
        {
            return Results.Unauthorized();
        }

        // Not found and not-owned are deliberately indistinguishable.
        var deleted = await contactNumberService.DeleteAsync(request.BuyerId, request.ContactNumberId, request.CancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
