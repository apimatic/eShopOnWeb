using System.Collections.Generic;
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
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan.
/// </summary>
/// <remarks>
/// Safe to repeat: a replay answers 200 with the subscription that already exists, and only a
/// genuinely new enrollment answers 201. Callers can therefore retry without fear of double billing.
/// </remarks>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService, CancellationToken>
{
    private readonly IMapper _mapper;

    public SubscriptionCreateEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateSubscriptionRequest request,
                ClaimsPrincipal user,
                ISubscriptionBillingService subscriptionBillingService,
                CancellationToken cancellationToken) =>
            {
                // The identity is whatever the bearer token proved, never what the body claims.
                request.UserName = user.Identity?.Name;
                return await HandleAsync(request, subscriptionBillingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Subscribes the authenticated user to a plan",
                description: "Idempotent. Returns 201 when a new subscription is created and 200 when the " +
                             "caller was already subscribed to the plan."));
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService subscriptionBillingService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(CreateSubscriptionRequest.PlanHandle)] = new[] { "A plan handle is required." }
            });
        }

        var subscribeRequest = new SubscribeRequest(request.UserName, request.PlanHandle.Trim())
        {
            // eShopOnWeb identities are keyed by e-mail address, so the user name doubles as the
            // contact address on the billing customer.
            Email = request.UserName,
            FirstName = request.FirstName,
            LastName = request.LastName,
            IdempotencyKey = request.IdempotencyKey
        };

        var result = await subscriptionBillingService.SubscribeAsync(subscribeRequest, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Created = result.Created,
            Subscription = _mapper.Map<SubscriptionDto>(result.Subscription)
        };

        return result.Created
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
