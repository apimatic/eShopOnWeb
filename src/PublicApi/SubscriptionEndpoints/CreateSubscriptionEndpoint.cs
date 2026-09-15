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
/// Enrolls the authenticated shopper in a Maxio subscription plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        ISubscriptionBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization();
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { message = "productHandle is required." });
        }

        var subscriber = await SubscriberIdentityFactory.FromHttpContextAsync(httpContext, _userManager);
        if (subscriber == null)
        {
            return Results.Unauthorized();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());
        var subscription = await _billingService.SubscribeAsync(subscriber, request.ProductHandle);
        response.Subscription = CreateSubscriptionResponse.ToDto(subscription);
        return Results.Created($"api/subscriptions/{subscription.Id}", response);
    }
}
