using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>
/// Operator action: lists the provider's own record of messages sent from this application's
/// configured sending number over a date range, lined up against what eShop believes it sent.
/// Covers the whole range. from/to are ISO-8601 date-times.
/// </summary>
public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest(from, to), notificationService);
            })
            .Produces<ReconcileNotificationsResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOrderNotificationService notificationService)
    {
        var response = new ReconcileNotificationsResponse(request.CorrelationId());

        if (!DateTimeOffset.TryParse(request.From, out var from) || !DateTimeOffset.TryParse(request.To, out var to) || to < from)
        {
            return Results.BadRequest(response);
        }

        var report = await notificationService.ReconcileAsync(from, to);

        response.From = report.From;
        response.To = report.To;
        response.FromNumber = report.FromNumber;
        response.ProviderMessageCount = report.ProviderMessageCount;
        response.MatchedCount = report.MatchedCount;
        response.ProviderOnlyCount = report.ProviderOnlyCount;
        response.LocalOnlyCount = report.LocalOnlyCount;
        response.ProviderMessages = report.ProviderMessages.Select(m => new ReconciliationEntryDto
        {
            MessageSid = m.MessageSid,
            To = m.To,
            Status = m.Status,
            DateSent = m.DateSent,
            MatchStatus = m.MatchStatus,
            NotificationId = m.NotificationId
        }).ToList();
        response.LocalOnly = report.LocalOnly.Select(OrderNotificationDto.FromEntity).ToList();

        return Results.Ok(response);
    }
}

public class ReconcileNotificationsRequest : BaseRequest
{
    public ReconcileNotificationsRequest(string from, string to)
    {
        From = from;
        To = to;
    }

    public string From { get; }
    public string To { get; }
}

public class ReconciliationEntryDto
{
    public string MessageSid { get; set; } = string.Empty;
    public string? To { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? DateSent { get; set; }
    public string MatchStatus { get; set; } = string.Empty;
    public int? NotificationId { get; set; }
}

public class ReconcileNotificationsResponse : BaseResponse
{
    public ReconcileNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public ReconcileNotificationsResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderMessageCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int LocalOnlyCount { get; set; }
    public List<ReconciliationEntryDto> ProviderMessages { get; set; } = new();
    public List<OrderNotificationDto> LocalOnly { get; set; } = new();
}
