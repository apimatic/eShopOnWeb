using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.WalmartEndpoints;

/// <summary>
/// Sets how many of a SKU the shop offers on Walmart. Afterwards Walmart's own record of the
/// SKU's quantity is that number; eShop's own level for the item is unchanged. Operator action.
/// </summary>
public class SetWalmartStockEndpoint : IEndpoint<IResult, string, SetWalmartStockRequest, IWalmartInventoryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPut("api/walmart/stock/{sku}",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string sku, SetWalmartStockRequest request, IWalmartInventoryService walmart) =>
            {
                return await HandleAsync(sku, request, walmart);
            })
            .Produces<WalmartStockResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("WalmartEndpoints");
    }

    public async Task<IResult> HandleAsync(string sku, SetWalmartStockRequest request, IWalmartInventoryService walmart)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return Results.BadRequest("A SKU is required.");
        }
        if (request.Quantity < 0)
        {
            return Results.BadRequest("Quantity must be zero or greater.");
        }

        var result = await walmart.SetStockAsync(sku, request.Quantity);
        return Results.Ok(new WalmartStockResponse(request.CorrelationId())
        {
            Sku = result.Sku,
            Quantity = result.Quantity
        });
    }
}

public class SetWalmartStockRequest : BaseRequest
{
    /// <summary>The quantity of the SKU to offer on Walmart. Must be zero or greater.</summary>
    public int Quantity { get; set; }
}

/// <summary>Shared response shape for the single-SKU Walmart stock endpoints.</summary>
public class WalmartStockResponse : BaseResponse
{
    public WalmartStockResponse(Guid correlationId) : base(correlationId)
    {
    }

    public WalmartStockResponse()
    {
    }

    public string Sku { get; set; } = string.Empty;

    /// <summary>The SKU's quantity as Walmart records it.</summary>
    public int Quantity { get; set; }
}
