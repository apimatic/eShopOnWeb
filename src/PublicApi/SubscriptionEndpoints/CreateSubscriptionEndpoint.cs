using System.Net;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the signed-in shopper to a plan, and confirms the resulting plan, price, state and
/// next billing date.
/// </summary>
/// <remarks>
/// The call is idempotent per shopper and plan: repeating it - a double-clicked button, a retried
/// request - returns the subscription that already exists, flagged with
/// <see cref="CreateSubscriptionResponse.AlreadySubscribed"/>, and never enrolls the shopper twice.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, SubscriberResolver>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService billingService, IMapper mapper)
    {
        _billingService = billingService;
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, SubscriberResolver subscriberResolver) =>
            {
                return await HandleAsync(request, httpContext, subscriberResolver);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext, SubscriberResolver subscriberResolver)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            // The plan must be named explicitly: the catalog is configuration, so this API has no
            // plan handle of its own to fall back on.
            throw new SubscriptionBillingException(
                "A planHandle is required. Call api/subscription-plans to see the plans on offer.",
                HttpStatusCode.BadRequest);
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscriber = await subscriberResolver.GetSubscriberAsync(httpContext.User, request.FirstName, request.LastName);
        var result = await _billingService.SubscribeAsync(subscriber, request.PlanHandle, httpContext.RequestAborted);

        response.Subscription = _mapper.Map<SubscriptionDto>(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
