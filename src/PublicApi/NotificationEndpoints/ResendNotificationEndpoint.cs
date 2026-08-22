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

public class ResendNotificationRequest : BaseRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
}

public class ResendNotificationEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest request, IOrderNotificationService notificationService) =>
            {
                try
                {
                    var notification = await notificationService.ResendAsync(notificationId, request.IdempotencyKey);
                    return Results.Ok(new ResendNotificationResponse
                    {
                        NotificationId = notification.Id,
                        Kind = notification.Kind.ToString(),
                        ProviderMessageSid = notification.ProviderMessageSid,
                        ProviderStatus = notification.ProviderStatus
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
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService notificationService)
    {
        return Task.FromResult<IResult>(Results.StatusCode(StatusCodes.Status501NotImplemented));
    }
}
