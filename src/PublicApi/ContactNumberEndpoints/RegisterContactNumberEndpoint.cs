using System;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    /// <summary>Top-level identifier of the created contact number.</summary>
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider and its
/// canonical form is stored; a number the provider does not consider usable is rejected here.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RegisterContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IContactNumberService service) =>
                await HandleAsync(request, service))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        var ownerId = _httpContextAccessor.GetOwnerId();
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.RegisterAsync(ownerId, request.PhoneNumber, _httpContextAccessor.RequestAborted());

        if (result.Status == ResultStatus.Invalid)
        {
            return Results.BadRequest(new { errors = result.ValidationErrors.Select(e => e.ErrorMessage) });
        }

        if (!result.IsSuccess)
        {
            return result.ToStatusResult();
        }

        var contactNumber = result.Value;
        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };

        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
