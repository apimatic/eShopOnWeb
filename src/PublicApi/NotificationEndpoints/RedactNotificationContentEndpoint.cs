using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class RedactNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public RedactNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}

public class RedactNotificationContentResponse : BaseResponse
{
}

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, IOrderWorkflowService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderWorkflowService orders) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest(notificationId), orders);
            })
            .Produces<RedactNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationContentRequest request, IOrderWorkflowService orders)
    {
        await orders.RedactContentAsync(request.NotificationId);
        return Results.Ok(new RedactNotificationContentResponse());
    }
}
