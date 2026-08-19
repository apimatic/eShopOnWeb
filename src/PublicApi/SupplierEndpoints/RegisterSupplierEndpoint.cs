using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Registers a supplier (a name and the URL of its product listing page). Operator-only.
/// </summary>
public class RegisterSupplierEndpoint : IEndpoint<IResult, RegisterSupplierRequest, ISupplierService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterSupplierRequest request, ISupplierService supplierService) =>
            {
                return await HandleAsync(request, supplierService);
            })
            .Produces<RegisterSupplierResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterSupplierRequest request, ISupplierService supplierService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.ProductListingUrl))
        {
            return Results.BadRequest("Both 'name' and 'productListingUrl' are required.");
        }

        if (!Uri.TryCreate(request.ProductListingUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.BadRequest("'productListingUrl' must be an absolute http(s) URL.");
        }

        var supplier = await supplierService.RegisterSupplierAsync(request.Name, request.ProductListingUrl);

        var response = new RegisterSupplierResponse(request.CorrelationId())
        {
            SupplierId = supplier.Id,
            Name = supplier.Name,
            ProductListingUrl = supplier.ProductListingUrl
        };

        return Results.Created($"api/catalog/suppliers/{supplier.Id}", response);
    }
}
