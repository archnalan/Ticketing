using Ticketing.Api.Data;
using Ticketing.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ── Config / DB ─────────────────────────────────────────────────────────────
var conn = builder.Configuration.GetConnectionString("TicketsData")
           ?? throw new InvalidOperationException("ConnectionStrings:TicketsData missing");

builder.Services.AddDbContext<TicketsDbContext>(o => o.UseSqlServer(conn));

// ── Services ────────────────────────────────────────────────────────────────
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

// ── Auth: validate JWTs issued by FRELODYAPIs ───────────────────────────────
var jwtKey = builder.Configuration["Jwt:Key"]
             ?? throw new InvalidOperationException("Jwt:Key missing");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.RequireHttpsMetadata = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            // FRELODYAPIs adds the role claim under the standard role claim type.
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
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        app.Services.GetRequiredService<ILogger<Program>>()
            .LogError(ex, "Ticketing migrate failed");
    }
}

app.MapOpenApi();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.Run();
