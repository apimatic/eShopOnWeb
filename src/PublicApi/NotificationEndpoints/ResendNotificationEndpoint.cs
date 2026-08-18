using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The request carries an
/// idempotency key (body field or <c>Idempotency-Key</c> header) — repeating under the same key
/// sends nothing new and returns the first result, while a fresh key is a legitimate new attempt.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, HttpContext>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public ResendNotificationEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext http) => await HandleAsync(notificationId, request, http))
            .Produces<ResendNotificationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, HttpContext http)
    {
        var idempotencyKey = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            idempotencyKey = http.Request.Headers["Idempotency-Key"].ToString();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.BadRequest("An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header).");

        var notification = await _orderNotificationService.ResendAsync(notificationId, idempotencyKey, http.RequestAborted);
        if (notification is null)
            return Results.NotFound();

        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            Status = notification.Status,
            ProviderMessageSid = notification.ProviderMessageSid
        };
        return Results.Ok(response);
    }
}
