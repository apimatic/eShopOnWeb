using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationEndpoint.Request, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, Request request, HttpContext http) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, http);
            })
            .Produces<Response>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, HttpContext http)
    {
        var key = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key) && http.Request.Headers.TryGetValue("Idempotency-Key", out var headerKey))
        {
            key = headerKey.ToString();
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return Results.BadRequest(new { error = "An idempotency key is required." });
        }

        try
        {
            var notification = await http.GetRequired<IOrderNotificationService>()
                .ResendAsync(request.NotificationId, key);
            return Results.Ok(new Response
            {
                NotificationId = notification.Id,
                ProviderMessageSid = notification.ProviderMessageSid,
                ProviderStatus = notification.ProviderStatus,
                Notification = NotificationDto.From(notification)
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    public class Request
    {
        public int NotificationId { get; set; }
        public string? IdempotencyKey { get; set; }
    }

    public class Response
    {
        public int NotificationId { get; set; }
        public string? ProviderMessageSid { get; set; }
        public string ProviderStatus { get; set; } = string.Empty;
        public NotificationDto Notification { get; set; } = new();
    }
}
