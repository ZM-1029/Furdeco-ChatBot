using Furdeco_ChatBot.Models;
using Furdeco_ChatBot.Service;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Furdeco_ChatBot.Middleware
{
    public class ApiLoggingMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiLoggingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, IApiLoggerService apiLogger)
        {
            var path = context.Request.Path.Value ?? "";

            // Only track /api/chat/* routes
            if (!path.StartsWith("/api/chat/", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var endpoint = path.Split('/').Last().ToLowerInvariant();

            // ── Capture request params BEFORE calling next ────────────────────────
            string? reference = null;
            string? postcode = null;
            string? refType = null;
            string? otpChannel = null;

            if (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                reference = context.Request.Query["reference"].ToString();
                postcode = context.Request.Query["postcode"].ToString();
                refType = context.Request.Query["refType"].ToString();
                if (string.IsNullOrEmpty(refType)) refType = "consignment";
            }
            else if (context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                context.Request.EnableBuffering();

                if (endpoint is "confirm" or "decline" or "instructions" or "note")
                {
                    var form = await context.Request.ReadFormAsync();
                    reference = form["reference"].ToString();
                    context.Request.Body.Seek(0, SeekOrigin.Begin);
                }
                else if (endpoint is "send-otp" or "verify-otp")
                {
                    try
                    {
                        var body = await new StreamReader(context.Request.Body, Encoding.UTF8,
                            leaveOpen: true).ReadToEndAsync();
                        context.Request.Body.Seek(0, SeekOrigin.Begin);

                        using var doc = JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("type", out var typeProp))
                            otpChannel = typeProp.GetString();
                        else if (doc.RootElement.TryGetProperty("Type", out var typeProp2))
                            otpChannel = typeProp2.GetString();
                    }
                    catch { /* non-critical */ }
                }
            }

            // ── Buffer the response to inspect content ────────────────────────────
            var originalBody = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;

            var sw = Stopwatch.StartNew();
            try
            {
                await _next(context);
            }
            finally
            {
                sw.Stop();
                buffer.Seek(0, SeekOrigin.Begin);
                var responseBody = await new StreamReader(buffer).ReadToEndAsync();
                buffer.Seek(0, SeekOrigin.Begin);
                await buffer.CopyToAsync(originalBody);
                context.Response.Body = originalBody;

                // ── Build and persist the log entry ──────────────────────────────
                bool? dataFound = null;
                if (endpoint == "track")
                    dataFound = !responseBody.Contains("<error>", StringComparison.OrdinalIgnoreCase);

                var entry = new ApiRequestLog
                {
                    UtcTimestamp    = DateTime.UtcNow,
                    Endpoint        = endpoint,
                    Reference       = NullIfEmpty(reference),
                    Postcode        = NullIfEmpty(postcode),
                    RefType         = NullIfEmpty(refType),
                    OtpChannel      = NullIfEmpty(otpChannel),
                    ResponseTimeMs  = sw.ElapsedMilliseconds,
                    HttpStatus      = context.Response.StatusCode,
                    IsSuccess       = context.Response.StatusCode < 400,
                    DataFound       = dataFound,
                    IsUserInput     = IsUserInputText(reference)
                };

                apiLogger.Log(entry);
            }
        }

        private static string? NullIfEmpty(string? s) =>
            string.IsNullOrWhiteSpace(s) ? null : s;

        // Heuristic: reference looks like typed text rather than an order ref
        private static bool IsUserInputText(string? reference)
        {
            if (string.IsNullOrWhiteSpace(reference)) return false;
            return reference.Contains(' ') ||
                   reference.Length > 30;
        }
    }
}
