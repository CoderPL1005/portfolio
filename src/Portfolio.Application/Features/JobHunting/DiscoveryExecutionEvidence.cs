using System.Text.Json;
using System.Text.RegularExpressions;

namespace Portfolio.Application.Features.JobHunting;

public sealed partial class DiscoveryExecutionService
{
    // The execution snapshot is server-created. Absence means historical STRICT;
    // changing live options never reinterprets an existing observation.
    private static string ExecutionEvidencePolicy(string configuration)
    {
        using var doc=JsonDocument.Parse(configuration);
        var policy=Text(doc.RootElement,"evidencePolicy")??"STRICT";
        if(policy is not("STRICT" or "V2"))throw Conflict("DISCOVERY_EVIDENCE_POLICY_INVALID");
        return policy;
    }

    private static bool Utf16Boundary(string raw,int n)=>n>=0&&n<=raw.Length&&
        !(n>0&&n<raw.Length&&char.IsHighSurrogate(raw[n-1])&&char.IsLowSurrogate(raw[n]));

    private static bool VerifiedSpan(JsonElement original,JsonElement submitted,string raw)
    {
        var quote=Text(original,"quote");
        if(string.IsNullOrEmpty(quote)||quote!=Text(submitted,"quote")||
            !submitted.TryGetProperty("start",out var s)||!s.TryGetInt32(out var start)||
            !submitted.TryGetProperty("end",out var e)||!e.TryGetInt32(out var end)||
            end<=start||!Utf16Boundary(raw,start)||!Utf16Boundary(raw,end)||raw[start..end]!=quote)return false;
        if(Canonical(original)==Canonical(submitted))return true;
        // Ordinal search counts overlapping matches. No normalization, fuzzy
        // search, byte indexing, or worker-supplied correction attestation.
        var occurrence=raw.IndexOf(quote,StringComparison.Ordinal);
        return occurrence==start&&end==start+quote.Length&&raw.IndexOf(quote,start+1,StringComparison.Ordinal)<0;
    }

