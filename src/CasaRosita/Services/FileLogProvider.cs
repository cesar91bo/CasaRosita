namespace CasaRosita.Services;
public sealed class FileLogProvider(string folder) : ILoggerProvider
{
    private readonly object gate = new();
    private readonly string logFolder = folder;
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }
    private sealed class FileLogger(FileLogProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState,Exception?,string> format)
        {
            if (!IsEnabled(level)) return;
            lock(owner.gate)
            {
                Directory.CreateDirectory(owner.logFolder);
                File.AppendAllText(Path.Combine(owner.logFolder,$"{DateTime.Now:yyyy-MM-dd}.log"),$"{DateTimeOffset.Now:O} {level} {category}: {format(state,error)} {error}\n");
            }
        }
    }
}
