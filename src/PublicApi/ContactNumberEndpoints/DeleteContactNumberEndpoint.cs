using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's registered numbers. Afterwards it no longer appears among their
/// numbers and nothing is ever sent to it again. A number that is not the caller's is not found.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    private readonly IContactNumberService _contactNumberService;

    public DeleteContactNumberEndpoint(IContactNumberService contactNumberService)
    {
        _contactNumberService = contactNumberService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, CancellationToken ct) =>
                await HandleAsync(contactNumberId, user, ct))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, ClaimsPrincipal user, CancellationToken ct)
    {
        var ownerId = user.GetUsername();
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var removed = await _contactNumberService.RemoveAsync(ownerId, contactNumberId, ct);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
