using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.Maxio.Models;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, IMaxioClient>
{
    private readonly IOptions<MaxioOptions> _options;

    public ListSubscriptionPlansEndpoint(IOptions<MaxioOptions> options)
    {
        _options = options;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioClient maxioClient) =>
            {
                return await HandleAsync(maxioClient);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(IMaxioClient maxioClient)
    {
        var response = new ListSubscriptionPlansResponse();

        try
        {
            var products = await maxioClient.GetProductsForFamilyAsync(_options.Value.ProductFamilyHandle);
            response.Plans = products.Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Handle = p.Handle ?? string.Empty,
                Description = p.Description ?? string.Empty,
                PriceInCents = p.PriceInCents,
                Interval = p.Interval,
                IntervalUnit = p.IntervalUnit,
                RequireCreditCard = p.RequireCreditCard
            }).ToList();
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorMessage = $"Failed to load subscription plans: {ex.Message}";
        }

        return Results.Ok(response);
    }
}
