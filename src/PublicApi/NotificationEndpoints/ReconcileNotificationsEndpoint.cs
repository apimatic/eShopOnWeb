using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int ApplicationOnlyCount { get; set; }
    public List<ReconciliationMatchDto> Matches { get; set; } = new();
    public List<ProviderMessageDto> ProviderOnly { get; set; } = new();
    public List<NotificationDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationMatchDto
{
    public NotificationDto Notification { get; set; } = new();
    public ProviderMessageDto Provider { get; set; } = new();
}

public class ProviderMessageDto
{
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Body { get; set; }
    public string? From { get; set; }
    public string? DateSent { get; set; }
    public string? DateCreated { get; set; }
    public string? Direction { get; set; }
}

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, IOrderWorkflowService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReconcileNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderWorkflowService orders) =>
            {
                return await ReconcileAsync(from, to, orders);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderWorkflowService orders)
    {
        var query = _httpContextAccessor.HttpContext?.Request.Query;
        var from = query?["from"].ToString() ?? string.Empty;
        var to = query?["to"].ToString() ?? string.Empty;
        return ReconcileAsync(from, to, orders);
    }

    private static async Task<IResult> ReconcileAsync(string from, string to, IOrderWorkflowService orders)
    {
        if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromUtc)
            || !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toUtc))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }

        var report = await orders.ReconcileAsync(fromUtc, toUtc);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.Matches.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            ApplicationOnlyCount = report.ApplicationOnly.Count
        };

        response.Matches.AddRange(report.Matches.Select(m => new ReconciliationMatchDto
        {
            Notification = NotificationDto.From(m.Notification),
            Provider = ToDto(m.ProviderMessage)
        }));
        response.ProviderOnly.AddRange(report.ProviderOnly.Select(ToDto));
        response.ApplicationOnly.AddRange(report.ApplicationOnly.Select(NotificationDto.From));

        return Results.Ok(response);
    }

    private static ProviderMessageDto ToDto(ProviderMessage message)
    {
        return new ProviderMessageDto
        {
            Sid = message.Sid,
            Status = message.Status,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            Body = message.Body,
            From = message.From,
            DateSent = message.DateSent,
            DateCreated = message.DateCreated,
            Direction = message.Direction
        };
    }
}
