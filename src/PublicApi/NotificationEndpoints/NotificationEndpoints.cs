using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationEndpoint : IEndpoint<IResult, ResendNotificationRequest, IOperatorOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/notifications/{notificationId}/resend",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, ResendNotificationRequest request, IOperatorOrderNotificationService operatorService) =>
            {
                request.NotificationId = notificationId;
                return await HandleAsync(request, operatorService);
            })
            .Produces<ResendNotificationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ResendNotificationRequest request, IOperatorOrderNotificationService operatorService)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Results.BadRequest(new { message = "idempotencyKey is required." });
        }

        var notification = await operatorService.ResendAsync(request.NotificationId, request.IdempotencyKey);
        return Results.Ok(new ResendNotificationResponse
        {
            NotificationId = notification.Id,
            ProviderStatus = notification.ProviderStatus,
            ProviderMessageSid = notification.ProviderMessageSid
        });
    }
}

public class DisposeNotificationContentEndpoint : IEndpoint<IResult, int, IOperatorOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/notifications/{notificationId}/content",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int notificationId, IOperatorOrderNotificationService operatorService) =>
            {
                return await HandleAsync(notificationId, operatorService);
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(int notificationId, IOperatorOrderNotificationService operatorService)
    {
        await operatorService.DisposeContentAsync(notificationId);
        return Results.Ok();
    }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOperatorOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOperatorOrderNotificationService operatorService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), operatorService);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOperatorOrderNotificationService operatorService)
    {
        var report = await operatorService.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                Notification = OrderResponseMapper.ToDto(m.Notification),
                Provider = ToProviderDto(m.ProviderMessage)
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToProviderDto).ToList(),
            EshopOnly = report.EshopOnly.Select(OrderResponseMapper.ToDto).ToList()
        });
    }

    private static ProviderMessageDto ToProviderDto(SmsMessageSnapshot snapshot)
    {
        return new ProviderMessageDto
        {
            Sid = snapshot.Sid,
            Status = snapshot.Status,
            ErrorCode = snapshot.ErrorCode,
            ErrorMessage = snapshot.ErrorMessage,
            DateCreated = snapshot.DateCreated,
            DateSent = snapshot.DateSent
        };
    }
}

public class ResendNotificationRequest
{
    [JsonIgnore]
    public int NotificationId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public string ProviderStatus { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

public record ReconciliationRequest(DateTimeOffset From, DateTimeOffset To);

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ProviderMessageDto> ProviderOnly { get; set; } = new();
    public List<OrderNotificationDto> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public OrderNotificationDto Notification { get; set; } = new();
    public ProviderMessageDto Provider { get; set; } = new();
}

public class ProviderMessageDto
{
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? DateCreated { get; set; }
    public string? DateSent { get; set; }
}
