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

public class GetNotificationReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        if (!DateTimeOffset.TryParse(request.From, out var from))
        {
            return Results.BadRequest("Query parameter 'from' must be an ISO-8601 date-time.");
        }

        if (!DateTimeOffset.TryParse(request.To, out var to))
        {
            return Results.BadRequest("Query parameter 'to' must be an ISO-8601 date-time.");
        }

        var report = await service.ReconcileAsync(from, to);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Matched = report.Matched.Select(m => new ReconciledItemDto
            {
                NotificationId = m.NotificationId,
                ProviderMessageSid = m.ProviderMessageSid,
                ApplicationStatus = m.ApplicationStatus,
                ProviderStatus = m.ProviderStatus
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(p => new ProviderOnlyItemDto
            {
                ProviderMessageSid = p.ProviderMessageSid,
                ProviderStatus = p.ProviderStatus,
                DateSent = p.DateSent
            }).ToList(),
            ApplicationOnly = report.ApplicationOnly.Select(a => new ApplicationOnlyItemDto
            {
                NotificationId = a.NotificationId,
                ProviderMessageSid = a.ProviderMessageSid,
                ApplicationStatus = a.ApplicationStatus
            }).ToList()
        });
    }
}

public class ReconciliationRequest
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}
