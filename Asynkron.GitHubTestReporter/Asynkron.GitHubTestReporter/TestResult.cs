namespace Asynkron.GitHubTestReporter;

public record TestResult(string Name, string TraceId, TimeSpan Duration, Exception? Exception = null);