using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment processor rejected a call or is unavailable. Carries the processor's
/// error name/issue and debug id (never any card data) so operators can follow up.
/// </summary>
public class PaymentProcessorException : Exception
{
    public PaymentProcessorException(string message, int? processorStatusCode = null, string? processorError = null,
        string? processorDebugId = null)
        : base(message)
    {
        ProcessorStatusCode = processorStatusCode;
        ProcessorError = processorError;
        ProcessorDebugId = processorDebugId;
    }

    public int? ProcessorStatusCode { get; }
    public string? ProcessorError { get; }
    public string? ProcessorDebugId { get; }
}
