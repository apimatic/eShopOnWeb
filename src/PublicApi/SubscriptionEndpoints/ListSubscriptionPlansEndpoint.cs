using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxioService) =>
            {
                var plans = await maxioService.GetAvailablePlansAsync();
                var response = new ListSubscriptionPlansResponse();
                response.Plans = plans.Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    MaxioPlanId = p.MaxioPlanId,
                    Name = p.Name,
                    Handle = p.Handle,
                    Description = p.Description,
                    PriceInCents = p.PriceInCents,
                    Currency = p.Currency
                }).ToList();

                return Results.Ok(response);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans")
            ;
    }

    public async Task<IResult> HandleAsync()
    {
        throw new System.NotImplementedException("This method is not called directly");
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
