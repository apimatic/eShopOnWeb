using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, MySubscriptionsQuery, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user,
             ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                var subscriber = user.ToSubscriberIdentity();
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new MySubscriptionsQuery(subscriber), billingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Lists the caller's subscriptions",
                description: "Returns every subscription held by the caller, with plan, price, state and next billing date."));
    }

    public Task<IResult> HandleAsync(MySubscriptionsQuery request, ISubscriptionBillingService billingService) =>
        HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        MySubscriptionsQuery request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var subscriptions = await billingService.ListSubscriptionsAsync(request.Subscriber, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(SubscriptionMappings.ToDto));

        return Results.Ok(response);
    }
}
