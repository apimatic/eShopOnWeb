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
/// Lists the authenticated caller's own subscriptions, read live from the billing system of record.
/// </summary>
public class MySubscriptionListEndpoint : IEndpoint<IResult, ClaimsPrincipal, ISubscriptionBillingService, CancellationToken>
{
    private readonly IMapper _mapper;

    public MySubscriptionListEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                ISubscriptionBillingService subscriptionBillingService,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, subscriptionBillingService, cancellationToken);
            })
            .Produces<ListMySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Lists the authenticated user's subscriptions",
                description: "Reads the caller's subscriptions straight from the billing system, so the " +
                             "state and next billing date are always current. Empty when never subscribed."));
    }

    public async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        ISubscriptionBillingService subscriptionBillingService,
        CancellationToken cancellationToken)
    {
        var userName = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var response = new ListMySubscriptionsResponse();

        var subscriptions = await subscriptionBillingService.ListSubscriptionsForUserAsync(userName, cancellationToken);
        response.Subscriptions.AddRange(subscriptions.Select(_mapper.Map<SubscriptionDto>));

        return Results.Ok(response);
    }
}
