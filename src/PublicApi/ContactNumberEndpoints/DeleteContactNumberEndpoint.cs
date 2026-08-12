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
/// DELETE /api/contact-numbers/{contactNumberId} — remove one of the caller's numbers. Afterwards it
/// no longer appears among the caller's numbers and nothing is sent to it again. A caller can only ever
/// delete their own number (a not-owned id is a 404).
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, IContactNumberService>
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
                return await HandleAsync(contactNumberId, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, IContactNumberService service)
    {
        var http = _httpContextAccessor.HttpContext!;
        var buyerId = http.User.Identity!.Name!;
        var removed = await service.DeleteAsync(buyerId, contactNumberId, http.RequestAborted);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
