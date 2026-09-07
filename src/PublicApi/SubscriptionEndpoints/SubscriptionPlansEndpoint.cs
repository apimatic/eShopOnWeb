using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", GetPlans)
            .Produces<SubscriptionPlansResponse>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError)
            .WithTags("Subscriptions")
            .WithName("GetSubscriptionPlans")
            .AllowAnonymous();
    }

    private static async Task<IResult> GetPlans(
        IMaxioSubscriptionService subscriptionService,
        CancellationToken ct)
    {
        try
        {
            var plans = await subscriptionService.ListPlansAsync(ct);
            var response = new SubscriptionPlansResponse
            {
                Plans = plans.Select(p => new SubscriptionPlanResponse
                {
                    Handle = p.Handle,
                    Name = p.Name,
                    PriceInCents = p.PriceInCents,
                    Price = (p.PriceInCents / 100m).ToString("C"),
                    BillingFrequency = p.BillingFrequency,
                    Description = p.Description
                }).ToList()
            };
            return Results.Ok(response);
        }
        catch (MaxioSubscriptionException ex)
        {
            var statusCode = ex.StatusCode ?? StatusCodes.Status500InternalServerError;
            return Results.Json(
                new ErrorResponse { Message = ex.Message },
                statusCode: statusCode);
        }
    }

    public class SubscriptionPlansResponse
    {
        public required List<SubscriptionPlanResponse> Plans { get; set; }
    }

    public class SubscriptionPlanResponse
    {
        public required string Handle { get; set; }
        public required string Name { get; set; }
        public required decimal PriceInCents { get; set; }
        public required string Price { get; set; }
        public required string BillingFrequency { get; set; }
        public string? Description { get; set; }
    }

    public class ErrorResponse
    {
        public required string Message { get; set; }
    }
}
