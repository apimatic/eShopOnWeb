using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioClient>
{
    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<CreateSubscriptionEndpoint> _logger;

    public CreateSubscriptionEndpoint(IMaxioClient maxioClient, UserManager<ApplicationUser> userManager, ILogger<CreateSubscriptionEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, HttpContext context, IMaxioClient maxioClient, UserManager<ApplicationUser> userManager, ILogger<CreateSubscriptionEndpoint> logger) =>
            {
                return await HandleAsync(request, maxioClient, context, userManager, logger);
            })
            .Produces<CreateSubscriptionResponse>()
            .RequireAuthorization()
            .WithName("CreateSubscription")
            .WithTags("SubscriptionEndpoints");
    }

    private async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioClient maxioClient, HttpContext context, UserManager<ApplicationUser> userManager, ILogger<CreateSubscriptionEndpoint> logger)
    {
        try
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.NotFound("User not found");
            }

            var maxioCustomerId = await maxioClient.GetOrCreateCustomerAsync(
                userId,
                user.Email ?? $"{user.UserName}@eshop.local",
                user.UserName ?? "eShop",
                "Customer");

            var maxioSubscription = await maxioClient.CreateSubscriptionAsync(maxioCustomerId, request.ProductHandle);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = new SubscriptionDto
                {
                    Id = maxioSubscription.Id,
                    CustomerId = maxioSubscription.CustomerId,
                    ProductId = maxioSubscription.ProductId,
                    State = maxioSubscription.State ?? "unknown",
                    NextBillingAt = maxioSubscription.NextBillingAt ?? "",
                    CurrentPrice = maxioSubscription.CurrentPriceInCents / 100m
                }
            };

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating subscription");
            return Results.StatusCode(500);
        }
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioClient repository)
    {
        throw new NotImplementedException();
    }
}
