using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Lists the authenticated shopper's subscriptions from Maxio Advanced Billing.
/// </summary>
public sealed class MySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioBillingClient _billingClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MySubscriptionsEndpoint(
        IMaxioBillingClient billingClient, UserManager<ApplicationUser> userManager,
        IHttpContextAccessor httpContextAccessor)
    {
        _billingClient = billingClient;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Microsoft.AspNetCore.Authorization.Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, CancellationToken cancellationToken) =>
                await HandleAsync(httpContext, cancellationToken))
            .Produces<MySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        var user = await SubscriptionEndpointHelpers.GetCurrentUserAsync(httpContext, _userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var response = new MySubscriptionsResponse(System.Guid.NewGuid());
        var customer = await _billingClient.FindCustomerByReferenceAsync(
            SubscriptionReference.ForCustomer(user.Id), cancellationToken);
        if (customer is not null)
        {
            var subscriptions = await _billingClient.ListCustomerSubscriptionsAsync(
                customer.Id, cancellationToken);
            foreach (var subscription in subscriptions)
            {
                response.Subscriptions.Add(SubscriptionDto.From(subscription));
            }
        }

        return Results.Ok(response);
    }

    public Task<IResult> HandleAsync() =>
        HandleAsync(_httpContextAccessor.HttpContext!, CancellationToken.None);
}
