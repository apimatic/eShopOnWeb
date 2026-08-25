using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IRepository<Order>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId,
                   IRepository<Order> orderRepo,
                   PayPalPaymentService paypal,
                   CancellationToken ct) =>
            {
                var spec = new OrderByIdSpec(orderId);
                var order = await orderRepo.FirstOrDefaultAsync(spec, ct);
                if (order == null) return Results.NotFound();
                if (order.Status != OrderStatus.PaymentAuthorized)
                    return Results.Conflict(new { error = $"Order is in status {order.Status}, cannot cancel." });

                try
                {
                    await paypal.VoidAsync(order.Payment!.AuthorizationId!, order.Id, ct);
                }
                catch (PayPalException ex)
                {
                    return Results.Problem(detail: ex.Message, statusCode: ex.HttpStatusCode);
                }

                order.Cancel();
                await orderRepo.UpdateAsync(order, ct);

                return Results.Ok(new CancelOrderResponse { OrderId = order.Id, Status = order.Status.ToString() });
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IRepository<Order> service)
        => throw new System.NotSupportedException();
}

public class CancelOrderRequest : BaseRequest { }

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
