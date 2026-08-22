using System.Linq;
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
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service, HttpContext httpContext) =>
            {
                var report = await service.ReconcileAsync(from, to, httpContext.RequestAborted);
                return Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    FromNumber = report.FromNumber,
                    Truncated = report.Truncated,
                    Matched = report.Matched.Select(ToDto).ToList(),
                    ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
                    ApplicationOnly = report.ApplicationOnly.Select(ToDto).ToList()
                });
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service)
        => Task.FromResult(Results.BadRequest());

    private static ReconciledMessageDto ToDto(ReconciledMessage message)
        => new()
        {
            ProviderSid = message.ProviderSid,
            NotificationId = message.NotificationId,
            ProviderStatus = message.ProviderStatus,
            ApplicationStatus = message.ApplicationStatus,
            DateSent = message.DateSent
        };
}
