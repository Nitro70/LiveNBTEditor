using System.Windows;
using LiveNBT.Protocol;

namespace LiveNBT.App;

public partial class AddTagWindow : Window
{
    public record AddResult(string Name, NbtNode Value);

    public AddResult? Result { get; private set; }

    public AddTagWindow()
    {
        InitializeComponent();
        WindowTheming.UseDarkTitleBar(this);
        foreach (NbtType type in Enum.GetValues<NbtType>())
            if (NbtTypes.IsScalar(type) || type == NbtType.Compound || type == NbtType.List)
                TypeBox.Items.Add(type);
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (TypeBox.SelectedItem is not NbtType type) return;
        NbtNode value;
        if (type == NbtType.Compound) value = new NbtNode(type) { Children = [] };
        else if (type == NbtType.List) value = new NbtNode(type) { Items = [] };
        else
        {
            if (!ValueParser.TryParse(type, ValueBox.Text, out string normalized, out string error))
            {
                MessageBox.Show(error, "Invalid value");
                return;
            }
            value = new NbtNode(type) { Scalar = normalized };
        }
        Result = new AddResult(NameBox.Text.Trim(), value);
        DialogResult = true;
    }
}
