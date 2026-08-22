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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To, default);
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Truncated = report.Truncated,
            Matched = report.Matched.Select(Map).ToList(),
            ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
            LocalOnly = report.LocalOnly.Select(Map).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationEntryDto Map(ReconciliationEntry entry)
    {
        return new ReconciliationEntryDto
        {
            ProviderSid = entry.ProviderSid,
            NotificationId = entry.NotificationId,
            LocalStatus = entry.LocalStatus,
            ProviderStatus = entry.ProviderStatus,
            DateSent = entry.DateSent
        };
    }
}
