using System.Security.Claims;
using System.Threading.Tasks;
using AutoMapper;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

/// <summary>
/// Create a Subscription (POST api/subscriptions)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionManager>
{
    private readonly IMapper _mapper;

    public CreateSubscriptionEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionManager subscriptionManager) =>
            {
                return await HandleAsync(request, user, subscriptionManager);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionsEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionManager subscriptionManager)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "A planHandle is required to subscribe."
            });
        }

        var userName = UserName(user);
        if (userName == null)
        {
            return Results.Unauthorized();
        }

        var result = await subscriptionManager.SubscribeAsync(userName, request.PlanHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = _mapper.Map<SubscriptionDto>(result.Subscription)
        };

        if (result.Created)
        {
            return Results.Created("api/my-subscriptions", response);
        }

        return Results.Ok(response);
    }

    private static string? UserName(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(user.Identity.Name))
        {
            return null;
        }

        return user.Identity.Name;
    }
}
