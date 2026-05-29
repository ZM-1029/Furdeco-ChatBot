using Furdeco_ChatBot.Models;
using Furdeco_ChatBot.Service;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

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

            // ── CHANGE: skip logging for internal add-note calls ──────────────────
            if (path.Equals("/api/chat/note", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }
            // ─────────────────────────────────────────────────────────────────────

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

                // ── Snapshot order details from successful track responses ────────
                if (endpoint == "track" && dataFound == true)
                {
                    try
                    {
                        var doc  = XDocument.Parse(responseBody);
                        var root = doc.Root;
                        string gx(string tag) =>
                            root?.Element(tag)?.Value?.Trim() is { Length: > 0 } v && v != "N/A" ? v : null!;
                        string gxi(string parent, string child) =>
                            root?.Element(parent)?.Element(child)?.Value?.Trim() is { Length: > 0 } v && v != "N/A" ? v : null!;

                        entry.OrderNumber      = NullIfEmpty(gx("order_number"));
                        entry.Recipient        = NullIfEmpty(gx("recipient"));
                        entry.SenderCompany    = NullIfEmpty(gx("sender_company"));
                        var addr               = gx("address");
                        var pc                 = gx("postcode");
                        entry.Address          = NullIfEmpty(string.IsNullOrWhiteSpace(pc) ? addr
                                                    : string.IsNullOrWhiteSpace(addr) ? pc : $"{addr}, {pc}");
                        entry.PlannedDate      = NullIfEmpty(gx("planned_date"));
                        entry.PlannedSlotStart = NullIfEmpty(gxi("planned_slot", "start"));
                        entry.PlannedSlotEnd   = NullIfEmpty(gxi("planned_slot", "end"));
                        entry.DeliveryStatus   = NullIfEmpty(gx("status"));
                        entry.ServiceLevel     = NullIfEmpty(gxi("services", "service_level"));
                        entry.DeliveryPoint    = NullIfEmpty(gxi("services", "delivery_point"));

                        var products = root?.Element("products")?.Descendants("product")
                            .Select(p => p.Element("description")?.Value?.Trim())
                            .Where(d => !string.IsNullOrWhiteSpace(d) && d != "N/A")
                            .ToList();
                        if (products?.Count > 0) entry.ProductDescriptions = products!;
                    }
                    catch { /* non-critical — log still saved without snapshot */ }
                }

                try { apiLogger.Log(entry); } catch { /* non-critical */ }
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
