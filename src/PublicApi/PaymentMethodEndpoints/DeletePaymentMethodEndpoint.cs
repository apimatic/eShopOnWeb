using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, IPaymentMethodService paymentMethods) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId }, paymentMethods);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService paymentMethods)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name ?? string.Empty;
        await paymentMethods.DeleteAsync(buyerId, request.PaymentMethodId, default);
        return Results.NoContent();
    }
}
