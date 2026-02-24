namespace Furdeco_ChatBot.Service
{
    public interface IOtpService
    {

        string GenerateOtp(string email);
        bool VerifyOtp(string email, string otp);
    }
}
