using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// Operator action: a report over a date range lining the provider's own record of THIS application's
/// messages (server-side filtered to <c>Twilio:FromNumber</c>) up against what eShop believes it sent,
/// so a message on either side only is visible. <c>from</c>/<c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
                await HandleAsync(from, to, service))
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderNotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service)
    {
        if (to < from)
        {
            return Results.BadRequest(new { message = "'to' must not be earlier than 'from'." });
        }

        try
        {
            var report = await service.ReconcileAsync(from.ToUniversalTime(), to.ToUniversalTime());
            return Results.Ok(ToResponse(report));
        }
        catch (SmsGatewayException ex)
        {
            return GatewayErrorMapper.Map(ex);
        }
    }

    private static ReconciliationResponse ToResponse(ReconciliationReport report) => new()
    {
        FromUtc = report.FromUtc,
        ToUtc = report.ToUtc,
        ProviderCount = report.ProviderCount,
        EShopCount = report.EShopCount,
        MatchedCount = report.MatchedCount,
        ProviderResultTruncated = report.ProviderResultTruncated,
        Matched = report.Matched.Select(ToEntry).ToList(),
        OnlyInProvider = report.OnlyInProvider.Select(ToEntry).ToList(),
        OnlyInEShop = report.OnlyInEShop.Select(ToEntry).ToList()
    };

    private static ReconciliationEntryDto ToEntry(ReconciliationEntry e) => new()
    {
        MessageSid = e.MessageSid,
        InProvider = e.InProvider,
        InEShop = e.InEShop,
        ProviderStatus = e.ProviderStatus,
        EShopStatus = e.EShopStatus,
        NotificationId = e.NotificationId,
        OrderId = e.OrderId,
        DateSentUtc = e.DateSentUtc
    };
}
