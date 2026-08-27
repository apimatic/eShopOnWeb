using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's content. The text is redacted at the
/// provider (not merely hidden here); the record that a message was sent, and its
/// outcome, survives.
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
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId)
    {
        var result = await _notificationService.RedactContentAsync(notificationId);

        return result.Status switch
        {
            RedactContentStatus.NotFound => Results.NotFound(),
            RedactContentStatus.ProviderRedactionFailed => Results.Json(new
            {
                message = "The provider did not confirm redaction; the content was left untouched."
            }, statusCode: StatusCodes.Status502BadGateway),
            _ => Results.Ok(new
            {
                notificationId,
                contentRedacted = true,
                alreadyRedacted = result.Status == RedactContentStatus.AlreadyRedacted
            })
        };
    }
}
