using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IShopperOrderService>
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest? request, IShopperOrderService service, HttpContext http) =>
            {
                var body = request ?? new ResendNotificationRequest();
                var key = http.Request.Headers[IdempotencyHeader].FirstOrDefault()
                    ?? body.IdempotencyKey;
                body.NotificationId = notificationId;
                body.IdempotencyKey = key;
                return await HandleAsync(body, service, http.RequestAborted);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ResendNotificationRequest request, IShopperOrderService service) =>
        HandleAsync(request, service, default);

    private async Task<IResult> HandleAsync(
        ResendNotificationRequest request,
        IShopperOrderService service,
        System.Threading.CancellationToken cancellationToken)
    {
        var result = await service.ResendAsync(request.NotificationId, request.IdempotencyKey ?? string.Empty, cancellationToken);
        var dto = NotificationDto.From(result);
        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = dto.NotificationId,
            Notification = dto
        });
    }
}

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(System.Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    public int NotificationId { get; set; }
    public NotificationDto Notification { get; set; } = new();
}
