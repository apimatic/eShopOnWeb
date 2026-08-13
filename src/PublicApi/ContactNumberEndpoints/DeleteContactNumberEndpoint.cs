using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's contact numbers. Afterwards it no longer appears among the caller's
/// numbers and nothing can be sent to it again. Only the owner can delete their own number.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IContactNumberService service, CancellationToken ct) =>
            {
                var callerId = user.GetCallerId();
                if (string.IsNullOrEmpty(callerId))
                {
                    return Results.Unauthorized();
                }

                var removed = await service.RemoveAsync(callerId, contactNumberId, ct);
                return removed ? Results.NoContent() : Results.NotFound();
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    // The route logic lives in the lambda above; the interface's HandleAsync is unused for this endpoint.
    public Task<IResult> HandleAsync(int contactNumberId, IContactNumberService service) =>
        Task.FromResult(Results.NoContent());
}
