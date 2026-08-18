using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: disposes of a message's content at the caller's request. The text is redacted at the
/// provider so it is no longer retrievable there — not merely hidden here — while the fact a message was
/// sent, and what became of it, survives.
/// </summary>
public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int>
{
    private readonly IOrderNotificationService _notificationService;

    public DisposeNotificationContentEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId) => await HandleAsync(notificationId))
            .Produces<DisposeNotificationContentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId)
    {
        try
        {
            await _notificationService.DisposeContentAsync(notificationId);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (TwilioApiException)
        {
            // The provider could not redact the content; surface it so the operator knows it is not disposed.
            return Results.Problem(
                title: "Content disposal failed at the provider.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(new DisposeNotificationContentResponse
        {
            NotificationId = notificationId,
            ContentDisposed = true
        });
    }
}
