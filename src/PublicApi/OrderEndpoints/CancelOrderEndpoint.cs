using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.Payments;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
                [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int orderId, HttpContext http, CancelOrderRequest request, PaymentService payments) =>
                {
                    request.OrderId = orderId;
                    request.RequestAborted = http.RequestAborted;
                    return await HandleAsync(request, payments);
                })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, PaymentService payments)
    {
        var payment = await payments.CancelAsync(request.OrderId, request.RequestAborted);
        return Results.Ok(new CancelOrderResponse { Payment = payment });
    }
}

public class CancelOrderRequest : PaymentRequestBase
{
    [JsonIgnore]
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public OrderPaymentView Payment { get; set; } = null!;
}
