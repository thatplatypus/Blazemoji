using Blazemoji;
using Blazemoji.Components;
using Blazemoji.Services.Library;
using Blazemoji.Emojicode.Intelligence;
using Blazemoji.Interop;
using Blazemoji.Services.Projects;
using Blazemoji.Shared.State;
using Blazemoji.Toolchain.Http;
using MudBlazor.Services;
using System;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // The editor hands a file's whole text to the server, and a saved file is read back
    // from the browser in one piece. The default limit of 32 KB is smaller than one of
    // Grapevine's source files, and a message over the limit ends the session. 4 MiB is
    // what the toolchain service accepts for a whole project.
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 4 * 1024 * 1024);
builder.Services.AddMudServices();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddTransient<ILibraryService, LibraryService>();
builder.Services.AddToolchainClient(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RunState>();
builder.Services.Configure<ProjectTemplateOptions>(builder.Configuration.GetSection(ProjectTemplateOptions.SectionName));
builder.Services.AddSingleton<IProjectTemplates, FileProjectTemplates>();
builder.Services.AddScoped<IProjectStore, LocalStorageProjectStore>();
builder.Services.AddScoped<ProjectState>();
builder.Services.AddScoped<RequestState>();
builder.Services.AddSingleton<IEmojiNames, EmojiNames>();
builder.Services.AddSingleton<ICodeIntelligence, CodeIntelligence>();
builder.Services.AddScoped<IPackageLibrary, PackageLibrary>();
builder.Services.AddScoped<EmojicodeLanguageInterop>();
builder.Services.AddSingleton(new LocalStorageFiles());

//Register emojicode keyword implementations
var emojicodeKeywordTypes = typeof(EmojicodeKeyword).Assembly.GetTypes()
    .Where(t => t.IsSubclassOf(typeof(EmojicodeKeyword)));

foreach (var type in emojicodeKeywordTypes)
{
    if (Activator.CreateInstance(type) is EmojicodeKeyword keyword && keyword.Emoji != null)
        builder.Services.AddSingleton(typeof(EmojicodeKeyword), type);
}

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
