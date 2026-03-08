namespace Furdeco_ChatBot.Service
{
    public interface IVoodooSmsService
    {
        Task<bool> SendAsync(string phone, string message);
    }
}
