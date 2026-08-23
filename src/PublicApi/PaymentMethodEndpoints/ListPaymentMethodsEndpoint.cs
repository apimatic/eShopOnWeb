using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentMethodService service, HttpContext httpContext) =>
            {
                var buyerId = BuyerIdentity.GetBuyerId(httpContext);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new ListPaymentMethodsRequest { BuyerId = buyerId }, service);
            })
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentMethodService service)
    {
        var methods = await service.ListAsync(request.BuyerId);
        var response = new PaymentMethodListResponse(request.CorrelationId());
        response.PaymentMethods.AddRange(methods.Select(m => PaymentMethodMapper.ToResponse(m, request.CorrelationId())));
        return Results.Ok(response);
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}
