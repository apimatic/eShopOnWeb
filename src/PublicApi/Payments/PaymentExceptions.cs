namespace Microsoft.eShopWeb.PublicApi.Payments;

public abstract class PaymentException : Exception
{
    protected PaymentException(string message) : base(message) { }
}

public sealed class PaymentValidationException : PaymentException
{
    public PaymentValidationException(string message) : base(message) { }
}

public sealed class PaymentNotFoundException : PaymentException
{
    public PaymentNotFoundException(string message) : base(message) { }
}

public sealed class PaymentConflictException : PaymentException
{
    public PaymentConflictException(string message) : base(message) { }
}

public sealed class PaymentConfigurationException : PaymentException
{
    public PaymentConfigurationException(string message) : base(message) { }
}

public sealed class PayPalChallengeRequiredException : PaymentException
{
    public PayPalChallengeRequiredException(string message) : base(message) { }
}

public sealed class PayPalApiException : PaymentException
{
    public PayPalApiException(int statusCode, string name, string message, string? debugId,
        IReadOnlyCollection<string> issues)
        : base(BuildMessage(statusCode, name, message, debugId, issues))
    {
        StatusCode = statusCode;
        Name = name;
        ProcessorMessage = message;
        DebugId = debugId;
        Issues = issues;
    }

    public int StatusCode { get; }
    public string Name { get; }
    public string ProcessorMessage { get; }
    public string? DebugId { get; }
    public IReadOnlyCollection<string> Issues { get; }

    private static string BuildMessage(int statusCode, string name, string message, string? debugId,
        IReadOnlyCollection<string> issues)
    {
        var issueText = issues.Count == 0 ? string.Empty : $" Issues: {string.Join("; ", issues)}.";
        var debugText = string.IsNullOrWhiteSpace(debugId) ? string.Empty : $" PayPal debug ID: {debugId}.";
        return $"PayPal rejected the operation ({statusCode} {name}): {message}.{issueText}{debugText}";
    }
}
