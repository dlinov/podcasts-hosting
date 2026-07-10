using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PodcastsHosting.Configuration;
using PodcastsHosting.Data;
using PodcastsHosting.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddLogging();
builder.Services.AddOptions<PodcastOptions>()
    .BindConfiguration(PodcastOptions.SectionName)
    .ValidateDataAnnotations()
    .Validate(
        options => options.PublicBaseUrl is { IsAbsoluteUri: true }
                   && (options.PublicBaseUrl.Scheme == Uri.UriSchemeHttp
                       || options.PublicBaseUrl.Scheme == Uri.UriSchemeHttps),
        "App:PublicBaseUrl must be an absolute HTTP or HTTPS URL.")
    .ValidateOnStart();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                builder.Configuration.GetConnectionString("PodcastsHosting"),
                sqlOptions => sqlOptions.EnableRetryOnFailure()));
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
    {
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultUI()
            .AddDefaultTokenProviders();
builder.Services.AddControllersWithViews();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestPropertiesAndHeaders
                            | HttpLoggingFields.RequestBody
                            | HttpLoggingFields.ResponsePropertiesAndHeaders;
    options.RequestBodyLogLimit = 4096;
    options.ResponseBodyLogLimit = 4096;
});

builder.Services.AddScoped<IFileService, FileService>();

// Configure the maximum request body size
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 1024L * 1024 * 1024; // 1 GB
});

var app = builder.Build();

// Apply migrations automatically
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var podcastOptions = scope.ServiceProvider.GetRequiredService<IOptions<PodcastOptions>>().Value;
    logger.LogInformation(
        "Current app settings: ChannelTitle={ChannelTitle}; ChannelDescription={ChannelDescription}; PublicBaseUrl={PublicBaseUrl}; RegistrationOpen={RegistrationOpen}",
        podcastOptions.ChannelTitle,
        podcastOptions.ChannelDescription,
        podcastOptions.PublicBaseUrl,
        podcastOptions.RegistrationOpen);
    logger.LogInformation(
        "Is database accessible: {}",
        dbContext.Database.CanConnect());
    logger.LogInformation(
        "Migrations already applied:\n{}",
        string.Join(Environment.NewLine, await dbContext.Database.GetAppliedMigrationsAsync()));
    logger.LogInformation(
        "Migrations pending application:\n{}",
        string.Join(Environment.NewLine, await dbContext.Database.GetPendingMigrationsAsync()));
    logger.LogInformation("Applying migrations...");
    dbContext.Database.Migrate();
    logger.LogInformation("Migrations applied successfully!");
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();
