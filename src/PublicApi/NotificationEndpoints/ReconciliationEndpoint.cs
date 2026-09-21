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

/// <summary>
/// Operator action: lists the provider's own record of messages from the configured sending number
/// over a date range, lined up against what eShop believes it sent — so a message one side knows and
/// the other does not is visible. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint
    : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), service, ct);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ReconciliationRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        if (request.From > request.To)
        {
            return Results.BadRequest(new { message = "'from' must not be after 'to'." });
        }

        var report = await service.ReconcileAsync(request.From, request.To, ct);

        var response = new ReconciliationResponse(
            report.From,
            report.To,
            report.FromNumber,
            report.Matched.Count,
            report.ProviderOnly.Count,
            report.EShopOnly.Count,
            report.Matched.Select(ReconciliationResponse.MapEntry).ToList(),
            report.ProviderOnly.Select(ReconciliationResponse.MapEntry).ToList(),
            report.EShopOnly.Select(ReconciliationResponse.MapEntry).ToList());
        return Results.Ok(response);
    }
}
