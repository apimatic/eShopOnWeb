using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: re-sends a message that did not reach the shopper. The caller-supplied idempotency
/// key makes a repeat under the same key a no-op (returning the message the first attempt produced),
/// while a fresh key is a genuine new send. Restricted to the administrator role.
/// </summary>
public class ResendNotificationEndpoint : IEndpoint
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, HttpContext http, ResendNotificationRequest? request, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(notificationId, http, request, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, HttpContext http, ResendNotificationRequest? request,
        IOrderNotificationService notificationService)
    {
        // The idempotency key may arrive via the Idempotency-Key header or in the request body.
        var idempotencyKey = http.Request.Headers.TryGetValue(IdempotencyHeader, out var header)
            ? header.ToString()
            : request?.IdempotencyKey;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { message = $"An idempotency key is required (header '{IdempotencyHeader}' or body 'idempotencyKey')." });
        }

        var existing = await notificationService.FindNotificationAsync(notificationId);
        if (existing is null)
        {
            return Results.NotFound();
        }

        try
        {
            var resend = await notificationService.ResendAsync(notificationId, idempotencyKey);
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = resend.Id,
                Notification = NotificationDto.From(resend)
            });
        }
        catch (InvalidOperationException ex)
        {
            // e.g. the original message's content was disposed of and cannot be resent
            return Results.Conflict(new { message = ex.Message });
        }
    }
}
