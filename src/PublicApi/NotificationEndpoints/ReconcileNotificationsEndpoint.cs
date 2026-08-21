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

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, DateTimeOffset, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, INotificationOperatorService notifications) =>
            {
                return await HandleAsync(from, to, notifications);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(DateTimeOffset from, INotificationOperatorService notifications)
        => throw new System.NotSupportedException();

    private async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, INotificationOperatorService notifications)
    {
        var report = await notifications.ReconcileAsync(from, to);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber
        };
        response.Entries.AddRange(report.Entries.Select(e => new ReconciliationEntryDto
        {
            Match = e.Match,
            NotificationId = e.NotificationId,
            ProviderSid = e.ProviderSid,
            ProviderStatus = e.ProviderStatus,
            EshopStatus = e.EshopStatus,
            Kind = e.Kind
        }));
        return Results.Ok(response);
    }
}
