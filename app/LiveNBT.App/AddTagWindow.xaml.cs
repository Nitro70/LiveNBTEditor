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
            TypeBox.Items.Add(type);
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        if (TypeBox.SelectedItem is not NbtType type) return;
        NbtNode value;
        if (type == NbtType.Compound) value = new NbtNode(type) { Children = [] };
        else if (type == NbtType.List) value = new NbtNode(type) { Items = [] };
        else if (type is NbtType.ByteArray or NbtType.IntArray or NbtType.LongArray)
        {
            // optional comma-separated initial elements, e.g. "1, 2, 3" (empty box = empty array)
            NbtType elementType = NbtTypes.ArrayElementType(type);
            var items = new List<NbtNode>();
            foreach (string part in ValueBox.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!ValueParser.TryParse(elementType, part, out string normalizedElement, out string elementError))
                {
                    MessageBox.Show($"'{part}': {elementError}", "Invalid value");
                    return;
                }
                items.Add(new NbtNode(elementType) { Scalar = normalizedElement });
            }
            value = new NbtNode(type) { Items = items };
        }
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
