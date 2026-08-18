using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller supplies an idempotency
/// key — repeating under the same key does not send a second message, while a fresh key is a new attempt.
/// The response's top-level notificationId is the message the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, HttpContext>
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly IOrderNotificationService _service;

    public ResendNotificationEndpoint(IOrderNotificationService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext http) =>
            {
                request ??= new ResendNotificationRequest();
                request.NotificationId = notificationId;
                return await HandleAsync(request, http);
            })
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, HttpContext http)
    {
        var idempotencyKey = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey) && http.Request.Headers.TryGetValue(IdempotencyKeyHeader, out var header))
        {
            idempotencyKey = header.ToString();
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest("An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header).");
        }

        var notification = await _service.ResendAsync(request.NotificationId, idempotencyKey, http.RequestAborted);
        if (notification is null)
        {
            return Results.NotFound();
        }

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            Notification = NotificationDto.From(notification)
        };
        return Results.Ok(response);
    }
}
