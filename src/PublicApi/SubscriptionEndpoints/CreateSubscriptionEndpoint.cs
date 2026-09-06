using System.Security.Claims;
using System.Threading;
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
/// Subscribe the calling shopper to a plan
/// </summary>
/// <remarks>
/// Idempotent per (shopper, plan): repeating the call while a live subscription to the same plan
/// exists returns that subscription with <c>alreadySubscribed</c> set, and creates nothing. The
/// shopper is taken from the bearer token, never from the request body.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
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
            (SubscribeRequest request,
                ISubscriptionBillingService billingService,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, billingService, user, cancellationToken);
            })
            .Produces<SubscribeResponse>()
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService) =>
        HandleAsync(request, billingService, user: null, CancellationToken.None);

    public async Task<IResult> HandleAsync(SubscribeRequest request,
        ISubscriptionBillingService billingService,
        ClaimsPrincipal? user,
        CancellationToken cancellationToken)
    {
        var userName = user?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var response = new SubscribeResponse(request.CorrelationId());

        var identity = BillingCustomerIdentity.ForUserName(userName!);
        var result = await billingService.SubscribeAsync(identity, request.PlanHandle, cancellationToken);

        response.Subscription = _mapper.Map<SubscriptionDto>(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;

        // A replay is not a creation: answering 200 keeps a double-click from looking like a second
        // enrollment, while a genuinely new subscription still reports 201.
        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions", response);
    }
}
