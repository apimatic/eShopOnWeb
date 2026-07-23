using MediatR;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.Fakes;

/// <summary>Captures log calls so a test can assert a failure was reported rather than swallowed.</summary>
internal sealed class RecordingLogger<T> : IAppLogger<T>
{
    public List<string> Information { get; } = new();

    public List<string> Warnings { get; } = new();

    public void LogInformation(string message, params object[] args) => Information.Add(Format(message, args));

    public void LogWarning(string message, params object[] args) => Warnings.Add(Format(message, args));

    private static string Format(string message, object[] args)
    {
        try
        {
            return args.Length == 0 ? message : string.Format(message, args);
        }
        catch (FormatException)
        {
            return message;
        }
    }
}

/// <summary>Records published notifications, and can be told to fail to prove eventing is best-effort.</summary>
internal sealed class RecordingPublisher : IPublisher
{
    public List<INotification> Published { get; } = new();

    /// <summary>When set, every publish throws — simulating a failing in-process handler.</summary>
    public Exception? ThrowOnPublish { get; set; }

    public Task Publish(object notification, CancellationToken cancellationToken = default) =>
        Publish((INotification)notification, cancellationToken);

    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        if (ThrowOnPublish is not null)
        {
            throw ThrowOnPublish;
        }

        Published.Add(notification);
        return Task.CompletedTask;
    }

    public bool PublishedAny<TNotification>() where TNotification : INotification =>
        Published.OfType<TNotification>().Any();

    public TNotification Single<TNotification>() where TNotification : INotification =>
        Published.OfType<TNotification>().Single();
}
