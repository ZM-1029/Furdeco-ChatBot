using ClosedXML.Excel;
using Furdeco_ChatBot.Models;

namespace Furdeco_ChatBot.Service
{
    public class ExcelReportService
    {
        private static readonly TimeZoneInfo UkTz =
            TimeZoneInfo.FindSystemTimeZoneById(
                System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows)
                ? "GMT Standard Time"   // Windows (local dev, Windows App Service)
                : "Europe/London");     // Linux (Azure App Service Linux)

        // ── Palette ──────────────────────────────────────────────────────────
        private static readonly XLColor ColDarkGreen    = XLColor.FromHtml("#1b5e20");
        private static readonly XLColor ColMedGreen     = XLColor.FromHtml("#2a8f38");
        private static readonly XLColor ColBrightGreen  = XLColor.FromHtml("#3AB54A");
        private static readonly XLColor ColMint         = XLColor.FromHtml("#c8e6c9");
        private static readonly XLColor ColLightMint    = XLColor.FromHtml("#e8f5e9");
        private static readonly XLColor ColPaleGreen    = XLColor.FromHtml("#f1f8e9");
        private static readonly XLColor ColErrorBg      = XLColor.FromHtml("#fce4d6");
        private static readonly XLColor ColWarnBg       = XLColor.FromHtml("#fff9c4");
        private static readonly XLColor ColRowAlt       = XLColor.FromHtml("#f1f8e9");
        private static readonly XLColor ColWhite        = XLColor.FromHtml("#FFFFFF");
        private static readonly XLColor ColBlack        = XLColor.FromHtml("#212121");
        private static readonly XLColor ColKpiBg        = XLColor.FromHtml("#f0faf1");

