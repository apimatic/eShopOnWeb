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
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's registered numbers. Ownership is enforced by the query itself: a number that
/// belongs to another shopper is simply not found. Afterwards the number no longer appears among the caller's
/// numbers and nothing can be sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IRepository<ContactNumber> repository, ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(contactNumberId, repository, user, ct);
            })
            .WithTags("ContactNumberEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        int contactNumberId,
        IRepository<ContactNumber> repository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        var buyerId = user.UserName();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var contactNumber = await repository.FirstOrDefaultAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId), ct);
        if (contactNumber is null)
        {
            return Results.NotFound();
        }

        await repository.DeleteAsync(contactNumber, ct);
        return Results.NoContent();
    }
}