    private static bool LiteralClaim(JsonElement claim,JsonElement[] spans)
    {
        bool Literal(string? value)=>!string.IsNullOrWhiteSpace(value)&&spans.Any(s=>
            Regex.IsMatch(Regex.Replace(Text(s,"quote")??"",@"https?://\S+|[\p{L}\p{N}._+%\-]+@[\p{L}\p{N}.\-]+\.[a-z]{2,}","",RegexOptions.IgnoreCase),
                @"(?<![\p{L}\p{N}])"+Regex.Escape(value)+@"(?![\p{L}\p{N}])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant));
        return claim.ValueKind==JsonValueKind.String?Literal(claim.GetString()):claim.ValueKind==JsonValueKind.Array&&
            claim.GetArrayLength()>0&&claim.EnumerateArray().All(v=>v.ValueKind==JsonValueKind.String&&Literal(v.GetString()));
    }

    private static bool SkillClaimSupported(string key,JsonElement claim,JsonElement[] spans,string raw)
    {
        if(claim.ValueKind!=JsonValueKind.Array)return false;
        // The same narrow direct-list/suffix obligation grammar as the Node
        // evidence boundary. Quote proximity alone never proves an obligation.
        var clauses=Regex.Matches(raw,@"[^;\r\n!?]+?(?:\.\s+(?=\S)|$|(?=[;\r\n!?]))",RegexOptions.Multiline);
        return claim.EnumerateArray().All(skill=>{
            if(skill.ValueKind!=JsonValueKind.String)return false;
            var name=skill.GetString()!;var pattern=@"(?<![\p{L}\p{N}])"+Regex.Escape(name)+@"(?![\p{L}\p{N}])";
            var relevant=clauses.Where(c=>spans.Any(e=>Regex.Matches(Text(e,"quote")??"",pattern,RegexOptions.IgnoreCase).Any(m=>
                e.GetProperty("start").GetInt32()+m.Index>=c.Index&&e.GetProperty("start").GetInt32()+m.Index+m.Length<=c.Index+c.Length))).ToArray();
            if(relevant.Length==0||relevant.Any(c=>Regex.IsMatch(c.Value,@"\b(?:not|never|no|without|\w+n['’]t)\b|không|chưa|chẳng",RegexOptions.IgnoreCase)||
                c.Index+c.Length<raw.Length&&raw[c.Index+c.Length]=='?'))return false;
            return relevant.Any(c=>{
                var text=c.Value.Trim();
                if(key=="mandatorySkills"&&Regex.IsMatch(text,@"optional|preferred|nice.to.have|ưu tiên|lợi thế",RegexOptions.IgnoreCase))return false;
                var prefix=Regex.Match(text,key=="mandatorySkills"?@"^(?:requirements?|required|mandatory|must(?:\s+(?:have|know))?|bắt buộc|yêu cầu)(?:\s+skills?)?\s*:?\s+":@"^(?:optional|preferred|nice.to.have|ưu tiên|lợi thế)\s*:?\s+",RegexOptions.IgnoreCase);
                if(prefix.Success){
                    var list=text[prefix.Length..].TrimEnd('.').Trim();
                    if(Regex.IsMatch(list,@"\b(?:or|but|however|although|we|they|use|uses|used|also|may|might|can|could|should|would)\b|hoặc|nhưng",RegexOptions.IgnoreCase)||
                        !Regex.IsMatch(list,@"^(?:[\p{L}\p{N}#+.\/\-]+(?:[ \t]+[\p{L}\p{N}#+.\/\-]+){0,2})(?:[ \t]*(?:,|&|\band\b|và)[ \t]*[\p{L}\p{N}#+.\/\-]+(?:[ \t]+[\p{L}\p{N}#+.\/\-]+){0,2})*$",RegexOptions.IgnoreCase))return false;
                    return Regex.Split(list,@"\s*(?:,|&|\band\b|\bvà\b)\s*",RegexOptions.IgnoreCase).Any(entry=>string.Equals(entry.Trim(),name,StringComparison.OrdinalIgnoreCase));
                }
                return Regex.Matches(text,pattern,RegexOptions.IgnoreCase).Any(m=>Regex.IsMatch(text[(m.Index+m.Length)..],key=="mandatorySkills"?
                    @"^\s+(?:(?:is|are)\s+)?(?:required|mandatory|bắt buộc)\b":@"^\s+(?:(?:is|are)\s+)?(?:optional|preferred|nice.to.have|là lợi thế|được ưu tiên)\b",RegexOptions.IgnoreCase));
            });
        });
    }

    private static bool SourceRoleBoundary(string line)
    {
        var text=Regex.Replace(line.Trim(),@"^(?:[-*#]+\s*|\d+[.)]\s*)","");
        var colon=text.IndexOf(':');var title=(colon<0?text:text[..colon]).Trim();
        var role=@"back[- ]?end|front[- ]?end|full[- ]?stack|mobile|\b(?:qa|pqa|tester|data|devops|security)\b|phía máy chủ|di động|kiểm thử|dữ liệu|bảo mật|other|khác";
        var profession=@"\b(?:developer|engineer|designer|tester|analyst)\b|lập trình viên|kỹ sư";
        var eligible=title.Length is >0 and <=120&&!Regex.IsMatch(title,@"[;!?]|^(?:requirements?|required|must|optional|preferred|yêu cầu|bắt buộc|ưu tiên)",RegexOptions.IgnoreCase);
        if(eligible&&(colon>=0?Regex.IsMatch(title,role+"|"+profession,RegexOptions.IgnoreCase):
            Regex.IsMatch(title,@"^(?:(?:intern|fresher|junior|middle|senior|lead)\s+)?(?:front[- ]?end|back[- ]?end|full[- ]?stack|mobile|qa|pqa|devops|data|security)(?:\s+(?:developer|engineer))?$|^(?:[\p{L}\p{N}#+.\/\-]+\s+){0,4}(?:developer|engineer|designer|tester|analyst)$",RegexOptions.IgnoreCase)))return true;
        var prefix=Regex.Match(line,@"^[\p{Lu}\p{N}][\p{L}\p{N} .&'\-]{0,79}?[ \t]+(?:đang[ \t]+)?(?:tuyển dụng|tuyển|is hiring|hiring)[ \t]+");
        return prefix.Success&&!Regex.IsMatch(line,@"^(?:ứng|liên hệ|cách|hồ sơ|yêu cầu|quyền lợi|apply|contact|application)(?:[ \t]|$)",RegexOptions.IgnoreCase)&&
            !Regex.IsMatch(prefix.Value,@"(?:^|\s)(?:nếu|bạn|ví|dụ|example|candidate)(?:\s|$)",RegexOptions.IgnoreCase);
    }

    private static bool SchemaQuoteSupported(string name,JsonElement original,string? quote)
    {
        if(string.IsNullOrWhiteSpace(quote))return false;
        var clean=Regex.Replace(quote,@"https?://\S+|[\p{L}\p{N}._+%\-]+@[\p{L}\p{N}.\-]+\.[a-z]{2,}","",RegexOptions.IgnoreCase).Trim();
        var negated=Regex.IsMatch(clean,@"\b(?:not|never|no|without|\w+n['’]t)\b|không|chưa|chẳng",RegexOptions.IgnoreCase);
        if(name=="recruitmentStatus"&&original.ValueKind==JsonValueKind.String){
            var closed=Regex.IsMatch(clean,@"đóng tuyển|ngừng tuyển|tạm dừng tuyển|\b(?:closed|paused|no longer (?:accepting|hiring|recruiting)|not hiring|stopped hiring)\b|không\s+(?:còn\s+)?tuyển|chưa\s+tuyển",RegexOptions.IgnoreCase);
            return original.GetString() switch{
                "CLOSED"=>closed,
                "ANNOUNCED_UNVERIFIED"=>!negated&&!closed&&!Regex.IsMatch(clean,@"\b(?:previously|former|candidate|looking for a job)\b|từng|đã từng|tìm việc",RegexOptions.IgnoreCase)&&
                    Regex.IsMatch(clean,@"\b(?:tuyển dụng|tuyển|hiring|recruiting|vacancy|vacancies)\b",RegexOptions.IgnoreCase),
                _=>false}; // Never accept VERIFIED_HIRING or ungrounded status codes.
        }
        if(name=="seniority"&&original.ValueKind==JsonValueKind.String){
            if(negated)return false;
            var matched=new[]{
                ("INTERN",Regex.IsMatch(clean,@"\bintern(?:ship)?\b|thực tập(?:\s+sinh)?",RegexOptions.IgnoreCase)),
                ("FRESHER",Regex.IsMatch(clean,@"\bfresher\b",RegexOptions.IgnoreCase)),
                ("JUNIOR",Regex.IsMatch(clean,@"\bjunior\b",RegexOptions.IgnoreCase)),
                ("MIDDLE",Regex.IsMatch(clean,@"\b(?:middle|mid[- ]level)\b",RegexOptions.IgnoreCase)),
                ("SENIOR",Regex.IsMatch(clean,@"\bsenior\b",RegexOptions.IgnoreCase)),
                ("LEAD",Regex.IsMatch(clean,@"\blead\b",RegexOptions.IgnoreCase))
            }.Where(x=>x.Item2).Select(x=>x.Item1).ToArray();
            return matched.Length==1&&matched[0]==original.GetString();
        }
        if(name=="experienceYears"&&(original.ValueKind==JsonValueKind.Number||original.ValueKind==JsonValueKind.Object)){
            if(Regex.IsMatch(clean,@"nice to have|preferred|optional|là lợi thế|ưu tiên",RegexOptions.IgnoreCase))return false;
            decimal? min=null,max=null;var count=0;
            if(Regex.IsMatch(clean,@"^(?:experience\s*:\s*|kinh nghiệm\s*:\s*)?(?:no experience required|không yêu cầu kinh nghiệm)\.?$",RegexOptions.IgnoreCase)){min=0m;max=null;count++;}
            var range=Regex.Matches(clean,@"\b(\d+(?:\.\d+)?)\s*(?:–|—|-|to|đến)\s*(\d+(?:\.\d+)?)\s*(years?|năm|months?|tháng)\b",RegexOptions.IgnoreCase);
            foreach(Match m in range){
                var factor=Regex.IsMatch(m.Groups[3].Value,@"month|tháng",RegexOptions.IgnoreCase)?12m:1m;
                min=decimal.Parse(m.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)/factor;
                max=decimal.Parse(m.Groups[2].Value,System.Globalization.CultureInfo.InvariantCulture)/factor;
                count++;
            }
            if(range.Count==0){
                var single=Regex.Matches(clean,@"(?:(up to|tối đa|at least|minimum|tối thiểu|từ)\s*)?\b(\d+(?:\.\d+)?)\s*(\+)?\s*(years?|năm|months?|tháng)\b",RegexOptions.IgnoreCase);
                foreach(Match m in single){
                    if(negated)return false;
                    var factor=Regex.IsMatch(m.Groups[4].Value,@"month|tháng",RegexOptions.IgnoreCase)?12m:1m;
                    var amount=decimal.Parse(m.Groups[2].Value,System.Globalization.CultureInfo.InvariantCulture)/factor;
                    var upper=Regex.IsMatch(m.Groups[1].Value,@"up to|tối đa",RegexOptions.IgnoreCase);
                    min=upper?null:amount;max=upper?amount:null;
                    count++;
                }
            }
            if(count!=1||(min.HasValue&&max.HasValue&&min.Value>max.Value))return false;
            if(original.ValueKind==JsonValueKind.Number)return original.TryGetDecimal(out var n)&&min.HasValue&&!max.HasValue&&Math.Abs(n-min.Value)<0.0001m;
            decimal? Bound(string prop)=>original.TryGetProperty(prop,out var b)&&b.ValueKind==JsonValueKind.Number&&b.TryGetDecimal(out var d)?d:b.ValueKind==JsonValueKind.Null?null:decimal.MinValue;
            var oMin=Bound("minimum");var oMax=Bound("maximum");
            if(oMin==decimal.MinValue||oMax==decimal.MinValue)return false;
            return (oMin.HasValue==min.HasValue&&(!oMin.HasValue||Math.Abs(oMin.Value-min!.Value)<0.0001m))&&
                (oMax.HasValue==max.HasValue&&(!oMax.HasValue||Math.Abs(oMax.Value-max!.Value)<0.0001m));
        }
        return false;
    }

    private static bool GeneratedFactV2(string? generation,string key,JsonElement submitted,string raw,string? submittedTitle)
    {
        if(generation is null||submitted.ValueKind!=JsonValueKind.Object||submittedTitle is null)return false;
        try{
            var saved=JsonSerializer.Deserialize<DiscoveryTextGeneration>(generation,Json);
            if(saved?.Output is null||saved.Output.Length>65536)return false;
            using var doc=JsonDocument.Parse(saved.Output);var root=doc.RootElement;
            if(Text(root,"schemaVersion")!="ai-job-extraction-v1")return false;
            var names=key switch {
                "companyName"=>new[]{"employer"},"positionTitle"=>["title"],"location"=>["location","sharedLocation"],
                "technologyStack"=>["technologies"],"experienceRequirements"=>["requirements","sharedRequirements","experienceYears"],
                "applicationUrl"=>["applicationUrl"],"benefits"=>["sharedBenefits"],"experience"=>["experienceYears"],
                "mentionedPublishedAt"=>["publishedAt"],_=>[key]};
            var spans=Array(submitted,"evidence");
            if(spans.Length is <1 or >40||!submitted.TryGetProperty("value",out var proposed))return false;
            var modelVacancies=Array(root,"vacancies");
            // Bind claims to the original model vacancy, not any matching field
            // elsewhere in the generation. Root fields remain post-level only.
            foreach(var vacancy in modelVacancies){
                if(!vacancy.TryGetProperty("title",out var title)||Text(title,"status") is not("POST_MENTIONED" or "VERIFIED")||Text(title,"value") is not {} modelTitle)continue;
                var titleSpans=Array(title,"evidence");
                if(titleSpans.Length!=1||Text(titleSpans[0],"quote") is not {Length:>0} tq||tq.Contains('\n')||tq.Contains('\r')||
                    !Regex.IsMatch(modelTitle,@"\b(?:backend|frontend|fullstack|mobile|qa|pqa|devops|data|security|developer|engineer|designer|tester|analyst)\b",RegexOptions.IgnoreCase))continue;
                var anchor=raw.IndexOf(tq,StringComparison.Ordinal);
                if(titleSpans[0].TryGetProperty("start",out var ts)&&ts.TryGetInt32(out var ta)&&ta>=0&&ta+tq.Length<=raw.Length&&raw.Substring(ta,tq.Length)==tq)anchor=ta;
                else if(anchor>=0&&raw.IndexOf(tq,anchor+1,StringComparison.Ordinal)>=0)continue;
                if(anchor<0||!tq.Contains(modelTitle,StringComparison.OrdinalIgnoreCase))continue;
                // All source role headings cut the scope, including omitted roles.
                var headings=Regex.Matches(raw,@"[^\r\n]+").Where(m=>SourceRoleBoundary(m.Value)).Select(m=>m.Index).ToArray();
                var lineStart=raw.LastIndexOf('\n',Math.Max(0,anchor-1))+1;
                if(!headings.Contains(lineStart))continue;
                var lineEnd=raw.IndexOf('\n',lineStart);if(lineEnd<0)lineEnd=raw.Length;
                var line=raw[lineStart..lineEnd].TrimEnd('\r');
                var heading=Regex.IsMatch(line,@"^\s*(?:[-*#]+\s*|\d+[.)]\s*)?"+Regex.Escape(modelTitle)+@"\s*(?::[^\r\n]*)?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
                var inline=Regex.IsMatch(line,@"^[\p{Lu}\p{N}][\p{L}\p{N} .&'\-]{0,79}?\s+(?:đang\s+)?(?:tuyển dụng|tuyển|is hiring|hiring)\s+"+Regex.Escape(modelTitle)+@"\s*(?:\([A-Za-z0-9#.+ /\-]{1,40}\))?(?:\s+(?:tại|in)\s+[\p{L}\p{N}][\p{L}\p{N} ,'\-]{0,79})?[.!]?\s*$",RegexOptions.CultureInvariant);
                if(!string.Equals(modelTitle,submittedTitle,StringComparison.OrdinalIgnoreCase)&&
                    !(modelVacancies.Length==1&&inline&&string.Equals(line.Trim(),submittedTitle.Trim(),StringComparison.OrdinalIgnoreCase)))continue;
                if(!heading&&!inline||Regex.IsMatch(line,@"\b(?:not|never|no|without|previously|former|closed|paused|example|candidate)\b|không|chưa|chẳng|đóng|ngừng|từng|đã",RegexOptions.IgnoreCase))continue;
                var scopeStart=inline?lineStart+line.IndexOf(modelTitle,StringComparison.OrdinalIgnoreCase):lineStart;
                var end=headings.Where(n=>n>lineStart).DefaultIfEmpty(raw.Length).Min();
                foreach(var scope in new[]{vacancy,root})foreach(var name in names){
                    if(scope.Equals(root)&&name is not("employer" or "sharedLocation" or "sharedRequirements" or "sharedBenefits" or "applicationUrl" or "publishedAt" or "recruitmentStatus"))continue;
                    if(!scope.TryGetProperty(name,out var fact)||Text(fact,"status") is not("POST_MENTIONED" or "VERIFIED")||
                        !fact.TryGetProperty("value",out var original)||original.ValueKind==JsonValueKind.Null)continue;
                    var originalSpans=Array(fact,"evidence");
                    var exactValue=Canonical(original)==Canonical(proposed);
                    var quoteValue=key is "experience" or "experienceRequirements" or "seniority" or "recruitmentStatus"&&
                        name is "experienceYears" or "seniority" or "recruitmentStatus"&&
                        proposed.ValueKind==JsonValueKind.String&&originalSpans.Length>0&&
                        Text(originalSpans[0],"quote")==proposed.GetString()&&SchemaQuoteSupported(name,original,proposed.GetString());
                    if(!exactValue&&!quoteValue)continue;
                    if(originalSpans.Length!=spans.Length||!originalSpans.Zip(spans).All(p=>VerifiedSpan(p.First,p.Second,raw)))continue;
                    var postLevel=scope.Equals(root);
                    var sharedStart=Regex.Matches(raw,@"(?im)^(?:yêu cầu chung|shared requirements|quyền lợi chung|shared benefits|địa chỉ chung|ứng tuyển chung)[^\n]*")
                        .Select(m=>m.Index).Where(n=>n>headings.Last()).DefaultIfEmpty(raw.Length).First();
                    if(!postLevel&&name!="title"&&spans.Any(s=>!(name=="seniority"&&s.GetProperty("end").GetInt32()<=headings[0])&&
                        (s.GetProperty("start").GetInt32()<scopeStart||s.GetProperty("end").GetInt32()>Math.Min(end,sharedStart))))continue;
                    if(postLevel&&name is "sharedLocation" or "sharedRequirements" or "sharedBenefits" or "applicationUrl" or "recruitmentStatus"&&headings.Length>1&&
                        spans.Any(s=>s.GetProperty("start").GetInt32()>=headings[0]&&s.GetProperty("start").GetInt32()<sharedStart))continue;
                    if(key=="applicationUrl"){
                        if(proposed.ValueKind!=JsonValueKind.String||!DiscoveryReviewRules.SafeUrl(proposed.GetString())||
                            !spans.Any(s=>(Text(s,"quote")??"").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Contains(proposed.GetString())))continue;
                    }else if(!quoteValue&&!LiteralClaim(proposed,spans))continue;
                    if(key is "mandatorySkills" or "optionalSkills"&&!SkillClaimSupported(key,proposed,spans,raw))continue;
                    if(key=="mentionedPublishedAt"){
                        var date=proposed.GetString();
                        if(date is null||!Regex.IsMatch(date,@"^\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}:\d{2}(?:\.\d{1,3})?Z)?$")||
                            !DateTimeOffset.TryParse(date,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out _))continue;
                        var supported=Regex.Matches(raw,@"[^;\r\n!?]+").Any(c=>
                            !Regex.IsMatch(c.Value,@"\b(?:not|never|no|without|\w+n['’]t)\b|không|chưa|chẳng|deadline|clos(?:e|es|ed|ing)|due|expir|hạn",RegexOptions.IgnoreCase)&&
                            Regex.Matches(c.Value,@"(?:^|[^\p{L}\p{N}])(?:published|posted)(?:\s+(?:at|on))?\s*:?\s*|(?:^|[^\p{L}\p{N}])(?:publication date|ngày đăng|đăng ngày)\s*:?\s*",RegexOptions.IgnoreCase).Any(m=>{
                                var start=c.Index+m.Index+m.Length;var end=start+date.Length;
                                return end<=raw.Length&&raw[start..end]==date&&(end==raw.Length||!Regex.IsMatch(raw[end].ToString(),@"[\dT:.Z+\-]"))&&
                                    spans.Any(e=>e.GetProperty("start").GetInt32()<=start&&e.GetProperty("end").GetInt32()>=end);
                            }));
                        if(!supported)continue;
                    }
                    return true;
                }
            }
        }catch(Exception e) when(e is JsonException or InvalidOperationException or ArgumentException){return false;}
        return false;
    }
}
