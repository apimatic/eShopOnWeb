using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// DELETE /api/contact-numbers/{contactNumberId} — remove one of the caller's numbers. Scoped to the owner,
/// so one shopper can never delete another's; afterwards nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int contactNumberId,
                IRepository<ContactNumber> repository,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                // Scope by owner: a number that exists but belongs to someone else is Not Found to this caller.
                var number = await repository.FirstOrDefaultAsync(
                    new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), ct);
                if (number is null)
                {
                    return Results.NotFound();
                }

                await repository.DeleteAsync(number, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }
}
