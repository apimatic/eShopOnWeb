using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the signed-in shopper to a plan.
/// </summary>
/// <remarks>
/// The operation is idempotent. Repeating it — a double-clicked button, a client retry, a shopper
/// who is already enrolled — returns the subscription that exists rather than creating a second one,
/// and answers 200 instead of 201 so the caller can tell the difference.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionApiService, CancellationToken>
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
            (CreateSubscriptionRequest request,
             [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
             ISubscriptionApiService subscriptions,
             CancellationToken cancellationToken) =>
            {
                // The header is the conventional carrier; the body field exists for clients that
                // cannot set headers. The header wins when both are present.
                request.IdempotencyKey = idempotencyKey ?? request.IdempotencyKey;

                return await HandleAsync(request, subscriptions, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionApiService subscriptions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(
                new System.Collections.Generic.Dictionary<string, string[]>
                {
                    [nameof(request.PlanHandle)] = new[]
                    {
                        "planHandle is required. Call GET /api/subscription-plans to see the available handles."
                    }
                });
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey!.Trim();

        var result = await subscriptions.SubscribeAsync(request.PlanHandle!.Trim(), idempotencyKey, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Created = result.Created,
            Subscription = _mapper.Map<SubscriptionDto>(result.Subscription)
        };

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
