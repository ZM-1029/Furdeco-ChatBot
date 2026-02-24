using System.Collections.Concurrent;

namespace Furdeco_ChatBot.Service
{
    public class OtpService : IOtpService
    {
        private static readonly ConcurrentDictionary<string, OtpEntry> _store = new();

        public string GenerateOtp(string email)
        {
            var otp = new Random().Next(100000, 999999).ToString();

            _store[email] = new OtpEntry
            {
                Email = email,
                Otp = otp,
                ExpiryTime = DateTime.UtcNow.AddMinutes(5)
            };

            return otp;
        }

        public bool VerifyOtp(string email, string otp)
        {
            if (!_store.ContainsKey(email))
                return false;

            var entry = _store[email];

            if (entry.ExpiryTime < DateTime.UtcNow)
            {
                _store.TryRemove(email, out _);
                return false;
            }

            if (entry.Otp == otp)
            {
                _store.TryRemove(email, out _);
                return true;
            }

            return false;
        }

    }
    public class OtpEntry
    {
        public string Email { get; set; } = null!;
        public string Otp { get; set; } = null!;
        public DateTime ExpiryTime { get; set; }
    }
}