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

public class RedactNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; init; }
    public RedactNotificationContentRequest(int notificationId) => NotificationId = notificationId;
}

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext httpContext, INotificationOperatorService operatorService) =>
            {
                try
                {
                    await operatorService.RedactContentAsync(notificationId, httpContext.RequestAborted);
                    return Results.NoContent();
                }
                catch (NotificationNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (SmsProviderException)
                {
                    return Results.Json(new { message = "The provider could not dispose of the message content." }, statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(RedactNotificationContentRequest request, INotificationOperatorService operatorService)
        => Task.FromResult(Results.NoContent());
}
