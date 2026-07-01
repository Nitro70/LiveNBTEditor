using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiveNBT.App.Inventory;

namespace LiveNBT.App;

/// <summary>
/// A plain text field with a filtered suggestion popup — a search box that never rewrites what you
/// type (unlike WPF's editable ComboBox, which auto-selects/auto-completes and fights typing). Type
/// to filter <see cref="Suggestions"/>; click or Enter to pick; Down to move into the list; Esc to
/// dismiss. Nothing is committed until you pick it.
/// </summary>
public partial class SearchBox : UserControl
{
    private bool _syncing;   // true while mirroring the Text DP into the TextBox (not user typing)

    public SearchBox() => InitializeComponent();

    public static readonly DependencyProperty SuggestionsProperty = DependencyProperty.Register(
        nameof(Suggestions), typeof(IReadOnlyList<string>), typeof(SearchBox), new PropertyMetadata(null));

    /// <summary>The full list to search. Filtering happens inside the control.</summary>
    public IReadOnlyList<string>? Suggestions
    {
        get => (IReadOnlyList<string>?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SearchBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    /// <summary>The current text / selected id. Two-way bindable.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (SearchBox)d;
        string val = (string)(e.NewValue ?? "");
        if (box.Input.Text == val) return;
        box._syncing = true;                 // external change (slot loaded / cleared): sync text, no popup
        box.Input.Text = val;
        box.Input.CaretIndex = val.Length;
        box._syncing = false;
    }

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;                // programmatic sync, not the user typing
        SetCurrentValue(TextProperty, Input.Text);
        var matches = InventoryFilter.Filter(Suggestions ?? [], Input.Text);
        List.ItemsSource = matches;
        Pop.IsOpen = matches.Count > 0 && Input.IsKeyboardFocusWithin;
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when Pop.IsOpen && List.Items.Count > 0:
                List.SelectedIndex = 0;
                (List.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.Focus();
                e.Handled = true;
                break;
            case Key.Enter when Pop.IsOpen && List.Items.Count > 0:
                Commit((List.SelectedItem ?? List.Items[0]) as string);
                e.Handled = true;
                break;
            case Key.Escape when Pop.IsOpen:
                Pop.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { Commit(List.SelectedItem as string); e.Handled = true; }
        else if (e.Key == Key.Escape) { Pop.IsOpen = false; Input.Focus(); e.Handled = true; }
    }

    private void OnListClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItem is string s) Commit(s);
    }

    // Close the popup when focus leaves the field — unless it moved into the popup's list (Down arrow).
    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!Pop.IsKeyboardFocusWithin) Pop.IsOpen = false;
    }

    private void Commit(string? value)
    {
        if (value is null) return;
        _syncing = true;
        Input.Text = value;
        Input.CaretIndex = value.Length;
        _syncing = false;
        SetCurrentValue(TextProperty, value);
        Pop.IsOpen = false;
        Input.Focus();
    }
}
