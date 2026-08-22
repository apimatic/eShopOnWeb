using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationRouteRequest : BaseRequest
{
    public int NotificationId { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;

    public ResendNotificationRouteRequest(int notificationId, string idempotencyKey)
    {
        NotificationId = notificationId;
        IdempotencyKey = idempotencyKey;
    }
}

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRouteRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId:int}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest body, IOrderNotificationService service) =>
            {
                return await HandleAsync(
                    new ResendNotificationRouteRequest(notificationId, body.IdempotencyKey),
                    service);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRouteRequest request, IOrderNotificationService service)
    {
        var resent = await service.ResendAsync(request.NotificationId, request.IdempotencyKey);
        var response = new ResendNotificationResponse(request.CorrelationId())
        {
            NotificationId = resent.Id,
            OrderId = resent.OrderId,
            ProviderSid = resent.ProviderSid,
            ProviderStatus = resent.ProviderStatus
        };
        return Results.Ok(response);
    }
}

public class RedactNotificationRequest : BaseRequest
{
    public int NotificationId { get; init; }
    public RedactNotificationRequest(int notificationId) => NotificationId = notificationId;
}

public class RedactNotificationEndpoint : IEndpoint<IResult, RedactNotificationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId:int}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new RedactNotificationRequest(notificationId), service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(RedactNotificationRequest request, IOrderNotificationService service)
    {
        await service.RedactContentAsync(request.NotificationId);
        return Results.NoContent();
    }
}

public class ReconciliationQuery : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }

    public ReconciliationQuery(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconciliationQuery(from, to), service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IOrderNotificationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                ProviderSid = e.ProviderSid,
                NotificationId = e.NotificationId,
                ProviderStatus = e.ProviderStatus,
                EshopStatus = e.EshopStatus,
                Match = e.Match
            }).ToList()
        };
        return Results.Ok(response);
    }
}
