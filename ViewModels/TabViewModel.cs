using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AvaloniaEdit.Document;
using AvaloniaNotePad.Models;

namespace AvaloniaNotePad.ViewModels;

/// <summary>
/// ViewModel for a single document tab
/// </summary>
  //  public partial class TabViewModel : ViewModelBase
public class TabViewModel : INotifyPropertyChanged
{
    private Document _document;
    private string _text = string.Empty;
    private int _caretOffset;
    private int _selectionStart;
    private int _selectionLength;
    private int _currentLine = 1;
    private int _currentColumn = 1;
    private double _zoomLevel = 100;
    private TextDocument _textDocument = new TextDocument();

    public TabViewModel(Document? document = null)
    {
        _document = document ?? new Document();
        _text = _document.Content;
        _textDocument = new TextDocument(_text);
    }

    public Document Document
    {
        get => _document;
        set
        {
            _document = value;
            _text = value.Content;
            _textDocument.Text = _text;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(TextDocument));
        }
    }

    public string Title => _document.DisplayName;

    public string Text
    {
        get => _text;
        set
        {
            if (_text != value)
            {
                _text = value;
                _document.Content = value;
                _document.IsModified = true;
                _textDocument.Text = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Title));
                UpdatePositionInfo();
            }
        }
    }

    public bool IsModified
    {
        get => _document.IsModified;
        set
        {
            _document.IsModified = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
        }
    }

    public int CaretOffset
    {
        get => _caretOffset;
        set
        {
            if (_caretOffset != value)
            {
                _caretOffset = value;
                OnPropertyChanged();
                UpdatePositionInfo();
            }
        }
    }

    public int SelectionStart
    {
        get => _selectionStart;
        set
        {
            _selectionStart = value;
            OnPropertyChanged();
        }
    }

    public int SelectionLength
    {
        get => _selectionLength;
        set
        {
            _selectionLength = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => _selectionLength > 0;

    public int CurrentLine
    {
        get => _currentLine;
        private set
        {
            if (_currentLine != value)
            {
                _currentLine = value;
                OnPropertyChanged();
            }
        }
    }

    public int CurrentColumn
    {
        get => _currentColumn;
        private set
        {
            if (_currentColumn != value)
            {
                _currentColumn = value;
                OnPropertyChanged();
            }
        }
    }

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            if (Math.Abs(_zoomLevel - value) > 0.001)
            {
                _zoomLevel = Math.Clamp(value, 10, 500);
                OnPropertyChanged();
            }
        }
    }

    public string EncodingDisplay => GetEncodingDisplayName(_document.Encoding);
    public string LineEndingDisplay => _document.LineEnding.ToString();

    // Text statistics
    public int CharacterCount => _text.Length;
    public int WordCount => string.IsNullOrWhiteSpace(_text) ? 0 : 
        _text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
    public int LineCount => _text.Split('\n').Length;

    private void UpdatePositionInfo()
    {
        if (string.IsNullOrEmpty(_text) || _caretOffset < 0)
        {
            CurrentLine = 1;
            CurrentColumn = 1;
            return;
        }

        var offset = Math.Min(_caretOffset, _text.Length);
        var textBeforeCaret = _text.Substring(0, offset);
        
        CurrentLine = textBeforeCaret.Count(c => c == '\n') + 1;
        
        var lastNewLine = textBeforeCaret.LastIndexOf('\n');
        CurrentColumn = lastNewLine == -1 ? offset + 1 : offset - lastNewLine;
        
        OnPropertyChanged(nameof(CharacterCount));
        OnPropertyChanged(nameof(WordCount));
        OnPropertyChanged(nameof(LineCount));
    }

    private string GetEncodingDisplayName(System.Text.Encoding encoding)
    {
        return encoding.WebName.ToUpperInvariant() switch
        {
            "UTF-8" => encoding.GetPreamble().Length > 0 ? "UTF-8 with BOM" : "UTF-8",
            "UTF-16" => "UTF-16 LE",
            "UNICODEFFFE" => "UTF-16 BE",
            _ => encoding.EncodingName
        };
    }

    // Selection operations
    public string GetSelectedText()
    {
        if (_selectionLength <= 0 || _selectionStart < 0 || _selectionStart >= _text.Length)
            return string.Empty;
            
        var end = Math.Min(_selectionStart + _selectionLength, _text.Length);
        return _text.Substring(_selectionStart, end - _selectionStart);
    }

    public void ReplaceSelection(string newText)
    {
        if (_selectionStart < 0 || _selectionStart > _text.Length)
            return;

        var before = _text.Substring(0, _selectionStart);
        var after = _selectionStart + _selectionLength <= _text.Length 
            ? _text.Substring(_selectionStart + _selectionLength) 
            : string.Empty;
            
        Text = before + newText + after;
        CaretOffset = _selectionStart + newText.Length;
        SelectionLength = 0;
    }

    public void SelectAll()
    {
        SelectionStart = 0;
        SelectionLength = _text.Length;
    }

    public void InsertText(string text)
    {
        if (_selectionLength > 0)
        {
            ReplaceSelection(text);
        }
        else
        {
            var before = _caretOffset > 0 ? _text.Substring(0, _caretOffset) : string.Empty;
            var after = _caretOffset < _text.Length ? _text.Substring(_caretOffset) : string.Empty;
            Text = before + text + after;
            CaretOffset = _caretOffset + text.Length;
        }
    }

    public void InsertDateTime()
    {
        InsertText(DateTime.Now.ToString("g")); // Short date, short time
    }

    // Go to line
    public void GoToLine(int lineNumber)
    {
        var lines = _text.Split('\n');
        var targetLine = Math.Clamp(lineNumber, 1, lines.Length);
        
        var offset = 0;
        for (var i = 0; i < targetLine - 1 && i < lines.Length; i++)
        {
            offset += lines[i].Length + 1; // +1 for \n
        }
        
        CaretOffset = offset;
        SelectionLength = 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public TextDocument TextDocument
    {
        get => _textDocument;
        set
        {
            if (_textDocument != value)
            {
                _textDocument = value;
                Text = value.Text;
                OnPropertyChanged();
            }
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
