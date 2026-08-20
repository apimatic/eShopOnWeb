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

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(from, to, notifications);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService notifications)
        => HandleAsync(DateTimeOffset.MinValue, DateTimeOffset.MaxValue, notifications);

    private async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notifications)
    {
        var report = await notifications.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Rows = report.Rows.Select(r => new ReconciliationRowDto
            {
                ProviderSid = r.ProviderSid,
                NotificationId = r.NotificationId,
                Match = r.Match,
                ProviderStatus = r.ProviderStatus,
                ApplicationStatus = r.ApplicationStatus,
                DateSent = r.DateSent
            }).ToList()
        });
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public System.Collections.Generic.List<ReconciliationRowDto> Rows { get; set; } = new();
}

public class ReconciliationRowDto
{
    public string? ProviderSid { get; set; }
    public int? NotificationId { get; set; }
    public string Match { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? ApplicationStatus { get; set; }
    public string? DateSent { get; set; }
}
