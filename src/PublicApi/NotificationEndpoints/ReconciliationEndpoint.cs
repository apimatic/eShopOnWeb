using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IShopperOrderService orderService, HttpContext httpContext) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), httpContext, orderService);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IShopperOrderService orderService)
        => HandleAsync(request, null!, orderService);

    private async Task<IResult> HandleAsync(
        ReconciliationRequest request,
        HttpContext httpContext,
        IShopperOrderService orderService)
    {
        var report = await orderService.ReconcileAsync(request.From, request.To, httpContext.RequestAborted);
        var response = new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Truncated = report.Truncated,
            Matched = report.Matched.Select(ToDto).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
            EShopOnly = report.EShopOnly.Select(ToDto).ToList()
        };
        return Results.Ok(response);
    }

    private static ReconciliationItemDto ToDto(ReconciliationEntry entry) =>
        new()
        {
            NotificationId = entry.NotificationId,
            ProviderSid = entry.ProviderSid,
            ProviderStatus = entry.ProviderStatus,
            EShopStatus = entry.EShopStatus,
            DateSent = entry.DateSent,
            DateCreated = entry.DateCreated
        };
}
