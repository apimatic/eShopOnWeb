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
/// Subscribes the authenticated shopper to a plan.
/// <para>
/// The call is idempotent: it creates the shopper's billing customer only if there is not one
/// already, and it will not enroll a shopper twice in a plan they still hold. Repeating it answers
/// <c>200 OK</c> with the existing subscription instead of <c>201 Created</c>.
/// </para>
/// </summary>
public class CreateSubscriptionEndpoint
    : IEndpoint<IResult, CreateSubscriptionRequest?, HttpContext, ISubscriptionService>
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
            (CreateSubscriptionRequest? request, HttpContext httpContext,
                ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, httpContext, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest? request, HttpContext httpContext,
        ISubscriptionService subscriptionService)
    {
        request ??= new CreateSubscriptionRequest();
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var subscriber = httpContext.User.ToSubscriberIdentity(request.FirstName, request.LastName,
            request.Organization);

        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle,
            httpContext.RequestAborted);

        response.Subscription = _mapper.Map<SubscriptionDto>(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;

        // A replay reports the subscription the shopper already holds; only a new enrollment is a
        // creation.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created("api/my-subscriptions", response);
    }
}
