using Ada.Data;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ada.Services.Ai;

/// <summary>OpenAI uyumlu /chat/completions (OpenAI, OpenRouter, Groq, Ollama, LM Studio…).</summary>
public sealed class OpenAiProtocol : IChatProtocol
{
    public (string, (string, string)[], JsonObject) Build(ProviderInfo p, AiProfile prof, List<ChatMsg> msgs, IReadOnlyList<ToolDef> tools, string sys)
    {
        var baseUrl = (prof.BaseUrl.Length > 0 ? prof.BaseUrl : p.DefaultBase).TrimEnd('/');
        var arr = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = sys } };
        foreach (var m in msgs) arr.Add(m.Node.DeepClone());
        var body = new JsonObject
        {
            ["model"] = prof.Model.Length > 0 ? prof.Model : p.DefaultModel,
            ["messages"] = arr,
            ["temperature"] = 0.3,
        };
        if (tools.Count > 0)
        {
            var ta = new JsonArray();
            foreach (var t in tools)
                ta.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["parameters"] = t.Schema.DeepClone() } });
            body["tools"] = ta;
        }
        var headers = new[] { ("Authorization", "Bearer " + prof.ApiKey) };
        return (baseUrl + "/chat/completions", headers, body);
    }

    public AssistantTurn Parse(JsonNode resp, List<ChatMsg> msgs)
    {
        var msg = resp["choices"]?[0]?["message"];
        if (msg == null) throw new Exception(resp["error"]?["message"]?.GetValue<string>() ?? "Boş yanıt");
        msgs.Add(new ChatMsg { Role = "assistant", Node = msg.DeepClone() });
        var text = msg["content"]?.GetValue<string>() ?? "";
        var calls = new List<ToolCall>();
        if (msg["tool_calls"] is JsonArray tc)
            foreach (var c in tc)
            {
                var fn = c!["function"]!;
                var args = ParseArgs(fn["arguments"]?.GetValue<string>());
                calls.Add(new ToolCall(c["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"), fn["name"]!.GetValue<string>(), args));
            }
        return new AssistantTurn(text, calls);
    }

    public void AddToolResults(List<ChatMsg> msgs, List<ToolResult> results)
    {
        foreach (var r in results)
            msgs.Add(new ChatMsg { Role = "tool", Node = new JsonObject { ["role"] = "tool", ["tool_call_id"] = r.Id, ["content"] = r.Content } });
    }

    public void AttachImage(List<ChatMsg> msgs, string caption, byte[] png)
    {
        var dataUrl = "data:image/png;base64," + Convert.ToBase64String(png);
        msgs.Add(new ChatMsg { Role = "user", Node = new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = caption },
                new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = dataUrl } },
            },
        } });
    }

    static JsonObject ParseArgs(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new JsonObject();
        try { return JsonNode.Parse(s) as JsonObject ?? new JsonObject(); } catch { return new JsonObject(); }
    }
}

/// <summary>Google Gemini generateContent.</summary>
public sealed class GeminiProtocol : IChatProtocol
{
    public (string, (string, string)[], JsonObject) Build(ProviderInfo p, AiProfile prof, List<ChatMsg> msgs, IReadOnlyList<ToolDef> tools, string sys)
    {
        var baseUrl = (prof.BaseUrl.Length > 0 ? prof.BaseUrl : p.DefaultBase).TrimEnd('/');
        var model = prof.Model.Length > 0 ? prof.Model : p.DefaultModel;
        var contents = new JsonArray();
        foreach (var m in msgs) contents.Add(m.Node.DeepClone());
        var body = new JsonObject
        {
            ["system_instruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = sys } } },
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject { ["temperature"] = 0.3 },
        };
        if (tools.Count > 0)
        {
            var decls = new JsonArray();
            foreach (var t in tools)
            {
                var fn = new JsonObject { ["name"] = t.Name, ["description"] = t.Description };
                if (((JsonObject)t.Schema)["properties"] is JsonObject props && props.Count > 0) fn["parameters"] = t.Schema.DeepClone();
                decls.Add(fn);
            }
            body["tools"] = new JsonArray { new JsonObject { ["functionDeclarations"] = decls } };
        }
        var url = $"{baseUrl}/v1beta/models/{model}:generateContent?key={Uri.EscapeDataString(prof.ApiKey)}";
        return (url, Array.Empty<(string, string)>(), body);
    }

    public AssistantTurn Parse(JsonNode resp, List<ChatMsg> msgs)
    {
        var cand = resp["candidates"]?[0]?["content"];
        if (cand == null) throw new Exception(resp["error"]?["message"]?.GetValue<string>() ?? "Boş yanıt");
        msgs.Add(new ChatMsg { Role = "model", Node = cand.DeepClone() });
        var text = ""; var calls = new List<ToolCall>();
        if (cand["parts"] is JsonArray parts)
            foreach (var part in parts)
            {
                if (part!["text"] is JsonNode tn) text += tn.GetValue<string>();
                if (part["functionCall"] is JsonObject fc)
                    calls.Add(new ToolCall(Guid.NewGuid().ToString("N"), fc["name"]!.GetValue<string>(), fc["args"] as JsonObject ?? new JsonObject()));
            }
        return new AssistantTurn(text, calls);
    }

    public void AddToolResults(List<ChatMsg> msgs, List<ToolResult> results)
    {
        var parts = new JsonArray();
        foreach (var r in results)
            parts.Add(new JsonObject { ["functionResponse"] = new JsonObject { ["name"] = r.Name, ["response"] = new JsonObject { ["result"] = r.Content } } });
        msgs.Add(new ChatMsg { Role = "user", Node = new JsonObject { ["role"] = "user", ["parts"] = parts } });
    }

    public void AttachImage(List<ChatMsg> msgs, string caption, byte[] png)
    {
        // Gemini'de ardışık user içerik sorun; son user içeriğine (varsa) part olarak ekle
        var imgPart = new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = "image/png", ["data"] = Convert.ToBase64String(png) } };
        if (msgs.Count > 0 && msgs[^1].Node is JsonObject last && last["role"]?.GetValue<string>() == "user" && last["parts"] is JsonArray pa)
        {
            pa.Add(new JsonObject { ["text"] = caption });
            pa.Add(imgPart);
            return;
        }
        msgs.Add(new ChatMsg { Role = "user", Node = new JsonObject { ["role"] = "user", ["parts"] = new JsonArray { new JsonObject { ["text"] = caption }, imgPart } } });
    }
}

