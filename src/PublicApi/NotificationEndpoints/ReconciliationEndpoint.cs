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
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service, HttpContext http) =>
            {
                var report = await service.ReconcileAsync(from, to, http.RequestAborted);
                var response = new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    FromNumber = report.FromNumber,
                    Truncated = report.Truncated,
                    Matched = report.Matched.Select(Map).ToList(),
                    ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
                    LocalOnly = report.LocalOnly.Select(Map).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service)
    {
        return Task.FromResult<IResult>(Results.Ok());
    }

    private static ReconciliationItemDto Map(ReconciliationEntry entry)
    {
        return new ReconciliationItemDto
        {
            NotificationId = entry.NotificationId,
            ProviderSid = entry.ProviderSid,
            Status = entry.Status,
            DateSent = entry.DateSent,
            Kind = entry.Kind
        };
    }
}
