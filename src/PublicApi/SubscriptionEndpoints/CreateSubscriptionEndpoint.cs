using System.Collections.Generic;
using System.Linq;
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
/// Subscribes the authenticated shopper to a plan.
/// <para>
/// Idempotent by design: the billing customer is created only if it does not already exist, and a
/// repeated request for the same shopper and plan resolves to the existing subscription and answers
/// 200 rather than enrolling them twice. A first-time enrollment answers 201.
/// </para>
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ICurrentSubscriber, ISubscriptionService>
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
            (CreateSubscriptionRequest request, ICurrentSubscriber currentSubscriber,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
                await HandleAsync(request, currentSubscriber, subscriptionService, cancellationToken))
            .Produces<CreateSubscriptionResponse>()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ICurrentSubscriber currentSubscriber,
        ISubscriptionService subscriptionService) =>
        HandleAsync(request, currentSubscriber, subscriptionService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ICurrentSubscriber currentSubscriber,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            var available = await subscriptionService.GetPlansAsync(cancellationToken);
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.PlanHandle)] = new[]
                {
                    "A plan handle is required. Available plans: " +
                    (available.Count == 0 ? "(none published)" : string.Join(", ", available.Select(plan => plan.Handle)))
                }
            });
        }

        var subscriber = await currentSubscriber.GetAsync();
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = _mapper.Map<SubscriptionDto>(result.Subscription),
            Created = result.Created
        };

        // 201 for a genuine enrollment, 200 when we resolved to a subscription that already existed —
        // so a double-clicked subscribe is visibly a no-op instead of a second charge.
        return result.Created
            ? Results.Created($"api/my-subscriptions#{result.Subscription.Id}", response)
            : Results.Ok(response);
    }
}
