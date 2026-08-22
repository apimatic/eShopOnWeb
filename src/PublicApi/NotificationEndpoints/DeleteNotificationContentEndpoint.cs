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

public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest, IOperatorNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOperatorNotificationService operatorNotificationService) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId), operatorNotificationService);
            })
            .Produces<DeleteNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteNotificationContentRequest request, IOperatorNotificationService operatorNotificationService)
    {
        await operatorNotificationService.RedactContentAsync(request.NotificationId);
        var response = new DeleteNotificationContentResponse(request.CorrelationId())
        {
            NotificationId = request.NotificationId,
            ContentRedacted = true
        };
        return Results.Ok(response);
    }
}

public class DeleteNotificationContentRequest : BaseRequest
{
    public DeleteNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; }
}

public class DeleteNotificationContentResponse : BaseResponse
{
    public DeleteNotificationContentResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}
