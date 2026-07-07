using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LiveNBT.App.Services;
using LiveNBT.App.ViewModels;
using LiveNBT.Protocol;

namespace LiveNBT.App;

/// <summary>
/// Deep find over the loaded tree (Ctrl+Shift+F). Searches the in-memory data model — no server
/// traffic, no tree materialization — then expands + selects hits through the jump callback.
/// Name and Value are ANDed; either supports substring (default) or regex matching.
/// </summary>
public partial class FindWindow : Window
{
    private sealed record Hit(string Path, string Display)
    {
        public override string ToString() => Display;
    }

    private const int MaxResults = 2000;
    // the walk runs synchronously on the UI thread; cap its total cost so a pathological regex
    // (catastrophic backtracking) or a giant tree can never hang the app past a couple of seconds
    private static readonly TimeSpan SearchBudget = TimeSpan.FromSeconds(2);

    private readonly MainViewModel _vm;
    private readonly AppSettings _settings;
    private readonly Action<string, string> _jump;   // (root, path)
    private readonly Stopwatch _searchTimer = new();
    private bool _searchTruncated;
    private List<Hit> _results = [];
    private int _index = -1;
    private bool _stale = true;
    private string _resultsRoot = "";

    public FindWindow(MainViewModel vm, AppSettings settings, Action<string, string> jump)
    {
        InitializeComponent();
        WindowTheming.UseDarkTitleBar(this);
        _vm = vm;
        _settings = settings;
        _jump = jump;
        NameBox.Text = settings.FindName;
        ValueBox.Text = settings.FindValue;
        RegexCheck.IsChecked = settings.FindRegex;
        Loaded += (_, _) => FocusQuery();
    }

    public void FocusQuery()
    {
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnQueryChanged(object sender, RoutedEventArgs e) => _stale = true;

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            e.Handled = true;
            OnFindPrev(sender, new RoutedEventArgs());
        }
    }

    private void OnClosingWindow(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _settings.FindName = NameBox.Text;
        _settings.FindValue = ValueBox.Text;
        _settings.FindRegex = RegexCheck.IsChecked == true;
    }

    private void OnFindNext(object sender, RoutedEventArgs e) => Step(+1);
    private void OnFindPrev(object sender, RoutedEventArgs e) => Step(-1);

    private void Step(int direction)
    {
        if (!EnsureResults()) return;
        if (_results.Count == 0)
        {
            StatusText.Text = "No results found";
            return;
        }
        _index = ((_index + direction) % _results.Count + _results.Count) % _results.Count;   // wraps
        Hit hit = _results[_index];
        StatusText.Text = $"{_index + 1} of {_results.Count} — {hit.Path}{TruncationNote()}";
        _jump(_resultsRoot, hit.Path);
    }

    private void OnFindAll(object sender, RoutedEventArgs e)
    {
        _stale = true;   // Find All always re-runs against current data
        if (!EnsureResults()) return;
        ResultsList.ItemsSource = _results;
        StatusText.Text = (_results.Count == 0 ? "No results found"
            : _results.Count >= MaxResults ? $"{_results.Count} results (capped) — click one to jump"
            : $"{_results.Count} result(s) — click one to jump") + TruncationNote();
    }

    /// <summary>Appended when the walk hit its time budget — results are partial (usually a slow
    /// regex); a simpler pattern will finish.</summary>
    private string TruncationNote() =>
        _searchTruncated ? "  ⚠ search stopped early (pattern too slow — try a simpler one)" : "";

    private void OnResultSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is Hit hit)
        {
            _index = _results.IndexOf(hit);
            _jump(_resultsRoot, hit.Path);
        }
    }

    /// <summary>(Re)build the result list when the query or root changed since last time.</summary>
    private bool EnsureResults()
    {
        if (_vm.TreeRoot is not { } tree)
        {
            StatusText.Text = "Connect and load a root first";
            return false;
        }
        if (!_stale && tree.Root == _resultsRoot && _results.Count > 0) return true;

        Func<string, bool>? nameOk = BuildMatcher(NameBox.Text, out string nameError);
        if (nameError.Length > 0) { StatusText.Text = $"Name pattern: {nameError}"; return false; }
        Func<string, bool>? valueOk = BuildMatcher(ValueBox.Text, out string valueError);
        if (valueError.Length > 0) { StatusText.Text = $"Value pattern: {valueError}"; return false; }
        if (nameOk is null && valueOk is null)
        {
            StatusText.Text = "Type a name and/or value to search for";
            return false;
        }

        var results = new List<Hit>();
        _searchTruncated = false;
        _searchTimer.Restart();
        Walk(tree.Node, "", nameOk, valueOk, results);
        _searchTimer.Stop();
        _results = results;
        _resultsRoot = tree.Root;
        _index = -1;
        _stale = false;
        ResultsList.ItemsSource = null;
        return true;
    }

    /// <summary>null = match everything (empty box); otherwise substring or compiled regex.</summary>
    private Func<string, bool>? BuildMatcher(string query, out string error)
    {
        error = "";
        if (query.Length == 0) return null;
        if (RegexCheck.IsChecked == true)
        {
            try
            {
                // short per-call timeout so a single catastrophic match can't dominate; the overall
                // SearchBudget in Walk bounds the aggregate across all nodes
                var regex = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                                      TimeSpan.FromMilliseconds(50));
                return s => { try { return regex.IsMatch(s); } catch (RegexMatchTimeoutException) { return false; } };
            }
            catch (ArgumentException ex)
            {
                error = ex.Message;
                return null;
            }
        }
        return s => s.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void Walk(NbtNode node, string path,
        Func<string, bool>? nameOk, Func<string, bool>? valueOk, List<Hit> results)
    {
        if (_searchTimer.Elapsed > SearchBudget) { _searchTruncated = true; return; }
        if (results.Count >= MaxResults) return;
        if (path.Length > 0)
        {
            string name = LastSegment(path);
            string valueText = NbtTypes.IsScalar(node.Type)
                ? node.Scalar ?? ""
                : $"{node.Items?.Count ?? node.Children?.Count ?? 0} entries";
            if ((nameOk is null || nameOk(name)) && (valueOk is null || valueOk(valueText)))
            {
                string preview = valueText.Length > 80 ? valueText[..80] + "…" : valueText;
                results.Add(new Hit(path, $"{path}  =  {preview}"));
            }
        }
        if (node.Children is not null)
        {
            foreach (var (childName, child) in node.Children)
                Walk(child, path.Length == 0 ? childName : $"{path}.{childName}", nameOk, valueOk, results);
        }
        else if (node.Items is not null)
        {
            for (int i = 0; i < node.Items.Count; i++)
                Walk(node.Items[i], $"{path}[{i}]", nameOk, valueOk, results);
        }
    }

    private static string LastSegment(string path)
    {
        int cut = Math.Max(path.LastIndexOf('.'), path.LastIndexOf('['));
        return cut < 0 ? path : path[(path[cut] == '[' ? cut : cut + 1)..];
    }
}
