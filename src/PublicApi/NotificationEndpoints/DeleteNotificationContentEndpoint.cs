using System;
using System.Threading;
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

public class DeleteNotificationContentResponse : BaseResponse
{
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId) { }
    public DeleteNotificationContentResponse() { }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}

/// <summary>
/// Operator action: disposes of a message's text — at the provider, not just
/// in this application. The fact the message was sent, and its outcome, survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(notificationId, notificationService, cancellationToken);
            })
            .Produces<DeleteNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId,
        IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        try
        {
            var redacted = await notificationService.RedactContentAsync(notificationId, cancellationToken);
            if (!redacted)
            {
                return Results.NotFound();
            }
        }
        catch (TextMessageProviderException)
        {
            // The provider still holds the text; do not hide it locally and pretend.
            return Results.Problem("The message content could not be disposed of at the provider.", statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(new DeleteNotificationContentResponse
        {
            NotificationId = notificationId,
            ContentRedacted = true
        });
    }
}
