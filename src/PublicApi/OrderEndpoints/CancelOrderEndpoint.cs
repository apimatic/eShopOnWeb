using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Finix;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    [JsonIgnore]
    public int OrderId { get; set; }
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IPaymentService>
{
    private readonly FinixOptions _finixOptions;

    public CancelOrderEndpoint(IOptions<FinixOptions> finixOptions)
    {
        _finixOptions = finixOptions.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, paymentService);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IPaymentService paymentService)
    {
        var details = await paymentService.CancelOrderAsync(request.OrderId, CancellationToken.None);
        return Results.Ok(details.ToOrderResponse(_finixOptions.Currency));
    }
}
