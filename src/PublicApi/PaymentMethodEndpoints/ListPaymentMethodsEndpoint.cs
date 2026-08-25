using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>The caller's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, BuyerContext<IPaymentMethodService>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentMethodService paymentMethodService) =>
            {
                var context = new BuyerContext<IPaymentMethodService>(user.Identity!.Name!, paymentMethodService);
                return await HandleAsync(new ListPaymentMethodsRequest(), context);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, BuyerContext<IPaymentMethodService> context)
    {
        var paymentMethods = await context.Service.ListForBuyerAsync(context.BuyerId, default);
        var response = new ListPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = paymentMethods.Select(PaymentMethodDto.FromPaymentMethod).ToList()
        };
        return Results.Ok(response);
    }
}
