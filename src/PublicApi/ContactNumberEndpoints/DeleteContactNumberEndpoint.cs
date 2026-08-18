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
/// Removes one of the shopper's own numbers. Afterwards it no longer appears among their numbers and
/// nothing is ever sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, HttpContext>
{
    private readonly IContactNumberService _service;

    public DeleteContactNumberEndpoint(IContactNumberService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext http) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), http);
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, HttpContext http)
    {
        var buyerId = CallerIdentity.Of(http.User);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        // Scoped to the caller: one shopper can never delete another's number (a number that is not theirs
        // is indistinguishable from one that does not exist).
        var removed = await _service.RemoveAsync(buyerId, request.ContactNumberId, http.RequestAborted);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
