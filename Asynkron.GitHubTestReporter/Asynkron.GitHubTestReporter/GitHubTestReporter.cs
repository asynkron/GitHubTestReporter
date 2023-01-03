using System.Text;

namespace Asynkron.GitHubTestReporter;

public static class GitHubTestReporter
{
    public static async Task WriteReportFile(IList<TestResult> testResults, string reportName, ReportSettings reportSettings)
    {
        var stringBuilder = new StringBuilder();
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
            stringBuilder.AppendLine($@"
<h2>{reportName}</h2>

<table>
<tr>
<th>
Test
</th>
<th>
Duration
</th>
</tr>");

            foreach (var res in testResults)
                try
                {
                    stringBuilder.AppendLine($@"
<tr>
<td>
{(res.Exception != null ? failIcon : successIcon)}
<a href=""{reportSettings.TraceViewUrl}/logs?traceId={res.TraceId}"">{res.Name}</a>
</td>
<td>
{res.Duration}
</td>
<td>
   <img height=""50"" src=""{reportSettings.TraceViewUrl}/api/limit/spanmap/{res.TraceId}/svg"" />
</td>
</tr>");
                    if (res.Exception is not null)
                        stringBuilder.AppendLine($@"
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

            stringBuilder.AppendLine("</table>");

            await File.AppendAllTextAsync(f, stringBuilder.ToString());
        }
    }
}