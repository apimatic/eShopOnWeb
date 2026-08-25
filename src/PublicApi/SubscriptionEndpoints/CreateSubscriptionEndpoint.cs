using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Billing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Idempotent: an existing live
/// subscription to the same plan is returned instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal principal, IMaxioBillingService billingService) =>
            {
                request.UserName = principal.Identity?.Name
                                   ?? principal.FindFirst(ClaimTypes.Name)?.Value
                                   ?? principal.FindFirst("unique_name")?.Value;
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization(policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser());
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
            return Results.BadRequest("ProductHandle is required.");

        if (string.IsNullOrEmpty(request.UserName))
            return Results.Unauthorized();

        var user = await _userManager.FindByNameAsync(request.UserName);
        if (user is null)
            return Results.Unauthorized();

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var subscription = await billingService.SubscribeAsync(user.Id, user.UserName!, request.ProductHandle);
            response.Subscription = SubscriptionDto.FromModel(subscription);
            return Results.Ok(response);
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionEndpointHelpers.ToErrorResult(ex);
        }
    }
}
