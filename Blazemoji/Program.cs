using Blazemoji;
using Blazemoji.Components;
using Blazemoji.Services.Compiler;
using Blazemoji.Services.Library;
using Blazemoji.Shared.State;
using MudBlazor.Services;
using System;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddTransient<ILibraryService, LibraryService>();
builder.Services.AddScoped<ICompilerService, CompilerService>();
builder.Services.AddScoped<ICodeRunner, CodeRunner>();
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
