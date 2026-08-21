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

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, HttpContext httpContext, IOrderNotificationService service) =>
            {
                var request = new ResendNotificationRequest { NotificationId = notificationId };

                if (httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var headerKey)
                    && !string.IsNullOrWhiteSpace(headerKey))
                {
                    request.IdempotencyKey = headerKey.ToString();
                }

                if (httpContext.Request.ContentLength > 0
                    && httpContext.Request.HasJsonContentType())
                {
                    var body = await httpContext.Request.ReadFromJsonAsync<ResendNotificationRequest>();
                    if (body is not null && !string.IsNullOrWhiteSpace(body.IdempotencyKey))
                    {
                        request.IdempotencyKey = body.IdempotencyKey;
                    }
                }

                return await HandleAsync(request, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOrderNotificationService service)
    {
        try
        {
            var notification = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
            return Results.Ok(new ResendNotificationResponse
            {
                NotificationId = notification.Id,
                Status = notification.Status,
                ProviderMessageSid = notification.ProviderMessageSid
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
