using System;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.SupplierAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.CatalogSupplierEndpoints;

/// <summary>
/// Registers a supplier: a name and the URL of its product listing page. Operator-only.
/// </summary>
[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class RegisterSupplierEndpoint : EndpointBaseAsync
    .WithRequest<RegisterSupplierRequest>
    .WithActionResult<RegisterSupplierResponse>
{
    private readonly IRepository<Supplier> _supplierRepository;

    public RegisterSupplierEndpoint(IRepository<Supplier> supplierRepository)
    {
        _supplierRepository = supplierRepository;
    }

    [HttpPost("api/catalog/suppliers")]
    [SwaggerOperation(
        Summary = "Registers a supplier and its product listing URL",
        Description = "Registers a supplier and the URL of its product listing page",
        OperationId = "catalog.suppliers.register",
        Tags = new[] { "CatalogSupplierEndpoints" })
    ]
    public override async Task<ActionResult<RegisterSupplierResponse>> HandleAsync(
        [FromBody] RegisterSupplierRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("A supplier name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ProductListingUrl)
            || !Uri.TryCreate(request.ProductListingUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest("A valid absolute http(s) product listing URL is required.");
        }

        var supplier = new Supplier(request.Name.Trim(), request.ProductListingUrl.Trim());
        supplier = await _supplierRepository.AddAsync(supplier, cancellationToken);

        var response = new RegisterSupplierResponse(request.CorrelationId())
        {
            SupplierId = supplier.Id,
            Name = supplier.Name,
            ProductListingUrl = supplier.ProductListingUrl
        };

        return Created($"api/catalog/suppliers/{supplier.Id}", response);
    }
}
