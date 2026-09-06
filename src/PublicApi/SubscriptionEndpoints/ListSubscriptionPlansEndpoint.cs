using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Billing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioApiClient>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IMaxioApiClient maxioClient) =>
            {
                var response = new ListSubscriptionPlansResponse();

                try
                {
                    var products = await maxioClient.GetProductsAsync();
                    response.Plans.AddRange(products.Select(p => new SubscriptionPlanDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Handle = p.Handle,
                        Description = p.Description,
                        PriceInCents = p.PriceInCents,
                        Interval = p.Interval,
                        IntervalUnit = p.IntervalUnit
                    }));
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints")
           .WithName("ListSubscriptionPlans");
    }

    public Task<IResult> HandleAsync(IMaxioApiClient maxioClient)
    {
        throw new NotImplementedException();
    }
}
