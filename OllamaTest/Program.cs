using Microsoft.SemanticKernel;
using System.Net.Http.Json;

class Program
{
    static async Task Main()
    {
        var settings = new PromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
        };


        var builder = Kernel.CreateBuilder();
        builder.AddOllamaTextGeneration(
            modelId: "llama3.1:8b",
            endpoint: new Uri("http://localhost:11434")
        );

        var kernel = builder.Build();

        // Native function: weer_vandaag
        var weerTool = kernel.CreateFunctionFromMethod(
            async () =>
            {
                using var http = new HttpClient();
                var json = await http.GetFromJsonAsync<Dictionary<string, string>>("http://localhost:5001/weer_vandaag");
                return json?["antwoord"] ?? "Geen weerinformatie beschikbaar.";
            },
            functionName: "weer_vandaag",
            description: "Geeft het weer van vandaag terug"
        );

        // Document search tool
        var docSearchTool = kernel.CreateFunctionFromMethod(
            async (string query) =>
            {
                using var http = new HttpClient();
                var url = $"http://localhost:5001/zoek_in_documenten?q={Uri.EscapeDataString(query)}&k=3";

                var resp = await http.GetAsync(url);
                var body = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    return $"[Zoekfout {((int)resp.StatusCode)}] {body}";
                }

                var results = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(body);
                if (results == null || results.Count == 0) return "Geen resultaten gevonden.";

                var topPassages = string.Join("\n---\n",
                    results.Select(r => r.TryGetValue("passage", out var p) ? p?.ToString() : "")
                );

                return topPassages;
            },
            functionName: "zoek_in_documenten",
            description: "Zoekt in gemeentelijke documenten op basis van een query"
        );


        // Chatfunctie die SK mag laten beslissen
        var chatFunction = kernel.CreateFunctionFromPrompt(
            """
            Jij bent een vriendelijke chatbot van Gemeente Amsterdam.
            - Als iemand vraagt naar het weer, gebruik de functie weer_vandaag.
            - Anders beantwoord zelf de vraag.
            Vraag: {{$input}}
            """,
            settings
        );

        // Input testen
        /* hacky cracky want ollama ziet function niet andere modellen wel
        Function calling in Semantic Kernel werkt alleen als:

        Het model function calling ondersteunt (Ollama-modellen doen dit meestal niet “out of the box” zoals GPT-4/3.5 dat doen via OpenAI’s API).

        De functie daadwerkelijk geregistreerd is in de Kernel voordat je de prompt uitvoert.

        De settings (FunctionChoiceBehavior.Auto) en de model-output structuur juist zijn.

        Omdat Ollama-modellen geen native “function calling”-protocol hebben, denkt SK nu: “Oké, ik kan die functie theoretisch gebruiken… maar ik kan net zo goed zelf iets verzinnen.”
        */
        var vraag = "Welk uitgangspunt staat er voor de projecten in het woningbouwplan document van Amsterdam?";

        if (vraag.ToLower().Contains("weer"))
        {
            var weerResult = await kernel.InvokeAsync(weerTool);
            Console.WriteLine(weerResult.GetValue<string>());
        }
        else if (vraag.ToLower().Contains("regeling") || vraag.ToLower().Contains("document") || vraag.ToLower().Contains("beleid"))
        {
            var docResult = await kernel.InvokeAsync(docSearchTool, new() { ["query"] = vraag });
            Console.WriteLine("Zoekresultaten:");
            Console.WriteLine(docResult.GetValue<string>());
        }
        else
        {
            var result = await kernel.InvokeAsync(chatFunction, new() { ["input"] = vraag });
            Console.WriteLine(result);
        }

    }
}
