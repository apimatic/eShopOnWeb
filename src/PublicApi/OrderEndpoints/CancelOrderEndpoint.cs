using System.Threading;
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
/// Operator: cancels the order before fulfilment, releasing the shopper's held funds.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService, IReadRepository<Payment> paymentRepository, CancellationToken ct) =>
            {
                return await HandleAsync(orderId, paymentService, paymentRepository, ct);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IPaymentService paymentService, IReadRepository<Payment> paymentRepository, CancellationToken ct)
    {
        var order = await paymentService.CancelAsync(orderId, ct);
        var payment = await paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

        var response = new CancelOrderResponse
        {
            OrderId = order.Id,
            OrderStatus = order.Status.ToString(),
            PaymentStatus = payment?.Status.ToString()
        };

        return Results.Ok(response);
    }
}
