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
/// Removes one of the signed-in shopper's numbers. Afterwards it no longer appears among their numbers
/// and nothing can be sent to it again. Another shopper's number is simply not found here.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IContactNumberService service, CancellationToken ct) =>
            {
                return await HandleAsync(contactNumberId, user, service, ct);
            })
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, ClaimsPrincipal user, IContactNumberService service, CancellationToken ct)
    {
        var ownerId = user.GetUserId();
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var removed = await service.RemoveAsync(ownerId, contactNumberId, ct);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
