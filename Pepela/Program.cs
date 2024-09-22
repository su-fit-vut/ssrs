using System.Data.Common;
using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pepela.Configuration;
using Pepela.Data;
using Pepela.Jobs;
using Pepela.Services;
using Quartz;
using Quartz.AspNetCore;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

var builder = WebApplication.CreateBuilder(args);
var appOptions = builder.Configuration.GetRequiredSection("App").Get<AppOptions>()!;

// Add services to the container.
builder.Services.Configure<SeatsOptions>(builder.Configuration.GetRequiredSection("Seats"));
builder.Services.Configure<MailOptions>(builder.Configuration.GetRequiredSection("Mail"));
// builder.Services.Configure<LinkGenerationOptions>(builder.Configuration.GetRequiredSection("Links"));

builder.Services.AddHttpContextAccessor();

// Quartz for e-mail scheduling
builder.Services.AddScoped<SendReminderEmailJob>();
builder.Services.AddSingleton<DbDataSource, NpgsqlDataSource>(sp =>
{
    var npgsqlBuilder = new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("AppDb"));
    npgsqlBuilder.UseNodaTime();
    return npgsqlBuilder.Build();
});

builder.Services.AddQuartz(q =>
{
    q.CheckConfiguration = true;
    q.UseDedicatedThreadPool(10);
    q.AddDataSourceProvider();
    q.UsePersistentStore(store =>
    {
        store.UseProperties = false;

        store.UsePostgres(ado => { ado.UseDataSourceConnectionProvider(); });
        store.UseSystemTextJsonSerializer();
    });
});
builder.Services.AddQuartzServer(options => { options.WaitForJobsToComplete = true; });

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("AppDb"),
        npgsql => npgsql.UseNodaTime());
});

builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<LinkService>();
builder.Services.AddScoped<ReservationService>();

builder.Services.AddRazorPages()
    .AddRazorRuntimeCompilation();
if (!string.IsNullOrWhiteSpace(appOptions.KnownProxyNetwork))
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders =
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse(appOptions.KnownProxyNetwork),
            appOptions.KnownProxyPrefixLength));
    });
}

var kisAuthOptions = builder.Configuration.GetRequiredSection("KisAuth").Get<KisAuthOptions>()!;
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultChallengeScheme = "kis";
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options => { })
    .AddOpenIdConnect("kis", "KIS Auth",
        options =>
        {
            options.Authority = kisAuthOptions.Authority;
            options.ClientId = kisAuthOptions.ClientId;
            options.ClientSecret = kisAuthOptions.ClientSecret;
            options.RequireHttpsMetadata = true;
            options.UsePkce = true;
            options.ResponseType = "code";

            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("roles");

            options.MapInboundClaims = true;
        });

var app = builder.Build();

var migrateDb = Environment.CommandLine.Contains("--migrate-db");
if (migrateDb)
{
    app.Logger.LogInformation("Migrating database");
    using var scope = app.Services.CreateScope();
    using var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

if (!string.IsNullOrWhiteSpace(appOptions.KnownProxyNetwork))
    app.UseForwardedHeaders();

if (!string.IsNullOrWhiteSpace(appOptions.PathBase))
    app.UsePathBase(appOptions.PathBase);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days.
    app.UseHsts();
}

if (appOptions.UseHttpsRedirection)
    app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

app.Run();