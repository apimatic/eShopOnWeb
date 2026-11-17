using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly PaymentService _paymentService;

    public ListPaymentMethodsEndpoint(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal user, CancellationToken cancellationToken) =>
                    await HandleAsync(new ListPaymentMethodsRequest(), user, cancellationToken))
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ListPaymentMethodsRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var buyerId = EndpointIdentity.GetBuyerId(user);
        var paymentMethods = await _paymentService.GetPaymentMethodsAsync(buyerId, cancellationToken);

        var response = new ListPaymentMethodsResponse();
        response.PaymentMethods.AddRange(paymentMethods);
        return Results.Ok(response);
    }
}

public class ListPaymentMethodsRequest : BaseRequest
{
}
