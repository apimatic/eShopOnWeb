using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: lines the provider's own record of messages sent from the configured sending number
/// over a date range up against what eShop believes it sent, so an either-way discrepancy is visible.
/// Administrators only. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : AuthenticatedEndpointBase,
    IEndpoint<IResult, ReconciliationRequest, IOrderNotificationService>
{
    public ReconciliationEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderNotificationService service) =>
                await HandleAsync(new ReconciliationRequest(from, to), service))
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderNotificationService service)
    {
        if (request.To < request.From)
        {
            return Results.BadRequest("'to' must not be earlier than 'from'.");
        }

        var report = await service.ReconcileAsync(request.From, request.To, RequestAborted);

        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.Matched.Count,
            InEShopOnlyCount = report.InEShopOnly.Count,
            InProviderOnlyCount = report.InProviderOnly.Count,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                NotificationId = m.NotificationId,
                OrderId = m.OrderId,
                Sid = m.Sid,
                EShopStatus = m.EShopStatus,
                ProviderStatus = m.ProviderStatus,
                ProviderErrorCode = m.ProviderErrorCode
            }).ToList(),
            InEShopOnly = report.InEShopOnly.Select(e => new ReconciliationEShopEntryDto
            {
                NotificationId = e.NotificationId,
                OrderId = e.OrderId,
                Sid = e.Sid,
                EShopStatus = e.EShopStatus
            }).ToList(),
            InProviderOnly = report.InProviderOnly.Select(p => new ReconciliationProviderEntryDto
            {
                Sid = p.Sid,
                ProviderStatus = p.ProviderStatus,
                ProviderErrorCode = p.ProviderErrorCode,
                DateSent = p.DateSent
            }).ToList()
        };
        return Results.Ok(response);
    }
}
