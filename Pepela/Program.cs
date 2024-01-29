using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Pepela.Configuration;
using Pepela.Data;
using Pepela.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.Configure<SeatsOptions>(builder.Configuration.GetRequiredSection("Seats"));
builder.Services.Configure<MailOptions>(builder.Configuration.GetRequiredSection("Mail"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("AppDb"),
        npgsql => npgsql.UseNodaTime());
});

builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<LinkService>();
builder.Services.AddScoped<ReservationService>();

builder.Services.AddRazorPages();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders();
    app.UsePathBase("/mucha");
    
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

//app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();