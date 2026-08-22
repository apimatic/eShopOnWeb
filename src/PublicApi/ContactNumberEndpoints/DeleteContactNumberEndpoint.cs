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

public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(contactNumberId, service, user);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(int contactNumberId, IOrderNotificationService service) =>
        HandleAsync(contactNumberId, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(int contactNumberId, IOrderNotificationService service, ClaimsPrincipal user)
    {
        var unauthorized = user.RequireBuyerId(out var buyerId);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var deleted = await service.DeleteContactNumberAsync(buyerId, contactNumberId);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
