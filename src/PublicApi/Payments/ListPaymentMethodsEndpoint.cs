using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Returns the caller's own saved cards.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, HttpContext http, IPaymentService paymentService) =>
            {
                var request = new ListPaymentMethodsRequest { BuyerId = user.GetBuyerId(), Cancellation = http.RequestAborted };
                return await HandleAsync(request, paymentService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentService paymentService)
    {
        var methods = await paymentService.GetPaymentMethodsAsync(request.BuyerId, request.Cancellation);
        var response = new ListPaymentMethodsResponse(request.CorrelationId()) { PaymentMethods = methods };
        return Results.Ok(response);
    }
}

public class ListPaymentMethodsRequest : PaymentRequestBase
{
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(System.Guid correlationId) : base(correlationId) { }
    public ListPaymentMethodsResponse() { }

    public IReadOnlyList<SavedPaymentMethodSummary> PaymentMethods { get; set; } = new List<SavedPaymentMethodSummary>();
}
