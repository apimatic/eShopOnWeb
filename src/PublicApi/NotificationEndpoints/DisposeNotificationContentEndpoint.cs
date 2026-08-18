using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// DELETE /api/notifications/{notificationId}/content — dispose of a message's content at the
/// shopper's request. The text is redacted at the provider and cleared locally; the fact a message
/// was sent, and what became of it, survives. If the provider redaction cannot be done, no disposal
/// is claimed (502).
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext http) => await HandleAsync(notificationId, http))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, HttpContext http)
    {
        var service = http.RequestServices.GetRequiredService<ISmsNotificationService>();

        try
        {
            var notification = await service.DisposeContentAsync(notificationId, http.RequestAborted);
            return notification is null ? Results.NotFound() : Results.NoContent();
        }
        catch (MessagingProviderException ex)
        {
            // The content could not be redacted at the provider, so disposal is not claimed.
            return Results.Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway,
                title: "Content could not be disposed at the provider.");
        }
    }
}
