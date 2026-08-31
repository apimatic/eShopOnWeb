using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Lines up the provider's own record of messages sent from this application's
/// configured sending number against what eShop believes it sent, over a date
/// range (operator). Messages from any other sender on the account are excluded
/// by asking the provider for this number's messages only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), notificationService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService notificationService)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest(new { errors = new[] { "'to' must not be earlier than 'from'." } });
        }

        var report = await notificationService.ReconcileAsync(request.From.ToUniversalTime(), request.To.ToUniversalTime());
        return Results.Ok(new ReconciliationResponse(request.CorrelationId())
        {
            Report = report
        });
    }
}

public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    /// <summary>ISO-8601 date-time; start of the range.</summary>
    public DateTimeOffset From { get; init; }

    /// <summary>ISO-8601 date-time; end of the range.</summary>
    public DateTimeOffset To { get; init; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public ReconciliationReport? Report { get; set; }
}
