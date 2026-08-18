using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator report: lists the provider's own record of messages for a date range and lines them up
/// against what eShop believes it sent, so a message either side knows about and the other does not is
/// visible. It counts only messages sent from the application's configured sending number.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, (DateTimeOffset, DateTimeOffset), INotificationAdminService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, INotificationAdminService service) =>
            {
                if (!DateTimeOffset.TryParse(from, null, System.Globalization.DateTimeStyles.RoundtripKind, out var fromDate))
                    return Results.BadRequest(new { error = "'from' must be an ISO-8601 date-time." });
                if (!DateTimeOffset.TryParse(to, null, System.Globalization.DateTimeStyles.RoundtripKind, out var toDate))
                    return Results.BadRequest(new { error = "'to' must be an ISO-8601 date-time." });
                if (toDate < fromDate)
                    return Results.BadRequest(new { error = "'to' must not be earlier than 'from'." });

                try
                {
                    var report = await service.ReconcileAsync(fromDate, toDate);
                    return Results.Ok(report);
                }
                catch (SmsGatewayException ex)
                {
                    return SmsProviderProblem.ToResult(ex);
                }
            })
            .Produces<Microsoft.eShopWeb.ApplicationCore.Services.ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync((DateTimeOffset, DateTimeOffset) request, INotificationAdminService service) =>
        Task.FromResult(Results.Ok());
}
