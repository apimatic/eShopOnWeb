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
/// Operator report: the provider's own record of messages sent from this application's configured
/// number over a date range, lined up against what eShop believes it sent — so a message one side
/// knows about and the other does not becomes visible. Covers the whole range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IOrderNotificationService notificationService) =>
            {
                if (from is null || to is null)
                    return Results.BadRequest(new { error = "Both 'from' and 'to' ISO-8601 date-times are required." });
                if (from > to)
                    return Results.BadRequest(new { error = "'from' must not be later than 'to'." });

                return await HandleAsync(new ReconciliationRequest { From = from.Value, To = to.Value }, notificationService);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notificationService)
    {
        var report = await notificationService.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            EShopOnlyCount = report.EShopOnly.Count,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            EShopOnly = report.EShopOnly.Select(ToDto).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry e) => new()
    {
        ProviderMessageSid = e.ProviderMessageSid,
        NotificationId = e.NotificationId,
        OrderId = e.OrderId,
        ProviderStatus = e.ProviderStatus,
        EShopStatus = e.EShopStatus,
        ProviderDateSent = e.ProviderDateSent
    };
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    public int MatchedCount { get; set; }

    /// <summary>Messages the provider knows about but eShop does not.</summary>
    public int ProviderOnlyCount { get; set; }

    /// <summary>Messages eShop believes it sent but the provider does not report in range.</summary>
    public int EShopOnlyCount { get; set; }

    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? ProviderMessageSid { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
}
