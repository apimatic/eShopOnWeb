using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services.Maxio;
using MinimalApi.Endpoint;

using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListEndpoint : IEndpoint<IResult, EmptyRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions", async (IMaxioSubscriptionService svc, ClaimsPrincipal user) =>
        {
            var reference = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity?.Name ?? "unknown";
            var subs = await svc.ListSubscriptionsForCustomerAsync(reference);
            var resp = new List<SubscriptionResponse>();
            foreach (var s in subs)
            {
                resp.Add(new SubscriptionResponse
                {
                    Id = s.Id,
                    State = s.State,
                    ProductHandle = s.ProductHandle,
                    Price = (double)s.Price,
                    NextBillingDate = s.NextBillingDate,
                    Reference = s.Reference
                });
            }
            return Results.Ok(resp);
        })
        .Produces<List<SubscriptionResponse>>(StatusCodes.Status200OK)
        .WithTags("SubscriptionEndpoints")
        .RequireAuthorization();
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IMaxioSubscriptionService svc)
        => Task.FromResult<IResult>(Results.Ok());
}

public class EmptyRequest : BaseRequest { }
