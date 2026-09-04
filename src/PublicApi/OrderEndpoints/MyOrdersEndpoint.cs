using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The caller's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    private readonly IOrderPaymentService _payments;

    public MyOrdersEndpoint(IOrderPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<MyOrdersResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = AuthenticatedUser.RequireIdentity(user);
        var orders = await _payments.GetOrdersForBuyerAsync(buyerId);

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(order => new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Items = order.OrderItems.Select(item => new MyOrderItemDto
                {
                    CatalogItemId = item.ItemOrdered.CatalogItemId,
                    ProductName = item.ItemOrdered.ProductName,
                    UnitPrice = item.UnitPrice,
                    Units = item.Units
                }).ToList(),
                Payment = PaymentDtos.PaymentDto.From(order)
            }).ToList()
        };

        return Results.Ok(response);
    }
}
