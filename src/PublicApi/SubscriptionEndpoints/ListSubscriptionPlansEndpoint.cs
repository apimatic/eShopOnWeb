using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioApiService _maxioApiService;

    public ListSubscriptionPlansEndpoint(IMaxioApiService maxioApiService)
    {
        _maxioApiService = maxioApiService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async () => await HandleAsync())
           .Produces<ListSubscriptionPlansResponse>()
           .Produces(StatusCodes.Status400BadRequest)
           .WithTags("SubscriptionEndpoints")
           .WithName("ListSubscriptionPlans");
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListSubscriptionPlansResponse();

        var products = await _maxioApiService.ListProductsAsync();

        if (products == null)
        {
            return Results.BadRequest(new { error = "Failed to fetch subscription plans" });
        }

        foreach (var product in products)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Id = product.Id,
                Name = product.Name,
                Handle = product.Handle,
                Description = product.Description,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit,
                Taxable = product.Taxable
            });
        }

        return Results.Ok(response);
    }
}

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Handle { get; set; } = null!;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = null!;
    public bool Taxable { get; set; }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
