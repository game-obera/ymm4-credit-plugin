using System.Text;

namespace YMM4CreditPlugin.Core;

/// <summary>使用素材と登録データから、クレジット.txt の本文を組み立てる。</summary>
public static class CreditBuilder
{
    private const string NewLine = "\r\n";

    public static string Build(IReadOnlyList<UsedMaterial> used, CreditDatabase db)
    {
        // 種類ごとのクレジット行(出現順、同じ文面は 1 回)
        var lines = new Dictionary<MaterialKind, List<string>>();
        var unmatched = new List<(MaterialKind Kind, string Label, string Key)>();

        var targets = used
            .Select(m => (Material: m, Entry: db.Find(m.KeyType, m.Key)))
            .Select(t => (t.Material, t.Entry, Source: t.Entry is null || string.IsNullOrWhiteSpace(t.Entry.Source) ? null : db.FindSource(t.Entry.Source)))
            .ToList();

        // まとめる出典のまとめ行を出す種類: 指定が無ければ、出力対象の素材の種類のうち出力順で先頭
        var mergedKinds = targets
            .Where(t => t.Entry is { NoCredit: false } && t.Source is { Merge: true, NoCredit: false })
            .GroupBy(t => t.Source!)
            .ToDictionary(g => g.Key, g => g.Key.MergedKind ?? g.Min(t => t.Entry!.Kind));

        foreach (var (material, entry, source) in targets)
        {
            if (entry is null)
            {
                unmatched.Add((material.GuessedKind, Label(material), material.Key));
                continue;
            }

            if (entry.NoCredit || source?.NoCredit == true) continue;

            var merged = source?.Merge == true;
            var text = NormalizeNewLines(merged ? source!.MergedCredit : entry.Credit);
            if (string.IsNullOrWhiteSpace(text))
            {
                unmatched.Add((entry.Kind, Label(material), material.Key));
                continue;
            }

            var kind = merged ? mergedKinds[source!] : entry.Kind;
            if (!lines.TryGetValue(kind, out var list))
            {
                lines[kind] = list = [];
            }
            if (!list.Contains(text, StringComparer.Ordinal))
            {
                list.Add(text);
            }
        }

        var sb = new StringBuilder();
        foreach (var kind in Enum.GetValues<MaterialKind>())
        {
            if (!lines.TryGetValue(kind, out var list)) continue;
            sb.Append('■').Append(kind.ToDisplayName()).Append(NewLine);
            foreach (var line in list)
            {
                sb.Append(line).Append(NewLine);
            }
        }

        if (unmatched.Count > 0)
        {
            if (sb.Length > 0) sb.Append(NewLine);
            sb.Append("# --- クレジット該当なし ---").Append(NewLine);
            foreach (var (kind, label, _) in unmatched.OrderBy(u => u.Kind).ThenBy(u => u.Key, MaterialKey.Comparer))
            {
                sb.Append("# [").Append(kind.ToDisplayName()).Append("] ").Append(label).Append(NewLine);
            }
        }

        return sb.ToString();
    }

    /// <summary>複数行のクレジットの改行を CRLF にそろえ、末尾の改行を取り除く</summary>
    private static string NormalizeNewLines(string text) =>
        text.ReplaceLineEndings(NewLine).TrimEnd('\r', '\n');

    /// <summary>該当なし区画での表記。ファイル・立ち絵はパス、音声・フォントは名前。</summary>
    private static string Label(UsedMaterial material) =>
        material.KeyType is MaterialKeyType.File or MaterialKeyType.Tachie ? material.Key : material.DefaultName;
}
