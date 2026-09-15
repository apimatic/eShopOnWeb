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
/// Operator action: a reconciliation report over a date range, listing the provider's own record of
/// messages sent from this application's configured sending number and lining them up against what
/// eShop believes it sent. Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly INotificationReconciliationService _reconciliationService;

    public ReconciliationEndpoint(INotificationReconciliationService reconciliationService)
    {
        _reconciliationService = reconciliationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to));
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { message = "'to' must be on or after 'from'." });
        }

        var report = await _reconciliationService.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.Matched.Count,
            AtProviderOnlyCount = report.AtProviderOnly.Count,
            AtEShopOnlyCount = report.AtEShopOnly.Count,
            Matched = report.Matched.Select(Map).ToList(),
            AtProviderOnly = report.AtProviderOnly.Select(Map).ToList(),
            AtEShopOnly = report.AtEShopOnly.Select(Map).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationEntryDto Map(ReconciliationEntry entry) => new()
    {
        ProviderMessageSid = entry.ProviderMessageSid,
        ProviderStatus = entry.ProviderStatus,
        EShopStatus = entry.EShopStatus,
        NotificationId = entry.NotificationId,
        OrderId = entry.OrderId,
        DateSent = entry.DateSent
    };
}

public class ReconciliationRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    /// <summary>Messages both the provider and eShop know about.</summary>
    public int MatchedCount { get; set; }

    /// <summary>Messages the provider recorded that eShop has no notification for.</summary>
    public int AtProviderOnlyCount { get; set; }

    /// <summary>Notifications eShop believes it sent in the range that the provider's record does not show.</summary>
    public int AtEShopOnlyCount { get; set; }

    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> AtProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> AtEShopOnly { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}
