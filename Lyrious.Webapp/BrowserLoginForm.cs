using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;
using Lyrious.UILib.Login;

namespace Lyrious.Webapp;

/// <summary>
/// The login page's username, password and Log in as real web fields (the form in index.html, through
/// <c>lyriousLogin</c> in wwwroot/main.js), laid over the fields the app draws, which stay underneath
/// as their frames. Browsers only offer to save a password typed into a real form and submitted by
/// the user (Safari, on iPhone too), and fill saved ones in only there; the app's own fields are
/// drawn, which they can't see. The fields follow the page as it moves or resizes.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed partial class BrowserLoginForm : ILoginForm
{
    // The page showing the fields now: a page going away hides them only if they're still its own.
    private static Control? shownFor;

    public void Attach(Control page, TextBox userName, TextBox password, Button logIn, Func<(string UserName, string Password)> get, Action<string, string> set, Action submit)
    {
        var userPlaceholder = userName.PlaceholderText ?? "";
        var passwordPlaceholder = password.PlaceholderText ?? "";
        var logInText = logIn.Content as string ?? "";

        // The page's own fields as frames only: their text, placeholder and focus are the web fields'.
        foreach (var box in new[] { userName, password })
        {
            box.Focusable = false;
            box.IsHitTestVisible = false;
            box.PlaceholderText = "";
            box.Foreground = Brushes.Transparent;
        }

        // The browser has no "show password" button in its field; the drawn one would lie under it.
        password.Classes.Remove("revealPasswordButton");

        DispatcherTimer? timer = null;
        (string, string) shownValues = ("", "");
        var placed = new Rect[3];

        page.AttachedToVisualTree += (_, _) =>
        {
            try
            {
                shownFor = page;
                shownValues = get();
                Show(userPlaceholder, passwordPlaceholder, logInText,
                    Colour(page, "TextControlForeground", "#FFFFFF"),
                    Colour(page, "TextControlPlaceholderForeground", "#99FFFFFF"),
                    Colour(page, "SystemAccentColor", "#8A5CF5"),
                    shownValues.Item1, shownValues.Item2,
                    (typedUserName, typedPassword) => Dispatcher.UIThread.Post(() =>
                    {
                        shownValues = (typedUserName, typedPassword);
                        set(typedUserName, typedPassword);
                    }),
                    () => Dispatcher.UIThread.Post(submit));
            }
            catch (JSException ex)
            {
                System.Diagnostics.Debug.WriteLine($"The browser's login form isn't available: {ex.Message}");
                return;
            }

            placed = new Rect[3];
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) =>
            {
                // Kept over the drawn fields as the page scrolls, zooms or the window resizes.
                Follow(page, "username", userName, 0);
                Follow(page, "password", password, 1);
                Follow(page, "button", logIn, 2);

                // Filled in by the app (e.g. the saved login, after it failed): shown in the fields too.
                var values = get();
                if (values != shownValues)
                {
                    shownValues = values;
                    SetValues(values.UserName, values.Password);
                }
            };
            timer.Start();
        };

        page.DetachedFromVisualTree += (_, _) =>
        {
            timer?.Stop();
            timer = null;
            if (shownFor == page)
            {
                shownFor = null;
                Hide();
            }
        };

        // The web field over the drawn control, in page pixels (the app's zoom included); none while
        // the control isn't shown.
        void Follow(Control root, string field, Control control, int index)
        {
            var top = TopLevel.GetTopLevel(root);
            var from = top == null || !control.IsEffectivelyVisible ? null : control.TranslatePoint(new Point(0, 0), top);
            var to = top == null || from == null ? null : control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), top);
            var rect = from is { } a && to is { } b ? new Rect(a, b) : default;
            if (rect == placed[index])
            {
                return;
            }

            placed[index] = rect;
            var scale = control.Bounds.Width > 0 ? rect.Width / control.Bounds.Width : 1;
            var fontSize = control is TextBox box ? box.FontSize * scale : 0;
            var padding = control is TextBox textBox ? (textBox.Padding.Left + textBox.BorderThickness.Left) * scale : 0;
            var radius = control is TemplatedControl templated ? templated.CornerRadius.TopLeft * scale : 0;
            Place(field, rect.X, rect.Y, rect.Width, rect.Height, fontSize, padding, radius);
        }
    }

    /// <summary>A theme colour as CSS ("#AARRGGBB" becomes rgba), for the web fields to look like the app's.</summary>
    private static string Colour(Control page, string key, string fallback)
    {
        if (!page.TryFindResource(key, page.ActualThemeVariant, out var value))
        {
            return fallback;
        }

        var colour = value switch
        {
            Color c => c,
            ISolidColorBrush brush => brush.Color,
            _ => (Color?)null,
        };
        return colour is { } found
            ? $"rgba({found.R},{found.G},{found.B},{(found.A / 255.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)})"
            : fallback;
    }

    [JSImport("globalThis.lyriousLogin.show")]
    private static partial void Show(string userPlaceholder, string passwordPlaceholder, string submitText, string foreground, string placeholder, string accent,
        string userName, string password,
        [JSMarshalAs<JSType.Function<JSType.String, JSType.String>>] Action<string, string> onInput,
        [JSMarshalAs<JSType.Function>] Action onSubmit);

    [JSImport("globalThis.lyriousLogin.place")]
    private static partial void Place(string field, double x, double y, double width, double height, double fontSize, double padding, double radius);

    [JSImport("globalThis.lyriousLogin.setValues")]
    private static partial void SetValues(string userName, string password);

    [JSImport("globalThis.lyriousLogin.hide")]
    private static partial void Hide();
}
