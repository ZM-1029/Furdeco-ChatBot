using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http;

[Route("api/chat")]
[ApiController]
public class ChatController : ControllerBase
{
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration _config;
    private readonly IOtpService _otpService;

    public ChatController(IHttpClientFactory factory, IConfiguration config, IOtpService otpService)
    {
        _factory = factory;
        _config = config;
        _otpService= otpService;
    }

    private string BaseUrl => _config["GSIT:BaseUrl"];
    private string ApiKey => _config["GSIT:ApiKey"];

    // TRACK
    [HttpGet("track")]
    public async Task<IActionResult> Track(string reference, string postcode)
    {
        var client = _factory.CreateClient();
        var url = $"{BaseUrl}/_portal/api/_tracking/?key={ApiKey}&carrier_reference={reference}&postcode={postcode}";
        var response = await client.GetAsync(url);
        var result = await response.Content.ReadAsStringAsync();
        return Content(result, "application/xml");
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
    [HttpPost("instructions")]
    public async Task<IActionResult> UpdateInstructions([FromForm] string reference, [FromForm] string instructions)
    {
        return await UpdateOrder(reference, "update_field",
            new Dictionary<string, string> { { "OtherInstructions", instructions } });
    }

    // ADD NOTE
    [HttpPost("note")]
    public async Task<IActionResult> AddNote([FromForm] string reference, [FromForm] string note)
    {
        return await UpdateOrder(reference, "add_note",
            new Dictionary<string, string> { { "Note", note } });
    }

    private async Task<IActionResult> UpdateOrder(string reference, string action,
        Dictionary<string, string>? extra = null)
    {
        var client = _factory.CreateClient();

        var url = $"{BaseUrl}/_portal/api/_orders/update/?key={ApiKey}&carrier_reference={reference}&action={action}";

        var content = extra != null
            ? new FormUrlEncodedContent(extra)
            : new FormUrlEncodedContent(new Dictionary<string, string>());

        var response = await client.PostAsync(url, content);
        var result = await response.Content.ReadAsStringAsync();

        return Content(result, "application/xml");
    }
    // STEP 1 - Send OTP
    [HttpPost("send-otp")]
    public IActionResult SendOtp([FromBody] SendOtpRequest request)
    {
        if (string.IsNullOrEmpty(request.Email))
            return BadRequest("Email required");

        var otp = _otpService.GenerateOtp(request.Email);

        // TODO: Replace with real email sending
        Console.WriteLine($"OTP for {request.Email}: {otp}");

        return Ok(new { message = "OTP sent successfully" });
    }

    // STEP 2 - Verify OTP
    [HttpPost("verify-otp")]
    public IActionResult VerifyOtp([FromBody] VerifyOtpRequest request)
    {
        var valid = _otpService.VerifyOtp(request.Email, request.Otp);

        if (!valid)
            return BadRequest("Invalid or expired OTP");

        return Ok(new { message = "OTP verified" });
    }
}

public class SendOtpRequest
{
    public string Email { get; set; } = null!;
}

public class VerifyOtpRequest
{
    public string Email { get; set; } = null!;
    public string Otp { get; set; } = null!;
}