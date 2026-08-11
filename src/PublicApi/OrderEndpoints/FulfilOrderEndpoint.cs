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
/// Operator action: marks the order fulfilled and captures the held funds (takes the money). A stale
/// authorization is renewed rather than failing the fulfilment; if it can no longer be renewed the
/// error says so. Administrator only.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderOperationRequest, IOrderPaymentService>
{
    private readonly IPaymentConfiguration _paymentConfiguration;

    public FulfilOrderEndpoint(IPaymentConfiguration paymentConfiguration)
    {
        _paymentConfiguration = paymentConfiguration;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
            {
                return await HandleAsync(new OrderOperationRequest { OrderId = orderId }, service);
            })
            .Produces<OrderSummaryDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderOperationRequest request, IOrderPaymentService service)
    {
        var order = await service.FulfilOrderAsync(request.OrderId);
        return Results.Ok(OrderMapper.ToSummary(order, _paymentConfiguration.Currency));
    }
}
