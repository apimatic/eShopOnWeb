using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioSubscriptionService>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, subscriptionService, user);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService)
    {
        return await HandleAsync(request, subscriptionService, null!);
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioSubscriptionService subscriptionService, ClaimsPrincipal? principal)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["productHandle"] = new[] { "ProductHandle is required." }
            });
        }

        var applicationUser = await ResolveUserAsync(principal);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await subscriptionService.SubscribeAsync(
                applicationUser.Id,
                applicationUser.UserName ?? applicationUser.Email ?? applicationUser.Id,
                applicationUser.Email,
                request.ProductHandle.Trim());

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = subscription.ToDto()
            };
            return Results.Created($"api/subscriptions/{subscription.Id}", response);
        }
        catch (MaxioPlanNotFoundException ex)
        {
            return Results.Problem(title: "Unknown subscription plan.", detail: ex.Message, statusCode: 404);
        }
        catch (MaxioApiException ex)
        {
            return MaxioErrorMapper.Map(ex);
        }
    }

    private async Task<ApplicationUser?> ResolveUserAsync(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }
        var name = principal.Identity!.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        return await _userManager.FindByNameAsync(name);
    }
}
