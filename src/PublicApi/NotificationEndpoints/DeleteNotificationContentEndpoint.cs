using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's content at the shopper's request. Afterwards the text is
/// no longer retrievable from the provider either — not merely hidden here — while the fact that a
/// message was sent, and what became of it, survives.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, [FromServices] IOrderNotificationService service) =>
                await HandleAsync(notificationId, service))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService service)
    {
        try
        {
            var found = await service.RedactContentAsync(notificationId);
            return found ? Results.NoContent() : Results.NotFound();
        }
        catch (SmsProviderException ex)
        {
            // The content could not be disposed of at the provider — surface it rather than silently
            // clearing only the local copy, since the requirement is that the provider no longer holds it.
            return Results.Problem(
                title: "Provider content disposal failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
