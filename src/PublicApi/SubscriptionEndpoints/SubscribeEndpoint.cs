using System.Security.Claims;
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
/// Enroll the calling user in a recurring plan (UC1). Idempotent: an existing active subscription is
/// returned rather than a second enrollment being created.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionService>
{
    private readonly IMapper _mapper;

    public SubscribeEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserReference = SubscriptionCaller.RequireUserReference(user);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<SubscribeResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionService subscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var subscription = await subscriptionService.SubscribeAsync(request.UserReference, request.PlanHandle);
        response.Subscription = _mapper.Map<SubscriptionDto>(subscription);

        return Results.Ok(response);
    }
}
