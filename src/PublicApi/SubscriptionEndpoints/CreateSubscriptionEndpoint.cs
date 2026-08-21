using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enroll the authenticated shopper in a Maxio Advanced Billing plan.
/// Idempotent: a second subscribe for the same live plan returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext>
{
    private readonly ISubscriptionBillingService _billing;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        ISubscriptionBillingService billing,
        UserManager<ApplicationUser> userManager)
    {
        _billing = billing;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext http) =>
            {
                return await HandleAsync(request, http);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext http)
    {
        var shopper = await SubscriptionEndpointHelpers.ResolveShopperAsync(http, _userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var result = await _billing.SubscribeAsync(shopper, request.ProductHandle, http.RequestAborted);
        return SubscriptionEndpointHelpers.MapResult(result, subscribe =>
        {
            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = subscribe.Subscription.ToDto()
            };

            return subscribe.Created
                ? Results.Created($"api/subscriptions/{subscribe.Subscription.Id}", response)
                : Results.Ok(response);
        });
    }
}
