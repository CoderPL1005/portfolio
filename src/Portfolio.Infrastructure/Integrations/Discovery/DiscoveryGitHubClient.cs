using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Portfolio.Application.Features.JobHunting;
namespace Portfolio.Infrastructure.Integrations.Discovery;

public sealed class DiscoveryGitHubClient(HttpClient http,IOptions<DiscoveryExecutionOptions> options):IDiscoveryGitHubClient
{
    private HttpRequestMessage Request(HttpMethod method,string path)
    {
        var s=options.Value;
        if(!Regex.IsMatch(s.Repository,"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")||s.Workflow!="job-discovery-ingestion.yml"||string.IsNullOrWhiteSpace(s.GitHubToken))
            throw new InvalidOperationException("DISCOVERY_GITHUB_CONFIGURATION_UNAVAILABLE");
        var r=new HttpRequestMessage(method,$"https://api.github.com/repos/{s.Repository}/{path}");
        r.Headers.Authorization=new AuthenticationHeaderValue("Bearer",s.GitHubToken);r.Headers.UserAgent.ParseAdd("Portfolio-Discovery/1.0");
        r.Headers.Accept.ParseAdd("application/vnd.github+json");r.Headers.Add("X-GitHub-Api-Version","2022-11-28");return r;
    }
    public async Task<string> DispatchAsync(Guid id,CancellationToken ct)
    {
        using var request=Request(HttpMethod.Post,$"actions/workflows/{options.Value.Workflow}/dispatches");
        request.Content=JsonContent.Create(new{@ref="main",inputs=new{execution_id=id.ToString()}});
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        return response.StatusCode==HttpStatusCode.NoContent?"ACCEPTED":(int)response.StatusCode is >=400 and <500?"REJECTED":"UNKNOWN";
    }
    public async Task<bool> VerifyRunAsync(Guid id,string runId,CancellationToken ct)
    {
        if(!Regex.IsMatch(runId,"^[1-9][0-9]{0,19}$"))return false;
        try{
            using var request=Request(HttpMethod.Get,$"actions/runs/{runId}");using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            if(response.StatusCode!=HttpStatusCode.OK||response.Content.Headers.ContentLength>65536)return false;
            await using var stream=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();var chunk=new byte[4096];int n;
            while((n=await stream.ReadAsync(chunk,ct))>0){if(buffer.Length+n>65536)return false;buffer.Write(chunk,0,n);}
            using var json=JsonDocument.Parse(buffer.ToArray());var r=json.RootElement;
            return r.GetProperty("id").GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)==runId&&r.GetProperty("run_attempt").GetInt32()==1&&
                r.GetProperty("head_branch").GetString()=="main"&&r.GetProperty("event").GetString()=="workflow_dispatch"&&r.GetProperty("path").GetString()==".github/workflows/"+options.Value.Workflow&&
                r.GetProperty("display_title").GetString()=="Job Discovery "+id&&r.GetProperty("head_repository").GetProperty("full_name").GetString()==options.Value.Repository&&r.GetProperty("status").GetString()=="in_progress";
        }catch{return false;}
    }
}
