using System.Reflection;
using System.Windows;

namespace YMM4CreditPlugin;

/// <param name="FilePath">プロジェクトの保存先(未保存なら null)</param>
/// <param name="IsSaved">未保存の変更が無ければ true</param>
/// <param name="Error">取得できなかった理由</param>
public sealed record ProjectState(string? FilePath, bool IsSaved, string? Error);

/// <summary>
/// YMM4 で開いているプロジェクトの保存先と保存状態を取得する。
/// 公式 API が無いため、メインウィンドウの ViewModel(MainViewModel)のプロパティをリフレクションで読む。
/// </summary>
public static class CurrentProjectLocator
{
    public static ProjectState Get()
    {
        try
        {
            var viewModel = Application.Current?.MainWindow?.DataContext;
            if (viewModel is null)
            {
                return new ProjectState(null, false, "YMM4 のメインウィンドウが見つかりません");
            }

            if (!TryReadProperty(viewModel, "ProjectFilePath", out var pathValue)
                || !TryReadProperty(viewModel, "IsSaved", out var savedValue))
            {
                return new ProjectState(null, false, "このバージョンの YMM4 からはプロジェクトの情報を取得できません。ymmp を選んで実行してください");
            }

            var path = pathValue as string;
            return new ProjectState(string.IsNullOrEmpty(path) ? null : path, savedValue is true, null);
        }
        catch (Exception ex)
        {
            return new ProjectState(null, false, $"プロジェクトの情報を取得できませんでした: {ex.Message}");
        }
    }

    /// <summary>プロパティを読む。ReactiveProperty のように Value を持つ型なら中身を返す。</summary>
    private static bool TryReadProperty(object target, string name, out object? value)
    {
        value = null;
        var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (property is null) return false;

        value = property.GetValue(target);
        if (value is not null and not string and not bool
            && value.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance) is { } inner)
        {
            value = inner.GetValue(value);
        }
        return true;
    }
}
