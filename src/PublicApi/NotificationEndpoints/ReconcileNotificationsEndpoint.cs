using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset? from, DateTimeOffset? to, IOrderNotificationService service) =>
            {
                if (!from.HasValue || !to.HasValue)
                {
                    return Results.BadRequest("Query parameters 'from' and 'to' are required as ISO-8601 date-times.");
                }

                return await HandleAsync(new ReconcileNotificationsRequest(from.Value, to.Value), service);
            })
            .Produces<ReconcileNotificationsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOrderNotificationService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconcileNotificationsResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched,
            ProviderOnly = report.ProviderOnly,
            LocalOnly = report.LocalOnly
        };
        return Results.Ok(response);
    }
}
