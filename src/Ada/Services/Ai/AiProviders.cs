using Ada.Data;
using System.Text.Json.Nodes;

namespace Ada.Services.Ai;

/// <summary>Bir yapay zeka sağlayıcısının bilgileri (ücretsiz seçenekler dahil).</summary>
public sealed record ProviderInfo(
    string Key, string Name, string Kind, string DefaultBase, string DefaultModel, string KeyUrl, string Note);

public static class Providers
{
    // Kind: "openai" (OpenAI uyumlu /chat/completions), "gemini", "anthropic"
    public static readonly ProviderInfo[] All =
    {
        new("gemini", "Google Gemini", "gemini", "https://generativelanguage.googleapis.com",
            "gemini-2.0-flash", "https://aistudio.google.com/apikey", "Ücretsiz kota var · kart gerekmez"),
        new("groq", "Groq", "openai", "https://api.groq.com/openai/v1",
            "llama-3.3-70b-versatile", "https://console.groq.com/keys", "Ücretsiz ve çok hızlı"),
        new("openrouter", "OpenRouter", "openai", "https://openrouter.ai/api/v1",
            "deepseek/deepseek-chat", "https://openrouter.ai/keys", "Birçok model · ücretsiz modeller de var"),
        new("ollama", "Ollama (yerel)", "openai", "http://localhost:11434/v1",
            "llama3.1", "https://ollama.com/download", "Tamamen ücretsiz · bilgisayarında çalışır · anahtar gerekmez"),
        new("openai", "OpenAI", "openai", "https://api.openai.com/v1",
            "gpt-4o-mini", "https://platform.openai.com/api-keys", "Ücretli"),
        new("anthropic", "Anthropic (Claude)", "anthropic", "https://api.anthropic.com",
            "claude-opus-4-8", "https://console.anthropic.com/settings/keys", "Ücretli"),
        new("custom", "Özel (OpenAI uyumlu)", "openai", "",
            "", "", "LM Studio, vLLM, kendi sunucun…"),
    };

    public static ProviderInfo Get(string key) => All.FirstOrDefault(p => p.Key == key) ?? All[0];
}

/// <summary>Sağlayıcıya özel istek gövdesi kurma ve yanıt çözme. JsonNode ile hafif, bağımlılıksız.</summary>
public interface IChatProtocol
{
    /// <summary>Konuşma + araçlardan istek gövdesi (URL + header + json).</summary>
    (string url, (string, string)[] headers, JsonObject body) Build(ProviderInfo p, AiProfile prof, List<ChatMsg> msgs, IReadOnlyList<ToolDef> tools, string systemPrompt);
    /// <summary>Yanıttan metin + araç çağrılarını çıkar, konuşmaya asistan mesajını ekle.</summary>
    AssistantTurn Parse(JsonNode resp, List<ChatMsg> msgs);
    /// <summary>Araç sonuçlarını konuşmaya ekle (bir sonraki isteğe gider).</summary>
    void AddToolResults(List<ChatMsg> msgs, List<ToolResult> results);
    /// <summary>Ekran görüntüsünü (PNG) konuşmaya görsel olarak ekle — asistanın görmesi için.</summary>
    void AttachImage(List<ChatMsg> msgs, string caption, byte[] png);
}

/// <summary>Konuşmadaki bir mesaj — ham JsonNode olarak tutulur (biçim sağlayıcıya göre değişir).</summary>
public sealed class ChatMsg
{
    public string Role = "user";   // user, assistant, model, tool, system
    public JsonNode Node = null!;  // sağlayıcının beklediği mesaj nesnesi
}

public sealed record ToolCall(string Id, string Name, JsonObject Args);
public sealed record ToolResult(string Id, string Name, string Content);
public sealed record AssistantTurn(string Text, List<ToolCall> Calls);
