using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The provider validates the number and its
/// canonical E.164 form is stored; a number the provider does not consider a usable destination is
/// rejected here rather than at send time.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, HttpContext http, IContactNumberService service) =>
            {
                request.BuyerId = http.User.Identity?.Name;
                return await HandleAsync(request, service, http.RequestAborted);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await service.RegisterAsync(request.BuyerId, request.PhoneNumber ?? string.Empty, ct);
            if (!result.Success)
            {
                return Results.Problem(detail: result.RejectionReason, statusCode: StatusCodes.Status400BadRequest,
                    title: "The number could not be registered.");
            }

            var response = new RegisterContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = result.ContactNumber!.Id,
                PhoneNumber = result.ContactNumber.PhoneNumber
            };
            return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
        }
        catch (SmsGatewayException)
        {
            return Results.Problem(detail: "The number could not be validated with the messaging provider. Please try again.",
                statusCode: StatusCodes.Status502BadGateway, title: "Messaging provider unavailable.");
        }
    }
}

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The raw mobile number to register (any provider-acceptable format).</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>The owning shopper — set from the token, never from the request body.</summary>
    public string? BuyerId { get; set; }
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }

    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}
