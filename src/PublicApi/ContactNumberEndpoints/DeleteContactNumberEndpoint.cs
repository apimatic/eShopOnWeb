using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's numbers. Scoped to the caller, so a shopper can
/// never delete another's number. Afterwards the number no longer appears among the caller's
/// numbers and nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int contactNumberId,
                ClaimsPrincipal user,
                IRepository<ContactNumber> repository) =>
            {
                var ownerId = user.GetOwnerId();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }

                var contact = await repository.FirstOrDefaultAsync(new ContactNumberByIdSpecification(contactNumberId, ownerId));
                if (contact is null)
                {
                    return Results.NotFound();
                }

                await repository.DeleteAsync(contact);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }
}
