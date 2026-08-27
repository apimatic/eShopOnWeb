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

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To, default);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Incomplete = report.Incomplete,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                NotificationId = m.NotificationId,
                ProviderSid = m.ProviderSid,
                Status = m.Status
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(p => new ReconciliationProviderEntryDto
            {
                ProviderSid = p.ProviderSid,
                Status = p.Status,
                DateSent = p.DateSent
            }).ToList(),
            LocalOnly = report.LocalOnly.Select(l => new ReconciliationLocalEntryDto
            {
                NotificationId = l.NotificationId,
                ProviderSid = l.ProviderSid,
                Status = l.Status
            }).ToList()
        };
        return Results.Ok(response);
    }
}
