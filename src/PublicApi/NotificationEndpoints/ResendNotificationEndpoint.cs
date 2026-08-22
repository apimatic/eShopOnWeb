using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(notificationId, request, notifications);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService notifications) =>
        throw new System.NotSupportedException();

    private async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, IOrderNotificationService notifications)
    {
        try
        {
            var result = await notifications.ResendAsync(notificationId, request.IdempotencyKey);
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = result.NotificationId,
                Notification = NotificationMapper.ToDto(result)
            });
        }
        catch (NotificationNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOrderStateException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }
}
