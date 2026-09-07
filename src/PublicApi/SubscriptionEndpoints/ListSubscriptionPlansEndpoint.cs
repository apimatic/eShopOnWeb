using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest>
{
    private readonly IMaxioClient _maxioClient;
    private readonly MaxioConfiguration _config;

    public ListSubscriptionPlansEndpoint(IMaxioClient maxioClient, MaxioConfiguration config)
    {
        _maxioClient = maxioClient;
        _config = config;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ListSubscriptionPlansRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request)
    {
        var response = new ListSubscriptionPlansResponse(request.CorrelationId());

        try
        {
            var productsResponse = await _maxioClient.ListProductsAsync(_config.ProductFamilyHandle);
            if (productsResponse != null)
            {
                response.Plans = productsResponse.Products
                    .Select(p => new PlanDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Description = p.Description ?? string.Empty,
                        PriceInCents = p.PriceInCents,
                        Interval = p.Interval,
                        IntervalUnit = p.IntervalUnit,
                        Handle = p.Handle ?? string.Empty,
                        RequiresPaymentMethod = p.RequireCreditCard
                    })
                    .ToList();
                response.Success = true;
            }
            else
            {
                response.Success = false;
                response.Message = "Failed to retrieve subscription plans from billing service";
            }
        }
        catch (Exception ex)
        {
            response.Success = false;
            response.Message = $"Error retrieving plans: {ex.Message}";
        }

        return Results.Ok(response);
    }
}

public class ListSubscriptionPlansRequest : BaseRequest
{
}

public class PlanDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<PlanDto> Plans { get; set; } = new();
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
