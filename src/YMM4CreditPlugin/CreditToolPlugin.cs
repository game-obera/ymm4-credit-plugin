using System.IO;
using YukkuriMovieMaker.Plugin;

namespace YMM4CreditPlugin;

/// <summary>ツールメニューに「クレジット自動出力」を追加する。</summary>
public class CreditToolPlugin : IToolPlugin
{
    public const string DisplayName = "クレジット自動出力";

    public string Name => DisplayName;
    public Type ViewModelType => typeof(CreditToolViewModel);
    public Type ViewType => typeof(CreditToolView);

    // 画面ごとに登録データを持つため、複数開くと互いの変更を上書きしてしまう
    public bool AllowMultipleInstances => false;

    /// <summary>登録データの保存先(プラグイン DLL と同じフォルダ)</summary>
    public static string DataFilePath { get; } =
        Path.Combine(Path.GetDirectoryName(typeof(CreditToolPlugin).Assembly.Location)!, "credit-data.json");
}
