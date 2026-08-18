using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public record DeleteContactNumberRequest(int ContactNumberId);

/// <summary>
/// DELETE /api/contact-numbers/{contactNumberId} — removes one of the caller's own numbers. Afterwards it
/// no longer appears among the caller's numbers and nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeleteContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IContactNumberService service) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
    {
        var ownerId = EndpointCaller.UserName(_httpContextAccessor);
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var deleted = await service.DeleteAsync(ownerId, request.ContactNumberId, EndpointCaller.RequestAborted(_httpContextAccessor));
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
