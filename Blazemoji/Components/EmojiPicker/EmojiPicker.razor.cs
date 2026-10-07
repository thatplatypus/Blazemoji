using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Utilities;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Blazemoji.Components.EmojiPicker
{
    public partial class EmojiPicker : MudPicker<string>
    {
        public EmojiPicker()
        {
            AdornmentIcon = Icons.Material.Outlined.EmojiEmotions;
            AdornmentAriaLabel = "Open Emoji Picker";
        }

        [Parameter]
        public EventCallback<string> OnEmojiPicked { get; set; }

        protected override IConverter<string?, string?> GetDefaultConverter() => new DefaultConverter<string>();

        protected async Task OnEmojiSelectedAsync(EmojicodeKeyword emoji)
        {
            await CloseAsync(PickerActions == null);
            await OnEmojiPicked.InvokeAsync(emoji.Emoji);
        }

        protected string ToolbarClassname =>
        new CssBuilder("mud-picker-timepicker-toolbar")
          .AddClass("mud-width-full")
          .AddClass($"mud-picker-timepicker-toolbar-landscape", Orientation == Orientation.Landscape && PickerVariant == PickerVariant.Static)
          .AddClass(ToolbarClass)
        .Build();

    }
}
