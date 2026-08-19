using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Create (or return the existing) Maxio subscription for the authenticated shopper
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest? request, ISubscriptionBillingService billingService, CurrentShopperResolver currentShopper, HttpContext httpContext) =>
            {
                return await HandleAsync(request ?? new CreateSubscriptionRequest(), billingService, currentShopper, httpContext);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
        => HandleAsync(request, billingService, currentShopper: null, httpContext: null);

    private static async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billingService,
        CurrentShopperResolver? currentShopper,
        HttpContext? httpContext)
    {
        if (httpContext?.User is null || currentShopper is null)
        {
            return Results.Unauthorized();
        }

        var shopper = await currentShopper.ResolveAsync(httpContext.User, request.ProductHandle);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var created = await billingService.SubscribeAsync(shopper);
        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ToDto(created)
        };

        return Results.Created($"api/subscriptions/{created.Id}", response);
    }

    internal static SubscriptionDto ToDto(ApplicationCore.Billing.SubscriptionSummary subscription) =>
        new()
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            State = subscription.State,
            NextBillingAt = subscription.NextBillingAt
        };
}
