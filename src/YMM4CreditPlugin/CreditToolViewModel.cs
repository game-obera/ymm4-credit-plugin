using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using YMM4CreditPlugin.Core;

namespace YMM4CreditPlugin;

/// <summary>素材種類の選択肢。Kind が null の項目は「変更しない」。</summary>
public sealed record KindOption(MaterialKind? Kind, string Name)
{
    public static IReadOnlyList<KindOption> All { get; } =
        Enum.GetValues<MaterialKind>().Select(k => new KindOption(k, k.ToDisplayName())).ToList();

    public static IReadOnlyList<KindOption> WithUnchanged { get; } =
        [new KindOption(null, "(変更しない)"), .. All];

    /// <summary>まとめ行の出力先の選択肢。null は使われた種類のうち出力順で先頭のもの</summary>
    public static IReadOnlyList<KindOption> WithAuto { get; } =
        [new KindOption(null, "(先頭の種類)"), .. All];
}

/// <summary>
/// MaterialKind? と <see cref="KindOption.WithAuto"/> の項目を相互に変換する。
/// SelectedValue に null をバインドすると何も選ばれない表示になるため、SelectedItem で受け渡す。
/// </summary>
public sealed class MergedKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        KindOption.WithAuto.First(o => Equals(o.Kind, value));

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as KindOption)?.Kind;
}

public sealed class CreditToolViewModel : INotifyPropertyChanged
{
    private const string OutputFileName = "クレジット.txt";

    private readonly string dataFilePath = CreditToolPlugin.DataFilePath;
    private readonly CreditDatabase db;
    // 登録データを読み込めなかったときは、元のファイルを守るため保存しない
    private readonly bool canSave = true;

    public ObservableCollection<MaterialEntry> Materials { get; }
    public ObservableCollection<SourceSetting> Sources { get; }

    private string status = "";
    public string Status { get => status; private set => Set(ref status, value); }

    private string bulkSource = "";
    public string BulkSource { get => bulkSource; set => Set(ref bulkSource, value); }

    private string bulkCredit = "";
    public string BulkCredit { get => bulkCredit; set => Set(ref bulkCredit, value); }

    private KindOption bulkKind = KindOption.WithUnchanged[0];
    public KindOption BulkKind { get => bulkKind; set => Set(ref bulkKind, value); }

    public IReadOnlyList<KindOption> BulkKindOptions => KindOption.WithUnchanged;

    /// <summary>素材リストで選択中の行(画面側が選択の変化に合わせて設定する)</summary>
    public IReadOnlyList<MaterialEntry> SelectedMaterials { get; set; } = [];

    public ICommand OutputCurrentCommand { get; }
    public ICommand OutputFileCommand { get; }
    public ICommand ExtractCurrentCommand { get; }
    public ICommand ExtractFileCommand { get; }
    public ICommand ApplyBulkCommand { get; }
    public ICommand DeleteSelectedCommand { get; }

    public CreditToolViewModel()
    {
        try
        {
            db = CreditDatabaseStore.Load(dataFilePath);
            Status = $"登録データ: {dataFilePath}";
        }
        catch (CreditDatabaseLoadException ex)
        {
            db = new CreditDatabase();
            canSave = false;
            Status = "登録データを読み込めなかったため、変更は保存されません";
            ShowError(ex.Message + "\n\n元のファイルを守るため、この画面での変更は保存しません。");
        }

        Materials = new ObservableCollection<MaterialEntry>(db.Materials);
        Sources = new ObservableCollection<SourceSetting>(db.Sources);
        foreach (var entry in Materials) entry.PropertyChanged += OnItemChanged;
        foreach (var source in Sources) source.PropertyChanged += OnItemChanged;

        OutputCurrentCommand = new RelayCommand(() => Output(GetCurrentProjectPath()));
        OutputFileCommand = new RelayCommand(() => Output(SelectYmmp()));
        ExtractCurrentCommand = new RelayCommand(() => Extract(GetCurrentProjectPath()));
        ExtractFileCommand = new RelayCommand(() => Extract(SelectYmmp()));
        ApplyBulkCommand = new RelayCommand(() => ApplyBulk(SelectedMaterials));
        DeleteSelectedCommand = new RelayCommand(() => DeleteSelected(SelectedMaterials));
    }

    private void Output(string? ymmpPath)
    {
        if (ymmpPath is null) return;
        try
        {
            var used = YmmpParser.Parse(ymmpPath);
            var text = CreditBuilder.Build(used, db);
            var outputPath = Path.Combine(Path.GetDirectoryName(ymmpPath)!, OutputFileName);
            File.WriteAllText(outputPath, text, new UTF8Encoding(true));
            Status = $"出力しました: {outputPath}";
            ShowMessage($"出力しました:\n{outputPath}");
        }
        catch (Exception ex) when (ex is YmmpParseException or IOException or UnauthorizedAccessException)
        {
            ShowError($"クレジットを出力できませんでした。\n{ex.Message}");
        }
    }

