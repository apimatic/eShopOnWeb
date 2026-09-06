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

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List the signed-in shopper's subscriptions
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    private readonly IMapper _mapper;

    public MySubscriptionListEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (bool? includeInactive, ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(new ListMySubscriptionsRequest(includeInactive), user, subscriptionBillingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(ListMySubscriptionsRequest request, ClaimsPrincipal user, ISubscriptionBillingService subscriptionBillingService) =>
        HandleAsync(request, user, subscriptionBillingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        ListMySubscriptionsRequest request,
        ClaimsPrincipal user,
        ISubscriptionBillingService subscriptionBillingService,
        CancellationToken cancellationToken)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        if (!SubscriptionCallerIdentity.TryResolve(user, out var identity, out var identityError))
        {
            return Results.BadRequest(identityError);
        }

        var subscriptions = await subscriptionBillingService.GetSubscriptionsAsync(identity, request.IncludeInactive, cancellationToken);

        response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));

        return Results.Ok(response);
    }
}
