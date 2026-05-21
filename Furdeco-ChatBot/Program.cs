using Furdeco_ChatBot.Service;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IOtpService, OtpService>();
builder.Services.AddHttpClient<IVoodooSmsService, VoodooSmsService>();
// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("GSIT", c => c.Timeout = TimeSpan.FromSeconds(45));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin",
        builder =>
        {
            builder.AllowAnyOrigin()
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
if (!app.Environment.IsDevelopment() )
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
   

    app.UseHsts();
}
app.UseCors("AllowSpecificOrigin");
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// API routes must be matched first — default route would otherwise match /api/chat/track as controller=api, action=chat
app.MapControllers();
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