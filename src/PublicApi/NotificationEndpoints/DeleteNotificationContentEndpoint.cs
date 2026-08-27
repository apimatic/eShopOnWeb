using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Services.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's text — at the provider, not just in this app —
/// while keeping the record that a message was sent and what became of it.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int>
{
    private readonly IOrderNotificationService _notificationService;

    public DeleteNotificationContentEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId) =>
            {
                return await HandleAsync(notificationId);
            })
            .Produces<DeleteNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId)
    {
        try
        {
            var found = await _notificationService.RedactContentAsync(notificationId);
            if (!found)
            {
                return Results.NotFound();
            }
        }
        catch (TwilioApiException ex)
        {
            // The provider still holds the text; the operator must be able to retry.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(new DeleteNotificationContentResponse
        {
            NotificationId = notificationId,
            ContentRedacted = true
        });
    }
}
