using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Sms;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action on a shopper's behalf: disposes of a message's content. Afterwards its text is no
/// longer retrievable from the provider (redacted there) nor from this application, while the fact that
/// a message was sent, and what became of it, survives.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService notifications) =>
            {
                try
                {
                    var outcome = await notifications.DisposeContentAsync(notificationId);
                    return outcome == ContentDisposalOutcome.NotFound
                        ? Results.NotFound()
                        : Results.NoContent();
                }
                catch (TwilioApiException ex)
                {
                    // Redaction at the provider failed; do not report the content as disposed.
                    return Results.Problem(
                        title: "The message content could not be disposed of at the provider.",
                        detail: ex.Message,
                        statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService notifications) =>
        Task.FromResult<IResult>(Results.Empty);
}
