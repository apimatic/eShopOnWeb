using System;
using System.Linq;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationQuery, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IShopperOrderService service) =>
            {
                return await HandleAsync(new ReconciliationQuery { From = from, To = to }, service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQuery request, IShopperOrderService service)
    {
        var result = await service.ReconcileAsync(request.From, request.To);
        if (!result.IsSuccess)
        {
            return EndpointResultMapper.Map(result);
        }

        return Results.Ok(new ReconciliationResponse
        {
            From = result.Value.From,
            To = result.Value.To,
            FromNumber = result.Value.FromNumber,
            Entries = result.Value.Entries.Select(e => new ReconciliationEntryDto
            {
                ProviderMessageSid = e.ProviderMessageSid,
                ProviderStatus = e.ProviderStatus,
                ProviderDateSent = e.ProviderDateSent,
                LocalNotificationId = e.LocalNotificationId,
                Match = e.Match
            }).ToList()
        });
    }
}
