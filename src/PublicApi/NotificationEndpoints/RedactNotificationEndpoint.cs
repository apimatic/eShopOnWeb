using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationEndpoint : IEndpoint<IResult, RedactNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, IOrderNotificationService service, HttpContext http) =>
            {
                return await HandleAsync(new RedactNotificationRequest(notificationId), service, http);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(RedactNotificationRequest request, IOrderNotificationService service)
        => throw new NotSupportedException();

    private async Task<IResult> HandleAsync(RedactNotificationRequest request, IOrderNotificationService service, HttpContext http)
    {
        await service.RedactContentAsync(request.NotificationId, http.RequestAborted);
        return Results.NoContent();
    }
}
