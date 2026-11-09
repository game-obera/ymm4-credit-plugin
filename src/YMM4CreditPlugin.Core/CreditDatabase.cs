using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YMM4CreditPlugin.Core;

/// <summary>画面のバインド用に変更通知を出す基底クラス。</summary>
public abstract class NotifyBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>登録済みの素材 1 つ。</summary>
public sealed class MaterialEntry : NotifyBase
{
    private MaterialKeyType keyType;
    private string key = "";
    private string name = "";
    private string source = "";
    private string credit = "";
    private MaterialKind kind = MaterialKind.Other;
    private bool noCredit;

    public MaterialKeyType KeyType { get => keyType; set => Set(ref keyType, value); }
    public string Key { get => key; set => Set(ref key, value); }
    public string Name { get => name; set => Set(ref name, value); }
    public string Source { get => source; set => Set(ref source, value); }
    public string Credit { get => credit; set => Set(ref credit, value); }
    public MaterialKind Kind { get => kind; set => Set(ref kind, value); }
    /// <summary>true ならクレジット.txt に出さない(該当なし区画にも出さない)</summary>
    public bool NoCredit { get => noCredit; set => Set(ref noCredit, value); }
}

/// <summary>出典ごとのまとめ設定。</summary>
public sealed class SourceSetting : NotifyBase
{
    private string name = "";
    private bool merge;
    private string mergedCredit = "";
    private MaterialKind? mergedKind;
    private bool noCredit;

    public string Name { get => name; set => Set(ref name, value); }
    /// <summary>true なら、この出典の素材は種類をまたいでも <see cref="MergedCredit"/> を 1 行だけ出す</summary>
    public bool Merge { get => merge; set => Set(ref merge, value); }
    public string MergedCredit { get => mergedCredit; set => Set(ref mergedCredit, value); }
    /// <summary>まとめ行を出す見出しの種類。null なら使われた種類のうち出力順で先頭のもの</summary>
    public MaterialKind? MergedKind { get => mergedKind; set => Set(ref mergedKind, value); }
    /// <summary>true ならこの出典の素材はすべてクレジット.txt に出さない</summary>
    public bool NoCredit { get => noCredit; set => Set(ref noCredit, value); }
}

/// <summary>クレジットの登録データ。</summary>
public sealed class CreditDatabase
{
    public List<MaterialEntry> Materials { get; set; } = [];
    public List<SourceSetting> Sources { get; set; } = [];

    public MaterialEntry? Find(MaterialKeyType type, string key) =>
        Materials.FirstOrDefault(m => m.KeyType == type && MaterialKey.Comparer.Equals(m.Key, key));

    public bool Contains(UsedMaterial material) => Find(material.KeyType, material.Key) is not null;

    /// <summary>抽出した素材を登録する。登録済みならその登録を返す。</summary>
    public MaterialEntry Add(UsedMaterial material)
    {
        if (Find(material.KeyType, material.Key) is { } existing) return existing;

        var entry = new MaterialEntry
        {
            KeyType = material.KeyType,
            Key = material.Key,
            Name = material.DefaultName,
            Kind = material.GuessedKind,
        };
        Materials.Add(entry);
        return entry;
    }

    /// <summary>
    /// 素材に入力された出典のうち、出典一覧に無いものを追加し、どの素材にも使われていない出典を消す。
    /// 残る出典の設定は変えない。
    /// 出典の前後の空白は取り除く(空白違いで別の出典にならないように)。
    /// </summary>
    public void SyncSources()
    {
        foreach (var material in Materials)
        {
            material.Source = material.Source.Trim();
        }

        var used = Materials.Select(m => m.Source).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList();

        // 誤字修正や素材の削除で使われなくなった出典は一覧から外す
        Sources.RemoveAll(s => !used.Contains(s.Name));

        foreach (var name in used)
        {
            if (FindSource(name) is null)
            {
                Sources.Add(new SourceSetting { Name = name });
            }
        }
    }

    public SourceSetting? FindSource(string name) =>
        Sources.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
}
