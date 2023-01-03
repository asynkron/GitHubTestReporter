#nullable enable
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Asynkron.GitHubTestReporter;

public class OtelTestRunner
{
    public OtelTestRunner(ReportSettings settings)
    {
        InitOpenTelemetry(settings);
    }
    private const string ActivitySourceName = "Proto.Cluster.Tests";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly object Lock = new();
    private static ILogger? _logger;
    private static TracerProvider? _tracerProvider;
    private static ILoggerFactory? _loggerFactory;
    private readonly List<TestResult> _results = new();


    private static Activity? StartActivity([CallerMemberName] string callerName = "N/A")
    {
        return ActivitySource.StartActivity(callerName);
    }

    public async Task Run(Func<Task> test, [CallerMemberName] string testName = "")
    {
        //
        await Task.Delay(1).ConfigureAwait(false);

        using var activity = StartActivity(testName);
        var traceId = activity?.Context.TraceId.ToString().ToUpperInvariant() ?? "N/A";
        _logger!.LogInformation("Test started");

        var sw = Stopwatch.StartNew();
        try
        {
            if (activity is not null)
            {
                activity.AddTag("test.name", testName);
            }

            await test();
            _logger!.LogInformation("Test succeeded");
            _results.Add(new TestResult(testName, traceId, sw.Elapsed));
        }
        catch (Exception x)
        {
            _logger!.LogError(x, "Test failed");
            _results.Add(new TestResult(testName, traceId, sw.Elapsed, x));
            throw;
        }
    }

    private static void InitOpenTelemetry(ReportSettings reportSettings)
    {
        lock (Lock)
        {
            if (_tracerProvider != null) return;

            var endpoint = new Uri(reportSettings.OpenTelemetryUrl!);
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
            _logger = _loggerFactory.CreateLogger<OtelTestRunner>();
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