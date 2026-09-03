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

public class ResendNotificationRouteRequest : ResendNotificationRequest
{
    public int NotificationId { get; set; }
}

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRouteRequest, INotificationAdminService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ResendNotificationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest body, INotificationAdminService service) =>
            {
                body ??= new ResendNotificationRequest();
                return await HandleAsync(new ResendNotificationRouteRequest
                {
                    NotificationId = notificationId,
                    IdempotencyKey = body.IdempotencyKey
                }, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRouteRequest request, INotificationAdminService service)
    {
        try
        {
            var created = await service.ResendAsync(request.NotificationId, request.IdempotencyKey, _httpContextAccessor.HttpContext!.RequestAborted);
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = created.Id,
                Status = created.Status
            });
        }
        catch (NotificationNotFoundException)
        {
            return Results.NotFound();
        }
        catch (NotificationNotResendableException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (System.InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
