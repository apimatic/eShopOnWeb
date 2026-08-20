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

public class ReconciliationEndpoint : IEndpoint<IResult, IOrderNotificationService>
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

    public Task<IResult> HandleAsync(IOrderNotificationService notificationService)
        => Task.FromResult(Results.BadRequest());

    private async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService)
    {
        var report = await notificationService.ReconcileAsync(from, to);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            ApplicationOnlyCount = report.ApplicationOnly.Count,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            ApplicationOnly = report.ApplicationOnly.Select(ToDto).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationItemDto ToDto(ReconciledMessage item) => new()
    {
        NotificationId = item.NotificationId,
        ProviderMessageSid = item.ProviderMessageSid,
        ProviderStatus = item.ProviderStatus,
        ApplicationStatus = item.ApplicationStatus,
        ProviderDateSent = item.ProviderDateSent,
        ApplicationCreatedAt = item.ApplicationCreatedAt
    };
}
