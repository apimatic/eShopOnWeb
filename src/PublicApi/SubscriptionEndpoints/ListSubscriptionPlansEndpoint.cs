using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansEndpoint.ListPlansRequest, IMaxioClient>
{
    private readonly IOptions<MaxioSettings> _settings;

    public ListSubscriptionPlansEndpoint(IOptions<MaxioSettings> settings)
    {
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioClient maxioClient) =>
            {
                return await HandleAsync(new ListPlansRequest(), maxioClient);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPlansRequest request, IMaxioClient maxioClient)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var products = await maxioClient.ListProductsAsync(_settings.Value.ProductFamilyHandle);

        foreach (var product in products)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Id = product.Id,
                Name = product.Name,
                Handle = product.Handle ?? string.Empty,
                Description = product.Description,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit,
                RequireCreditCard = product.RequireCreditCard,
                Taxable = product.Taxable
            });
        }

        return Results.Ok(response);
    }

    public class ListPlansRequest : BaseRequest
    {
    }
}
