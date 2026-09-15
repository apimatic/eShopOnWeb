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
        var response = new DeleteNotificationContentResponse(request.CorrelationId());
        await operatorNotificationService.RedactContentAsync(request.NotificationId);
        response.NotificationId = request.NotificationId;
        return Results.Ok(response);
    }
}
