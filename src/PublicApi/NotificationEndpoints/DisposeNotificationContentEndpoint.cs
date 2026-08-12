using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: dispose of a message's content at the provider so its text is no longer
/// retrievable there — not merely hidden by this application — while the fact a message was sent,
/// and what became of it, survives. Restricted to the administrator role.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, NotificationActionRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new NotificationActionRequest(notificationId), service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(NotificationActionRequest request, IOrderNotificationService service)
    {
        try
        {
            await service.DisposeNotificationContentAsync(request.NotificationId);
            return Results.NoContent();
        }
        catch (ArgumentException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
        catch (SmsGatewayException ex)
        {
            // The provider could not dispose of the content — do not report success.
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
