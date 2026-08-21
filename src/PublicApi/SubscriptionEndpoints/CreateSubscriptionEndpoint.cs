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
/// Enrolls the authenticated shopper in a Maxio plan. Idempotent for a given user + plan.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(
        UserManager<ApplicationUser> users,
        IHttpContextAccessor httpContextAccessor)
    {
        _users = users;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ISubscriptionBillingService billing) =>
                await HandleAsync(request, billing))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
    {
        request ??= new CreateSubscriptionRequest();

        var http = _httpContextAccessor.HttpContext;
        if (http is null)
        {
            return Results.Unauthorized();
        }

        var (shopper, error) = await CurrentShopper.ResolveAsync(http.User, _users);
        if (error is not null || shopper is null)
        {
            return error ?? Results.Unauthorized();
        }

        var result = await billing.SubscribeAsync(shopper, request.ProductHandle, http.RequestAborted);
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = result.Subscription.ToDto(),
            Created = result.Created
        };

        return result.Created
            ? Results.Created($"api/subscriptions/{response.Subscription.Id}", response)
            : Results.Ok(response);
    }
}
