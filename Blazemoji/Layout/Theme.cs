using MudBlazor;

namespace Blazemoji.Layout
{
    /// <summary>
    /// Neutral surfaces, round corners and no shadows, so that the emoji are the colourful
    /// thing on the page. The one accent is taken from the 🔥 in the title.
    /// </summary>
    /// <remarks>
    /// Every colour that carries text, and every colour that text is drawn in, was checked
    /// against the WCAG contrast minimum of 4.5 to 1. That is why the light theme's accent is
    /// a deeper orange than the dark theme's: the bright one is unreadable as text on white.
    /// </remarks>
    public class Theme : MudTheme
    {
        private const string Ink = "#1F1F23";
        private const string NightInk = "#1B1B1F";

        public Theme()
        {
            PaletteLight = new PaletteLight()
            {
                Primary = "#B84A00",
                PrimaryContrastText = "#FFFFFF",
                Secondary = "#FFCC4D",
                SecondaryContrastText = Ink,
                Tertiary = "#55525E",
                TertiaryContrastText = "#FFFFFF",
                Success = "#2E7D32",
                SuccessContrastText = "#FFFFFF",
                Error = "#C62828",
                ErrorContrastText = "#FFFFFF",
                Warning = "#9A6700",
                WarningContrastText = "#FFFFFF",
                Info = "#1565C0",
                InfoContrastText = "#FFFFFF",
                Background = "#F6F5F3",
                BackgroundGray = "#EDEBE8",
                Surface = "#FFFFFF",
                AppbarBackground = "#FFFFFF",
                AppbarText = Ink,
                DrawerBackground = "#FFFFFF",
                TextPrimary = Ink,
                TextSecondary = "rgba(31,31,35,0.68)",
                TextDisabled = "rgba(31,31,35,0.38)",
                ActionDefault = "rgba(31,31,35,0.62)",
                LinesDefault = "rgba(31,31,35,0.14)",
                LinesInputs = "rgba(31,31,35,0.45)",
                Divider = "rgba(31,31,35,0.12)",
                TableLines = "rgba(31,31,35,0.14)",
                Dark = "#150F1A",
                DarkLighten = "#E1DBDE",
            };

            PaletteDark = new PaletteDark()
            {
                Primary = "#F4900C",
                PrimaryContrastText = NightInk,
                Secondary = "#FFCC4D",
                SecondaryContrastText = NightInk,
                Tertiary = "#A7A5B0",
                TertiaryContrastText = NightInk,
                Success = "#4ADE80",
                SuccessContrastText = NightInk,
                Error = "#F87171",
                ErrorContrastText = NightInk,
                Warning = "#FBBF24",
                WarningContrastText = NightInk,
                Info = "#60A5FA",
                InfoContrastText = NightInk,
                Background = "#18181B",
                BackgroundGray = "#1F1F23",
                Surface = "#222226",
                AppbarBackground = "#222226",
                AppbarText = "rgba(255,255,255,0.87)",
                DrawerBackground = "#222226",
                TextPrimary = "rgba(255,255,255,0.87)",
                TextSecondary = "rgba(255,255,255,0.64)",
                TextDisabled = "rgba(255,255,255,0.38)",
                ActionDefault = "rgba(255,255,255,0.70)",
                ActionDisabled = "rgba(255,255,255,0.30)",
                ActionDisabledBackground = "rgba(255,255,255,0.12)",
                LinesDefault = "rgba(255,255,255,0.14)",
                LinesInputs = "rgba(255,255,255,0.40)",
                Divider = "rgba(255,255,255,0.12)",
                TableLines = "rgba(255,255,255,0.14)",
            };

            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = "12px",
            };

            Typography = new Typography
            {
                Default = new DefaultTypography
                {
                    // Served by the component library, so the app looks the same offline.
                    FontFamily = ["Nunito", "Helvetica", "Arial", "sans-serif"],
                },

                // Buttons say what is written on them, not the same in capitals. The app's own
                // tabs (SegmentedTabs) take their text from the same setting.
                Button = new ButtonTypography
                {
                    TextTransform = "none",
                },
            };

            // Panels are told apart by an outline and a tint, not by a shadow. Only what
            // floats above the page (menus, dialogs) keeps one.
            Shadows = new Shadow();
            for (var elevation = 1; elevation <= 4; elevation++)
                Shadows.Elevation[elevation] = "none";
        }
    }
}
