using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The hero flow: subscribe the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// Ensures a billing customer exists for the shopper, then enrolls them. The call is idempotent:
/// repeating it - a double-clicked button, a client retry - returns the subscription that already
/// exists with <c>200 OK</c> rather than creating a second one. A genuinely new signup answers
/// <c>201 Created</c>.
/// </remarks>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeCommand, ISubscriptionService>
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
             ClaimsPrincipal user,
             UserManager<ApplicationUser> userManager,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                var userName = SubscriberIdentity.GetUserName(user);
                if (string.IsNullOrWhiteSpace(userName))
                {
                    return Results.Unauthorized();
                }

                // The token carries the user name; the email comes from the identity store so the
                // billing customer is created with the address the shopper actually registered.
                var applicationUser = await userManager.FindByNameAsync(userName!);
                var email = string.IsNullOrWhiteSpace(applicationUser?.Email) ? userName! : applicationUser!.Email!;

                var subscriber = new SubscriberProfile(
                    SubscriberIdentity.ToUserKey(userName!),
                    email,
                    request.FirstName,
                    request.LastName,
                    request.Organization);

                var subscribeRequest = new SubscribeRequest(subscriber, request.PlanHandle, request.IdempotencyKey);
                var command = new SubscribeCommand(request.CorrelationId(), subscribeRequest, cancellationToken);

                return await HandleAsync(command, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeCommand command, ISubscriptionService subscriptionService)
    {
        var result = await subscriptionService.SubscribeAsync(command.SubscribeRequest, command.CancellationToken);

        var response = new CreateSubscriptionResponse(command.CorrelationId)
        {
            Created = result.Created,
            Subscription = _mapper.Map<SubscriptionDto>(result.Subscription)
        };

        // A replay is not a creation, so it does not get a 201.
        return result.Created
            ? Results.Created("/api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
