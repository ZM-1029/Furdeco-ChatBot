using Furdeco_ChatBot.Service;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IOtpService, OtpService>();
builder.Services.AddHttpClient<IVoodooSmsService, VoodooSmsService>();
// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("GSIT", c => c.Timeout = TimeSpan.FromSeconds(20));

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

var app = builder.Build();

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