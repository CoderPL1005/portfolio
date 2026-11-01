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
                "technologyStack"=>["technologies"],"experienceRequirements"=>["requirements","sharedRequirements"],
                "applicationUrl"=>["applicationUrl"],"benefits"=>["sharedBenefits"],
                "mentionedPublishedAt"=>["publishedAt"],_=>[key]};
            var spans=Array(submitted,"evidence");
            if(spans.Length is <1 or >40||!submitted.TryGetProperty("value",out var proposed))return false;
            // Bind claims to the original model vacancy, not any matching field
            // elsewhere in the generation. Root fields remain post-level only.
            foreach(var vacancy in Array(root,"vacancies")){
                if(!vacancy.TryGetProperty("title",out var title)||Text(title,"status") is not("POST_MENTIONED" or "VERIFIED")||Text(title,"value") is not {} modelTitle||
                    !string.Equals(modelTitle,submittedTitle,StringComparison.OrdinalIgnoreCase))continue;
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
                if(!heading&&!inline||Regex.IsMatch(line,@"\b(?:not|never|no|without|previously|former|closed|paused|example|candidate)\b|không|chưa|chẳng|đóng|ngừng|từng|đã",RegexOptions.IgnoreCase))continue;
                var scopeStart=inline?lineStart+line.IndexOf(modelTitle,StringComparison.OrdinalIgnoreCase):lineStart;
                var end=headings.Where(n=>n>lineStart).DefaultIfEmpty(raw.Length).Min();
                foreach(var scope in new[]{vacancy,root})foreach(var name in names){
                    if(scope.Equals(root)&&name is not("employer" or "sharedLocation" or "sharedRequirements" or "sharedBenefits" or "applicationUrl" or "publishedAt" or "recruitmentStatus"))continue;
                    if(!scope.TryGetProperty(name,out var fact)||Text(fact,"status") is not("POST_MENTIONED" or "VERIFIED")||
                        !fact.TryGetProperty("value",out var original)||original.ValueKind==JsonValueKind.Null||Canonical(original)!=Canonical(proposed))continue;
                    var originalSpans=Array(fact,"evidence");
                    if(originalSpans.Length!=spans.Length||!originalSpans.Zip(spans).All(p=>VerifiedSpan(p.First,p.Second,raw)))continue;
                    var postLevel=scope.Equals(root);
                    var sharedStart=Regex.Matches(raw,@"(?im)^(?:yêu cầu chung|shared requirements|quyền lợi chung|shared benefits|địa chỉ chung|ứng tuyển chung)[^\n]*")
                        .Select(m=>m.Index).Where(n=>n>headings.Last()).DefaultIfEmpty(raw.Length).First();
                    if(!postLevel&&name!="title"&&spans.Any(s=>!(name=="seniority"&&s.GetProperty("end").GetInt32()<=headings[0])&&
                        (s.GetProperty("start").GetInt32()<scopeStart||s.GetProperty("end").GetInt32()>Math.Min(end,sharedStart))))continue;
                    if(postLevel&&name is "sharedLocation" or "sharedRequirements" or "sharedBenefits" or "applicationUrl"&&headings.Length>1&&
                        spans.Any(s=>s.GetProperty("start").GetInt32()>=headings[0]&&s.GetProperty("start").GetInt32()<sharedStart))continue;
                    // V2 accepts only original literal claims. Existing STRICT
                    // compatibility conversions are never extended to corrections.
                    if(key=="applicationUrl"){
                        if(proposed.ValueKind!=JsonValueKind.String||!DiscoveryReviewRules.SafeUrl(proposed.GetString())||
                            !spans.Any(s=>(Text(s,"quote")??"").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Contains(proposed.GetString())))continue;
                    }else if(!LiteralClaim(proposed,spans))continue;
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
