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
/// Removes one of the caller's contact numbers. Afterwards it no longer appears among the caller's
/// numbers and nothing is ever sent to it again. A number belonging to another shopper is not
/// found for this caller.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberCommand, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                var ownerId = user.UserName();
                if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();
                return await HandleAsync(new DeleteContactNumberCommand(ownerId, contactNumberId), service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberCommand request, IOrderNotificationService service)
    {
        var deleted = await service.DeleteContactNumberAsync(request.OwnerId, request.ContactNumberId);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
