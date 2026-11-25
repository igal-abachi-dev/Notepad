using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using System;
using System.Linq;

namespace NotepadAvalonia.Views;

public sealed class PrintPagePreview : Control
{
    private static readonly Pen Outline = new(Brushes.Gray, 1);
    private static readonly Pen Margins = new(Brushes.LightGray, 1, DashStyle.Dash);
    private PageSetupSettings _settings = new();
    private EditorSettings _font = new();
    private string _filename = "Untitled";
    private string _text = "";
    private DateTime _moment;

    internal void Initialize(string filename, string text, EditorSettings font)
    {
        _filename = filename;
        int end = Math.Min(3000, text.Length);
        if (end > 0 && char.IsHighSurrogate(text[end - 1])) end--;
        _text = text[..end];
        _font = font;
        _moment = DateTime.Now;
    }

    internal void ShowSettings(PageSetupSettings setup)
    {
        _settings = setup;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var (width, height) = _settings.PaperDimensions;
        var scale = Math.Min((Bounds.Width - 8) / width, (Bounds.Height - 8) / height);
        if (scale <= 0) return;
        var sheet = new Rect((Bounds.Width - width * scale) / 2, (Bounds.Height - height * scale) / 2,
            width * scale, height * scale);
        context.DrawRectangle(Brushes.White, Outline, sheet);

        if (!_settings.HasValidMargins()) return;
        var body = new Rect(sheet.Left + _settings.MarginLeft * scale, sheet.Top + _settings.MarginTop * scale,
            sheet.Width - (_settings.MarginLeft + _settings.MarginRight) * scale,
            sheet.Height - (_settings.MarginTop + _settings.MarginBottom) * scale);
        context.DrawRectangle(null, Margins, body);
        double size = Math.Max(1, _font.FontSize * 25.4 / 72 * scale);
        double rowHeight = size * 1.3;
        int rows = (int)(body.Height / rowHeight);
        int headerRows = PrintService.HasHeader(_settings) ? 2 : 0;
        int footerRows = PrintService.HasFooter(_settings) ? 2 : 0;
        if (rows <= headerRows + footerRows) return;
        var typeface = new Typeface(_font.FontFamily, _font.IsItalic ? FontStyle.Italic : FontStyle.Normal,
            _font.IsBold ? FontWeight.Bold : FontWeight.Normal);

        int Fit(string line)
        {
            using var layout = new TextLayout(line, typeface, size, Brushes.Black, textWrapping: TextWrapping.Wrap,
                maxWidth: body.Width, maxLines: 1);
            return layout.TextLines[0].Length;
        }

        var page = PrintService.BuildPages(_text, _filename, _settings, rows, Fit, _moment).First();
        using (context.PushClip(body))
        {
            DrawAligned(page.Header, body.Top);
            double y = body.Top + headerRows * rowHeight;
            foreach (string line in page.Body)
            {
                Draw(line, y, TextAlignment.Left);
                y += rowHeight;
            }
            DrawAligned(page.Footer, body.Bottom - rowHeight);
        }

        void DrawAligned(PrintService.AlignedText text, double y)
        {
            Draw(text.Left, y, TextAlignment.Left);
            Draw(text.Center, y, TextAlignment.Center);
            Draw(text.Right, y, TextAlignment.Right);
        }

        void Draw(string text, double y, TextAlignment align)
        {
            if (text.Length == 0) return;
            using var layout = new TextLayout(text, typeface, size, Brushes.Black, textAlignment: align,
                maxWidth: body.Width, lineHeight: rowHeight);
            layout.Draw(context, new Point(body.Left, y));
        }
    }
}
