using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Empty request for listing the caller's subscriptions (identity comes from the JWT).</summary>
public class MySubscriptionsListRequest : BaseRequest
{
}

/// <summary>Response carrying the caller's subscriptions.</summary>
public class MySubscriptionsListResponse : BaseResponse
{
    public MySubscriptionsListResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsListResponse()
    {
    }

    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}

/// <summary>
/// Lists the authenticated user's subscriptions. Returns an empty list if the user has never subscribed.
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, MySubscriptionsListRequest, ISubscriptionAppService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ISubscriptionAppService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new MySubscriptionsListRequest(), subscriptionService, cancellationToken);
            })
            .Produces<MySubscriptionsListResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(MySubscriptionsListRequest request, ISubscriptionAppService subscriptionService)
        => HandleAsync(request, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(MySubscriptionsListRequest request, ISubscriptionAppService subscriptionService, CancellationToken cancellationToken)
    {
        var response = new MySubscriptionsListResponse(request.CorrelationId());
        var subscriptions = await subscriptionService.GetMySubscriptionsAsync(cancellationToken);
        response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();
        return Results.Ok(response);
    }
}
