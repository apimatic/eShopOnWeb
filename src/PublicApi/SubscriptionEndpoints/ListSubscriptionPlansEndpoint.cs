using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult>
{
    public Task<IResult> HandleAsync() => Task.FromResult<IResult>(Results.NoContent());

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioService maxioService) =>
            {
                try
                {
                    var plans = await maxioService.GetSubscriptionPlansAsync();
                    var response = new ListSubscriptionPlansResponse(Guid.NewGuid())
                    {
                        Success = true
                    };

                    foreach (var plan in plans)
                    {
                        response.Plans.Add(new SubscriptionPlanDto
                        {
                            Id = plan.Id,
                            Handle = plan.Handle,
                            Name = plan.Name,
                            Description = plan.Description,
                            Price = plan.Price,
                            BillingCycle = plan.BillingCycle
                        });
                    }

                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { success = false, message = $"Failed to fetch subscription plans: {ex.Message}" });
                }
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints")
            .WithName("ListSubscriptionPlans");
    }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
        Plans = new List<SubscriptionPlanDto>();
    }

    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<SubscriptionPlanDto> Plans { get; set; }
}
