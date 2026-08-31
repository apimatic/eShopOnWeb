using System.Security.Claims;
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
/// Operator action: disposes of the content of a message about a shopper. The text is
/// redacted at the provider (not merely hidden here); the fact a message was sent, and
/// what became of it, survive.
/// </summary>
public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
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
            (int notificationId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(notificationId, user);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ClaimsPrincipal user)
    {
        var result = await _notificationService.RedactContentAsync(notificationId);

        return result.Outcome switch
        {
            RedactOutcome.NotFound => Results.NotFound(),
            RedactOutcome.ProviderError => Results.Problem(detail: result.Error, statusCode: StatusCodes.Status502BadGateway),
            _ => Results.NoContent()
        };
    }
}
