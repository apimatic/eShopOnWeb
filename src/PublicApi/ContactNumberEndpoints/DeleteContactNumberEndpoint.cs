using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; init; }
    public DeleteContactNumberRequest(int contactNumberId) => ContactNumberId = contactNumberId;
}

/// <summary>
/// DELETE /api/contact-numbers/{contactNumberId} — removes one of the shopper's own numbers.
/// Afterwards it no longer appears among their numbers and nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : ApiEndpointBase,
    IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public DeleteContactNumberEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IContactNumberService service) =>
                await HandleAsync(new DeleteContactNumberRequest(contactNumberId), service))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
    {
        var ownerId = CallerId;
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        // Scoped to the caller: one shopper can never delete another's number.
        var deleted = await service.DeleteAsync(ownerId, request.ContactNumberId, Aborted);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
