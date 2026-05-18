using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;
using Ticketing.Api.Data;
using Ticketing.Api.Models;
using Ticketing.Api.Seeding;
using Ticketing.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Config / DB ─────────────────────────────────────────────────────────────
var conn = builder.Configuration.GetConnectionString("TicketsData")
           ?? throw new InvalidOperationException("ConnectionStrings:TicketsData missing");

builder.Services.AddDbContext<TicketsDbContext>(o => o.UseSqlServer(conn));

// ── Identity ────────────────────────────────────────────────────────────────
// Small, internal app — low-friction password rules. Tighten if exposed publicly.
builder.Services.AddIdentity<TicketingUser, IdentityRole>(o =>
    {
        o.Password.RequireDigit = false;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequiredLength = 8;
        o.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<TicketsDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IdentitySeeder>();

// ── Domain services ─────────────────────────────────────────────────────────
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddScoped<OtlpLogsReceiver>();
builder.Services.AddScoped<ITicketAiService, TicketAiService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IDigestService, DigestService>();
builder.Services.AddHttpClient(TicketAiService.HttpClientName, c =>
{
    // NVIDIA NIM endpoint; overridable via NvidiaApi:BaseUrl.
    var baseUrl = builder.Configuration["NvidiaApi:BaseUrl"] ?? "https://integrate.api.nvidia.com/v1/";
    c.BaseAddress = new Uri(baseUrl);
    c.Timeout = TimeSpan.FromSeconds(20);
});

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddOpenApi();

// Generous body limit so big stack-trace bursts from the collector aren't dropped.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 32_000_000);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 32_000_000);

// ── Auth: validate Ticketing's own JWTs ─────────────────────────────────────
// AddIdentity above already set the default scheme to ApplicationCookie; we
// override here so [Authorize] on API controllers uses JWT bearer by default,
// while Identity's cookie scheme stays available for any future MVC pages.
var jwtKey = builder.Configuration["Jwt:Key"]
             ?? throw new InvalidOperationException("Jwt:Key missing");
builder.Services.AddAuthentication(o =>
    {
        o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(o =>
    {
        o.RequireHttpsMetadata = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "ticketing",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "ticketing",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            NameClaimType = System.Security.Claims.ClaimTypes.Email,
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddPolicy("AllowAll",
    p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ── Pipeline ────────────────────────────────────────────────────────────────
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    var logger = sp.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = sp.GetRequiredService<TicketsDbContext>();
        db.Database.Migrate();

        var seeder = sp.GetRequiredService<IdentitySeeder>();
        await seeder.SeedAsync();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Ticketing startup (migrate / seed) failed");
    }
}

app.MapOpenApi();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.Run();
