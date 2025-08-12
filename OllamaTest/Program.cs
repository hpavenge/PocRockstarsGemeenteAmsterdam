using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;

class Program
{
    static async Task Main()
    {
        // 1. Kernel builder maken
        var builder = Kernel.CreateBuilder();

        // 2. Ollama koppelen
        builder.AddOllamaTextGeneration(
            modelId: "llama3.1:8b",
            endpoint: new Uri("http://localhost:11434")
        );

        var kernel = builder.Build();

        // 3. Prompt functie aanmaken
        var chatFunction = kernel.CreateFunctionFromPrompt(
            """
            Jij bent een vriendelijke chatbot van Gemeente Amsterdam.
            Antwoord in het Nederlands, kort en duidelijk.
            Vraag: {{$input}}
            """
        );

        // 4. Input en resultaat
        var vraag = "Wat kun je me vertellen over parkeren in Amsterdam?";
        var result = await kernel.InvokeAsync(chatFunction, new() { ["input"] = vraag });

        Console.WriteLine($"Vraag: {vraag}");
        Console.WriteLine("Antwoord:");
        Console.WriteLine(result);
    }
}
