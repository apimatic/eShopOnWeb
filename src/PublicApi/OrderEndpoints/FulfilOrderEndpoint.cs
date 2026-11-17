using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderEndpoint : IEndpoint<IResult, OrderByIdRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
            {
                return await HandleAsync(new OrderByIdRequest { OrderId = orderId }, paymentService);
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderByIdRequest request, IPaymentService paymentService)
    {
        var order = await paymentService.FulfilAsync(request.OrderId);
        return Results.Ok(OrderDtoMapper.ToActionResponse(order, request.CorrelationId()));
    }
}
