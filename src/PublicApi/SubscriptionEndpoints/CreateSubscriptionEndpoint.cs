using System.Security.Claims;
using System.Threading;
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
/// Enroll the authenticated shopper in a Maxio subscription plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ISubscriptionBillingService billing, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, billing, user, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
        => HandleAsync(request, billing, new ClaimsPrincipal(), CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billing,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var customer = await BillingCustomerResolver.ResolveAsync(_userManager, user);
        if (customer is null)
        {
            return Results.Unauthorized();
        }

        var result = await billing.SubscribeAsync(customer, request.ProductHandle, cancellationToken);
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            AlreadySubscribed = result.AlreadySubscribed,
            Subscription = Map(result.Subscription)
        };

        if (result.AlreadySubscribed)
        {
            return Results.Ok(response);
        }

        return Results.Created("api/my-subscriptions", response);
    }

    private static UserSubscriptionDto Map(UserSubscription subscription)
        => new()
        {
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            State = subscription.State,
            NextBillingDate = subscription.NextBillingDate,
            Reference = subscription.Reference
        };
}
