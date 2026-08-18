using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// DELETE /api/contact-numbers/{contactNumberId} — removes one of the caller's numbers. Afterwards it no
/// longer appears among the caller's numbers and nothing is ever sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint
    : IEndpoint<IResult, int, IContactNumberService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IContactNumberService service, HttpContext http) =>
                await HandleAsync(contactNumberId, service, http))
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, IContactNumberService service, HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var deleted = await service.DeleteAsync(buyerId, contactNumberId, http.RequestAborted);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
