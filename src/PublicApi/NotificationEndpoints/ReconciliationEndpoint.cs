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
        try
        {
            var report = await service.ReconcileAsync(request.From, request.To);
            var response = new ReconciliationResponse(request.CorrelationId())
            {
                From = report.From,
                To = report.To,
                FromNumber = report.FromNumber,
                ProviderCount = report.ProviderCount,
                ApplicationCount = report.ApplicationCount,
                Matched = report.Matched.Select(Map).ToList(),
                ProviderOnly = report.ProviderOnly.Select(Map).ToList(),
                ApplicationOnly = report.ApplicationOnly.Select(Map).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return ex.ToResult();
        }
    }

    private static ReconciliationEntryDto Map(ReconciliationEntry entry)
        => new()
        {
            NotificationId = entry.NotificationId,
            ProviderMessageSid = entry.ProviderMessageSid,
            ProviderStatus = entry.ProviderStatus,
            Kind = entry.Kind,
            Match = entry.Match
        };
}
