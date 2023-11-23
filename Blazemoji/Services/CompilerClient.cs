using Blazemoji.Contracts.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blazemoji.Services
{
    public class CompilerClient(HttpClient httpClient)
    {
        private readonly HttpClient _httpClient = httpClient;

        public async Task<EmojicodeResult> ExecuteCodeAsync(string code)
        {
            try
            {
                var res = await _httpClient.PostAsync("execute", new StringContent(code, Encoding.Unicode, "application/text"));
                Console.WriteLine(await res.Content.ReadAsStringAsync());
                res.EnsureSuccessStatusCode();
                var result = JsonSerializer.Deserialize<EmojicodeResult>(await res.Content.ReadAsStringAsync());
                return result;
            }
            catch(Exception ex)
            {
                return new EmojicodeResult
                {
                    Error = true,
                    Result = ex.Message,
                };
            }
        }
    }
}
