using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationQuery
{
    [FromQuery(Name = "from")]
    public DateTimeOffset From { get; set; }

    [FromQuery(Name = "to")]
    public DateTimeOffset To { get; set; }
}

/// <summary>
/// Operator action: a report lining up the provider's own record of messages this application sent
/// (from the configured sending number, over a date range) against what eShop believes it sent.
/// Restricted to administrators.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ReconciliationEndpoint : EndpointBaseAsync
    .WithRequest<ReconciliationQuery>
    .WithActionResult<ReconciliationResponse>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public ReconciliationEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    [HttpGet("api/notifications/reconciliation")]
    [SwaggerOperation(
        Summary = "Reconciles provider vs eShop message records over a range (operator)",
        Description = "Lists the provider's messages from the configured number in [from,to] against eShop's records",
        OperationId = "notifications.reconciliation",
        Tags = new[] { "NotificationEndpoints" })]
    public override async Task<ActionResult<ReconciliationResponse>> HandleAsync(
        [FromQuery] ReconciliationQuery request,
        CancellationToken cancellationToken = default)
    {
        if (request.To < request.From)
        {
            return BadRequest("'to' must not be earlier than 'from'.");
        }

        try
        {
            var report = await _orderNotificationService.ReconcileAsync(request.From, request.To, cancellationToken);
            return Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                Truncated = report.Truncated,
                ProviderPagesFetched = report.ProviderPagesFetched,
                Matched = report.Matched.Select(Map).ToList(),
                InProviderOnly = report.InProviderOnly.Select(Map).ToList(),
                InEShopOnly = report.InEShopOnly.Select(Map).ToList()
            });
        }
        catch (SmsProviderException)
        {
            return StatusCode((int)HttpStatusCode.BadGateway, "The provider could not be reached to build the reconciliation report.");
        }
    }

    private static ReconciliationEntryDto Map(ReconciliationEntry e) => new()
    {
        ProviderSid = e.ProviderSid,
        ProviderStatus = e.ProviderStatus,
        NotificationId = e.NotificationId,
        LocalState = e.LocalState,
        ProviderDateSent = e.ProviderDateSent
    };
}
