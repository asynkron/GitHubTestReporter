namespace Asynkron.GitHubTestReporter;

internal record TestResult(string Name, string TraceId, TimeSpan Duration, Exception? Exception = null);