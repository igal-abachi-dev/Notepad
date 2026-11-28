using System;
using System.Globalization;

namespace NotepadAvalonia.Services.Tools;

internal enum SelectionWebAction { Search, Explain, Summarize, Rewrite, Translate, Define }

internal static class SelectionWebLink
{
    internal const int MaxSelectionLength = 2048;
    private const int MaxAddressLength = 8000;
    private const string GoogleSearch = "https://www.google.com/search?q=";
    private const string GoogleAiMode = "https://www.google.com/search?udm=50&q=";

    internal static Uri? Create(SelectionWebAction action, string text, CultureInfo uiCulture)
    {
        if (text.Length > MaxSelectionLength || string.IsNullOrWhiteSpace(text)) return null;

        string address = action switch
        {
            SelectionWebAction.Search => GoogleSearch + Uri.EscapeDataString(text),
            SelectionWebAction.Define => GoogleSearch + Uri.EscapeDataString("define:" + text.Trim()),
            SelectionWebAction.Translate => "https://translate.google.com/?sl=auto&op=translate&tl=" +
                Uri.EscapeDataString(TranslateLanguage(uiCulture)) + "&text=" + Uri.EscapeDataString(text),
            SelectionWebAction.Explain => GoogleAiMode + Prompt("Explain this error, log excerpt, or code snippet. Describe what it means and what to check next.", text),
            SelectionWebAction.Summarize => GoogleAiMode + Prompt("Summarize the selected text. Keep the main points and important details.", text),
            SelectionWebAction.Rewrite => GoogleAiMode + Prompt("Rewrite the selected text for clarity. Preserve its meaning and important details.", text),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        return address.Length <= MaxAddressLength ? new Uri(address) : null;
    }

    private static string Prompt(string instruction, string text) =>
        Uri.EscapeDataString(instruction + "\n\nSelected text:\n" + text);

    private static string TranslateLanguage(CultureInfo culture)
    {
        if (culture.Name.Length == 0) return "en";
        if (culture.TwoLetterISOLanguageName != "zh") return culture.TwoLetterISOLanguageName;
        return culture.Name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
            culture.Name is "zh-TW" or "zh-HK" or "zh-MO" ? "zh-TW" : "zh-CN";
    }
}
