using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public record DeleteContactNumberRequest(int ContactNumberId, string? OwnerId);

/// <summary>Removes one of the signed-in shopper's numbers. Afterwards it no longer appears among their
/// numbers and nothing is ever sent to it again. One shopper can never delete another's.</summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IContactNumberService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId, user.FindFirstValue(ClaimTypes.Name)), service);
            })
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.OwnerId)) return Results.Unauthorized();

        var deleted = await service.DeleteAsync(request.OwnerId, request.ContactNumberId, default);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
