using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using YMM4CreditPlugin.Core;

namespace YMM4CreditPlugin;

public partial class CreditToolView : UserControl
{
    public CreditToolView()
    {
        InitializeComponent();

        // DataGrid は外のボタンを押しても編集中のセルを確定しないため、
        // ボタンのコマンドが動く前(Click の伝播時)と画面を閉じるときに確定させる
        AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => CommitEdits()));
        Unloaded += (_, _) => CommitEdits();

        // 選択行は CommandParameter のバインドに頼らず、選択が変わるたびに ViewModel へ渡す
        MaterialGrid.SelectionChanged += (_, _) =>
        {
            if (DataContext is CreditToolViewModel vm)
            {
                vm.SelectedMaterials = MaterialGrid.SelectedItems.OfType<MaterialEntry>().ToList();
            }
        };
    }

    private void CommitEdits()
    {
        MaterialGrid.CommitEdit(DataGridEditingUnit.Row, true);
        SourceGrid.CommitEdit(DataGridEditingUnit.Row, true);
    }
}
