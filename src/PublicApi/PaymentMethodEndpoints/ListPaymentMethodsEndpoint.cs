using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsRequest : BaseRequest
{
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) {}
    public ListPaymentMethodsResponse() {}

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IPaymentService paymentService) =>
            {
                return await HandleAsync(new ListPaymentMethodsRequest
                {
                    BuyerId = httpContext.User.Identity?.Name ?? string.Empty
                }, paymentService);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentService paymentService)
    {
        var methods = await paymentService.GetPaymentMethodsAsync(request.BuyerId);
        return Results.Ok(new ListPaymentMethodsResponse(request.CorrelationId())
        {
            PaymentMethods = methods.Select(PaymentMethodDtoMapper.ToDto).ToList()
        });
    }
}
