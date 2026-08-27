using System;
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

/// <summary>
/// Reconciliation report (operator): the provider's own record of messages sent from
/// this application's configured sending number over a date range, lined up against
/// what eShop believes it sent.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, notificationService, cancellationToken);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    private async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notificationService, CancellationToken cancellationToken)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest("'to' must not be earlier than 'from'.");
        }

        var items = await notificationService.ReconcileAsync(request.From, request.To, cancellationToken);

        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = request.From,
            To = request.To
        };
        response.Items.AddRange(items);
        return Results.Ok(response);
    }
}
