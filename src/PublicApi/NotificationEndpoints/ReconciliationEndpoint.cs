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

public class ReconciliationEndpoint : IEndpoint<IResult, IOperatorOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOperatorOrderNotificationService operatorService) =>
            {
                return await HandleAsync(from, to, operatorService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOperatorOrderNotificationService operatorService)
        => HandleAsync(string.Empty, string.Empty, operatorService);

    private async Task<IResult> HandleAsync(string from, string to, IOperatorOrderNotificationService operatorService)
    {
        if (!DateTimeOffset.TryParse(from, out var fromDate) || !DateTimeOffset.TryParse(to, out var toDate))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }

        var report = await operatorService.ReconcileAsync(fromDate, toDate, default);
        var response = new ReconciliationResponse
        {
            From = report.From.ToString("O"),
            To = report.To.ToString("O"),
            FromNumber = report.FromNumber,
            Truncated = report.Truncated,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                Local = NotificationDto.From(m.Local),
                Provider = ProviderMessageDto.FromSnapshot(m.Provider)
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ProviderMessageDto.FromSnapshot).ToList(),
            EShopOnly = report.EShopOnly.Select(NotificationDto.From).ToList()
        };

        return Results.Ok(response);
    }
}
