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
/// Operator action: re-send a message that did not reach the shopper. The request carries a
/// caller-supplied idempotency key — repeating a request under the same key returns the existing
/// resend rather than sending a second message; a fresh key is a genuine new attempt. Returns the
/// notificationId of the message the resend produced. Restricted to the administrator role.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationBody? body, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ResendNotificationRequest(notificationId, body?.IdempotencyKey), service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.Problem(detail: "An idempotencyKey is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var notification = await service.ResendNotificationAsync(request.NotificationId, request.IdempotencyKey!);
            return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
            {
                NotificationId = notification.Id,
                Status = notification.Status
            });
        }
        catch (ArgumentException ex)
        {
            // Only remaining cause is an unknown notification id.
            return Results.NotFound(new { message = ex.Message });
        }
    }
}
