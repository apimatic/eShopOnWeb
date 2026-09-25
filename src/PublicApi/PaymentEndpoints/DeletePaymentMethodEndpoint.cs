using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Removes one of the caller's saved cards, so it can no longer appear or be used to pay.</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, OrderActionRequest, ISavedCardService>
{
    private readonly IHttpContextAccessor _http;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedCardService service) =>
                await HandleAsync(new OrderActionRequest { OrderId = paymentMethodId }, service))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, ISavedCardService service)
    {
        var ctx = _http.HttpContext!;
        var buyerId = CallerIdentity.BuyerId(ctx.User);
        await service.DeleteAsync(buyerId, request.OrderId, ctx.RequestAborted);
        return Results.NoContent();
    }
}
