using LiveNBT.Protocol;

namespace LiveNBT.App.ViewModels;

public sealed class WatchItemViewModel(string root, string path) : ViewModelBase
{
    private string _valueText = "…";

    public string Root { get; } = root;
    public string Path { get; } = path;
    public string Display => $"{Root}  {Path}";

    public string ValueText
    {
        get => _valueText;
        private set => Set(ref _valueText, value);
    }

    public void ApplyUpdate(NbtNode? value) =>
        ValueText = value is null ? "(gone)" : SnbtWriter.Write(value);
}
