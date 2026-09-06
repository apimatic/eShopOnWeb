using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// The call is idempotent. It ensures a billing-system customer exists for the caller and then
/// enrolls them, so repeating it — a double-clicked button, a retried request — returns the
/// subscription that already exists with <c>200 OK</c> instead of creating a second one. Only a
/// genuinely new enrollment answers <c>201 Created</c>.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly IMapper _mapper;
    private readonly SubscriberIdentityResolver _subscriberIdentityResolver;

    public CreateSubscriptionEndpoint(IMapper mapper, SubscriberIdentityResolver subscriberIdentityResolver)
    {
        _mapper = mapper;
        _subscriberIdentityResolver = subscriberIdentityResolver;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
        HandleAsync(request, user: null, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal? user,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = $"'{nameof(request.PlanHandle)}' is required. Choose one from api/subscription-plans." });
        }

        var subscriber = await _subscriberIdentityResolver.ResolveAsync(user);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var result = await billingService.SubscribeAsync(
            new SubscribeRequest(subscriber, request.PlanHandle, request.IdempotencyKey),
            cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = _mapper.Map<SubscriptionDto>(result.Subscription),
            Created = result.Created
        };

        return result.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
