namespace Furdeco_ChatBot.Models
{
    public class ApiRequestLog
    {
        public DateTime UtcTimestamp { get; set; }
        public string Endpoint { get; set; } = "";
        public string? Reference { get; set; }
        public string? Postcode { get; set; }
        public string? RefType { get; set; }       // "consignment" | "order"
        public string? OtpChannel { get; set; }    // "phone" | "email" (OTP endpoints only)
        public long ResponseTimeMs { get; set; }
        public int HttpStatus { get; set; }
        public bool IsSuccess { get; set; }
        public bool? DataFound { get; set; }       // null = N/A, true = found, false = not found
        public bool IsUserInput { get; set; }      // reference looks like free-text, not a ref number
    }
}
