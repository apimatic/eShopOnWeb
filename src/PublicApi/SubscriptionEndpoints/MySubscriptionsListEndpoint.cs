using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
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
/// Lists the subscriptions held by the authenticated shopper.
/// </summary>
/// <remarks>
/// Answered straight from the billing system of record, so it reflects changes made in Maxio itself,
/// not just those made through eShopOnWeb.
/// </remarks>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ISubscriberResolver, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public MySubscriptionsListEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user,
             ISubscriberResolver subscriberResolver,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                var request = new ListMySubscriptionsRequest(user.Identity?.Name, cancellationToken);
                return await HandleAsync(request, subscriberResolver, subscriptionService);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Lists the subscriptions of the caller",
                description: "Returns every subscription held by the authenticated shopper, newest first, read from the billing provider.")
            {
                OperationId = "subscriptions.listMine"
            });
    }

    public async Task<IResult> HandleAsync(
        ListMySubscriptionsRequest request,
        ISubscriberResolver subscriberResolver,
        ISubscriptionService subscriptionService)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        var subscriber = await subscriberResolver.ResolveAsync(request.UserName ?? string.Empty, request.CancellationToken);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var subscriptions = await subscriptionService.ListSubscriptionsAsync(subscriber, request.CancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));
        response.ActiveCount = response.Subscriptions.Count(subscription => subscription.IsLive);

        return Results.Ok(response);
    }
}
