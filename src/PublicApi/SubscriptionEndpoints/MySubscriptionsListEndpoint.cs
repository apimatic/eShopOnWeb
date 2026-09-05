using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// List subscriptions for the authenticated user
/// </summary>
public class MySubscriptionsListEndpoint : IEndpoint<IResult, GetMySubscriptionsRequest, IMaxioApiClient>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CatalogContext _context;

    public MySubscriptionsListEndpoint(UserManager<ApplicationUser> userManager, CatalogContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            async (IMaxioApiClient maxioClient, HttpContext httpContext) =>
            {
                var endpoint = new MySubscriptionsEndpointHandler(_userManager, _context);
                return await endpoint.HandleAsync(new GetMySubscriptionsRequest(), maxioClient, httpContext);
            })
            .Produces<MySubscriptionsListResponse>()
            .RequireAuthorization()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioApiClient maxioClient)
    {
        throw new NotImplementedException("Use MySubscriptionsEndpointHandler directly");
    }
}

internal class MySubscriptionsEndpointHandler
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CatalogContext _context;

    public MySubscriptionsEndpointHandler(UserManager<ApplicationUser> userManager, CatalogContext context)
    {
        _userManager = userManager;
        _context = context;
    }

    public async Task<IResult> HandleAsync(GetMySubscriptionsRequest request, IMaxioApiClient maxioClient, HttpContext httpContext)
    {
        try
        {
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Results.NotFound();
            }

            // Get Maxio customer mapping
            var mapping = _context.MaxioCustomerMappings.FirstOrDefault(m => m.EshopUserId == userId);
            if (mapping == null)
            {
                // No subscriptions yet
                var emptyResponse = new MySubscriptionsListResponse(Guid.NewGuid());
                return Results.Ok(emptyResponse);
            }

            // Get subscriptions from Maxio
            var subscriptions = await maxioClient.GetCustomerSubscriptionsAsync(mapping.MaxioCustomerId);

            var response = new MySubscriptionsListResponse(Guid.NewGuid());
            response.Subscriptions.AddRange(subscriptions.Select(s => new SubscriptionDto
            {
                Id = s.Id,
                State = s.State,
                ProductId = s.ProductId,
                ProductName = s.Product?.Name ?? "",
                ProductHandle = s.Product?.Handle ?? "",
                ProductPrice = s.Product != null ? s.Product.PriceInCents / 100m : 0,
                CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                NextAssessmentAt = s.NextAssessmentAt,
                ActivatedAt = s.ActivatedAt,
                CreatedAt = s.CreatedAt
            }));

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

public class GetMySubscriptionsRequest : BaseRequest
{
}

public class MySubscriptionsListResponse : BaseResponse
{
    public MySubscriptionsListResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
