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
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEntryDto
{
    public string ProviderSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? NotificationId { get; set; }

    public static ReconciliationEntryDto FromEntry(ReconciliationEntry e) => new()
    {
        ProviderSid = e.ProviderSid,
        ProviderStatus = e.ProviderStatus,
        EShopStatus = e.EShopStatus,
        NotificationId = e.NotificationId
    };
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider knows about that eShop does not.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider does not report.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} — lists the provider's own record of
/// messages sent from the configured sending number over the range and lines them up against what
/// eShop believes it sent. (Administrators.)
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, INotificationOperationsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, INotificationOperationsService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, INotificationOperationsService service)
    {
        if (request.From is null || request.To is null)
            return Results.BadRequest("Both 'from' and 'to' are required ISO-8601 date-times.");
        if (request.From > request.To)
            return Results.BadRequest("'from' must be on or before 'to'.");

        var report = await service.ReconcileAsync(request.From.Value, request.To.Value);
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            EShopOnlyCount = report.EShopOnly.Count,
            Matched = report.Matched.Select(ReconciliationEntryDto.FromEntry).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ReconciliationEntryDto.FromEntry).ToList(),
            EShopOnly = report.EShopOnly.Select(ReconciliationEntryDto.FromEntry).ToList()
        };
        return Results.Ok(response);
    }
}
