using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the authenticated shopper's subscriptions. The caller's identity comes from the JWT.
/// </summary>
public class MySubscriptionsEndpoint : IEndpoint<IResult, MySubscriptionsRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                var request = new MySubscriptionsRequest(user.Identity?.Name);
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<MySubscriptionsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Lists the caller's subscriptions")
            {
                Description = "Returns the subscriptions belonging to the authenticated shopper."
            });
    }

    public Task<IResult> HandleAsync(MySubscriptionsRequest request, ISubscriptionBillingService billingService)
        => HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        MySubscriptionsRequest request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        var response = new MySubscriptionsResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.CallerUserName))
            return Results.Unauthorized();

        var subscriber = SubscriberIdentity.FromUser(request.CallerUserName!);
        var subscriptions = await billingService.GetSubscriptionsAsync(subscriber, cancellationToken);

        response.Subscriptions = subscriptions.Select(s => s.ToDto()).ToList();
        return Results.Ok(response);
    }
}
