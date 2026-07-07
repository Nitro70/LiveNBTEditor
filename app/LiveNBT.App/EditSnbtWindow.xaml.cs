using System.Windows;
using System.Windows.Input;
using LiveNBT.Protocol;

namespace LiveNBT.App;

/// <summary>
/// Multiline SNBT editor. Two modes: edit an existing subtree (requiredType enforced so a set
/// can't silently change a tag's type) and add a new tag (allowNamed accepts "name:value").
/// The text box is multiline, so Enter inserts a newline and Ctrl+Enter applies.
/// </summary>
public partial class EditSnbtWindow : Window
{
    public NbtNode? ParsedValue { get; private set; }
    public string ParsedName { get; private set; } = "";

    private readonly NbtType? _requiredType;
    private readonly bool _allowNamed;

    public EditSnbtWindow(string title, string initialText, NbtType? requiredType, bool allowNamed)
    {
        InitializeComponent();
        WindowTheming.UseDarkTitleBar(this);
        Title = title;
        _requiredType = requiredType;
        _allowNamed = allowNamed;
        SnbtBox.Text = initialText;
        Loaded += (_, _) => { SnbtBox.Focus(); SnbtBox.CaretIndex = SnbtBox.Text.Length; };
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OnApply(sender, new RoutedEventArgs());
        }
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        string text = SnbtBox.Text.Trim();
        string name = "";
        NbtNode? value;
        string? error;
        bool ok = _allowNamed
            ? SnbtParser.TryParseNamed(text, out name, out value, out error)
            : SnbtParser.TryParse(text, out value, out error);
        if (!ok || value is null)
        {
            ShowError(error is { Length: > 0 } ? error : "Not valid SNBT.");
            return;
        }
        if (_requiredType is { } required && value.Type != required)
        {
            ShowError($"Wrong type: this tag is {NbtTypes.ToWire(required)}, the text parses as {NbtTypes.ToWire(value.Type)}.");
            return;
        }
        ParsedValue = value;
        ParsedName = name;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
