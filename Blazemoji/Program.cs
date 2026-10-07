using Blazemoji;
using Blazemoji.Components;
using Blazemoji.Services.Library;
using Blazemoji.Services.Projects;
using Blazemoji.Toolchain.Http;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // The editor hands a file's whole text to the server, and a saved file is read back
    // from the browser in one piece. The default limit of 32 KB is smaller than one of
    // Grapevine's source files, and a message over the limit ends the session. 4 MiB is
    // what the toolchain service accepts for a whole project.
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 4 * 1024 * 1024);

// What any host of the editor registers. docs/hosting.md explains each line.
builder.Services.AddMudServices();
builder.Services.AddToolchainClient(builder.Configuration);
builder.Services.AddBlazemojiEditor(builder.Configuration);

// What this host supplies because it runs in a browser: projects and snippets in local storage.
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<IProjectStore, LocalStorageProjectStore>();
builder.Services.AddTransient<ILibraryService, LibraryService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
