using System.Windows;

namespace LiveNBT.App;

/// <summary>Tiny modal for renaming a compound key (F2). Validation beyond non-empty happens in
/// <see cref="ViewModels.MainViewModel.RenameAsync"/> which knows the live sibling names.</summary>
public partial class RenameWindow : Window
{
    public string NewName { get; private set; } = "";

    public RenameWindow(string currentName)
    {
        InitializeComponent();
        WindowTheming.UseDarkTitleBar(this);
        NameBox.Text = currentName;
        NameBox.SelectAll();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnRename(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            ErrorText.Text = "Name can't be empty.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        NewName = name;
        DialogResult = true;
    }
}
