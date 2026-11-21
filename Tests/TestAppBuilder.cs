using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Notepad.Tests.TestAppBuilder))]

namespace Notepad.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
