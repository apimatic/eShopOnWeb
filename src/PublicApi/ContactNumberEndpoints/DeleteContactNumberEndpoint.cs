using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// DELETE /api/contact-numbers/{contactNumberId} — remove one of the caller's own numbers. Afterwards it
/// no longer appears among the caller's numbers and nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int contactNumberId,
                ClaimsPrincipal user,
                IContactNumberService service,
                CancellationToken cancellationToken) =>
            {
                var ownerId = user.GetUserName();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }

                // Returns false when no such number belongs to the caller, so one shopper can never delete another's.
                var removed = await service.RemoveAsync(ownerId, contactNumberId, cancellationToken);
                return removed ? Results.NoContent() : Results.NotFound();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }
}
