using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a Maxio plan.
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
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest(new { errors = new[] { "productHandle is required." } });
        }

        var user = await SubscriptionEndpointUser.ResolveAsync(_userManager, httpContext);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var subscription = await _billingService.SubscribeAsync(new SubscribeShopperRequest
        {
            BuyerId = user.Id,
            Email = user.Email ?? user.UserName ?? string.Empty,
            UserName = user.UserName,
            ProductHandle = request.ProductHandle.Trim()
        });

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionMappings.ToDto(subscription)
        };

        if (subscription.AlreadyExisted)
        {
            return Results.Ok(response);
        }

        return Results.Created($"api/subscriptions/{subscription.Id}", response);
    }
}
