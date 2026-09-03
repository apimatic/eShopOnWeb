using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
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
/// Enroll the signed-in shopper in a Maxio subscription plan (idempotent).
/// </summary>
public class CreateShopperSubscriptionEndpoint : IEndpoint<IResult, CreateShopperSubscriptionRequest, ClaimsPrincipal>
{
    private readonly ISubscriptionBillingService _billing;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateShopperSubscriptionEndpoint(
        ISubscriptionBillingService billing,
        UserManager<ApplicationUser> userManager)
    {
        _billing = billing;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateShopperSubscriptionRequest request, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, cancellationToken);
            })
            .RequireAuthorization()
            .Produces<CreateShopperSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateShopperSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateShopperSubscriptionRequest request, ClaimsPrincipal user)
        => HandleAsync(request, user, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        CreateShopperSubscriptionRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var shopper = await ShopperIdentityFactory.FromUserAsync(user, _userManager);
        if (shopper is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.Json(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "productHandle is required."
            }, statusCode: StatusCodes.Status400BadRequest);
        }

        var enrollment = await _billing.EnrollAsync(shopper, request.ProductHandle.Trim(), cancellationToken);
        var response = CreateShopperSubscriptionResponse.From(enrollment, request.CorrelationId());
        return enrollment.Created
            ? Results.Created("api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
