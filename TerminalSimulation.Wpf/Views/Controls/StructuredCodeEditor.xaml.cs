using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using TerminalSimulation.Wpf.ViewModels.Utilities;

namespace TerminalSimulation.Wpf.Views.Controls;

public partial class StructuredCodeEditor : UserControl
{
    private static readonly Regex JsonPropertyRegex = new(
        "\\\"(?<name>(?:\\\\.|[^\\\"\\\\])+)\\\"\\s*:",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] CommonJsonProperties =
        ["id", "name", "key", "value", "type", "data", "status", "message", "command", "timestamp"];

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(StructuredCodeEditor),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnTextPropertyChanged));

    public static readonly DependencyProperty ContentTypeProperty = DependencyProperty.Register(
        nameof(ContentType),
        typeof(string),
        typeof(StructuredCodeEditor),
        new PropertyMetadata("application/json", OnContentTypeChanged));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly),
        typeof(bool),
        typeof(StructuredCodeEditor),
        new PropertyMetadata(false, OnIsReadOnlyChanged));

    private readonly DispatcherTimer _validationTimer;
    private CompletionWindow? _completionWindow;
    private bool _updatingText;

    public StructuredCodeEditor()
    {
        InitializeComponent();

        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.IndentationSize = 2;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.EnableHyperlinks = false;
        Editor.TextChanged += Editor_TextChanged;
        Editor.TextArea.TextEntering += TextArea_TextEntering;
        Editor.TextArea.TextEntered += TextArea_TextEntered;
        Editor.TextArea.PreviewKeyDown += TextArea_PreviewKeyDown;
        Editor.TextArea.Caret.PositionChanged += Caret_PositionChanged;

        _validationTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(320)
        };
        _validationTimer.Tick += ValidationTimer_Tick;

        Loaded += StructuredCodeEditor_Loaded;
        Unloaded += StructuredCodeEditor_Unloaded;
        ApplyLanguage();
        UpdateCaretStatus();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string ContentType
    {
        get => (string)GetValue(ContentTypeProperty);
        set => SetValue(ContentTypeProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    private static void OnTextPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (StructuredCodeEditor)dependencyObject;
        var text = args.NewValue as string ?? string.Empty;
        if (control._updatingText || string.Equals(control.Editor.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        var caretOffset = control.Editor.CaretOffset;
        control._updatingText = true;
        try
        {
            control.Editor.Text = text;
            control.Editor.CaretOffset = Math.Min(caretOffset, control.Editor.Document.TextLength);
        }
        finally
        {
            control._updatingText = false;
        }
        control.ScheduleValidation();
    }

    private static void OnContentTypeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (StructuredCodeEditor)dependencyObject;
        control.ApplyLanguage();
        control.ScheduleValidation();
    }

    private static void OnIsReadOnlyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var control = (StructuredCodeEditor)dependencyObject;
        control.Editor.IsReadOnly = (bool)args.NewValue;
    }

    private void StructuredCodeEditor_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyThemeColors();
        ScheduleValidation();
    }

    private void StructuredCodeEditor_Unloaded(object sender, RoutedEventArgs e)
    {
        _validationTimer.Stop();
        _completionWindow?.Close();
        _completionWindow = null;
    }

    private void Editor_TextChanged(object? sender, EventArgs e)
    {
        if (!_updatingText)
        {
            _updatingText = true;
            try
            {
                SetCurrentValue(TextProperty, Editor.Text);
            }
            finally
            {
                _updatingText = false;
            }
        }

        ScheduleValidation();
        UpdateCaretStatus();
    }

    private void TextArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Editor.IsReadOnly)
        {
            return;
        }

        if (e.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ShowJsonCompletion(JsonCompletionContext.Auto);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter &&
            _completionWindow is null &&
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            InsertIndentedNewLine();
            e.Handled = true;
        }
    }

    private void TextArea_TextEntering(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || Editor.IsReadOnly)
        {
            return;
        }

        var typed = e.Text[0];
        if (typed is '}' or ']' or ')' or '"')
        {
            var offset = Editor.CaretOffset;
            if (offset < Editor.Document.TextLength && Editor.Document.GetCharAt(offset) == typed)
            {
                Editor.CaretOffset++;
                e.Handled = true;
                return;
            }
        }

        if (_completionWindow is not null && !char.IsLetterOrDigit(typed) && typed is not '_' and not '-')
        {
            _completionWindow.CompletionList.RequestInsertion(e);
        }
    }

    private void TextArea_TextEntered(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var typed = e.Text[0];
        if (TryGetClosingCharacter(typed, out var closing))
        {
            var caretOffset = Editor.CaretOffset;
            Editor.Document.Insert(caretOffset, closing.ToString());
            Editor.CaretOffset = caretOffset;
        }

        if (!StructuredBodyFormatter.IsJson(ContentType))
        {
            return;
        }

        if (e.Text == "\"" && IsJsonPropertyPosition())
        {
            ShowJsonCompletion(JsonCompletionContext.PropertyAfterQuote);
        }
        else if (e.Text == ":")
        {
            ShowJsonCompletion(JsonCompletionContext.Value);
        }
    }

    private void InsertIndentedNewLine()
    {
        var document = Editor.Document;
        var offset = Editor.CaretOffset;
        var line = document.GetLineByOffset(offset);
        var beforeCaret = document.GetText(line.Offset, offset - line.Offset);
        var baseIndent = new string(beforeCaret.TakeWhile(char.IsWhiteSpace).ToArray());
        var trimmedBefore = beforeCaret.TrimEnd();
        var nextCharacter = offset < document.TextLength ? document.GetCharAt(offset) : '\0';
        var increasesIndent = trimmedBefore.EndsWith('{') || trimmedBefore.EndsWith('[');
        var closesBlock = nextCharacter is '}' or ']';
        var innerIndent = baseIndent + (increasesIndent ? new string(' ', Editor.Options.IndentationSize) : string.Empty);

        if (increasesIndent && closesBlock)
        {
            var insertion = Environment.NewLine + innerIndent + Environment.NewLine + baseIndent;
            document.Insert(offset, insertion);
            Editor.CaretOffset = offset + Environment.NewLine.Length + innerIndent.Length;
        }
        else
        {
            var insertion = Environment.NewLine + innerIndent;
            document.Insert(offset, insertion);
            Editor.CaretOffset = offset + insertion.Length;
        }
    }

    private bool IsJsonPropertyPosition()
    {
        var offset = Math.Max(0, Editor.CaretOffset - 1);
        for (var index = offset - 1; index >= 0; index--)
        {
            var character = Editor.Document.GetCharAt(index);
            if (char.IsWhiteSpace(character))
            {
                continue;
            }
            return character is '{' or ',';
        }
        return true;
    }

    private void ShowJsonCompletion(JsonCompletionContext context)
    {
        if (!StructuredBodyFormatter.IsJson(ContentType) || _completionWindow is not null)
        {
            return;
        }

        var items = CreateJsonCompletionItems(context).ToList();
        if (items.Count == 0)
        {
            return;
        }

        _completionWindow = new CompletionWindow(Editor.TextArea)
        {
            Width = 300,
            MaxHeight = 260,
            CloseWhenCaretAtBeginning = false
        };
        foreach (var item in items)
        {
            _completionWindow.CompletionList.CompletionData.Add(item);
        }
        _completionWindow.Closed += (_, _) => _completionWindow = null;
        _completionWindow.Show();
    }

    private IEnumerable<ICompletionData> CreateJsonCompletionItems(JsonCompletionContext context)
    {
        if (context == JsonCompletionContext.Auto)
        {
            context = IsJsonPropertyPosition()
                ? JsonCompletionContext.Property
                : JsonCompletionContext.Value;
        }

        if (context is JsonCompletionContext.Property or JsonCompletionContext.PropertyAfterQuote)
        {
            var existingNames = JsonPropertyRegex.Matches(Editor.Text)
                .Select(match => Regex.Unescape(match.Groups["name"].Value));
            foreach (var propertyName in existingNames.Concat(CommonJsonProperties).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var insertion = context == JsonCompletionContext.PropertyAfterQuote
                    ? propertyName + "\": "
                    : "\"" + propertyName + "\": ";
                yield return new JsonCompletionData(
                    propertyName,
                    insertion,
                    "JSON 属性",
                    caretBackOffset: 0,
                    priority: 1,
                    removeFollowingQuote: context == JsonCompletionContext.PropertyAfterQuote);
            }
            yield break;
        }

        yield return new JsonCompletionData("对象 { }", "{}", "插入 JSON 对象", 1, 2);
        yield return new JsonCompletionData("数组 [ ]", "[]", "插入 JSON 数组", 1, 2);
        yield return new JsonCompletionData("字符串", "\"\"", "插入字符串值", 1, 1);
        yield return new JsonCompletionData("true", "true", "布尔值 true", 0, 1);
        yield return new JsonCompletionData("false", "false", "布尔值 false", 0, 1);
        yield return new JsonCompletionData("null", "null", "空值 null", 0, 1);
    }

    private static bool TryGetClosingCharacter(char character, out char closing)
    {
        closing = character switch
        {
            '{' => '}',
            '[' => ']',
            '(' => ')',
            '"' => '"',
            _ => '\0'
        };
        return closing != '\0';
    }

    private void ApplyLanguage()
    {
        if (Editor is null)
        {
            return;
        }

        var language = StructuredBodyFormatter.GetLanguageName(ContentType);
        LanguageText.Text = language;
        Editor.SyntaxHighlighting = language switch
        {
            "JSON" => HighlightingManager.Instance.GetDefinition("JavaScript"),
            "XML" => HighlightingManager.Instance.GetDefinition("XML"),
            _ => null
        };
    }

    private void ApplyThemeColors()
    {
        if (TryFindResource("MaterialDesignBodyLight") is Brush bodyLight)
        {
            Editor.LineNumbersForeground = bodyLight;
        }
    }

    private void ScheduleValidation()
    {
        if (_validationTimer is null)
        {
            return;
        }
        _validationTimer.Stop();
        _validationTimer.Start();
    }

    private void ValidationTimer_Tick(object? sender, EventArgs e)
    {
        _validationTimer.Stop();
        if (string.IsNullOrWhiteSpace(Editor.Text))
        {
            ValidationText.Text = "Ctrl+Space 显示补全";
            ValidationText.Foreground = FindBrush("MaterialDesignBodyLight", Brushes.Gray);
            return;
        }

        if (StructuredBodyFormatter.TryValidate(Editor.Text, ContentType, out var error))
        {
            ValidationText.Text = StructuredBodyFormatter.IsJson(ContentType) || StructuredBodyFormatter.IsXml(ContentType)
                ? "结构有效"
                : "Ctrl+Space 显示补全";
            ValidationText.Foreground = Brushes.SeaGreen;
        }
        else
        {
            ValidationText.Text = error;
            ValidationText.Foreground = Brushes.IndianRed;
        }
    }

    private void Caret_PositionChanged(object? sender, EventArgs e) => UpdateCaretStatus();

    private void UpdateCaretStatus()
    {
        if (CaretText is not null)
        {
            CaretText.Text = $"Ln {Editor.TextArea.Caret.Line}, Col {Editor.TextArea.Caret.Column}";
        }
    }

    private Brush FindBrush(string resourceKey, Brush fallback) =>
        TryFindResource(resourceKey) as Brush ?? fallback;

    private enum JsonCompletionContext
    {
        Auto,
        Property,
        PropertyAfterQuote,
        Value
    }

    private sealed class JsonCompletionData(
        string displayText,
        string insertionText,
        string description,
        int caretBackOffset,
        double priority,
        bool removeFollowingQuote = false) : ICompletionData
    {
        public ImageSource? Image => null;
        public string Text => displayText;
        public object Content => displayText;
        public object Description => description;
        public double Priority => priority;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            if (removeFollowingQuote &&
                completionSegment.EndOffset < textArea.Document.TextLength &&
                textArea.Document.GetCharAt(completionSegment.EndOffset) == '"')
            {
                textArea.Document.Remove(completionSegment.EndOffset, 1);
            }
            textArea.Document.Replace(completionSegment, insertionText);
            if (caretBackOffset > 0)
            {
                textArea.Caret.Offset = Math.Max(completionSegment.Offset, textArea.Caret.Offset - caretBackOffset);
            }
        }
    }
}