        public byte[] Generate(List<ApiRequestLog> logs, DateTime reportUtcDate)
        {
            var ukDate    = TimeZoneInfo.ConvertTimeFromUtc(reportUtcDate.Date, UkTz);
            var dateLabel = ukDate.ToString("dd-MMM-yyyy");
            var shortLabel = ukDate.ToString("dd-MMM");

            using var wb = new XLWorkbook();

            BuildDashboard(wb, logs, ukDate, dateLabel);
            BuildDetailSheet(wb, logs, shortLabel);
            BuildEndpointSheet(wb, logs, dateLabel);
            BuildHourlySheet(wb, logs, dateLabel);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SHEET 1 — DASHBOARD
        // ════════════════════════════════════════════════════════════════════════
        private void BuildDashboard(IXLWorkbook wb, List<ApiRequestLog> logs,
            DateTime ukDate, string dateLabel)
        {
            var ws = wb.AddWorksheet("Dashboard");
            ws.ShowGridLines = false;

            // ── Main Title ───────────────────────────────────────────────────
            MergeTitle(ws, 1, 8, "FURDECO CHATBOT  —  DAILY ACTIVITY REPORT",
                XLColor.FromHtml("#c8e6c9"), XLColor.FromHtml("#1b5e20"), 15, bold: true);
            ws.Row(1).Height = 34;

            // ── Subtitle ─────────────────────────────────────────────────────
            var genTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, UkTz);
            MergeTitle(ws, 2, 8,
                $"Report Date: {ukDate:dddd, dd MMMM yyyy}  |  Timezone: UK (GMT/BST)  |  Generated: {genTime:HH:mm  dd-MMM-yyyy}",
                XLColor.FromHtml("#e8f5e9"), XLColor.FromHtml("#2a8f38"), 10, bold: false);
            ws.Row(2).Height = 18;

            ws.Row(3).Height = 8; // spacer

            // ── KEY METRICS header ────────────────────────────────────────────
            MergeSectionHeader(ws, 4, 8, "  KEY METRICS");
            ws.Row(4).Height = 24;

            // ── Compute stats ─────────────────────────────────────────────────
            var trackLogs    = logs.Where(l => l.Endpoint == "track").ToList();
            var otpSend      = logs.Count(l => l.Endpoint == "send-otp");
            var otpSuccess   = logs.Count(l => l.Endpoint == "verify-otp" && l.IsSuccess);
            var instructions = logs.Count(l => l.Endpoint == "instructions");
            var notes        = logs.Count(l => l.Endpoint == "note");
            var confirms     = logs.Count(l => l.Endpoint == "confirm");
            var declines     = logs.Count(l => l.Endpoint == "decline");
            var dataFound    = trackLogs.Count(l => l.DataFound == true);
            var notFound     = trackLogs.Count(l => l.DataFound == false);
            var userInputs   = trackLogs.Count(l => l.IsUserInput);
            var totalCalls   = logs.Count;
            var avgResp      = logs.Any() ? (long)logs.Average(l => l.ResponseTimeMs) : 0L;
            var minResp      = logs.Any() ? logs.Min(l => l.ResponseTimeMs) : 0L;
            var maxResp      = logs.Any() ? logs.Max(l => l.ResponseTimeMs) : 0L;
            var otpRate      = otpSend > 0 ? Math.Round(otpSuccess * 100.0 / otpSend, 1) : 0.0;
            var peakHour     = trackLogs
                .GroupBy(l => TimeZoneInfo.ConvertTimeFromUtc(l.UtcTimestamp, UkTz).Hour)
                .OrderByDescending(g => g.Count()).FirstOrDefault();
            var peakLabel    = peakHour != null ? $"{peakHour.Key:00}:00" : "N/A";

            var kpis = new[]
            {
                ("Total API Calls",     totalCalls.ToString(),    "Track Requests",    trackLogs.Count.ToString()),
                ("Data Found",          dataFound.ToString(),     "Not Found / Error", notFound.ToString()),
                ("OTP Sessions Sent",   otpSend.ToString(),       "OTP Verified",      otpSuccess.ToString()),
                ("Instructions Saved",  instructions.ToString(),  "Notes Saved",       notes.ToString()),
                ("Confirms",            confirms.ToString(),      "Declines",          declines.ToString()),
                ("User Input Detected", userInputs.ToString(),    "Peak Hour (UK)",    peakLabel),
                ("Avg Response (ms)",   avgResp.ToString(),       "Min Response (ms)", minResp.ToString()),
                ("Max Response (ms)",   maxResp.ToString(),       "OTP Success Rate",  $"{otpRate}%"),
            };

            int row = 5;
            foreach (var (l1, v1, l2, v2) in kpis)
            {
                WriteKpiRow(ws, row, l1, v1, l2, v2);
                row++;
            }

            ws.Row(row).Height = 8;
            row++;

            // ── Hourly summary ────────────────────────────────────────────────
            MergeSectionHeader(ws, row, 8, "  HOURLY ACTIVITY BREAKDOWN");
            ws.Row(row).Height = 24;
            row++;

            WriteTableHeader(ws, row,
                new[] { "Hour (UK)", "Track", "Confirm", "Decline", "OTP Sends", "Instructions", "Notes", "Total" });
            ws.Row(row).Height = 22;
            row++;

            for (int h = 0; h < 24; h++)
            {
                int hour  = h;
                int t  = trackLogs.Count(l => ToUkHour(l) == hour);
                int c  = logs.Count(l => l.Endpoint == "confirm"      && ToUkHour(l) == hour);
                int d  = logs.Count(l => l.Endpoint == "decline"      && ToUkHour(l) == hour);
                int o  = logs.Count(l => l.Endpoint == "send-otp"     && ToUkHour(l) == hour);
                int n  = logs.Count(l => l.Endpoint == "instructions" && ToUkHour(l) == hour);
                int nt = logs.Count(l => l.Endpoint == "note"         && ToUkHour(l) == hour);
                int tot = t + c + d + o + n + nt;

                var bg = tot > 0 ? (h % 2 == 0 ? ColLightMint : ColPaleGreen) : ColWhite;
                var vals = new object[] { $"{h:00}:00", t, c, d, o, n, nt, tot };
                for (int ci = 0; ci < vals.Length; ci++)
                {
                    var cell = ws.Cell(row, ci + 1);
                    cell.Value = vals[ci].ToString();
                    cell.Style.Fill.BackgroundColor = bg;
                    cell.Style.Font.FontColor = ColBlack;
                    cell.Style.Font.Bold = ci == 7 && tot > 0;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = ci == 0
                        ? XLAlignmentHorizontalValues.Left
                        : XLAlignmentHorizontalValues.Center;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = ColMint;
                }
                row++;
            }

            ws.Row(row).Height = 8;
            row++;

            // ── Endpoint summary ──────────────────────────────────────────────
            MergeSectionHeader(ws, row, 8, "  ENDPOINT SUMMARY");
            ws.Row(row).Height = 24;
            row++;

            WriteTableHeader(ws, row,
                new[] { "Endpoint", "Total Calls", "Success (2xx)", "Failed", "Avg (ms)", "Min (ms)", "Max (ms)", "Rate %" });
            ws.Row(row).Height = 22;
            row++;

            int epIdx = 0;
            foreach (var grp in logs.GroupBy(l => l.Endpoint).OrderBy(g => g.Key))
            {
                var tot  = grp.Count();
                var succ = grp.Count(l => l.IsSuccess);
                var fail = tot - succ;
                var avg  = (long)grp.Average(l => l.ResponseTimeMs);
                var mn   = grp.Min(l => l.ResponseTimeMs);
                var mx   = grp.Max(l => l.ResponseTimeMs);
                var rate = Math.Round(succ * 100.0 / tot, 1);
                var bg   = fail > 0 ? ColWarnBg : (epIdx % 2 == 0 ? ColPaleGreen : ColWhite);

                var vals = new object[] { $"/api/chat/{grp.Key}", tot, succ, fail, avg, mn, mx, $"{rate}%" };
                for (int ci = 0; ci < vals.Length; ci++)
                {
                    var cell = ws.Cell(row, ci + 1);
                    cell.Value = vals[ci].ToString();
                    cell.Style.Fill.BackgroundColor = bg;
                    cell.Style.Font.FontColor = ColBlack;
                    cell.Style.Font.Bold = ci == 0;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = ci == 0
                        ? XLAlignmentHorizontalValues.Left
                        : XLAlignmentHorizontalValues.Center;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = ColMint;
                }
                row++;
                epIdx++;
            }

            // Column widths
            ws.Column(1).Width = 28;
            for (int c = 2; c <= 8; c++) ws.Column(c).Width = 18;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SHEET 2 — FULL DETAIL LOG
        // ════════════════════════════════════════════════════════════════════════
        private void BuildDetailSheet(IXLWorkbook wb, List<ApiRequestLog> logs, string shortLabel)
        {
            var ws = wb.AddWorksheet(shortLabel);
            ws.ShowGridLines = false;

            MergeTitle(ws, 1, 10, $"Full API Request Log — {shortLabel}",
                XLColor.FromHtml("#c8e6c9"), XLColor.FromHtml("#1b5e20"), 13, bold: true);
            ws.Row(1).Height = 28;

            WriteTableHeader(ws, 2,
                new[] { "#", "Date (UK)", "Time (UK)", "Endpoint", "Reference / Order No.", "Postcode", "Type", "OTP Channel", "Response (ms)", "Status" });
            ws.Row(2).Height = 22;

            var sorted = logs.OrderBy(l => l.UtcTimestamp).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                var log   = sorted[i];
                var ukDt  = TimeZoneInfo.ConvertTimeFromUtc(log.UtcTimestamp, UkTz);
                var dataRow = i + 3;

                XLColor bg;
                if (!log.IsSuccess)                     bg = ColErrorBg;
                else if (log.DataFound == true)         bg = i % 2 == 0 ? ColLightMint : ColPaleGreen;
                else if (log.DataFound == false)        bg = ColErrorBg;
                else                                    bg = i % 2 == 0 ? ColPaleGreen : ColWhite;

                var statusText = log.IsSuccess
                    ? (log.DataFound == true ? "Found" : log.DataFound == false ? "Not Found" : "OK")
                    : $"Error ({log.HttpStatus})";

                var vals = new object[]
                {
                    (i + 1).ToString(),
                    ukDt.ToString("dd-MMM-yyyy"),
                    ukDt.ToString("HH:mm:ss"),
                    $"/api/chat/{log.Endpoint}",
                    log.Reference  ?? "-",
                    log.Postcode   ?? "-",
                    log.RefType    ?? "-",
                    log.OtpChannel ?? "-",
                    log.ResponseTimeMs.ToString(),
                    statusText
                };

                for (int ci = 0; ci < vals.Length; ci++)
                {
                    var cell = ws.Cell(dataRow, ci + 1);
                    cell.Value = vals[ci].ToString();
                    cell.Style.Fill.BackgroundColor = bg;
                    cell.Style.Font.FontColor = ColBlack;
                    cell.Style.Font.FontSize = 9;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
                    cell.Style.Border.OutsideBorderColor = ColMint;
                    cell.Style.Alignment.Horizontal = ci == 0 || ci == 8
                        ? XLAlignmentHorizontalValues.Center
                        : XLAlignmentHorizontalValues.Left;
                }
            }

            ws.Column(1).Width  = 6;
            ws.Column(2).Width  = 15;
            ws.Column(3).Width  = 12;
            ws.Column(4).Width  = 24;
            ws.Column(5).Width  = 26;
            ws.Column(6).Width  = 14;
            ws.Column(7).Width  = 14;
            ws.Column(8).Width  = 14;
            ws.Column(9).Width  = 16;
            ws.Column(10).Width = 16;
            ws.SheetView.FreezeRows(2);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SHEET 3 — ENDPOINT BREAKDOWN
        // ════════════════════════════════════════════════════════════════════════
        private void BuildEndpointSheet(IXLWorkbook wb, List<ApiRequestLog> logs, string dateLabel)
        {
            var ws = wb.AddWorksheet("Endpoint Breakdown");
            ws.ShowGridLines = false;

            MergeTitle(ws, 1, 7, $"Endpoint Breakdown — {dateLabel}",
                XLColor.FromHtml("#c8e6c9"), XLColor.FromHtml("#1b5e20"), 13, bold: true);
            ws.Row(1).Height = 28;

            WriteTableHeader(ws, 2,
                new[] { "Endpoint", "Total Calls", "Success (2xx)", "Failed", "Avg (ms)", "Min (ms)", "Max (ms)" });
            ws.Row(2).Height = 22;

            var groups = logs.GroupBy(l => l.Endpoint).OrderByDescending(g => g.Count()).ToList();
            for (int i = 0; i < groups.Count; i++)
            {
                var grp  = groups[i];
                var tot  = grp.Count();
                var succ = grp.Count(l => l.IsSuccess);
                var fail = tot - succ;
                var avg  = (long)grp.Average(l => l.ResponseTimeMs);
                var mn   = grp.Min(l => l.ResponseTimeMs);
                var mx   = grp.Max(l => l.ResponseTimeMs);
                var bg   = fail > 0 ? ColWarnBg : (i % 2 == 0 ? ColPaleGreen : ColWhite);
                var vals = new object[] { $"/api/chat/{grp.Key}", tot, succ, fail, avg, mn, mx };

                for (int ci = 0; ci < vals.Length; ci++)
                {
                    var cell = ws.Cell(i + 3, ci + 1);
                    cell.Value = vals[ci].ToString();
                    cell.Style.Fill.BackgroundColor = bg;
                    cell.Style.Font.FontColor = ColBlack;
                    cell.Style.Font.Bold = ci == 0;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = ci == 0
                        ? XLAlignmentHorizontalValues.Left
                        : XLAlignmentHorizontalValues.Center;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = ColMint;
                }
            }

            // Totals footer
            if (groups.Any())
            {
                int totRow = groups.Count + 3;
                var totVals = new object[]
                {
                    "TOTAL",
                    logs.Count,
                    logs.Count(l => l.IsSuccess),
                    logs.Count(l => !l.IsSuccess),
                    logs.Any() ? (long)logs.Average(l => l.ResponseTimeMs) : 0L,
                    logs.Any() ? logs.Min(l => l.ResponseTimeMs) : 0L,
                    logs.Any() ? logs.Max(l => l.ResponseTimeMs) : 0L
                };
                for (int ci = 0; ci < totVals.Length; ci++)
                {
                    var cell = ws.Cell(totRow, ci + 1);
                    cell.Value = totVals[ci].ToString();
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#a5d6a7");
                    cell.Style.Font.FontColor = XLColor.FromHtml("#1b5e20");
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = ci == 0
                        ? XLAlignmentHorizontalValues.Left
                        : XLAlignmentHorizontalValues.Center;
                }
            }

            ws.Column(1).Width = 28;
            for (int c = 2; c <= 7; c++) ws.Column(c).Width = 16;
            ws.SheetView.FreezeRows(2);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SHEET 4 — HOURLY BREAKDOWN
        // ════════════════════════════════════════════════════════════════════════
        private void BuildHourlySheet(IXLWorkbook wb, List<ApiRequestLog> logs, string dateLabel)
        {
            var ws = wb.AddWorksheet("Hourly Breakdown");
            ws.ShowGridLines = false;

            MergeTitle(ws, 1, 8, $"Hourly Activity — {dateLabel}  (UK Time)",
                XLColor.FromHtml("#c8e6c9"), XLColor.FromHtml("#1b5e20"), 13, bold: true);
            ws.Row(1).Height = 28;

            WriteTableHeader(ws, 2,
                new[] { "Hour (UK)", "Track", "Confirm", "Decline", "OTP Sends", "Instructions", "Notes", "Total" });
            ws.Row(2).Height = 22;

            for (int h = 0; h < 24; h++)
            {
                int hour = h;
                int t  = logs.Count(l => l.Endpoint == "track"        && ToUkHour(l) == hour);
                int c  = logs.Count(l => l.Endpoint == "confirm"      && ToUkHour(l) == hour);
                int d  = logs.Count(l => l.Endpoint == "decline"      && ToUkHour(l) == hour);
                int o  = logs.Count(l => l.Endpoint == "send-otp"     && ToUkHour(l) == hour);
                int n  = logs.Count(l => l.Endpoint == "instructions" && ToUkHour(l) == hour);
                int nt = logs.Count(l => l.Endpoint == "note"         && ToUkHour(l) == hour);
                int tot = t + c + d + o + n + nt;

                var bg   = tot > 0 ? (h % 2 == 0 ? ColLightMint : ColPaleGreen) : ColWhite;
                var vals = new object[] { $"{h:00}:00 – {h:00}:59", t, c, d, o, n, nt, tot };
                for (int ci = 0; ci < vals.Length; ci++)
                {
                    var cell = ws.Cell(h + 3, ci + 1);
                    cell.Value = vals[ci].ToString();
                    cell.Style.Fill.BackgroundColor = bg;
                    cell.Style.Font.FontColor = ColBlack;
                    cell.Style.Font.Bold = ci == 7 && tot > 0;
                    cell.Style.Font.FontSize = 10;
                    cell.Style.Alignment.Horizontal = ci == 0
                        ? XLAlignmentHorizontalValues.Left
                        : XLAlignmentHorizontalValues.Center;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = ColMint;
                }
            }

            // Totals footer
            int totalsRow = 27;
            var totVals = new object[]
            {
                "DAILY TOTAL",
                logs.Count(l => l.Endpoint == "track"),
                logs.Count(l => l.Endpoint == "confirm"),
                logs.Count(l => l.Endpoint == "decline"),
                logs.Count(l => l.Endpoint == "send-otp"),
                logs.Count(l => l.Endpoint == "instructions"),
                logs.Count(l => l.Endpoint == "note"),
                logs.Count
            };
            for (int ci = 0; ci < totVals.Length; ci++)
            {
                var cell = ws.Cell(totalsRow, ci + 1);
                cell.Value = totVals[ci].ToString();
                cell.Style.Fill.BackgroundColor = ColDarkGreen;
                cell.Style.Font.FontColor = ColWhite;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 10;
                cell.Style.Alignment.Horizontal = ci == 0
                    ? XLAlignmentHorizontalValues.Left
                    : XLAlignmentHorizontalValues.Center;
            }

            ws.Column(1).Width = 22;
            for (int c = 2; c <= 8; c++) ws.Column(c).Width = 16;
            ws.SheetView.FreezeRows(2);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════════════
        private int ToUkHour(ApiRequestLog log) =>
            TimeZoneInfo.ConvertTimeFromUtc(log.UtcTimestamp, UkTz).Hour;

        /// <summary>
        /// Merges cols 1..colCount on the given row.
        /// Uses DARK text on LIGHT background so text is always visible
        /// regardless of whether the fill renders in the target Excel version.
        /// </summary>
        private static void MergeTitle(IXLWorksheet ws, int row, int colCount,
            string text, XLColor bgColor, XLColor fgColor, int fontSize, bool bold)
        {
            ws.Range(row, 1, row, colCount).Merge();
            var cell = ws.Cell(row, 1);
            cell.Value = text;
            cell.Style.Fill.BackgroundColor      = bgColor;
            cell.Style.Font.FontColor            = fgColor;
            cell.Style.Font.Bold                 = bold;
            cell.Style.Font.FontSize             = fontSize;
            cell.Style.Alignment.Horizontal      = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical        = XLAlignmentVerticalValues.Center;
            cell.Style.Border.BottomBorder       = XLBorderStyleValues.Medium;
            cell.Style.Border.BottomBorderColor  = XLColor.FromHtml("#1b5e20");
        }

        /// <summary>
        /// Section header bar — dark green bold text on mint background.
        /// Dark text on light bg guarantees visibility.
        /// </summary>
        private static void MergeSectionHeader(IXLWorksheet ws, int row, int colCount, string text)
        {
            ws.Range(row, 1, row, colCount).Merge();
            var cell = ws.Cell(row, 1);
            cell.Value = text;
            cell.Style.Fill.BackgroundColor      = XLColor.FromHtml("#a5d6a7");
            cell.Style.Font.FontColor            = XLColor.FromHtml("#1b5e20");
            cell.Style.Font.Bold                 = true;
            cell.Style.Font.FontSize             = 11;
            cell.Style.Alignment.Vertical        = XLAlignmentVerticalValues.Center;
            cell.Style.Border.TopBorder          = XLBorderStyleValues.Medium;
            cell.Style.Border.TopBorderColor     = XLColor.FromHtml("#1b5e20");
            cell.Style.Border.BottomBorder       = XLBorderStyleValues.Medium;
            cell.Style.Border.BottomBorderColor  = XLColor.FromHtml("#1b5e20");
        }

        /// <summary>
        /// Column header row — mint background, dark green bold text.
        /// Dark text on light bg is always visible.
        /// </summary>
        private static void WriteTableHeader(IXLWorksheet ws, int row, string[] headers)
        {
            for (int ci = 0; ci < headers.Length; ci++)
            {
                var cell = ws.Cell(row, ci + 1);
                cell.Value = headers[ci];
                cell.Style.Fill.BackgroundColor      = XLColor.FromHtml("#c8e6c9");
                cell.Style.Font.FontColor            = XLColor.FromHtml("#1b5e20");
                cell.Style.Font.Bold                 = true;
                cell.Style.Font.FontSize             = 10;
                cell.Style.Alignment.Horizontal      = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical        = XLAlignmentVerticalValues.Center;
                cell.Style.Border.OutsideBorder      = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#2a8f38");
            }
        }

        private void WriteKpiRow(IXLWorksheet ws, int row,
            string label1, string value1, string label2, string value2)
        {
            // Left KPI — label (cols 1-3 merged) + value (col 4)
            for (int c = 1; c <= 4; c++)
                ws.Cell(row, c).Style.Fill.BackgroundColor = ColKpiBg;
            ws.Range(row, 1, row, 3).Merge();

            var lc1 = ws.Cell(row, 1);
            lc1.Value = label1;
            lc1.Style.Fill.BackgroundColor = ColKpiBg;
            lc1.Style.Font.FontColor       = ColBlack;
            lc1.Style.Font.FontSize        = 10;
            lc1.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            lc1.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            lc1.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            lc1.Style.Border.OutsideBorderColor = ColMint;

            var vc1 = ws.Cell(row, 4);
            vc1.Value = value1;
            vc1.Style.Fill.BackgroundColor = ColKpiBg;
            vc1.Style.Font.FontColor       = XLColor.FromHtml("#1b5e20");
            vc1.Style.Font.Bold            = true;
            vc1.Style.Font.FontSize        = 14;
            vc1.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            vc1.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            vc1.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            vc1.Style.Border.OutsideBorderColor = ColMint;

            // Spacer col 5
            ws.Cell(row, 5).Style.Fill.BackgroundColor = ColWhite;

            // Right KPI — label (cols 6-8 merged) + value (col 9)  … but we only have 8 cols
            // Remap to cols 5-7 (label) + col 8 (value)
            for (int c = 5; c <= 8; c++)
                ws.Cell(row, c).Style.Fill.BackgroundColor = ColLightMint;
            ws.Range(row, 5, row, 7).Merge();

            var lc2 = ws.Cell(row, 5);
            lc2.Value = label2;
            lc2.Style.Fill.BackgroundColor = ColLightMint;
            lc2.Style.Font.FontColor       = ColBlack;
            lc2.Style.Font.FontSize        = 10;
            lc2.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            lc2.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            lc2.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            lc2.Style.Border.OutsideBorderColor = ColMint;

            var vc2 = ws.Cell(row, 8);
            vc2.Value = value2;
            vc2.Style.Fill.BackgroundColor = ColLightMint;
            vc2.Style.Font.FontColor       = XLColor.FromHtml("#1b5e20");
            vc2.Style.Font.Bold            = true;
            vc2.Style.Font.FontSize        = 14;
            vc2.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            vc2.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            vc2.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            vc2.Style.Border.OutsideBorderColor = ColMint;

            ws.Row(row).Height = 26;
        }
    }
}
