using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (IPaymentApplicationService payments, ClaimsPrincipal user,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        var buyerId = PaymentEndpointHelpers.RequireBuyerId(user);
                        var orders = await payments.GetMyOrdersAsync(buyerId, cancellationToken);

                        return Results.Ok(new MyOrdersResponse
                        {
                            Orders = orders.Select(PaymentEndpointHelpers.ToDto).ToList()
                        });
                    }
                    catch (PaymentOperationException exception)
                    {
                        return PaymentEndpointHelpers.Fail(exception);
                    }
                })
            .Produces<MyOrdersResponse>()
            .WithTags("PaymentEndpoints");
    }
}
