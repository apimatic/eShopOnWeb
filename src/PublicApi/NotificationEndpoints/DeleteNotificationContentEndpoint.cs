using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class DeleteNotificationContentRequest : BaseRequest
{
    public DeleteNotificationContentRequest(int notificationId)
    {
        NotificationId = notificationId;
    }

    public int NotificationId { get; set; }
}

public class DeleteNotificationContentResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}

public class DeleteNotificationContentEndpoint : IEndpoint<IResult, DeleteNotificationContentRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, HttpContext http, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new DeleteNotificationContentRequest(notificationId), http, notifications);
            })
            .Produces<DeleteNotificationContentResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteNotificationContentRequest request, IOrderNotificationService notifications)
        => HandleAsync(request, null!, notifications);

    private async Task<IResult> HandleAsync(
        DeleteNotificationContentRequest request,
        HttpContext http,
        IOrderNotificationService notifications)
    {
        try
        {
            await notifications.RedactContentAsync(request.NotificationId, http.RequestAborted);
            return Results.Ok(new DeleteNotificationContentResponse
            {
                NotificationId = request.NotificationId,
                ContentRedacted = true
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
