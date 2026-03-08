using System.Text;
using System.Text.Json;

namespace Furdeco_ChatBot.Service
{
    public class VoodooSmsService : IVoodooSmsService
    {
        private readonly IConfiguration _config;
        private readonly HttpClient _httpClient;

        public VoodooSmsService(IConfiguration config, HttpClient httpClient)
        {
            _config = config;
            _httpClient = httpClient;
        }

        public async Task<bool> SendAsync(string phone, string message)
        {
            bool isActive = bool.TryParse(_config["VoodooSms:IsActive"], out bool activeValue) && activeValue;

            if (!isActive)
            {
                Console.WriteLine("SMS service is disabled in configuration.");
                return false;
            }

            var bodyObj = new
            {
                to = phone,
                from = "ZMAPPK",
                msg = message
            };

            string json = JsonSerializer.Serialize(bodyObj);

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{_config["VoodooSms:BaseUrl"]}/sendsms");

            request.Headers.Add("Authorization", $"Bearer {_config["VoodooSms:ApiKey"]}");
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);

            return response.IsSuccessStatusCode;
        }
    }
}
