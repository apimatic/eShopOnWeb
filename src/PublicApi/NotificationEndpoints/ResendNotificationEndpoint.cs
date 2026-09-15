using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRouteRequest, INotificationOperatorService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ResendNotificationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest body, INotificationOperatorService service) =>
            {
                return await HandleAsync(new ResendNotificationRouteRequest(notificationId, body.IdempotencyKey), service);
            })
            .Produces<ResendNotificationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRouteRequest request, INotificationOperatorService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }

        var http = _httpContextAccessor.HttpContext!;
        var created = await service.ResendAsync(request.NotificationId, request.IdempotencyKey.Trim(), http.RequestAborted);
        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = created.Id,
            ProviderSid = created.ProviderSid,
            ProviderStatus = created.ProviderStatus,
            SendFailure = created.SendFailure
        };
        return Results.Ok(response);
    }
}
