using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Removes one of the signed-in shopper's numbers; afterwards nothing is sent to it again.</summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext http, IContactNumberService service, System.Threading.CancellationToken ct) =>
            {
                var ownerId = CallerContext.GetUserName(http);
                if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

                var removed = await service.RemoveAsync(ownerId, contactNumberId, ct);
                return removed ? Results.NoContent() : Results.NotFound();
            })
            .WithTags("ContactNumberEndpoints");
    }
}
