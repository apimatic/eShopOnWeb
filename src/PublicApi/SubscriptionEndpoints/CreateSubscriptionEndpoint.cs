using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan.
/// </summary>
/// <remarks>
/// Idempotent by design. The shopper's billing customer record is created on first use, and a
/// repeated request — the double-click, the retried POST — returns the subscription that already
/// exists with <c>created: false</c> instead of enrolling them twice.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionBillingService>
{
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, httpContext, billingService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        HttpContext httpContext,
        ISubscriptionBillingService billingService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new
            {
                message = "'planHandle' is required. Call GET /api/subscription-plans for the available handles.",
            });
        }

        var subscriber = SubscriberIdentity.Resolve(
            httpContext.User, request.FirstName, request.LastName, request.Organization);

        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var result = await billingService.SubscribeAsync(
            new SubscribeToPlanRequest(subscriber, request.PlanHandle!, request.PaymentCollectionMethod),
            httpContext.RequestAborted);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = _mapper.Map<SubscriptionDto>(result.Subscription),
            Created = result.Created,
        };

        // 201 the first time, 200 for every repeat — the standard way to signal that an idempotent
        // create found the resource already there.
        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
