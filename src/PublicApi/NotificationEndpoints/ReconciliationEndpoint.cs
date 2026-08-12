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

/// <summary>
/// Operator reconciliation over a date range: the provider's own record for eShop's configured
/// sending number, lined up against what eShop believes it sent. Administrator-only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(from, to, notificationService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService)
    {
        if (to < from)
            return Results.BadRequest(new { error = "'to' must be on or after 'from'." });

        var report = await notificationService.ReconcileAsync(from, to);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            ProviderCount = report.ProviderCount,
            EShopCount = report.EShopCount,
            MatchedCount = report.MatchedCount,
            ProviderOnlyCount = report.ProviderOnlyCount,
            EShopOnlyCount = report.EShopOnlyCount,
            Entries = report.Entries.Select(e => new ReconciliationEntryDto
            {
                Sid = e.Sid,
                Discrepancy = e.Discrepancy.ToString(),
                ProviderStatus = e.ProviderStatus,
                EShopStatus = e.EShopStatus,
                OrderId = e.OrderId,
                Kind = e.Kind?.ToString(),
                DateSent = e.DateSent
            }).ToList()
        };
        return Results.Ok(response);
    }
}