/// <summary>Anthropic Messages API.</summary>
public sealed class AnthropicProtocol : IChatProtocol
{
    public (string, (string, string)[], JsonObject) Build(ProviderInfo p, AiProfile prof, List<ChatMsg> msgs, IReadOnlyList<ToolDef> tools, string sys)
    {
        var baseUrl = (prof.BaseUrl.Length > 0 ? prof.BaseUrl : p.DefaultBase).TrimEnd('/');
        var arr = new JsonArray();
        foreach (var m in msgs) arr.Add(m.Node.DeepClone());
        var body = new JsonObject
        {
            ["model"] = prof.Model.Length > 0 ? prof.Model : p.DefaultModel,
            ["max_tokens"] = 4096,
            ["system"] = sys,
            ["messages"] = arr,
        };
        if (tools.Count > 0)
        {
            var ta = new JsonArray();
            foreach (var t in tools) ta.Add(new JsonObject { ["name"] = t.Name, ["description"] = t.Description, ["input_schema"] = t.Schema.DeepClone() });
            body["tools"] = ta;
        }
        var headers = new[] { ("x-api-key", prof.ApiKey), ("anthropic-version", "2023-06-01") };
        return (baseUrl + "/v1/messages", headers, body);
    }

    public AssistantTurn Parse(JsonNode resp, List<ChatMsg> msgs)
    {
        if (resp["content"] is not JsonArray content) throw new Exception(resp["error"]?["message"]?.GetValue<string>() ?? "Boş yanıt");
        msgs.Add(new ChatMsg { Role = "assistant", Node = new JsonObject { ["role"] = "assistant", ["content"] = content.DeepClone() } });
        var text = ""; var calls = new List<ToolCall>();
        foreach (var b in content)
        {
            var type = b!["type"]?.GetValue<string>();
            if (type == "text") text += b["text"]!.GetValue<string>();
            else if (type == "tool_use")
                calls.Add(new ToolCall(b["id"]!.GetValue<string>(), b["name"]!.GetValue<string>(), b["input"] as JsonObject ?? new JsonObject()));
        }
        return new AssistantTurn(text, calls);
    }

    public void AddToolResults(List<ChatMsg> msgs, List<ToolResult> results)
    {
        var content = new JsonArray();
        foreach (var r in results)
            content.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = r.Id, ["content"] = r.Content });
        msgs.Add(new ChatMsg { Role = "user", Node = new JsonObject { ["role"] = "user", ["content"] = content } });
    }

    public void AttachImage(List<ChatMsg> msgs, string caption, byte[] png)
    {
        var img = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = caption },
            new JsonObject { ["type"] = "image", ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = "image/png", ["data"] = Convert.ToBase64String(png) } },
        };
        // Anthropic ardışık user mesajını kabul etmez → son user mesajının içeriğine ekle
        if (msgs.Count > 0 && msgs[^1].Node is JsonObject last && last["role"]?.GetValue<string>() == "user" && last["content"] is JsonArray ca)
        {
            foreach (var b in img) ca.Add(b!.DeepClone());
            return;
        }
        msgs.Add(new ChatMsg { Role = "user", Node = new JsonObject { ["role"] = "user", ["content"] = img } });
    }
}

public static class Protocol
{
    public static IChatProtocol For(string kind) => kind switch
    {
        "gemini" => new GeminiProtocol(),
        "anthropic" => new AnthropicProtocol(),
        _ => new OpenAiProtocol(),
    };

    /// <summary>Kullanıcı metnini sağlayıcının beklediği biçimde ekle.</summary>
    public static ChatMsg UserMessage(string kind, string text) => kind switch
    {
        "gemini" => new ChatMsg { Role = "user", Node = new JsonObject { ["role"] = "user", ["parts"] = new JsonArray { new JsonObject { ["text"] = text } } } },
        "anthropic" => new ChatMsg { Role = "user", Node = new JsonObject { ["role"] = "user", ["content"] = text } },
        _ => new ChatMsg { Role = "user", Node = new JsonObject { ["role"] = "user", ["content"] = text } },
    };
}
