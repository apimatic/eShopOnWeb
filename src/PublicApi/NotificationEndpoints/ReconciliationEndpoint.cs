using System;
using System.Linq;
using System.Threading;
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
            (string from, string to, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(from, to, service, ct);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service)
        => HandleAsync(string.Empty, string.Empty, service, CancellationToken.None);

    private async Task<IResult> HandleAsync(string from, string to, IOrderNotificationService service, CancellationToken ct)
    {
        if (!DateTimeOffset.TryParse(from, out var fromDate) || !DateTimeOffset.TryParse(to, out var toDate))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }

        var report = await service.ReconcileAsync(fromDate, toDate, ct);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Truncated = report.Truncated,
            Matched = report.Matched.Select(ReconciliationEntryDto.From).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ReconciliationEntryDto.From).ToList(),
            EshopOnly = report.EshopOnly.Select(ReconciliationEntryDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
