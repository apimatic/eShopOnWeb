using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: a report lining up the provider's own record of this application's sent messages
/// (from the configured sending number) against what eShop believes it sent, over a date-time range.
/// </summary>
public class ReconciliationEndpoint
    : IEndpoint<IResult, ReconciliationRequest, IOperatorOrderService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to, IOperatorOrderService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, service, ct);
            })
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOperatorOrderService service,
        CancellationToken ct)
    {
        if (request.From is null || request.To is null)
        {
            return Results.BadRequest(new { error = "Both 'from' and 'to' ISO-8601 date-times are required." });
        }

        if (request.From > request.To)
        {
            return Results.BadRequest(new { error = "'from' must not be after 'to'." });
        }

        try
        {
            var report = await service.ReconcileAsync(request.From.Value, request.To.Value, ct);
            return Results.Ok(report);
        }
        catch (TwilioProviderException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}
