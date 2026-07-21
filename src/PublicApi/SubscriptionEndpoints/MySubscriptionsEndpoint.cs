using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    internal string CustomerReference { get; set; } = string.Empty;
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }

    public System.Collections.Generic.List<SubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>Lists the authenticated customer's own subscriptions.</summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscriptions/mine",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                var request = new MySubscriptionsRequest { CustomerReference = user.FindFirstValue(ClaimTypes.Name)! };
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionService subscriptionService)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await subscriptionService.ListMySubscriptionsAsync(request.CustomerReference);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionDto.FromDomain));

        return Results.Ok(response);
    }
}
