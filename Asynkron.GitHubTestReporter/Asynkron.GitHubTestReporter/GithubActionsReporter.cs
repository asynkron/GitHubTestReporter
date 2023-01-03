﻿#nullable enable
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Asynkron.GitHubTestReporter;

public class GithubActionsReporter
{
    private const string ActivitySourceName = "Proto.Cluster.Tests";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly object Lock = new();
    private static ILogger? _logger;
    private static TracerProvider? _tracerProvider;
    private static ILoggerFactory? _loggerFactory;
    private readonly StringBuilder _output = new();
    private readonly string _reportName;

    private readonly List<TestResult> _results = new();

    public GithubActionsReporter(string reportName)
    {
        _reportName = reportName;
    }

    private static Activity? StartActivity([CallerMemberName] string callerName = "N/A")
    {
        return ActivitySource.StartActivity(callerName);
    }

    public async Task Run(Func<Task> test, [CallerMemberName] string testName = "")
    {
        await Task.Delay(1).ConfigureAwait(false);

        using var activity = StartActivity(testName);
        var traceId = activity?.Context.TraceId.ToString().ToUpperInvariant() ?? "N/A";
        _logger.LogInformation("Test started");

        var sw = Stopwatch.StartNew();
        try
        {
            if (activity is not null)
            {
                traceId = activity.TraceId.ToString();
                activity.AddTag("test.name", testName);

                var traceViewUrl =
                    $"{ReportSettings.TraceViewUrl}/logs?traceId={traceId}";

                Console.WriteLine($"Running test: {testName}");
                Console.WriteLine(traceViewUrl);
            }

            await test();
            _logger.LogInformation("Test succeeded");
            _results.Add(new TestResult(testName, traceId, sw.Elapsed));
        }
        catch (Exception x)
        {
            _results.Add(new TestResult(testName, traceId, sw.Elapsed, x));
            _logger.LogError(x, "Test failed");
            throw;
        }
        finally
        {
            sw.Stop();
        }
    }

    public async Task WriteReportFile()
    {
        _tracerProvider!.ForceFlush();


        var failIcon =
            "<img src=\"https://gist.githubusercontent.com/rogeralsing/d8566b01e0850be70f7af9bc9757691e/raw/e025b5d58fe3aec1029a5c74f5ab2ee198960fcb/fail.svg\">";
        var successIcon =
            "<img src=\"https://gist.githubusercontent.com/rogeralsing/b9165f8eaeb25f05226745c94ab011b6/raw/cb28ccf1a11c44c8b4c9173bc4aeb98bfa79ca4b/success.svg\">";

        var serverUrl = Environment.GetEnvironmentVariable("GITHUB_SERVER_URL");
        var repositorySlug = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        var workspacePath = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        var commitHash = Environment.GetEnvironmentVariable("GITHUB_SHA");
        var f = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (f != null)
        {
            //get some time for traces to propagate
            //TODO: how to handle?
            await Task.Delay(2000);
            _output.AppendLine($@"
<h2>{_reportName}</h2>

<table>
<tr>
<th>
Test
</th>
<th>
Duration
</th>
</tr>");

            foreach (var res in _results)
                try
                {
                    _output.AppendLine($@"
<tr>
<td>
{(res.Exception != null ? failIcon : successIcon)}
<a href=""{ReportSettings.TraceViewUrl}/logs?traceId={res.TraceId}"">{res.Name}</a>
</td>
<td>
{res.Duration}
</td>
<td>
   <img height=""50"" src=""{ReportSettings.TraceViewUrl}/api/limit/spanmap/{res.TraceId}/svg"" />
</td>
</tr>");
                    if (res.Exception is not null)
                        _output.AppendLine($@"
<tr>
<td colspan=""3"">
<code>
{res.Exception.ToString()}
</code>
</td>
</tr>");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }

            _output.AppendLine("</table>");

            await File.AppendAllTextAsync(f, _output.ToString());
        }
    }

    public static void InitOpenTelemetryTracing()
    {
        lock (Lock)
        {
            if (_tracerProvider != null) return;

            var endpoint = new Uri(ReportSettings.OpenTelemetryUrl!);
            var builder = ResourceBuilder.CreateDefault();
            var services = new ServiceCollection();
            services.AddLogging(l =>
            {
                l.SetMinimumLevel(LogLevel.Debug);
                l.AddOpenTelemetry(
                    options =>
                    {
                        options
                            .SetResourceBuilder(builder)
                            .AddOtlpExporter(o =>
                            {
                                o.Endpoint = endpoint;
                                o.ExportProcessorType = ExportProcessorType.Batch;
                            });
                    });
            });

            var serviceProvider = services.BuildServiceProvider();

            _loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            _logger = _loggerFactory.CreateLogger<GithubActionsReporter>();
            _tracerProvider = Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(builder.AddService("Proto.Cluster.Tests"))
                .AddSource(ActivitySourceName)
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = endpoint;
                    options.ExportProcessorType = ExportProcessorType.Batch;
                })
                .Build();
        }
    }
}