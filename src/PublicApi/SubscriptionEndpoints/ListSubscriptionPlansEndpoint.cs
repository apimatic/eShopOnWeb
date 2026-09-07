using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, MaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (MaxioSubscriptionService service) =>
            {
                return await HandleAsync(service);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MaxioSubscriptionService service)
    {
        try
        {
            var plans = await service.GetSubscriptionPlansAsync();
            var response = new ListSubscriptionPlansResponse
            {
                Plans = plans.Select(p => new SubscriptionPlanResponse
                {
                    Id = p.Id,
                    Name = p.Name,
                    Handle = p.Handle,
                    Price = p.Price,
                    Interval = p.Interval,
                    IntervalUnit = p.IntervalUnit
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (MaxioException)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }
    }

    public class ListSubscriptionPlansResponse
    {
        public List<SubscriptionPlanResponse> Plans { get; set; } = new();
    }

    public class SubscriptionPlanResponse
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Handle { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Interval { get; set; }
        public string IntervalUnit { get; set; } = string.Empty;
    }
}
