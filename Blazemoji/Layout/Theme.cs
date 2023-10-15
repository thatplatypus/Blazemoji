using MudBlazor;

namespace Blazemoji.Layout
{
    public class Theme : MudTheme
    {
        public Theme() 
        {
            Palette = new PaletteLight()
            {
                Primary = "#312d67",
                AppbarBackground = "#312d67",
                Secondary = "#565E88",
                Tertiary = "#6E8FAB",
                Dark = "#150F1A",
                DarkLighten = "#E1DBDE"
            };

            PaletteDark = new PaletteDark()
            {
                Primary = "#172a4a",
            };
        }
    }
}
