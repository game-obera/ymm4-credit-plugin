using System.Windows;
using YMM4CreditPlugin.Core;

namespace YMM4CreditPlugin;

/// <summary>抽出した素材のうち、リストに追加するものをチェックボックスで選ぶ。</summary>
public partial class ExtractSelectionWindow : Window
{
    public sealed class Row(UsedMaterial material) : NotifyBase
    {
        private bool isChecked = true;

        public UsedMaterial Material { get; } = material;
        public string KindName => Material.GuessedKind.ToDisplayName();
        public bool IsChecked { get => isChecked; set => Set(ref isChecked, value); }
    }

    private readonly List<Row> rows;

    public IReadOnlyList<UsedMaterial> SelectedMaterials => rows.Where(r => r.IsChecked).Select(r => r.Material).ToList();

    public ExtractSelectionWindow(IEnumerable<UsedMaterial> candidates)
    {
        InitializeComponent();
        rows = candidates.Select(m => new Row(m)).ToList();
        Grid.ItemsSource = rows;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => rows.ForEach(r => r.IsChecked = true);

    private void UnselectAll_Click(object sender, RoutedEventArgs e) => rows.ForEach(r => r.IsChecked = false);

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
