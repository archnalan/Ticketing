using Ticketing.Web.Components;
using Ticketing.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Refit;

var builder = WebApplication.CreateBuilder(args);

// ── Data protection: persist keys so antiforgery survives container restarts.
var dpKeysDir = builder.Configuration["DataProtection:KeysDirectory"]
                ?? Path.Combine(builder.Environment.ContentRootPath, "dp-keys");
Directory.CreateDirectory(dpKeysDir);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dpKeysDir));

// ── Blazor Server with auth-aware routing ───────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddAuthenticationCore();
builder.Services.AddAuthorizationCore();

// One session per circuit; AuthState exposes the JWT to the BearerHandler.
builder.Services.AddScoped<SessionStore>();
builder.Services.AddScoped<AuthState>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthState>());
builder.Services.AddTransient<BearerHandler>();

// ── Refit clients ───────────────────────────────────────────────────────────
var ticketsApiBase = new Uri(builder.Configuration["Apis:Tickets"] ?? "http://localhost:8090");
var frelodyApiBase = new Uri(builder.Configuration["Apis:Frelody"] ?? "http://localhost:8080");

builder.Services.AddRefitClient<ITicketsApi>()
    .ConfigureHttpClient(c => c.BaseAddress = ticketsApiBase)
    .AddHttpMessageHandler<BearerHandler>();

builder.Services.AddRefitClient<IFrelodyAuthApi>()
    .ConfigureHttpClient(c => c.BaseAddress = frelodyApiBase);

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
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
