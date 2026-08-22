using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, IOrderNotificationOrchestrator>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderNotificationOrchestrator orchestrator) =>
            {
                if (!DateTimeOffset.TryParse(from, out var fromValue) ||
                    !DateTimeOffset.TryParse(to, out var toValue))
                {
                    return Results.BadRequest(new { errors = new[] { "from and to must be ISO-8601 date-times." } });
                }

                var result = await orchestrator.ReconcileAsync(fromValue, toValue);
                return result.ToHttpResult(report => Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    FromNumber = report.FromNumber,
                    MatchedCount = report.MatchedCount,
                    ProviderOnlyCount = report.ProviderOnlyCount,
                    EshopOnlyCount = report.EshopOnlyCount,
                    Entries = report.Entries
                }));
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationOrchestrator orchestrator)
    {
        return Task.FromResult(Results.BadRequest());
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public System.Collections.Generic.IReadOnlyList<ReconciliationEntry> Entries { get; set; } = Array.Empty<ReconciliationEntry>();
}
