using System.Net.Http.Json;
using System.Text.Json;

namespace RecipePlanner.Api.Ai;

// Shared plumbing for "call Claude and force it to answer with one specific tool".
// Forcing a tool means the reply is always JSON in our schema, never free text.
internal static class AnthropicToolCaller
{
    public static async Task<JsonElement> CallAsync(
        HttpClient http, AnthropicOptions opts, ILogger logger,
        string systemPrompt, object userContent, string toolName, string toolDescription, object toolSchema,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            model = opts.Model,
            max_tokens = opts.MaxTokens,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userContent } },
            tools = new[] { new { name = toolName, description = toolDescription, input_schema = toolSchema } },
            tool_choice = new { type = "tool", name = toolName }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages") { Content = JsonContent.Create(body) };
        request.Headers.Add("x-api-key", opts.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Log the status only: the response body could echo the prompt back
            logger.LogWarning("Anthropic API returned {StatusCode}", (int)response.StatusCode);
            throw new MealIdeaException($"AI provider returned {(int)response.StatusCode}.");
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        var root = doc.RootElement;

        // Token counts are what you pay for, so log them to keep an eye on cost
        if (root.TryGetProperty("usage", out var usage))
            logger.LogInformation("AI {Tool} request used {InputTokens} input and {OutputTokens} output tokens",
                toolName, usage.GetProperty("input_tokens").GetInt32(), usage.GetProperty("output_tokens").GetInt32());

        foreach (var block in root.GetProperty("content").EnumerateArray())
        {
            if (block.GetProperty("type").GetString() == "tool_use" && block.GetProperty("name").GetString() == toolName)
                return block.GetProperty("input").Clone(); // Clone: the document is disposed when we return
        }

        throw new MealIdeaException("AI response did not contain the expected tool call.");
    }
}
