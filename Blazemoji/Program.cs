using Blazemoji.Emojicode;
using Blazemoji.Services.Compiler;
using Blazemoji.Services.Execution;
using Blazemoji.Services.Library;
using Blazemoji.Shared.State;
using Blazored.LocalStorage;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddTransient<ICompilerService, CompilerService>();
builder.Services.AddTransient<ICodeExecutionService, CodeExecutionService>();
builder.Services.AddTransient<ILibraryService, LibraryService>();
builder.Services.AddSingleton(new LocalStorageFiles());

//Register emojicode keyword implementations
var emojicodeKeywordTypes = typeof(EmojicodeKeyword).Assembly.GetTypes()
    .Where(t => t.IsSubclassOf(typeof(EmojicodeKeyword)));

foreach (var type in emojicodeKeywordTypes)
{
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

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
