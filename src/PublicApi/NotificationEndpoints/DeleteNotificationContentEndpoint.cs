using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentResponse : BaseResponse
{
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId) { }
    public DeleteNotificationContentResponse() { }

    public int NotificationId { get; set; }
    public bool ContentDisposed { get; set; }
}

/// <summary>
/// Disposes of a message's content (operator): the text is redacted at the
/// provider and discarded locally, while the record of the message and its
/// delivery outcome survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(notificationId, notificationService);
            })
            .Produces<DeleteNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOrderNotificationService notificationService)
    {
        bool disposed;
        try
        {
            disposed = await notificationService.DisposeContentAsync(notificationId);
        }
        catch (TwilioApiException)
        {
            // The provider could not redact the message; the local copy is kept so the state stays honest.
            return Results.Problem("The messaging provider could not dispose of the message content.", statusCode: StatusCodes.Status502BadGateway);
        }
        if (!disposed)
        {
            return Results.NotFound();
        }

        return Results.Ok(new DeleteNotificationContentResponse
        {
            NotificationId = notificationId,
            ContentDisposed = true
        });
    }
}
