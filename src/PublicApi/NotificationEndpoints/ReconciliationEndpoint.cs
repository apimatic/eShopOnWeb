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
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, INotificationReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, INotificationReconciliationService reconciliationService) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, reconciliationService);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, INotificationReconciliationService reconciliationService)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { message = "'to' must be on or after 'from'." });
        }

        var report = await reconciliationService.ReconcileAsync(request.From, request.To);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            LocalOnly = report.LocalOnly.Select(ToDto).ToList()
        });
    }

    private static ReconciliationEntryDto ToDto(NotificationReconciliationEntry entry)
    {
        return new ReconciliationEntryDto
        {
            NotificationId = entry.NotificationId,
            ProviderMessageSid = entry.ProviderMessageSid,
            LocalStatus = entry.LocalStatus,
            ProviderStatus = entry.ProviderStatus,
            Match = entry.Match
        };
    }
}
