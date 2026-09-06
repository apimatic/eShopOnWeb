using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansEndpoint : IEndpoint<IResult, ListSubscriptionPlansRequest, IMaxioSubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IMaxioSubscriptionService service) =>
            {
                return await HandleAsync(new ListSubscriptionPlansRequest(), service);
            })
            .Produces<ListSubscriptionPlansResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(ListSubscriptionPlansRequest request, IMaxioSubscriptionService service)
    {
        var response = new ListSubscriptionPlansResponse();
        try
        {
            var plans = await service.GetSubscriptionPlansAsync();
            response.Plans = plans.Select(p => new SubscriptionPlanResponse
            {
                Id = p.Id,
                Handle = p.Handle,
                Name = p.Name,
                PriceInCents = p.PriceInCents
            }).ToList();
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            response.ErrorMessage = ex.Message;
            return Results.BadRequest(response);
        }
    }
}

public class ListSubscriptionPlansRequest : BaseRequest { }

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse() : base() { }
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }

    public List<SubscriptionPlanResponse> Plans { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class SubscriptionPlanResponse
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
}
