using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing the
/// shopper's held funds so no money ever moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderPaymentService, IRepository<Payment>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService paymentService, IRepository<Payment> paymentRepository) =>
            {
                return await HandleAsync(orderId, paymentService, paymentRepository);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderPaymentService paymentService,
        IRepository<Payment> paymentRepository)
    {
        var order = await paymentService.CancelOrderAsync(orderId);
        var payment = await paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId));

        var response = new CancelOrderResponse
        {
            OrderId = order.Id,
            OrderStatus = order.Status.ToString(),
            Payment = payment is null ? null : PaymentDto.FromPayment(payment)
        };
        return Results.Ok(response);
    }
}
