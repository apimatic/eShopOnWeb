using System.Security.Claims;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal>
{
    private readonly IMaxioBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        IMaxioBillingService billingService,
        UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (CreateSubscriptionRequest request, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
                    await HandleAsync(request, principal, cancellationToken))
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.ProductHandle)] = new[] { "A product handle is required." }
            });
        }

        var user = await FindUserAsync(principal, cancellationToken);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await _billingService.SubscribeAsync(
                SubscriptionEndpointHelpers.ToMaxioUser(user),
                request.ProductHandle,
                cancellationToken);

            if (subscription is null)
            {
                return Results.NotFound();
            }

            var response = new CreateSubscriptionResponse(request.CorrelationId(), subscription);
            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (MaxioApiException exception)
        {
            return SubscriptionEndpointHelpers.BillingFailure(exception);
        }
    }

    private async Task<ApplicationUser?> FindUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await _userManager.FindByNameAsync(userName);
    }

    Task<IResult> IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal>.HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal principal)
        => HandleAsync(request, principal, CancellationToken.None);
}
