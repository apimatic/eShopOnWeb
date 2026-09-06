using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansEndpoint : IEndpoint<IResult, EmptyRequest>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public GetSubscriptionPlansEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", async () => await HandleAsync(new EmptyRequest()))
            .Produces<GetSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("GetSubscriptionPlans");
    }

    [SwaggerOperation(
        Summary = "Get available subscription plans",
        Description = "Returns a list of available subscription plans from Maxio",
        OperationId = "subscription.getPlans",
        Tags = new[] { "SubscriptionEndpoints" })]
    public async Task<IResult> HandleAsync(EmptyRequest request)
    {
        var response = await _subscriptionService.GetSubscriptionPlansAsync();
        return Results.Ok(response);
    }
}

public class EmptyRequest : BaseRequest
{
}

public class GetSubscriptionPlansResponse : BaseResponse
{
    public GetSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public GetSubscriptionPlansResponse() { }

    public List<Infrastructure.Services.SubscriptionPlanDto> Plans { get; set; } = new();
}
