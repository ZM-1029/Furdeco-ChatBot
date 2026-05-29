using Furdeco_ChatBot.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace Furdeco_ChatBot.Service
{
    /// <summary>
    /// Parses an existing Serilog "Request finished" log file into ApiRequestLog entries.
    /// Works on the logs already on disk (e.g. Azure App Service log files).
    /// </summary>
    public class LogFileParserService
    {
        public List<ApiRequestLog> ParseSerilogFile(string filePath)
        {
            var results = new List<ApiRequestLog>();

            foreach (var line in File.ReadLines(filePath))
            {
                // Only care about completed API chat requests
                if (!line.Contains("Request finished", StringComparison.Ordinal)) continue;
                if (!line.Contains("/api/chat/", StringComparison.Ordinal)) continue;

                var entry = ParseLine(line);
                if (entry != null) results.Add(entry);
            }

            return results.OrderBy(l => l.UtcTimestamp).ToList();
        }

        /// <summary>
        /// Parses a single "Request finished" log line.
        ///
        /// GET  format: {ts} +00:00 [INF] Request finished HTTP/1.1 GET  {url} - - - {sc} {bodyLen} {ct} {ms}ms
        /// POST format: {ts} +00:00 [INF] Request finished HTTP/1.1 POST {url} {reqCt} {reqLen} - {sc} {bodyLen/-} {ct} {ms}ms
        ///
        /// Working backwards from end:
        ///   [-1] = elapsed  e.g.  1447.2126ms
        ///   [-2] = content type   application/xml
        ///   [-3] = body length    9212  or  -
        ///   [-4] = status code    200
        /// </summary>
        private static ApiRequestLog? ParseLine(string line)
        {
            var tokens = line.Split(' ');
            if (tokens.Length < 10) return null;

            // ── Timestamp ─────────────────────────────────────────────────────
            // tokens[0] = "2026-05-18"  tokens[1] = "08:34:55.670"
            if (!DateTime.TryParse($"{tokens[0]} {tokens[1]}", out var ts)) return null;

            // ── Method + URL ──────────────────────────────────────────────────
            // tokens[7] = GET/POST   tokens[8] = full URL
            if (tokens.Length <= 8) return null;
            var method = tokens[7];
            var urlStr = tokens[8];
            if (string.IsNullOrEmpty(urlStr) || !urlStr.StartsWith("http")) return null;

            // ── Status / body-length / elapsed  (from the right) ─────────────
            var lastToken = tokens[tokens.Length - 1];
            if (!lastToken.EndsWith("ms", StringComparison.Ordinal)) return null;
            if (!double.TryParse(lastToken.AsSpan(0, lastToken.Length - 2), out var ms)) return null;

            var statusStr = tokens[tokens.Length - 4];
            if (!int.TryParse(statusStr, out var statusCode) || statusCode < 100 || statusCode > 599)
                return null;

            long.TryParse(tokens[tokens.Length - 3], out var bodyLen); // 0 when "-"

            // ── Parse URL ─────────────────────────────────────────────────────
            if (!Uri.TryCreate(urlStr, UriKind.Absolute, out var uri)) return null;

            var pathSegments = uri.AbsolutePath.Split('/');
            var endpoint = pathSegments.Last().ToLowerInvariant();

            // Only process known chat endpoints
            if (!IsKnownEndpoint(endpoint)) return null;

            // ── Query params (for track endpoint) ────────────────────────────
            string? reference = null;
            string? postcode  = null;
            string? refType   = "consignment";

            if (!string.IsNullOrEmpty(uri.Query))
            {
                var qp = QueryHelpers.ParseQuery(uri.Query);
                reference = qp.TryGetValue("reference", out var r) ? r.ToString() : null;
                postcode  = qp.TryGetValue("postcode",  out var p) ? p.ToString() : null;
                refType   = qp.TryGetValue("refType",   out var t) && !string.IsNullOrWhiteSpace(t)
                            ? t.ToString() : "consignment";
            }

            // ── Data found heuristic for track ────────────────────────────────
            // The "not found" error XML is ~137 bytes; real order data is always >> 200 bytes
            bool? dataFound = null;
            if (endpoint == "track")
                dataFound = bodyLen > 200;

            // ── User-input detection ──────────────────────────────────────────
            // If the reference contains spaces (after URL-decoding) it's typed text not a ref no.
            var isUserInput = !string.IsNullOrEmpty(reference) &&
                              (reference.Contains(' ') || reference.Length > 30);

            return new ApiRequestLog
            {
                UtcTimestamp   = DateTime.SpecifyKind(ts, DateTimeKind.Utc),
                Endpoint       = endpoint,
                Reference      = string.IsNullOrWhiteSpace(reference) ? null : reference,
                Postcode       = string.IsNullOrWhiteSpace(postcode)  ? null : postcode,
                RefType        = refType,
                ResponseTimeMs = (long)ms,
                HttpStatus     = statusCode,
                IsSuccess      = statusCode < 400,
                DataFound      = dataFound,
                IsUserInput    = isUserInput
            };
        }

        private static bool IsKnownEndpoint(string ep) =>
            ep is "track" or "confirm" or "decline" or "instructions"
                 or "note" or "send-otp" or "verify-otp";
    }
}
