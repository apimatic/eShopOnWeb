using System;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IShopperOrderService orders, HttpContext http) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, orders, http);
            })
            .Produces<ReconciliationReport>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IShopperOrderService orders)
        => HandleAsync(request, orders, null!);

    private async Task<IResult> HandleAsync(ReconciliationRequest request, IShopperOrderService orders, HttpContext http)
    {
        if (request.To < request.From)
        {
            throw new OrderNotificationException("'to' must be on or after 'from'.");
        }

        var report = await orders.ReconcileAsync(request.From, request.To, http.RequestAborted);
        return Results.Ok(report);
    }
}
