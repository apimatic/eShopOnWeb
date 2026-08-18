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

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.NotificationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(System.Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();
}

/// <summary>
/// GET /api/notifications/reconciliation?from={from}&amp;to={to} &ndash; the provider's own record of
/// messages sent from this application's configured sending number over the range, lined up against
/// what eShop believes it sent. Covers the whole range.
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
                {
                    return Results.BadRequest(new { message = "'from' and 'to' are required ISO-8601 date-times." });
                }
                return await HandleAsync(new ReconciliationRequest { From = from.Value, To = to.Value }, notificationService);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notificationService)
    {
        var report = await notificationService.ReconcileAsync(request.From, request.To);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            ProviderCount = report.ProviderCount,
            EShopCount = report.EShopCount,
            MatchedCount = report.MatchedCount,
            Matched = report.Matched.Select(ReconciliationEntryDto.From).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ReconciliationEntryDto.From).ToList(),
            EShopOnly = report.EShopOnly.Select(ReconciliationEntryDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
