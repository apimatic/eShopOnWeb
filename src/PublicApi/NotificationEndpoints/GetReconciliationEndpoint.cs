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

public class GetReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber
        };
        response.Matches.AddRange(report.Matches.Select(m => new ReconciledNotificationDto
        {
            NotificationId = m.NotificationId,
            ProviderMessageSid = m.ProviderMessageSid,
            Kind = m.Kind,
            Status = m.Status,
            CreatedAt = m.CreatedAt
        }));
        response.ProviderOnly.AddRange(report.ProviderOnly.Select(p => new ProviderOnlyMessageDto
        {
            ProviderMessageSid = p.ProviderMessageSid,
            Status = p.Status,
            DateCreated = p.DateCreated,
            DateSent = p.DateSent
        }));
        response.EShopOnly.AddRange(report.EShopOnly.Select(m => new ReconciledNotificationDto
        {
            NotificationId = m.NotificationId,
            ProviderMessageSid = m.ProviderMessageSid,
            Kind = m.Kind,
            Status = m.Status,
            CreatedAt = m.CreatedAt
        }));
        return Results.Ok(response);
    }
}
