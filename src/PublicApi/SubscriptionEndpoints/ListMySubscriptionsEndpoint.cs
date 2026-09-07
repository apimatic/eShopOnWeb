using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsEndpoint : IEndpoint<IResult, ListMySubscriptionsRequest>
{
    private readonly IMaxioClient _maxioClient;

    public ListMySubscriptionsEndpoint(IMaxioClient maxioClient)
    {
        _maxioClient = maxioClient;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, ListMySubscriptionsRequest request) =>
            {
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var contextRequest = new ListMySubscriptionsRequest
                {
                    UserId = userId
                };
                return await HandleAsync(contextRequest);
            })
            .Produces<ListMySubscriptionsResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMySubscriptionsRequest request)
    {
        var response = new ListMySubscriptionsResponse(request.CorrelationId());

        try
        {
            var userId = request.UserId;
            if (string.IsNullOrEmpty(userId))
            {
                response.Success = false;
                response.Message = "User not authenticated";
                return Results.Unauthorized();
            }

            // Look up customer by reference
            var customer = await _maxioClient.LookupCustomerAsync(userId);
            if (customer == null)
            {
                response.Success = true;
                response.Subscriptions = new List<SubscriptionDto>();
                response.Message = "No customer found";
                return Results.Ok(response);
            }

            // Get customer subscriptions
            var subscriptionsResponse = await _maxioClient.ListCustomerSubscriptionsAsync(customer.Id);
            if (subscriptionsResponse != null)
            {
                response.Subscriptions = subscriptionsResponse.Subscriptions
                    .Select(s => new SubscriptionDto
                    {
                        Id = s.Id,
                        CustomerId = s.CustomerId,
                        State = s.State,
                        ActivatedAt = s.ActivatedAt,
                        CurrentPeriodEndsAt = s.CurrentPeriodEndsAt,
                        Plan = s.Product != null ? new SubscriptionPlanDto
                        {
                            Id = s.Product.Id,
                            Name = s.Product.Name,
                            PriceInCents = s.Product.PriceInCents,
                            Interval = s.Product.Interval,
                            IntervalUnit = s.Product.IntervalUnit
                        } : null,
                        CreatedAt = s.CreatedAt,
                        UpdatedAt = s.UpdatedAt
                    })
                    .ToList();
                response.Success = true;
            }
            else
            {
                response.Success = false;
                response.Message = "Failed to retrieve subscriptions";
            }
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Error retrieving subscriptions: {ex.Message}";
        }

        return Results.Ok(response);
    }
}

public class ListMySubscriptionsRequest : BaseRequest
{
    public string? UserId { get; set; }
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public SubscriptionPlanDto? Plan { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
