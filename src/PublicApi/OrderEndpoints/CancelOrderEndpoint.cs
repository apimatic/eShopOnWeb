using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    private readonly PayPalService _payPal;

    public CancelOrderEndpoint(PayPalService payPal)
    {
        _payPal = payPal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   IRepository<Order> orderRepository,
                   CancellationToken ct) =>
            {
                var spec = new OrderByIdSpec(orderId);
                var order = await orderRepository.FirstOrDefaultAsync(spec, ct);

                if (order == null) return Results.NotFound();
                if (order.Status != OrderStatus.Authorized)
                    return Results.BadRequest($"Order is in status {order.Status} and cannot be cancelled.");
                if (string.IsNullOrEmpty(order.PayPalAuthorizationId))
                    return Results.BadRequest("Order has no authorization to void.");

                await _payPal.VoidAsync(order.PayPalAuthorizationId, ct);

                order.SetCancelled();
                await orderRepository.UpdateAsync(order, ct);

                return Results.Ok(new CancelOrderResponse
                {
                    OrderId = order.Id,
                    Status = order.Status.ToString()
                });
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> dep)
        => Task.FromResult(Results.StatusCode(501));
}

public class CancelOrderRequest : BaseRequest { }

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse() : base(System.Guid.NewGuid()) { }
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
