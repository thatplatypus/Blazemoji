using Microsoft.AspNetCore.Components;
using MudBlazor;
using MudBlazor.Utilities;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Blazemoji.Components.EmojiPicker
{
    public partial class EmojiPicker : MudPicker<string>
    {
        public EmojiPicker() : base(new DefaultConverter<string>()) 
        {
            Converter.GetFunc = OnGet;
            Converter.SetFunc = OnSet;
            AdornmentIcon = Icons.Material.Outlined.EmojiEmotions;
            AdornmentAriaLabel = "Open Emoji Picker";
        }

        [Parameter]
        public EventCallback<string> OnEmojiPicked { get; set; }

        private string OnSet(string emoji)
        {
            if (emoji == null)
                return string.Empty;

            return emoji;
        }

        private string OnGet(string value)
        {
            return value;
        }

        protected void OnEmojiSelected(EmojicodeKeyword emoji)
        {
            SubmitAndClose();
            OnSet(emoji.Emoji);
            OnEmojiPicked.InvokeAsync(emoji.Emoji);
        }

        protected void SubmitAndClose()
        {
            if (PickerActions == null)
            {
                Submit();
            }

            Close();
        }

        protected string ToolbarClass =>
        new CssBuilder("mud-picker-timepicker-toolbar")
          .AddClass($"mud-picker-timepicker-toolbar-landscape", Orientation == Orientation.Landscape && PickerVariant == PickerVariant.Static)
          .AddClass(Class)
        .Build();

    }
}
