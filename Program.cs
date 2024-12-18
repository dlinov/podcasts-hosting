using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PodcastsHosting.Data;
using PodcastsHosting.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddLogging();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("PodcastsHosting")));
builder.Services.AddDefaultIdentity<IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultUI()
            .AddDefaultTokenProviders();
builder.Services.AddControllersWithViews();
builder.Services.AddHttpLogging(options =>
{
    options.LoggingFields = HttpLoggingFields.RequestPropertiesAndHeaders
                            | HttpLoggingFields.RequestBody
                            | HttpLoggingFields.ResponsePropertiesAndHeaders;
    options.RequestBodyLogLimit = 4096;
    options.ResponseBodyLogLimit = 4096;
});

builder.Services.AddScoped<FileService>();

// Configure the maximum request body size
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 512 * 1024 * 1024; // 512 MB
});

var app = builder.Build();

// Apply migrations automatically
try
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
    var appSettings = config.GetSection("App");
    var appSettingsAsString = string.Join("; ",
        appSettings.AsEnumerable().Select(x => x.ToString()));
    logger.LogInformation("Current app settings: {Settings}", appSettingsAsString);
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
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
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

app.Run();
