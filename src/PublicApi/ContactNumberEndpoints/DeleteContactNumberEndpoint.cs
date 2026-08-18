using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// DELETE /api/contact-numbers/{contactNumberId} — removes one of the caller's own numbers. After
/// this it no longer appears among the caller's numbers and nothing is ever sent to it again.
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
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var service = http.RequestServices.GetRequiredService<IContactNumberService>();

        // Scoped to the caller: another shopper's number is simply "not found" here, never removable.
        var removed = await service.RemoveAsync(buyerId, contactNumberId, http.RequestAborted);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
