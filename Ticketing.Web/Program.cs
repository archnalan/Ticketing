using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Refit;
using Ticketing.Web.Components;
using Ticketing.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Data protection: persist keys so antiforgery + auth cookies survive restarts.
var dpKeysDir = builder.Configuration["DataProtection:KeysDirectory"]
                ?? Path.Combine(builder.Environment.ContentRootPath, "dp-keys");
Directory.CreateDirectory(dpKeysDir);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dpKeysDir));

// ── Blazor Server + MVC controllers (for /_auth/* signin/signout) ───────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

// ── Cookie authentication ───────────────────────────────────────────────────
// AuthorizeRouteView's challenge path needs a registered scheme; this also
// gives us a normal sign-in/sign-out flow for the kanban.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "ticketing.auth";
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/login";
        o.LogoutPath = "/_auth/signout";
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = false; // expire when the JWT expires
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddTransient<BearerHandler>();

// ── Refit clients ───────────────────────────────────────────────────────────
var ticketsApiBase = new Uri(builder.Configuration["Apis:Tickets"] ?? "http://localhost:8090");

builder.Services.AddRefitClient<ITicketsApi>()
    .ConfigureHttpClient(c => c.BaseAddress = ticketsApiBase)
    .AddHttpMessageHandler<BearerHandler>();

// Login proxy — no bearer needed (the /api/auth/login endpoint is anonymous).
builder.Services.AddRefitClient<ITicketingAuthApi>()
    .ConfigureHttpClient(c => c.BaseAddress = ticketsApiBase);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

if (!string.IsNullOrEmpty(app.Configuration["ASPNETCORE_HTTPS_PORT"]))
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
