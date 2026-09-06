using System;
using System.Linq;
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

public class ListMySubscriptionsEndpoint : IEndpoint<IResult>
{
    private readonly IMaxioClient _maxioClient;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ListMySubscriptionsEndpoint> _logger;

    public ListMySubscriptionsEndpoint(IMaxioClient maxioClient, UserManager<ApplicationUser> userManager, ILogger<ListMySubscriptionsEndpoint> logger)
    {
        _maxioClient = maxioClient;
        _userManager = userManager;
        _logger = logger;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (HttpContext context, IMaxioClient maxioClient, UserManager<ApplicationUser> userManager, ILogger<ListMySubscriptionsEndpoint> logger) =>
            {
                return await HandleAsync(context, maxioClient, userManager, logger);
            })
            .Produces<ListMySubscriptionsResponse>()
            .RequireAuthorization()
            .WithName("GetMySubscriptions")
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        throw new NotImplementedException();
    }

    private async Task<IResult> HandleAsync(HttpContext context, IMaxioClient maxioClient, UserManager<ApplicationUser> userManager, ILogger<ListMySubscriptionsEndpoint> logger)
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

            var maxioCustomerId = await maxioClient.FindCustomerByReferenceAsync(userId);
            if (maxioCustomerId == 0)
            {
                return Results.Ok(new ListMySubscriptionsResponse(Guid.NewGuid()));
            }

            var maxioSubscriptions = await maxioClient.GetCustomerSubscriptionsAsync(maxioCustomerId);

            var response = new ListMySubscriptionsResponse(Guid.NewGuid());
            response.Subscriptions.AddRange(maxioSubscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                CustomerId = s.CustomerId,
                ProductId = s.ProductId,
                State = s.State ?? "unknown",
                NextBillingAt = s.NextBillingAt ?? "",
                CurrentPrice = s.CurrentPriceInCents / 100m
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing subscriptions");
            return Results.StatusCode(500);
        }
    }
}
