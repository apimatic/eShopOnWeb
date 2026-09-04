using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Logging;

/// <summary>
/// Minimal safe file sink: enabled only when Logging:File:Path is configured. Used to
/// capture host output when the app runs detached. Card data never reaches ILogger calls,
/// so this adds no exposure.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();

    public FileLoggerProvider(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(path, append: true) { AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose() => _writer.Dispose();

    internal void Write(string category, LogLevel level, EventId id, Exception? exception, string? state)
    {
        lock (_gate)
        {
            _writer.WriteLine($"{DateTimeOffset.UtcNow:O} [{level}] {category}({id.Id}) {state}{(exception is null ? string.Empty : Environment.NewLine + exception)}");
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(category, logLevel, eventId, exception, formatter(state, exception));
            }
        }
    }
}
