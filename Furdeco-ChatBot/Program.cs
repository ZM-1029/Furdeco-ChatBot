using Furdeco_ChatBot.Data;
using Furdeco_ChatBot.Hubs;
using Furdeco_ChatBot.Middleware;
using Furdeco_ChatBot.Service;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
//builder.Services.AddDbContext<AppDbContext>(options =>
//{
//    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
//    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
//    options.EnableSensitiveDataLogging();
//    options.LogTo(Console.WriteLine, LogLevel.Error);
//});
// ── Existing chatbot services (unchanged) ─────────────────────────────────
builder.Services.AddSingleton<IOtpService, OtpService>();
builder.Services.AddHttpClient<IVoodooSmsService, VoodooSmsService>();

// Daily report services
builder.Services.AddSingleton<IApiLoggerService, ApiLoggerService>();
builder.Services.AddSingleton<ExcelReportService>();
builder.Services.AddHostedService<DailyReportHostedService>();

builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("GSIT", c => c.Timeout = TimeSpan.FromSeconds(45));
// ─────────────────────────────────────────────────────────────────────────

// ── Live Chat Layer ───────────────────────────────────────────────────────
// PostgreSQL (EF Core)
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("LiveChat")));

// Live chat services
builder.Services.AddScoped<ChatSessionService>();
builder.Services.AddScoped<TicketService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<CannedReplyService>();
builder.Services.AddScoped<SettingsService>();

// SLA breach monitor
builder.Services.AddHostedService<SlaMonitorHostedService>();

// SignalR — use Azure SignalR Service when a connection string is configured;
// otherwise fall back to in-process SignalR so the app still starts (hub logic unchanged).
var azureSignalRConn = builder.Configuration["Azure:SignalR:ConnectionString"];
var signalRBuilder = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(azureSignalRConn))
{
    // Namespace the hub per environment. A single Azure SignalR resource shared
    // across environments otherwise pools ALL app servers (dev + prod) under the
    // same hub, so client connections and hub calls get routed to the wrong
    // environment's server. ApplicationName is used as a hub-name prefix,
    // isolating them. This chatbot and the WALMS API in the SAME environment MUST
    // use the SAME value so the customer and agent still share groups.
    // Absent/empty => unchanged behaviour (no prefix).
    var signalRAppName = builder.Configuration["Azure:SignalR:ApplicationName"];
    signalRBuilder.AddAzureSignalR(options =>
    {
        options.ConnectionString = azureSignalRConn;
        if (!string.IsNullOrWhiteSpace(signalRAppName))
            options.ApplicationName = signalRAppName;
        // CRITICAL: this chatbot and WALMS deliberately share the hub (same
        // resource + ApplicationName + hub name) so customer and agent share
        // groups. Azure SignalR load-balances each CLIENT across ALL app servers
        // on the hub — without stickiness a CUSTOMER's calls (JoinQueue/
        // SendMessage/...) can be routed to the WALMS server, and an agent's to
        // this one, hitting a hub that lacks those methods → silent failures.
        // Required = every client's invocations go to the app server that served
        // its negotiate. Group broadcasts still reach both apps' clients.
        options.ServerStickyMode = Microsoft.Azure.SignalR.ServerStickyMode.Required;
    });
    Console.WriteLine($"[SignalR] Using Azure SignalR Service. ApplicationName='{signalRAppName}'.");
}
else
{
    Console.WriteLine("[SignalR] Azure SignalR connection string not configured — using in-process SignalR.");
}

// JWT authentication
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey     = jwtSection["Key"]      ?? throw new InvalidOperationException("Jwt:Key missing");
var jwtIssuer  = jwtSection["Issuer"]   ?? "FurdecoLiveChat";
var jwtAudience = jwtSection["Audience"] ?? "FurdecoAgents";

// Additionally trust WALMS-issued tokens so WALMS-APP users can reach the chat
// API without a separate login. These come from the WALMS backend's JWT config;
// if WalmsKey is blank, only the chatbot's own tokens are accepted (unchanged).
var walmsKey      = jwtSection["WalmsKey"];
var walmsIssuer   = jwtSection["WalmsIssuer"];
var walmsAudience = jwtSection["WalmsAudience"];
var hasWalms      = !string.IsNullOrWhiteSpace(walmsKey);

var validIssuers   = new List<string> { jwtIssuer };
var validAudiences = new List<string> { jwtAudience };
var signingKeys    = new List<SecurityKey> { new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)) };
if (hasWalms)
{
    signingKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(walmsKey!)));
    if (!string.IsNullOrWhiteSpace(walmsIssuer))   validIssuers.Add(walmsIssuer!);
    if (!string.IsNullOrWhiteSpace(walmsAudience)) validAudiences.Add(walmsAudience!);
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            // If WALMS tokens carry no audience claim, set Jwt:ValidateAudience=false in config.
            ValidateAudience         = jwtSection.GetValue("ValidateAudience", true),
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuers             = validIssuers,
            ValidAudiences           = validAudiences,
            IssuerSigningKeys        = signingKeys
        };
        // Allow JWT via query string for SignalR connections
        opts.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                var path        = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });
// ─────────────────────────────────────────────────────────────────────────

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin",
        policy =>
        {
            // Allow any origin for the chatbot iframe + admin dashboard.
            // Credentials (cookies) not used — JWT via header/query is fine with AllowAnyOrigin.
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
});

var logPath = Path.Combine(builder.Environment.ContentRootPath, "Logs", "log-.txt");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Async(a => a.File(
        path: logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 10,
        fileSizeLimitBytes: 10_000_000,
        rollOnFileSizeLimit: true,
        shared: true
    ))
    .CreateLogger();

// Replace default logger
builder.Host.UseSerilog();

var app = builder.Build();

// ── Run EF Core migrations on startup ────────────────────────────────────
// Chat schema is owned/migrated by WALMS (central Pushpak-Prod3 DB) — do not auto-migrate here.
//using (var scope = app.Services.CreateScope())
//{
//    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
//    db.Database.Migrate();
//}
// ─────────────────────────────────────────────────────────────────────────

// Optional: global exception logging
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionHandler = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
        var ex = exceptionHandler?.Error;

        if (ex != null)
        {
            Log.Error(ex, "Unhandled exception");
        }

        context.Response.StatusCode = 500;
    });
});

app.Use(async (context, next) =>
{
    context.Response.Headers.Remove("X-Frame-Options");
    context.Response.Headers.Append("Content-Security-Policy", "frame-ancestors *;");
    await next();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{

    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();  // HTTPS only in production
}
app.UseCors("AllowSpecificOrigin");
app.UseStaticFiles();

app.UseMiddleware<ApiLoggingMiddleware>();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// API routes must be matched first — default route would otherwise match /api/chat/track as controller=api, action=chat
app.MapControllers();
app.MapHub<LiveChatHub>("/hubs/livechat");
app.MapControllerRoute("chat", "chat", new { controller = "ChatPage", action = "Index" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=ChatPage}/{action=Index}/{id?}",
    constraints: new { controller = new Microsoft.AspNetCore.Routing.Constraints.RegexRouteConstraint("^(?!api$).*") });

app.Run();

// Use this script for chatbot in static site 

//@section Scripts
//{
//    <script src="https://dev.snapsend.co:550/scripts/embed.js"></script>
//}





//https://localhost:7116/api/report/from-log?path=D:\log-20260518.txt
