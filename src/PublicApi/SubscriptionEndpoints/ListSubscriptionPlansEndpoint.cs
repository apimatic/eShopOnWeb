using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public ListSubscriptionPlansEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", HandleAsync)
            .WithName("ListSubscriptionPlans")
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
    }

    public async Task<IResult> HandleAsync()
    {
        var response = new ListSubscriptionPlansResponse();

        var plans = await _subscriptionService.ListProductsForFamilyAsync();
        response.Plans = plans
            .Select(p => new SubscriptionPlanDto
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                Price = p.PriceInCents.HasValue ? p.PriceInCents.Value / 100m : 0m,
                Description = p.Description
            })
            .ToList();

        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
