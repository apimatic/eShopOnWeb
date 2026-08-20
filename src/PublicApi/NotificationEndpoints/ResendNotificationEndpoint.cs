using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
}

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderFlowService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext http, IOrderFlowService orders) =>
            {
                return await HandleAsync(request, notificationId, orders, http.RequestAborted);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderFlowService orders) =>
        HandleAsync(request, 0, orders, default);

    private static async Task<IResult> HandleAsync(
        ResendNotificationRequest request,
        int notificationId,
        IOrderFlowService orders,
        System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            var notification = await orders.ResendAsync(notificationId, request.IdempotencyKey, cancellationToken);
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = notification.Id
            });
        }
        catch (System.Exception ex)
        {
            return EndpointErrors.FromException(ex);
        }
    }
}
