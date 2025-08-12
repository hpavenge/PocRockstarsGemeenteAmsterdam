using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        var prompt = "Hoi, hoe gaat het vandaag?";

        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:11434/") };

        var requestBody = new
        {
            model = "llama3.1:8b", // Zorg dat je dit model hebt gepulled
            prompt = prompt,
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/generate", content);
        var responseString = await response.Content.ReadAsStringAsync();

        Console.WriteLine("=== Ollama Response ===");
        Console.WriteLine(responseString);
    }
}
