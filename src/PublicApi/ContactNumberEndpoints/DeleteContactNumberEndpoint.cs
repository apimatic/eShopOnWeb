using System.Linq;
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
/// Removes one of the caller's registered numbers. Scoped to the owner: one shopper cannot delete
/// another's. Afterwards the number no longer appears among the caller's numbers and is never messaged.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext) =>
            {
                return await HandleAsync(contactNumberId, httpContext);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var repository = httpContext.RequestServices.GetRequiredService<IRepository<ContactNumber>>();

        // Scoped lookup: a number that belongs to another shopper is simply not found for this caller.
        var contactNumber = (await repository.ListAsync(
            new ContactNumberByIdAndBuyerSpecification(contactNumberId, buyerId), httpContext.RequestAborted)).FirstOrDefault();
        if (contactNumber is null)
            return Results.NotFound();

        await repository.DeleteAsync(contactNumber, httpContext.RequestAborted);
        return Results.NoContent();
    }
}
