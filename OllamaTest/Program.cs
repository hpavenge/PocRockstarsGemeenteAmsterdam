using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;

public record Passage(int id, string passage, string source, int chunk, double? score);

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

        // Tool: weer_vandaag
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

        // Tool: zoek_in_documenten  ➜ GEEF RUW JSON TERUG (niet vooraf samenvoegen)
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

                // Geef raw JSON terug zodat we hieronder netjes kunnen parsen + citeren
                return body;
            },
            functionName: "zoek_in_documenten",
            description: "Zoekt in gemeentelijke documenten op basis van een query"
        );

        // Chat (fallback)
        var chatFunction = kernel.CreateFunctionFromPrompt(
            """
            Jij bent een vriendelijke chatbot van Gemeente Amsterdam.
            - Als iemand vraagt naar het weer, gebruik de functie weer_vandaag.
            - Anders beantwoord zelf de vraag.
            Vraag: {{$input}}
            """,
            settings
        );

        // Synthese-prompt ZONDER {{source}}/{{chunk}} placeholders (die breken SK)
        var synthesize = kernel.CreateFunctionFromPrompt(
            """
            Je bent een assistent voor Gemeente Amsterdam.
            Antwoord UITSLUITEND op basis van de CONTEXT hieronder.
            Als het antwoord niet in de context staat, zeg: "Onvoldoende informatie in de beschikbare documenten."
            Neem aan het eind de gebruikte bron-tags letterlijk over (bijv. [bron: … chunk …]).

            Vraag: {{$vraag}}

            --- CONTEXT ---
            {{$context}}
            --- EINDE CONTEXT ---
            """
        );

        // Kleine helper om PDF-artefacts op te schonen
        static string Clean(string s) =>
            string.IsNullOrWhiteSpace(s) ? "" :
            s.Replace("-\n", "")   // afbrekingen
             .Replace("\r", " ")
             .Replace("\n", " ")
             .Replace("  ", " ");

        // ===== Testvraag =====
        var vraag = "Welk uitgangspunt staat er voor de projecten in het woningbouwplan document van Amsterdam?";

        if (vraag.ToLower().Contains("weer"))
        {
            var weerResult = await kernel.InvokeAsync(weerTool);
            Console.WriteLine(weerResult.GetValue<string>());
        }
        else if (vraag.ToLower().Contains("regeling") ||
                 vraag.ToLower().Contains("document") ||
                 vraag.ToLower().Contains("beleid") ||
                 vraag.ToLower().Contains("woningbouw") ||
                 vraag.ToLower().Contains("plan"))
        {
            // 1) Zoek passages
            var docResult = await kernel.InvokeAsync(docSearchTool, new() { ["query"] = vraag });
            var raw = docResult.GetValue<string>() ?? "";
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            List<Passage>? passages = null;
            try
            {
                passages = JsonSerializer.Deserialize<List<Passage>>(raw, jsonOptions);
            }
            catch
            {
                // als server plain tekst gaf, gebruiken we die als context
            }

            // 2) Bouw context met nette bron-tags
            string context;
            if (passages is { Count: > 0 })
            {
                context = string.Join("\n---\n", passages
                    .Where(p => p is not null)
                    .Take(3)
                    .Select(p => $"[bron: {p.source} chunk {p.chunk}]\n{Clean(p.passage)}"));
            }
            else
            {
                context = Clean(raw); // fallback
            }

            // 3) Synthese (samenvatting/antwoord + bron-tags)
            var answer = await kernel.InvokeAsync(synthesize, new()
            {
                ["vraag"] = vraag,
                ["context"] = context
            });

            Console.WriteLine(answer);
        }
        else
        {
            var result = await kernel.InvokeAsync(chatFunction, new() { ["input"] = vraag });
            Console.WriteLine(result);
        }
    }
}
