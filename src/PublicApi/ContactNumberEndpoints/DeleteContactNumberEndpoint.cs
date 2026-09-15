using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// DELETE /api/contact-numbers/{contactNumberId} — remove one of the caller's numbers. Scoped to the caller, so
/// one shopper can never delete another's. Afterwards it no longer appears among the caller's numbers and nothing
/// is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, ContactNumberEndpointServices>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ContactNumberEndpointServices services) =>
                await HandleAsync(contactNumberId, services))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, ContactNumberEndpointServices services)
    {
        var buyerId = services.User.UserName();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        // Ownership is part of the query: a number belonging to another shopper is simply not found.
        var contactNumber = await services.ContactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdForBuyerSpecification(contactNumberId, buyerId));
        if (contactNumber is null)
            return Results.NotFound();

        await services.ContactNumbers.DeleteAsync(contactNumber);
        return Results.NoContent();
    }
}
