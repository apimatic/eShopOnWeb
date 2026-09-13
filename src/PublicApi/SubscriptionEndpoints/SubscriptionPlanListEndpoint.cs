using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, Maxio.MaxioClient>
{
    private readonly IOptions<Maxio.MaxioSettings> _maxioSettings;

    public SubscriptionPlanListEndpoint(IOptions<Maxio.MaxioSettings> maxioSettings)
    {
        _maxioSettings = maxioSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (Maxio.MaxioClient maxioClient) =>
            {
                var request = new ListSubscriptionPlansRequest
                {
                    ProductFamilyHandle = _maxioSettings.Value.ProductFamilyHandle
                };
                return await HandleAsync(request, maxioClient);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, Maxio.MaxioClient maxioClient)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        var products = await maxioClient.ListProductsByFamilyAsync(request.ProductFamilyHandle);

        foreach (var product in products)
        {
            response.Plans.Add(new SubscriptionPlanDto
            {
                Id = product.Id,
                Name = product.Name,
                Handle = product.Handle ?? string.Empty,
                Description = product.Description ?? string.Empty,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit,
                RequireCreditCard = product.RequireCreditCard,
                Taxable = product.Taxable
            });
        }

        return Results.Ok(response);
    }
}
