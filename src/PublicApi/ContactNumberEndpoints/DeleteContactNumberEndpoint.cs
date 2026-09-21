using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's numbers. Scoped to the caller: one shopper can never delete
/// another's. Afterwards the number no longer appears among the caller's numbers and is never messaged again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext http) => await HandleAsync(contactNumberId, http))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, HttpContext http)
    {
        var ownerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var ct = http.RequestAborted;
        var repository = http.RequestServices.GetRequiredService<IRepository<ContactNumber>>();

        // Scope by owner so another shopper's number can neither be found nor deleted.
        var contactNumber = await repository.FirstOrDefaultAsync(
            new ContactNumberByOwnerAndIdSpecification(ownerId, contactNumberId), ct);
        if (contactNumber is null) return Results.NotFound();

        await repository.DeleteAsync(contactNumber, ct);
        return Results.NoContent();
    }
}