    private void Extract(string? ymmpPath)
    {
        if (ymmpPath is null) return;
        IReadOnlyList<UsedMaterial> used;
        try
        {
            used = YmmpParser.Parse(ymmpPath);
        }
        catch (YmmpParseException ex)
        {
            ShowError(ex.Message);
            return;
        }

        var candidates = used.Where(m => !db.Contains(m)).ToList();
        if (candidates.Count == 0)
        {
            ShowMessage("新しい素材はありません");
            return;
        }

        var window = new ExtractSelectionWindow(candidates) { Owner = ActiveWindow };
        if (window.ShowDialog() != true) return;

        foreach (var material in window.SelectedMaterials)
        {
            var entry = db.Add(material);
            entry.PropertyChanged += OnItemChanged;
            Materials.Add(entry);
        }
        Save();
        Status = $"{window.SelectedMaterials.Count} 件の素材を追加しました";
    }

    private void ApplyBulk(IReadOnlyList<MaterialEntry> selected)
    {
        if (selected.Count == 0)
        {
            ShowMessage("リストで素材を選択してください");
            return;
        }

        // 空欄の項目は変更しない
        foreach (var entry in selected)
        {
            entry.PropertyChanged -= OnItemChanged;
            if (!string.IsNullOrWhiteSpace(BulkSource)) entry.Source = BulkSource.Trim();
            if (!string.IsNullOrWhiteSpace(BulkCredit)) entry.Credit = BulkCredit;
            if (BulkKind.Kind is { } kind) entry.Kind = kind;
            entry.PropertyChanged += OnItemChanged;
        }
        Save();
        Status = $"{selected.Count} 件に適用しました";
    }

    private void DeleteSelected(IReadOnlyList<MaterialEntry> selected)
    {
        if (selected.Count == 0)
        {
            ShowMessage("リストで素材を選択してください");
            return;
        }
        if (ShowMessage($"{selected.Count} 件の素材を削除しますか?", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;

        // 削除で選択が変わり SelectedMaterials が差し替わるため、先に控えておく
        var targets = selected.ToList();
        foreach (var entry in targets)
        {
            entry.PropertyChanged -= OnItemChanged;
            Materials.Remove(entry);
        }
        Save();
        Status = $"{targets.Count} 件の素材を削除しました";
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Save();

    /// <summary>画面の内容を登録データに反映して保存する</summary>
    private void Save()
    {
        // SyncSources が出典の空白を整えると変更通知で Save が再度呼ばれるため、入れ子の呼び出しは無視する
        if (saving) return;
        saving = true;
        try
        {
            SaveCore();
        }
        finally
        {
            saving = false;
        }
    }

    private bool saving;

    private void SaveCore()
    {
        db.Materials = Materials.ToList();
        db.Sources = Sources.ToList();
        db.SyncSources();
        // 使われなくなって登録データから消えた出典を画面からも外す
        foreach (var source in Sources.Where(s => !db.Sources.Contains(s)).ToList())
        {
            source.PropertyChanged -= OnItemChanged;
            Sources.Remove(source);
        }
        foreach (var source in db.Sources.Where(s => !Sources.Contains(s)))
        {
            source.PropertyChanged += OnItemChanged;
            Sources.Add(source);
        }

        if (!canSave) return;
        try
        {
            CreditDatabaseStore.Save(dataFilePath, db);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError($"登録データを保存できませんでした。\n{ex.Message}");
        }
    }

    /// <summary>開いているプロジェクトの ymmp パス。実行できない状態なら理由を表示して null。</summary>
    private static string? GetCurrentProjectPath()
    {
        var state = CurrentProjectLocator.Get();
        if (state.Error is not null)
        {
            ShowError(state.Error);
            return null;
        }
        if (state.FilePath is null)
        {
            ShowError("プロジェクトが保存されていません。保存してから実行してください");
            return null;
        }
        if (!state.IsSaved)
        {
            ShowError("未保存の変更があります。保存してから実行してください");
            return null;
        }
        return state.FilePath;
    }

    private static string? SelectYmmp()
    {
        var dialog = new OpenFileDialog { Filter = "YMM4 プロジェクト (*.ymmp)|*.ymmp" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>ダイアログの親。指定しないと YMM4 の画面の裏に隠れることがある</summary>
    private static Window? ActiveWindow =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    private static MessageBoxResult ShowMessage(string message, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None) =>
        ActiveWindow is { } owner
            ? MessageBox.Show(owner, message, CreditToolPlugin.DisplayName, button, image)
            : MessageBox.Show(message, CreditToolPlugin.DisplayName, button, image);

    private static void ShowError(string message) => ShowMessage(message, image: MessageBoxImage.Warning);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
