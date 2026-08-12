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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: a report over a date range listing the provider's own record of messages sent
/// from this application's configured number, lined up against what eShop believes it sent, so a
/// message one side knows about and the other does not is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(from, to, service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service)
    {
        if (to < from)
        {
            return Results.BadRequest(new { error = "'to' must be on or after 'from'." });
        }

        var report = await service.ReconcileAsync(from, to);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            EShopOnlyCount = report.EShopOnly.Count,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                MessageSid = m.MessageSid,
                ProviderStatus = m.ProviderStatus,
                EShopStatus = m.EShopStatus,
                NotificationId = m.NotificationId
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToEntryDto).ToList(),
            EShopOnly = report.EShopOnly.Select(ToEntryDto).ToList()
        };

        return Results.Ok(response);
    }

    private static ReconciliationEntryDto ToEntryDto(ReconciliationEntry e) => new()
    {
        MessageSid = e.MessageSid,
        Status = e.Status,
        NotificationId = e.NotificationId,
        DateSent = e.DateSent
    };
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string FromNumber { get; init; } = string.Empty;
    public int MatchedCount { get; init; }
    public int ProviderOnlyCount { get; init; }
    public int EShopOnlyCount { get; init; }
    public List<ReconciliationMatchDto> Matched { get; init; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; init; } = new();
    public List<ReconciliationEntryDto> EShopOnly { get; init; } = new();
}

public class ReconciliationMatchDto
{
    public string MessageSid { get; init; } = string.Empty;
    public string ProviderStatus { get; init; } = string.Empty;
    public string EShopStatus { get; init; } = string.Empty;
    public int? NotificationId { get; init; }
}

public class ReconciliationEntryDto
{
    public string MessageSid { get; init; } = string.Empty;
    public string? Status { get; init; }
    public int? NotificationId { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}
