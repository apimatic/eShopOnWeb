using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationWorkflow>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, IOrderNotificationWorkflow workflow) =>
            {
                return await HandleAsync(request, workflow);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationWorkflow workflow)
    {
        var buyerId = BuyerIdentity.GetBuyerId(_httpContextAccessor.HttpContext!.User);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var lines = new List<CatalogOrderLine>();
        foreach (var item in request.Items ?? new List<CreateOrderItemRequest>())
        {
            lines.Add(new CatalogOrderLine { CatalogItemId = item.CatalogItemId, Quantity = item.Quantity });
        }

        var result = await workflow.PlaceOrderAsync(buyerId, lines);
        if (!result.Succeeded || result.Order == null)
        {
            return ApiResults.From(result.StatusCode, error: result.Error);
        }

        return ApiResults.From(result.StatusCode, new CreateOrderResponse
        {
            OrderId = result.Order.Id,
            Status = result.Order.Status.ToString(),
            Total = result.Order.Total()
        });
    }
}

public class CreateOrderRequest : BaseRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
}
