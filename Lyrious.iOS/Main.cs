using UIKit;

namespace Lyrious.iOS;

public class Application
{
    // The main entry point of the application.
    static void Main(string[] args)
    {
        // To use a different application delegate class than AppDelegate, specify it here.
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
