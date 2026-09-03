using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Models;
using Twilio.Models.Enums;

namespace Twilio.Api;

public sealed class Api20100401Payment
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    internal Api20100401Payment(RawClient rawClient, Server server, AuthSchemes auth)
    {
        _rawClient = rawClient;
        _server = server;
        _auth = auth;
    }

    /// <summary>
    /// create an instance of payments. This will start a new payments session
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that will create the resource.</param>
    /// <param name="callSid">The SID of the call that will create the resource. Call leg associated with this sid is expected to provide payment information thru DTMF.</param>
    /// <param name="idempotencyKey"></param>
    /// <param name="statusCallback"></param>
    /// <param name="bankAccountType"></param>
    /// <param name="chargeAmount"></param>
    /// <param name="currency"></param>
    /// <param name="description"></param>
    /// <param name="input"></param>
    /// <param name="minPostalCodeLength"></param>
    /// <param name="parameter"></param>
    /// <param name="paymentConnector"></param>
    /// <param name="paymentMethod"></param>
    /// <param name="postalCode"></param>
    /// <param name="securityCode"></param>
    /// <param name="timeout"></param>
    /// <param name="tokenType"></param>
    /// <param name="validCardTypes"></param>
    /// <param name="requireMatchingInputs"></param>
    /// <param name="confirmation"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCallPayments"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// create an instance of payments. This will start a new payments session
    /// </remarks>
    public Task<ApiV2010AccountCallPayments> CreatePayments(string accountSid,
        string callSid,
        string idempotencyKey,
        string statusCallback,
        PaymentsEnumBankAccountType? bankAccountType,
        double? chargeAmount,
        string? currency,
        string? description,
        string? input,
        int? minPostalCodeLength,
        object? parameter,
        string? paymentConnector,
        PaymentsEnumPaymentMethod? paymentMethod,
        bool? postalCode,
        bool? securityCode,
        int? timeout,
        PaymentsEnumTokenType? tokenType,
        string? validCardTypes,
        string? requireMatchingInputs,
        Confirmation? confirmation,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Payments.json"),
            [new TemplateParam("AccountSid", accountSid), new TemplateParam("CallSid", callSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("IdempotencyKey", idempotencyKey),
                    new Param("StatusCallback", statusCallback),
                    new Param("BankAccountType", bankAccountType),
                    new Param("ChargeAmount", chargeAmount),
                    new Param("Currency", currency),
                    new Param("Description", description),
                    new Param("Input", input),
                    new Param("MinPostalCodeLength", minPostalCodeLength),
                    new Param("Parameter", parameter),
                    new Param("PaymentConnector", paymentConnector),
                    new Param("PaymentMethod", paymentMethod),
                    new Param("PostalCode", postalCode),
                    new Param("SecurityCode", securityCode),
                    new Param("Timeout", timeout),
                    new Param("TokenType", tokenType),
                    new Param("ValidCardTypes", validCardTypes),
                    new Param("RequireMatchingInputs", requireMatchingInputs),
                    new Param("Confirmation", confirmation)]),
            JsonResponse.Create<ApiV2010AccountCallPayments>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// update an instance of payments with different phases of payment flows.
    /// </summary>
    /// <param name="accountSid">The SID of the <see href="https://www.twilio.com/docs/iam/api/account">Account</see> that will update the resource.</param>
    /// <param name="callSid">The SID of the call that will update the resource. This should be the same call sid that was used to create payments resource.</param>
    /// <param name="sid">The SID of Payments session that needs to be updated.</param>
    /// <param name="idempotencyKey"></param>
    /// <param name="statusCallback"></param>
    /// <param name="capture"></param>
    /// <param name="status"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="ApiV2010AccountCallPayments"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// update an instance of payments with different phases of payment flows.
    /// </remarks>
    public Task<ApiV2010AccountCallPayments> UpdatePayments(string accountSid,
        string callSid,
        string sid,
        string idempotencyKey,
        string statusCallback,
        PaymentsEnumCapture? capture,
        PaymentsEnumStatus? status,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default("/2010-04-01/Accounts/{AccountSid}/Calls/{CallSid}/Payments/{Sid}.json"),
            [new TemplateParam("AccountSid", accountSid),
                new TemplateParam("CallSid", callSid),
                new TemplateParam("Sid", sid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            FormUrlEncodedRequest.Create([new Param("IdempotencyKey", idempotencyKey),
                    new Param("StatusCallback", statusCallback),
                    new Param("Capture", capture),
                    new Param("Status", status)]),
            JsonResponse.Create<ApiV2010AccountCallPayments>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
