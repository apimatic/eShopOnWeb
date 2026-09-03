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

public class RedactNotificationRequest : BaseRequest
{
    public int NotificationId { get; init; }

    public RedactNotificationRequest(int notificationId)
    {
        NotificationId = notificationId;
    }
}

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationRequest, INotificationAdminService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RedactNotificationContentEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, INotificationAdminService service) =>
            {
                return await HandleAsync(new RedactNotificationRequest(notificationId), service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationRequest request, INotificationAdminService service)
    {
        try
        {
            await service.RedactContentAsync(request.NotificationId, _httpContextAccessor.HttpContext!.RequestAborted);
            return Results.NoContent();
        }
        catch (NotificationNotFoundException)
        {
            return Results.NotFound();
        }
        catch (System.InvalidOperationException ex)
        {
            return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
