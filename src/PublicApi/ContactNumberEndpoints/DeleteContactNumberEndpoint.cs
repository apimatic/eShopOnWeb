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
/// Removes one of the signed-in shopper's registered mobile numbers. Nothing may be
/// sent to it afterwards.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IOrderNotificationService _notificationService;

    public DeleteContactNumberEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext) =>
            {
                return await HandleAsync(contactNumberId, httpContext);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, HttpContext httpContext)
    {
        var deleted = await _notificationService.DeleteContactNumberAsync(
            httpContext.User.Identity!.Name!, contactNumberId, httpContext.RequestAborted);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
