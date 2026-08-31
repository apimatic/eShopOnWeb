using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Raises a bill with the provider for one of the caller's orders. What is billed comes from the order
/// itself; the request carries only the due date and (optionally) the customer details. The bill starts
/// as a draft, not yet put to the shopper.
/// </summary>
public class RaiseInvoiceForOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/invoice",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, RaiseInvoiceForOrderRequest request, IInvoiceOrchestrator orchestrator, HttpContext httpContext) =>
                await orchestrator.RaiseInvoiceAsync(orderId, request, httpContext.User, httpContext.RequestAborted))
            .Produces<CreateInvoiceResponse>(StatusCodes.Status201Created)
            .WithTags("InvoiceEndpoints");
    }
}
