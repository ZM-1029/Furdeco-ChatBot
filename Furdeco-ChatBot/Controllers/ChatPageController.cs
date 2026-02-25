// ─────────────────────────────────────────────────────────────────
// Add this to your existing Program.cs route mappings
// OR create a new ChatPageController.cs (code below)
// ─────────────────────────────────────────────────────────────────

// In Program.cs, add CORS + the /chat route:
//
//   builder.Services.AddCors(options =>
//   {
//       options.AddPolicy("EmbedPolicy", policy =>
//           policy
//               .WithOrigins(
//                   "https://your-static-site.com",   // ← add your static site origins
//                   "https://www.your-static-site.com"
//               )
//               .AllowAnyMethod()
//               .AllowAnyHeader()
//       );
//   });
//
//   // After app.UseRouting():
//   app.UseCors("EmbedPolicy");
//
//   // Add alongside your default route:
//   app.MapControllerRoute(
//       name: "chat",
//       pattern: "chat",
//       defaults: new { controller = "ChatPage", action = "Index" }
//   );

using Microsoft.AspNetCore.Mvc;

namespace Furdeco_ChatBot.Controllers
{
    /// <summary>
    /// Serves the chatbot UI at /chat — iframe-friendly (no layout, CORS-safe).
    /// </summary>
    public class ChatPageController : Controller
    {
        public IActionResult Index()
        {
            // Allow embedding from any origin (the page itself has no sensitive data)
            Response.Headers["X-Frame-Options"] = "ALLOWALL";
            Response.Headers["Content-Security-Policy"] = "frame-ancestors *";
            return View();
        }
    }
}
