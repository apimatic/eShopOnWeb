using System;
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
    public RedactNotificationContentResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RedactNotificationContentResponse()
    {
    }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; } = true;
}

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest(notificationId), notifications);
            })
            .Produces<RedactNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationContentRequest request, IOrderNotificationService notifications)
    {
        await notifications.RedactContentAsync(request.NotificationId);
        return Results.Ok(new RedactNotificationContentResponse(request.CorrelationId())
        {
            NotificationId = request.NotificationId
        });
    }
}
