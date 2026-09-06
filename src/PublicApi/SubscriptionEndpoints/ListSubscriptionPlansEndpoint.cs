using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioService>
{
    private readonly IMaxioService _maxioService;

    public ListSubscriptionPlansEndpoint(IMaxioService maxioService)
    {
        _maxioService = maxioService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService service) =>
            {
                return await HandleAsync(service);
            })
           .Produces<ListSubscriptionPlansResponse>()
           .WithName("GetSubscriptionPlans")
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioService service)
    {
        try
        {
            var response = new ListSubscriptionPlansResponse();
            var products = await service.ListProductsAsync();

            var plans = products
                .Select(p => new SubscriptionPlanDto
                {
                    Id = p.Id,
                    Handle = p.Handle,
                    Name = p.Name,
                    Price = p.PriceInCents.HasValue ? p.PriceInCents.Value / 100m : 0,
                    Description = p.Description ?? "",
                    IntervalUnit = p.IntervalUnit == "month" ? 1 : (p.IntervalUnit == "year" ? 2 : 0),
                    Interval = p.Interval ?? 1
                })
                .ToList();

            response.Plans.AddRange(plans);
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    public class ListSubscriptionPlansResponse
    {
        public List<SubscriptionPlanDto> Plans { get; set; } = new();
    }
}
