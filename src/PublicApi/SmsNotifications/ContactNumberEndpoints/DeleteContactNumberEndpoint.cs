using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's registered numbers. The lookup is scoped to the caller, so a number
/// belonging to another shopper is simply not found. Afterwards it no longer appears among the
/// caller's numbers and nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int contactNumberId,
                IRepository<ContactNumber> repository,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var contactNumber = await repository.FirstOrDefaultAsync(
                    new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId), cancellationToken);
                if (contactNumber is null)
                    return Results.NotFound();

                await repository.DeleteAsync(contactNumber, cancellationToken);
                return Results.NoContent();
            })
            .WithTags("ContactNumberEndpoints");
    }
}
