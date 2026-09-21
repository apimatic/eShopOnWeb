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
/// Removes one of the caller's numbers. Afterwards it no longer appears among the caller's numbers and
/// nothing is ever sent to it again. Scoped to the owner, so one shopper can never delete another's.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IContactNumberService service, ClaimsPrincipal user, HttpContext http) =>
            {
                var request = new DeleteContactNumberRequest { ContactNumberId = contactNumberId, CallerId = user.Identity?.Name };
                return await HandleAsync(request, service, http.RequestAborted);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service) =>
        HandleAsync(request, service, default);

    private static async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.CallerId))
        {
            return Results.Unauthorized();
        }

        var removed = await service.DeleteAsync(request.CallerId, request.ContactNumberId, ct);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
