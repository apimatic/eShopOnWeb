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

public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse : BaseResponse
{
    public ResendNotificationResponse(Guid correlationId) : base(correlationId) { }
    public ResendNotificationResponse() { }

    public int NotificationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest? request, INotificationOperatorService notificationService) =>
            {
                request ??= new ResendNotificationRequest();
                request.NotificationId = notificationId;
                return await HandleAsync(request, notificationService);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, INotificationOperatorService notificationService)
    {
        var notification = await notificationService.ResendAsync(request.NotificationId, request.IdempotencyKey);
        return Results.Ok(new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = notification.Id,
            Status = notification.ProviderStatus,
            ProviderMessageSid = notification.ProviderMessageSid
        });
    }
}

public class RedactNotificationContentRequest : BaseRequest
{
    public int NotificationId { get; init; }
    public RedactNotificationContentRequest(int notificationId) => NotificationId = notificationId;
}

public class RedactNotificationContentEndpoint : IEndpoint<IResult, RedactNotificationContentRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, INotificationOperatorService notificationService) =>
            {
                return await HandleAsync(new RedactNotificationContentRequest(notificationId), notificationService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationContentRequest request, INotificationOperatorService notificationService)
    {
        await notificationService.RedactContentAsync(request.NotificationId);
        return Results.NoContent();
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class NotificationReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, INotificationOperatorService notificationService) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromValue) || !DateTimeOffset.TryParse(to, out var toValue))
                {
                    return Results.BadRequest(new { message = "`from` and `to` must be ISO-8601 date-times." });
                }

                return await HandleAsync(new ReconciliationRequest(fromValue, toValue), notificationService);
            })
            .Produces<NotificationReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, INotificationOperatorService notificationService)
    {
        var report = await notificationService.ReconcileAsync(request.From, request.To);
        return Results.Ok(report);
    }
}
