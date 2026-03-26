using Furdeco_ChatBot.Controllers;
using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

[Route("api/chat")]
[ApiController]
public class ChatController : ControllerBase
{
    private const string NoOrderMessage = "We couldn't find an order with that consignment number and postcode. Please check both and try again.";
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration _config;
    private readonly IOtpService _otpService;
    private readonly IVoodooSmsService _voodooSmsService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IHttpClientFactory factory, IConfiguration config, IOtpService otpService, IVoodooSmsService voodooSmsService, ILogger<ChatController> logger)
    {
        _factory = factory;
        _config = config;
        _otpService = otpService;
        _voodooSmsService = voodooSmsService;
        _logger = logger;
    }

    private string BaseUrl => _config["GSIT:BaseUrl"];
    private string ApiKey => _config["GSIT:ApiKey"];

    // TRACK
    [HttpGet("track")]
    public async Task<IActionResult> Track(string reference, string postcode, string? refType = "consignment")
    {
        var client = _factory.CreateClient("GSIT");
        var refParam = string.Equals(refType, "order", StringComparison.OrdinalIgnoreCase)
            ? $"order_number={reference}"
            : $"carrier_reference={reference}";
        var url = $"{BaseUrl}/_portal/api/_tracking/?key={ApiKey}&{refParam}&postcode={postcode}";
        _logger.LogInformation($"Tracking URL: {url} ");
        try
        {
            var response = await client.GetAsync(url);
            var result = await response.Content.ReadAsStringAsync();
            if (result.IndexOf("<error>", StringComparison.OrdinalIgnoreCase) >= 0)
                return Content($"<response><error>{NoOrderMessage}</error></response>", "application/xml");
            return Content(result, "application/xml");
        }
        catch (TaskCanceledException)
        {
            return new ContentResult { Content = "<response><error>Request timed out. Please try again.</error></response>", ContentType = "application/xml", StatusCode = 504 };
        }
    }

    // CONFIRM
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromForm] string reference)
    {
        return await UpdateOrder(reference, "confirm");
    }

    // DECLINE
    [HttpPost("decline")]
    public async Task<IActionResult> Decline([FromForm] string reference)
    {
        return await UpdateOrder(reference, "decline");
    }

    // UPDATE INSTRUCTIONS
   // [HttpPost("instructions")]
   /* public async Task<IActionResult> UpdateInstructions([FromForm] string reference, [FromForm] string instructions)
    {
        *//*return await UpdateOrder(reference, "update_field",
                        new Dictionary<string, string> { { "CrewInstructions", instructions } });*//*
        return await UpdateOrder(reference, "add_deliveryinstructions",
                        new Dictionary<string, string> { { "CrewInstructions", instructions } });

        // new Dictionary<string, string> { { "OtherInstructions", instructions } });
    }*/
    // UPDATE INSTRUCTIONS
    [HttpPost("instructions")]
    public async Task<IActionResult> UpdateInstructions([FromForm] string reference, [FromForm] string instructions)
    {
        return await UpdateOrder(reference, "add_deliveryinstructions",
                        new Dictionary<string, string> { { "Instructions", instructions } }, append: true);
    }
    // ADD NOTE
    [HttpPost("note")]
    public async Task<IActionResult> AddNote([FromForm] string reference, [FromForm] string note)
    {
        return await UpdateOrder(reference, "add_note",
            new Dictionary<string, string> { { "Note", note } });
    }
    private async Task<IActionResult> UpdateOrder(string reference, string action,
        Dictionary<string, string>? extra = null, bool append = false)
    {
        var client = _factory.CreateClient("GSIT");
        var url = $"{BaseUrl}/_portal/api/_orders/update/?key={ApiKey}&carrier_reference={reference}&action={action}{(append ? "&append=true" : "")}";
        _logger.LogInformation($"Updating order URL: {url} ");
        var content = extra != null
            ? new FormUrlEncodedContent(extra)
            : new FormUrlEncodedContent(new Dictionary<string, string>());
        try
        {
            var response = await client.PostAsync(url, content);
            var result = await response.Content.ReadAsStringAsync();
            return Content(result, "application/xml");
        }
        catch (TaskCanceledException)
        {
            return new ContentResult { Content = "<response><error>Request timed out. Please try again.</error></response>", ContentType = "application/xml", StatusCode = 504 };
        }
    }

    /* private async Task<IActionResult> UpdateOrderNew(string reference, string action,
       Dictionary<string, string>? extra = null)
     {
         var client = _factory.CreateClient("GSIT");
         var url = $"{BaseUrl}/_portal/api/_orders/update/?key={ApiKey}&carrier_reference={reference}&action={action}&append=true";
         var content = extra != null
             ? new FormUrlEncodedContent(extra)
             : new FormUrlEncodedContent(new Dictionary<string, string>());
         try
         {
             var response = await client.PostAsync(url, content);
             var result = await response.Content.ReadAsStringAsync();
             return Content(result, "application/xml");
         }
         catch (TaskCanceledException)
         {
             return new ContentResult { Content = "<response><error>Request timed out. Please try again.</error></response>", ContentType = "application/xml", StatusCode = 504 };
         }
     }
     private async Task<IActionResult> UpdateOrder(string reference, string action,
         Dictionary<string, string>? extra = null)
     {
         var client = _factory.CreateClient("GSIT");
         var url = $"{BaseUrl}/_portal/api/_orders/update/?key={ApiKey}&carrier_reference={reference}&action={action}";
         var content = extra != null
             ? new FormUrlEncodedContent(extra)
             : new FormUrlEncodedContent(new Dictionary<string, string>());
         try
         {
             var response = await client.PostAsync(url, content);
             var result = await response.Content.ReadAsStringAsync();
             return Content(result, "application/xml");
         }
         catch (TaskCanceledException)
         {
             return new ContentResult { Content = "<response><error>Request timed out. Please try again.</error></response>", ContentType = "application/xml", StatusCode = 504 };
         }
     }*/

    // STEP 1 - Send OTP
    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpRequest request)
    {
        string? identifier;
        bool isPhone = string.Equals(request.Type, "phone", StringComparison.OrdinalIgnoreCase);

        if (isPhone)
        {
            if (string.IsNullOrWhiteSpace(request.Phone))
                return BadRequest(new { error = "Phone required" });
            identifier = NormalizePhone(request.Phone);
            if (string.IsNullOrEmpty(identifier))
                return BadRequest(new { error = "Invalid phone number" });
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return BadRequest(new { error = "Email required" });
            identifier = request.Email.Trim();
        }

        var otp = _otpService.GenerateOtp(identifier);

        if (isPhone)
        {
            var sent = await _voodooSmsService.SendAsync(identifier, $"Your Ask Frankie verification code is: {otp}. It expires in 5 minutes.");
            if (!sent)
            {
                Console.WriteLine($"[OTP SMS Error] Failed to send to {identifier}");
                Console.WriteLine($"[DEV] OTP for {identifier}: {otp}");
            }
        }
        else
        {
            try
            {
                await SendOtpEmail(identifier, otp);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OTP Email Error] {ex.Message}");
                Console.WriteLine($"[DEV] OTP for {identifier}: {otp}");
            }
        }

        return Ok(new { message = "OTP sent successfully" });
    }

    // STEP 2 - Verify OTP
    [HttpPost("verify-otp")]
    public IActionResult VerifyOtp([FromBody] VerifyOtpRequest request)
    {
        if (string.IsNullOrEmpty(request.Otp))
            return BadRequest(new { error = "OTP required" });

        string? identifier = null;
        if (!string.IsNullOrWhiteSpace(request.Phone))
            identifier = NormalizePhone(request.Phone);
        if (string.IsNullOrEmpty(identifier) && !string.IsNullOrWhiteSpace(request.Email))
            identifier = request.Email.Trim();

        if (string.IsNullOrEmpty(identifier))
            return BadRequest(new { error = "Email or Phone required" });

        var valid = _otpService.VerifyOtp(identifier, request.Otp);

        if (!valid)
            return BadRequest(new { error = "Invalid or expired OTP" });

        return Ok(new { message = "OTP verified" });
    }

    private static string? NormalizePhone(string phone)
    {
        var digits = Regex.Replace(phone, @"\D", "");
        if (string.IsNullOrEmpty(digits)) return null;
        if (digits.StartsWith("0") && digits.Length >= 10)
            return "44" + digits.TrimStart('0');
        if (digits.Length == 10 && !digits.StartsWith("44"))
            return "44" + digits;
        return digits;
    }
    private async Task SendOtpEmail(string toEmail, string otp)
    {
        var smtp = _config["EmailSettings:SmtpServer"] ?? "smtp.gmail.com";
        var port = int.Parse(_config["EmailSettings:Port"] ?? "587");
        var username = _config["EmailSettings:Username"] ?? "";
        var password = _config["EmailSettings:Password"] ?? "";

        using var client = new SmtpClient(smtp, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(username, password)
        };

        var mail = new MailMessage
        {
            From = new MailAddress(username, "Ask Frankie by Furdeco"),
            Subject = "Your Ask Frankie Verification Code",
            IsBodyHtml = true,
            Body = $@"
                    <!DOCTYPE html>
                    <html>
                    <head><meta charset='UTF-8'></head>
                    <body style='margin:0;padding:0;background:#f5f5f7;font-family:""DM Sans"",Arial,sans-serif'>
                      <table width='100%' cellpadding='0' cellspacing='0' style='padding:40px 0'>
                        <tr><td align='center'>
                          <table width='480' cellpadding='0' cellspacing='0' style='background:white;border-radius:16px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.08)'>
                            <tr>
                              <td style='background:linear-gradient(135deg,#2a8f38,#3AB54A);padding:32px 40px;text-align:center'>
                                <div style='font-size:32px;margin-bottom:8px'>🚚</div>
                                <div style='font-family:""Syne"",Arial,sans-serif;font-size:22px;font-weight:800;color:white'>Ask Frankie by Furdeco</div>
                                <div style='font-size:13px;color:rgba(255,255,255,0.8);margin-top:4px'>Secure Delivery Portal</div>
                              </td>
                            </tr>
                            <tr>
                              <td style='padding:40px'>
                                <p style='font-size:16px;color:#1c1c1e;margin:0 0 8px'>Your verification code is:</p>
                                <div style='background:#f0faf1;border:2px solid #3AB54A;border-radius:12px;padding:20px;text-align:center;margin:20px 0'>
                                  <span style='font-family:""Syne"",Arial,sans-serif;font-size:42px;font-weight:800;color:#2a8f38;letter-spacing:10px'>{otp}</span>
                                </div>
                                <p style='font-size:13px;color:#8e8e93;margin:0 0 6px'>⏱ This code expires in <strong>5 minutes</strong>.</p>
                                <p style='font-size:13px;color:#8e8e93;margin:0'>If you didn't request this, you can safely ignore this email.</p>
                              </td>
                            </tr>
                            <tr>
                              <td style='background:#f5f5f7;padding:16px 40px;text-align:center;font-size:11px;color:#8e8e93'>
                                © 2026 Furdeco · Secure &amp; Encrypted
                              </td>
                            </tr>
                          </table>
                        </td></tr>
                      </table>
                    </body>
                    </html>"
        };

        mail.To.Add(toEmail);
        try
        {
            await client.SendMailAsync(mail);
        }
        catch (Exception)
        {
            // Logged by caller
        }
    }
}

public class SendOtpRequest
{
    public string? Type { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class VerifyOtpRequest
{
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Otp { get; set; } = null!;
}
