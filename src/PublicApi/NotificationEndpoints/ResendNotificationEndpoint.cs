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

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The request carries an
/// idempotency key (body field or Idempotency-Key header). Repeating a request under the same key
/// returns the same result without sending a second message; a fresh key sends anew. Returns the
/// identifier of the message the resend produced.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, int, ResendNotificationRequest, HttpContext>
{
    private readonly INotificationService _notificationService;

    public ResendNotificationEndpoint(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, HttpContext http) =>
            {
                return await HandleAsync(notificationId, request, http);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, ResendNotificationRequest request, HttpContext http)
    {
        var response = new ResendNotificationResponse(request?.CorrelationId() ?? Guid.NewGuid());

        var key = request?.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key) && http.Request.Headers.TryGetValue("Idempotency-Key", out var header))
            key = header.ToString();

        if (string.IsNullOrWhiteSpace(key))
            return Results.BadRequest(new { message = "An idempotency key is required (body 'idempotencyKey' or 'Idempotency-Key' header)." });

        var result = await _notificationService.ResendAsync(notificationId, key!);
        if (result is null)
            return Results.NotFound();

        response.NotificationId = result.Id;
        response.Status = result.Status;
        response.ResendOfNotificationId = result.ResendOfNotificationId;
        return Results.Ok(response);
    }
}
