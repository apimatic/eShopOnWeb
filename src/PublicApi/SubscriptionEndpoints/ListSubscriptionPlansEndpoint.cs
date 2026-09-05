using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioSettings _maxioSettings;

    public ListSubscriptionPlansEndpoint(IMaxioClient maxioClient, MaxioSettings maxioSettings)
    {
        _maxioClient = maxioClient;
        _maxioSettings = maxioSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleAsync)
           .Produces<ListSubscriptionPlansResponse>()
           .WithName("ListSubscriptionPlans")
           .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        try
        {
            var products = await _maxioClient.ListProductsAsync(_maxioSettings.ProductFamilyHandle);

            var plans = products.Select(p => new SubscriptionPlanDto
            {
                Id = p.Product.Id,
                Name = p.Product.Name,
                Handle = p.Product.Handle,
                Description = p.Product.Description,
                Price = p.Product.Price_in_cents / 100m,
                Interval = p.Product.Interval,
                IntervalUnit = p.Product.Interval_unit,
                RequiresCreditCard = p.Product.Require_credit_card
            }).ToList();

            var response = new ListSubscriptionPlansResponse { Plans = plans };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
