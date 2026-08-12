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
/// Removes one of the signed-in shopper's numbers. A shopper can only delete a number that is theirs, and
/// afterwards nothing is ever sent to it again (order events only ever message currently-registered numbers).
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int contactNumberId,
                ClaimsPrincipal user,
                IRepository<ContactNumber> repository,
                CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                // Scoped by buyer, so one shopper can never delete another's number.
                var contactNumber = await repository.FirstOrDefaultAsync(
                    new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), ct);
                if (contactNumber is null)
                    return Results.NotFound();

                await repository.DeleteAsync(contactNumber, ct);
                return Results.NoContent();
            })
            .WithTags("ContactNumberEndpoints");
    }
}
