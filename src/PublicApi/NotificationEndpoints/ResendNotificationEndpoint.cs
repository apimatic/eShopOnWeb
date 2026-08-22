using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOrderNotificationService service, HttpContext httpContext) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, service, httpContext);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
        => HandleAsync(request, service, null!);

    private static async Task<IResult> HandleAsync(
        ResendNotificationRequest request,
        IOrderNotificationService service,
        HttpContext httpContext)
    {
        var resent = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, httpContext.RequestAborted);
        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = resent.Id,
            Status = resent.ProviderStatus,
            ProviderSid = resent.ProviderSid
        });
    }
}
