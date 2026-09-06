using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Ensures a billing customer exists for the user
/// first, and is idempotent: repeating the request never produces a second customer or a second
/// subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService, CancellationToken>
{
    /// <summary>Header a client may use to carry the idempotency key instead of the request body.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest? request,
             ClaimsPrincipal user,
             HttpRequest httpRequest,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                request ??= new CreateSubscriptionRequest();

                // Identity comes from the token only; anything the body says about the user is ignored.
                request.UserName = user.Identity?.Name ?? string.Empty;

                if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
                    && httpRequest.Headers.TryGetValue(IdempotencyKeyHeader, out var headerValue))
                {
                    request.IdempotencyKey = headerValue.ToString();
                }

                return await HandleAsync(request, subscriptionService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionService.SubscribeAsync(
            new SubscribeRequest(
                request.UserName,
                request.PlanHandle,
                request.IdempotencyKey,
                request.FirstName,
                request.LastName),
            cancellationToken);

        response.Subscription = SubscriptionMapper.ToDto(result.Subscription);
        response.AlreadySubscribed = !result.IsNew;

        // A replayed or duplicate subscribe is a success, not a conflict, but it did not create a
        // new resource, so it answers 200 rather than 201.
        return result.IsNew
            ? Results.Created($"api/my-subscriptions#{result.Subscription.Id}", response)
            : Results.Ok(response);
    }
}
