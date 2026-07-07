using System.Windows;
using System.Windows.Input;

namespace LiveNBT.App;

/// <summary>In-app usage reference (Help button / F1). Static content, modeless so the user can
/// read it while editing.</summary>
public partial class HelpWindow : Window
{
    private sealed record HotkeyRow(string Keys, string What);

    public HelpWindow()
    {
        InitializeComponent();
        WindowTheming.UseDarkTitleBar(this);
        // tall by default, but never taller than the screen it opens on
        double maxHeight = SystemParameters.WorkArea.Height - 40;
        if (Height > maxHeight) Height = Math.Max(MinHeight, maxHeight);
        // focus the scroller so PgUp/PgDn/Home/End/arrows scroll right away
        Loaded += (_, _) => Scroller.Focus();
        HotkeyList.ItemsSource = new HotkeyRow[]
        {
            new("Enter / Ctrl+E", "Edit the selected value inline"),
            new("Ctrl+Shift+E", "Edit the selected tag as SNBT text (whole subtree, multiline)"),
            new("F2", "Rename a compound key"),
            new("Del", "Delete — selection moves to the next sibling for rapid deleting"),
            new("Ctrl+C", "Copy the tag as SNBT (compound keys copy as name:value)"),
            new("Ctrl+Shift+C", "Copy the tag's path"),
            new("Ctrl+X", "Cut (copy, then delete — no confirmation, it's on the clipboard)"),
            new("Ctrl+V", "Paste SNBT into the selected container (multi-line pastes many tags)"),
            new("Ctrl+D", "Duplicate, auto-named (Health → Health1)"),
            new("Ctrl+Z", "Undo your last edit"),
            new("Ctrl+Shift+Z / Ctrl+Y", "Redo"),
            new("Alt+Up / Alt+Down", "Move a list element up / down"),
            new("Space", "Expand / collapse the selected branch"),
            new("Ctrl+Space", "Expand everything under the selection (capped)"),
            new("Ctrl+Up", "Jump to the parent tag"),
            new("Ctrl+F", "Filter top-level tags by name"),
            new("Ctrl+Shift+F", "Deep find across all names and values (regex supported)"),
            new("F5", "Refresh the tree from the game"),
            new("Ctrl+I", "Open the inventory editor"),
            new("Ctrl+mouse wheel", "Zoom the tree text"),
            new("F1", "This window"),
        };
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
