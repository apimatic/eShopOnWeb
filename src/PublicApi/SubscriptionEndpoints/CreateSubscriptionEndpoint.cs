using System.Threading.Tasks;
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
/// Enrolls the authenticated buyer in a Maxio subscription plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, billing);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var (buyer, failure) = await BuyerIdentity.ResolveAsync(user, _userManager);
        if (failure is not null || buyer is null)
        {
            return failure ?? Results.Unauthorized();
        }

        var (firstName, lastName, email) = BuyerIdentity.Describe(buyer);
        var created = await billing.SubscribeAsync(new SubscribeCommand
        {
            BuyerId = buyer.Id,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            ProductHandle = request.ProductHandle
        });

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = Map(created)
        };

        return Results.Created($"api/subscriptions/{created.Id}", response);
    }

    private static SubscriptionDto Map(SubscriptionSummary subscription)
        => new()
        {
            Id = subscription.Id,
            State = subscription.State,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            Reference = subscription.Reference
        };
}
