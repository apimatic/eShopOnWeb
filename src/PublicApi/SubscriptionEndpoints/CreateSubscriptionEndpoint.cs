using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext>
{
    private readonly ISubscriptionBillingService _billingService;
    private readonly AuthenticatedShopperResolver _shopperResolver;

    public CreateSubscriptionEndpoint(
        ISubscriptionBillingService billingService,
        AuthenticatedShopperResolver shopperResolver)
    {
        _billingService = billingService;
        _shopperResolver = shopperResolver;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", (CreateSubscriptionRequest request, HttpContext context) =>
                HandleAsync(request, context))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>()
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("SubscriptionEndpoints")
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            });
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ProductHandle)] = new[] { "ProductHandle is required." }
            });
        }

        var shopper = await _shopperResolver.ResolveAsync(
            context.User,
            request.FirstName,
            request.LastName);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        var result = await _billingService.SubscribeAsync(
            shopper,
            request.ProductHandle.Trim(),
            context.RequestAborted);
        var response = new CreateSubscriptionResponse
        {
            Subscription = result.Subscription.ToDto(),
            Created = result.Created
        };

        return result.Created
            ? Results.Created("/api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
