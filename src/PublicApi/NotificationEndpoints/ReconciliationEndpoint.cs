using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: lines up the provider's own record of messages for a date range
/// against what eShop believes it sent. Only messages sent from this application's own
/// configured sending number are counted — the provider is asked for that number's
/// messages directly. The whole range is covered (provider pagination is followed).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly IOrderNotificationService _notificationService;

    public ReconciliationEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset? from, DateTimeOffset? to) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to });
            })
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (request.From is null || request.To is null)
        {
            return Results.BadRequest(new { message = "Both 'from' and 'to' (ISO-8601 date-times) are required." });
        }
        if (request.From > request.To)
        {
            return Results.BadRequest(new { message = "'from' must not be after 'to'." });
        }

        try
        {
            var report = await _notificationService.ReconcileAsync(request.From.Value, request.To.Value);
            return Results.Ok(report);
        }
        catch (TwilioApiException ex)
        {
            return Results.Problem($"The provider's message records could not be retrieved (error {ex.ErrorCode}).",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
}
