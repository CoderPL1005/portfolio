using System.Text.Json;
using System.Text.Json.Nodes;

namespace Portfolio.Infrastructure.AI;

// Provider guidance only. The canonical contract and strict local JS shape /
// evidence validation remain authoritative, including every omitted bound.
internal static class GeminiDiscoverySchema
{
    public static JsonElement Project(JsonElement schema)=>JsonSerializer.SerializeToElement(Node(schema,0));

    private static JsonObject Node(JsonElement schema,int depth)
    {
        if(depth>32||schema.ValueKind!=JsonValueKind.Object)throw Invalid();
        var result=new JsonObject();
        foreach(var p in schema.EnumerateObject()){
            switch(p.Name){
                case "type": case "required": case "additionalProperties":
                    result[p.Name]=JsonNode.Parse(p.Value.GetRawText());break;
                case "const":
                    if(p.Value.ValueKind!=JsonValueKind.String)throw Invalid();
                    result["type"]="string";result["enum"]=new JsonArray(p.Value.GetString());break;
                case "enum":
                    var kinds=p.Value.EnumerateArray().Select(x=>x.ValueKind switch {
                        JsonValueKind.String=>"string",JsonValueKind.Number=>"number",JsonValueKind.Null=>"null",_=>throw Invalid()
                    }).Distinct().ToArray();
                    if(kinds.Length==0)throw Invalid();
                    result["type"]=Types(kinds);result["enum"]=JsonNode.Parse(p.Value.GetRawText());break;
                case "properties":
                    var properties=new JsonObject();foreach(var child in p.Value.EnumerateObject())properties[child.Name]=Node(child.Value,depth+1);
                    result["properties"]=properties;break;
                case "items": result["items"]=Node(p.Value,depth+1);break;
                case "anyOf":
                    var branches=new List<JsonObject>();
                    foreach(var child in p.Value.EnumerateArray()){
                        var projected=Node(child,depth+1);
                        if(projected.Count==1&&projected["anyOf"] is JsonArray nested)
                            branches.AddRange(nested.Select(x=>(JsonObject)x!.DeepClone()));
                        else branches.Add(projected);
                    }
                    if(branches.Count==0)throw Invalid();
                    // Scalar nullable facts need no branching grammar. Ranges
                    // retain their number/object/null alternatives, flattened.
                    if(branches.All(b=>b.Count==1&&b["type"] is not null)){
                        var types=branches.SelectMany(b=>b["type"] is JsonArray a?a.Select(x=>x!.GetValue<string>()):new[]{b["type"]!.GetValue<string>()}).Distinct().ToArray();
                        result["type"]=Types(types);
                    }else result["anyOf"]=new JsonArray(branches.Select(x=>(JsonNode)x).ToArray());
                    break;
                // These constraints remain in the local schema/validator. Large
                // nested item limits can explode the provider decoding grammar.
                case "minLength": case "maxLength": case "minItems": case "maxItems": case "minimum": case "maximum": break;
                default: throw Invalid(); // Contract drift requires explicit review.
            }
        }
        if(!result.ContainsKey("type")&&!result.ContainsKey("anyOf"))throw Invalid();
        return result;
    }
    private static JsonNode Types(string[] types)=>types.Length==1?JsonValue.Create(types[0])!:new JsonArray(types.Select(t=>(JsonNode)JsonValue.Create(t)!).ToArray());
    private static InvalidOperationException Invalid()=>new("DISCOVERY_SEMANTIC_SCHEMA_UNSUPPORTED");
}
