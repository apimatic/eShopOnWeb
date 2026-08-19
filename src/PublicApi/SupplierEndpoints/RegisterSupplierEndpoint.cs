using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SupplierEndpoints;

/// <summary>
/// Registers a supplier: a name and the URL of its product listing page. Operator-only.
/// </summary>
public class RegisterSupplierEndpoint : IEndpoint<IResult, RegisterSupplierRequest, IRepository<Supplier>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/catalog/suppliers",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterSupplierRequest request, IRepository<Supplier> supplierRepository) =>
            {
                return await HandleAsync(request, supplierRepository);
            })
            .Produces<RegisterSupplierResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("SupplierEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterSupplierRequest request, IRepository<Supplier> supplierRepository)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest("A supplier name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ProductListingUrl) ||
            !Uri.TryCreate(request.ProductListingUrl.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Results.BadRequest("A valid absolute http(s) product listing URL is required.");
        }

        var supplier = new Supplier(request.Name, request.ProductListingUrl);
        supplier = await supplierRepository.AddAsync(supplier);

        var response = new RegisterSupplierResponse(request.CorrelationId())
        {
            SupplierId = supplier.Id,
            Name = supplier.Name,
            ProductListingUrl = supplier.ProductListingUrl
        };

        return Results.Created($"api/catalog/suppliers/{supplier.Id}", response);
    }
}
