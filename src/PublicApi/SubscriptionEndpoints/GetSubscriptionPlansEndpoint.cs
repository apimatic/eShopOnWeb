using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.MaxioIntegration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            async (IMaxioService maxioService) =>
            {
                var response = new GetSubscriptionPlansResponse();

                try
                {
                    var products = await maxioService.ListProductsAsync();
                    response.Plans = products.Select(p => new SubscriptionPlanDto
                    {
                        Handle = p.Handle,
                        Name = p.Name,
                        Price = p.PriceInCents / 100m
                    }).ToList();

                    response.Success = true;
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    response.Success = false;
                    response.ErrorMessage = ex.Message;
                    return Results.BadRequest(response);
                }
            })
            .RequireAuthorization()
            .Produces<GetSubscriptionPlansResponse>()
            .WithName("GetSubscriptionPlans")
            .WithTags("SubscriptionEndpoints");
    }
}

public class GetSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
