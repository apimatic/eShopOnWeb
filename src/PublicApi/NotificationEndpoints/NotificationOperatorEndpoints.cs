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

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRouteRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, ResendNotificationRequest body, INotificationOperatorService service) =>
            {
                return await HandleAsync(new ResendNotificationRouteRequest
                {
                    NotificationId = notificationId,
                    IdempotencyKey = body.IdempotencyKey,
                    Correlation = body.CorrelationId()
                }, service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRouteRequest request, INotificationOperatorService service)
    {
        var notification = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
        return Results.Ok(new ResendNotificationResponse(request.Correlation)
        {
            NotificationId = notification.Id,
            ProviderStatus = notification.ProviderStatus,
            ProviderMessageSid = notification.ProviderMessageSid
        });
    }
}

public class ResendNotificationRouteRequest : BaseRequest
{
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public System.Guid Correlation { get; set; }
}

public class DeleteNotificationContentEndpoint : IEndpoint<IResult, int, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int notificationId, INotificationOperatorService service) =>
            {
                return await HandleAsync(notificationId, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, INotificationOperatorService service)
    {
        await service.RedactContentAsync(notificationId);
        return Results.NoContent();
    }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, INotificationOperatorService service) =>
            {
                return await HandleAsync(new ReconciliationQuery { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, INotificationOperatorService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            ProviderMessageCount = report.Matched.Count + report.ProviderOnly.Count,
            EShopMessageCount = report.Matched.Count + report.EShopOnly.Count,
            MatchedCount = report.Matched.Count
        };

        foreach (var message in report.Matched)
        {
            response.Matched.Add(new ReconciliationMessageDto
            {
                ProviderMessageSid = message.Sid,
                Status = message.Status,
                DateSent = message.DateSent,
                DateCreated = message.DateCreated
            });
        }

        foreach (var message in report.ProviderOnly)
        {
            response.ProviderOnly.Add(new ReconciliationMessageDto
            {
                ProviderMessageSid = message.Sid,
                Status = message.Status,
                DateSent = message.DateSent,
                DateCreated = message.DateCreated
            });
        }

        foreach (var local in report.EShopOnly)
        {
            response.EShopOnly.Add(new ReconciliationEShopOnlyDto
            {
                NotificationId = local.Id,
                ProviderMessageSid = local.ProviderMessageSid,
                ProviderStatus = local.ProviderStatus,
                CreatedAt = local.CreatedAt,
                ProviderDateSent = local.ProviderDateSent
            });
        }

        return Results.Ok(response);
    }
}

public class ReconciliationQuery : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}
