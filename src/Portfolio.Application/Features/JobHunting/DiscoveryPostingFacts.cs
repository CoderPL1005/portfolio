using System.Globalization;
using System.Text.RegularExpressions;

namespace Portfolio.Application.Features.JobHunting;

// Independent server validation; never trust a worker scalar or quote alone.
public static class DiscoveryPostingFacts
{
    public sealed record Fact(object Value,string Quote);
    public static Dictionary<string,Fact> Extract(string raw)
    {
        var facts=new Dictionary<string,Fact>();
        var lines=raw.Split('\n').Select(x=>x.TrimEnd('\r')).ToArray();
        var options=RegexOptions.IgnoreCase|RegexOptions.CultureInvariant;
        var labelled=lines.Where(x=>Regex.IsMatch(x.Trim(),@"^(?:salary|lương)\s*:",options)).ToArray();
        if(labelled.Length==1)
        {
            var quote=labelled[0];
            var m=Regex.Match(quote.Trim(),@"^(?:salary|lương)\s*:\s*((?:[0-9]{1,3}(?:[.,][0-9]{3})+|[0-9]+))(?:\s*[-–]\s*((?:[0-9]{1,3}(?:[.,][0-9]{3})+|[0-9]+)))?\s*(triệu\s+)?(VND|USD|EUR)(?:\s*/\s*(tháng|month|năm|year|giờ|hour))?\s*$",options);
            bool Amount(string s,out decimal value)=>decimal.TryParse(s.Replace(".","").Replace(",",""),NumberStyles.None,CultureInfo.InvariantCulture,out value);
            if(m.Success&&Amount(m.Groups[1].Value,out var min)&&Amount(m.Groups[2].Success?m.Groups[2].Value:m.Groups[1].Value,out var max))
            {
                var currency=m.Groups[4].Value.ToUpperInvariant();var factor=m.Groups[3].Success?1000000m:1m;
                if(min>0&&max>=min&&max<=9007199254740991m/factor&&(!m.Groups[3].Success||currency=="VND"))
                {
                    facts["salaryMinimum"]=new(min*factor,quote);facts["salaryMaximum"]=new(max*factor,quote);facts["salaryCurrency"]=new(currency,quote);
                    var period=m.Groups[5].Value.ToLowerInvariant() switch{"tháng" or "month"=>"MONTHLY","năm" or "year"=>"YEARLY","giờ" or "hour"=>"HOURLY",_=>null};
                    if(period is not null)facts["salaryPeriod"]=new(period,quote);
                }
            }
        }
        var dates=lines.Select(line=>(Line:line,Match:Regex.Match(line.Trim(),@"^(?:deadline|hạn (?:nộp|ứng tuyển))\s*:\s*([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:Z|[+-][0-9]{2}:[0-9]{2}))\s*$",options))).Where(x=>x.Match.Success).ToArray();
        if(dates.Length==1&&DateTimeOffset.TryParseExact(dates[0].Match.Groups[1].Value,["yyyy-MM-dd'T'HH:mm:sszzz","yyyy-MM-dd'T'HH:mm:ss'Z'"],CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out _))
            facts["expiresAt"]=new(dates[0].Match.Groups[1].Value,dates[0].Line);
        return facts;
    }
}
