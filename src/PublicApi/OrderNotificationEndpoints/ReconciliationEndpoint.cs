using System;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// Operator action: reconcile the provider's own record of messages (for this app's sending number)
/// against what eShop believes it sent, over an ISO-8601 date-time range. Covers the whole range;
/// the response flags truncation if a page cap is hit.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IOrderNotificationService service, System.Threading.CancellationToken ct) =>
            {
                if (from is null || to is null)
                    return Results.BadRequest("Both 'from' and 'to' (ISO-8601 date-times) are required.");
                if (from > to)
                    return Results.BadRequest("'from' must not be after 'to'.");

                try
                {
                    var report = await service.ReconcileAsync(from.Value, to.Value, ct);
                    return Results.Ok(report);
                }
                catch (ProviderGatewayException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
                }
            })
            .Produces<ReconciliationReport>()
            .WithTags("OrderNotificationEndpoints");
    }
}
