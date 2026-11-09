using System.Text.Json;
using System.Text.RegularExpressions;

namespace YMM4CreditPlugin.Core;

/// <summary>ymmp の読み込みに失敗したことを表す。</summary>
public sealed class YmmpParseException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>ymmp(YMM4 のプロジェクトファイル)から使用素材を抽出する。</summary>
public static partial class YmmpParser
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".flv" };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".svg", ".psd" };

    [GeneratedRegex(@"^([A-Za-z]:\\|\\\\)")]
    private static partial Regex AbsolutePathRegex();

    public static IReadOnlyList<UsedMaterial> Parse(string ymmpPath)
    {
        JsonDocument doc;
        try
        {
            using var stream = File.OpenRead(ymmpPath);
            doc = JsonDocument.Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new YmmpParseException($"ymmp を読み込めませんでした: {ymmpPath}\n{ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Timelines", out var timelines)
                || timelines.ValueKind != JsonValueKind.Array)
            {
                throw new YmmpParseException($"YMM4 のプロジェクトファイルではありません: {ymmpPath}");
            }

            var collector = new Collector(ReadTachieDirectories(root));
            foreach (var timeline in timelines.EnumerateArray())
            {
                if (timeline.ValueKind == JsonValueKind.Object && timeline.TryGetProperty("Items", out var items))
                {
                    collector.Walk(items, propertyName: null);
                }
            }
            collector.AddCharacters(root);

            return collector.Results.Values
                .OrderBy(m => m.KeyType)
                .ThenBy(m => m.Key, MaterialKey.Comparer)
                .ToList();
        }
    }

    /// <summary>
    /// キャラ設定に登録された立ち絵フォルダを集める(タイムラインで使われていないキャラも含む)。
    /// 入れ子のフォルダでは内側を優先するため、長い順に並べる。
    /// </summary>
    private static List<string> ReadTachieDirectories(JsonElement root)
    {
        if (!root.TryGetProperty("Characters", out var characters) || characters.ValueKind != JsonValueKind.Array) return [];

        return characters.EnumerateArray()
            .Select(GetTachieDirectory)
            .OfType<string>()
            .Distinct(MaterialKey.Comparer)
            .OrderByDescending(d => d.Length)
            .ToList();
    }

    /// <summary>キャラの立ち絵フォルダ(末尾の区切り文字なし)。未設定なら null</summary>
    private static string? GetTachieDirectory(JsonElement character) =>
        character.ValueKind == JsonValueKind.Object
        && character.TryGetProperty("TachieCharacterParameter", out var tachie)
        && tachie.ValueKind == JsonValueKind.Object
        && GetString(tachie, "Directory")?.TrimEnd('\\', '/') is { Length: > 0 } directory
            ? directory
            : null;

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed class Collector(IReadOnlyList<string> tachieDirectories)
    {
        public Dictionary<(MaterialKeyType, string), UsedMaterial> Results { get; } = new(new KeyComparer());

        // タイムラインから参照されているキャラ名(声 / 立ち絵)
        private readonly HashSet<string> voiceCharacters = new(StringComparer.Ordinal);
        private readonly HashSet<string> tachieCharacters = new(StringComparer.Ordinal);

        public void Walk(JsonElement element, string? propertyName)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    CollectCharacterReference(element);
                    foreach (var prop in element.EnumerateObject())
                    {
                        Walk(prop.Value, prop.Name);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var child in element.EnumerateArray())
                    {
                        Walk(child, propertyName);
                    }
                    break;
                case JsonValueKind.String:
                    CollectString(element.GetString()!, propertyName);
                    break;
            }
        }

        private void CollectCharacterReference(JsonElement obj)
        {
            if (!obj.TryGetProperty("CharacterName", out var nameElement) || nameElement.ValueKind != JsonValueKind.String) return;
            if (!obj.TryGetProperty("$type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String) return;

            var name = nameElement.GetString()!;
            var type = typeElement.GetString()!;
            if (type.Contains(".VoiceItem,", StringComparison.Ordinal))
            {
                voiceCharacters.Add(name);
                tachieCharacters.Add(name);
            }
            else if (type.Contains(".Tachie", StringComparison.Ordinal))
            {
                tachieCharacters.Add(name);
            }
        }

        private void CollectString(string value, string? propertyName)
        {
            if (AbsolutePathRegex().IsMatch(value))
            {
                // 立ち絵フォルダ配下のファイルは個別のファイルにせず、そのキャラの立ち絵 1 件にまとめる
                if (FindTachieDirectory(value) is { } directory)
                {
                    AddTachie(directory);
                    return;
                }
                if (propertyName == "FilePath")
                {
                    Add(new UsedMaterial(MaterialKeyType.File, value, Path.GetFileNameWithoutExtension(value), GuessKind(value)));
                }
                return;
            }

            if (propertyName == "Font" && !string.IsNullOrWhiteSpace(value))
            {
                Add(new UsedMaterial(MaterialKeyType.Font, value, value, MaterialKind.Font));
            }
        }

        /// <summary>タイムラインから参照されたキャラの声と立ち絵を加える</summary>
        public void AddCharacters(JsonElement root)
        {
            if (!root.TryGetProperty("Characters", out var characters) || characters.ValueKind != JsonValueKind.Array) return;

            foreach (var character in characters.EnumerateArray())
            {
                if (character.ValueKind != JsonValueKind.Object) continue;
                var name = GetString(character, "Name");
                if (name is null) continue;

                if (voiceCharacters.Contains(name) && character.TryGetProperty("Voice", out var voice) && voice.ValueKind == JsonValueKind.Object)
                {
                    var api = GetString(voice, "API") ?? "";
                    var arg = GetString(voice, "Arg") ?? "";
                    if (api.Length > 0)
                    {
                        Add(new UsedMaterial(MaterialKeyType.Voice, $"{api}|{arg}", $"{api} / {arg}", MaterialKind.Voice));
                    }
                }

                if (tachieCharacters.Contains(name) && GetTachieDirectory(character) is { } directory)
                {
                    AddTachie(directory);
                }
            }
        }

        private string? FindTachieDirectory(string path) =>
            tachieDirectories.FirstOrDefault(d =>
                path.Length > d.Length
                && path.StartsWith(d, StringComparison.OrdinalIgnoreCase)
                && path[d.Length] is '\\' or '/');

        private void AddTachie(string directory) =>
            Add(new UsedMaterial(MaterialKeyType.Tachie, directory, Path.GetFileName(directory), MaterialKind.Tachie));

        private void Add(UsedMaterial material) => Results.TryAdd((material.KeyType, material.Key), material);

        private static MaterialKind GuessKind(string path)
        {
            var ext = Path.GetExtension(path);
            if (VideoExtensions.Contains(ext)) return MaterialKind.Video;
            if (ImageExtensions.Contains(ext)) return MaterialKind.Image;
            return MaterialKind.Other;
        }
    }

    private sealed class KeyComparer : IEqualityComparer<(MaterialKeyType Type, string Key)>
    {
        public bool Equals((MaterialKeyType Type, string Key) x, (MaterialKeyType Type, string Key) y) =>
            x.Type == y.Type && MaterialKey.Comparer.Equals(x.Key, y.Key);

        public int GetHashCode((MaterialKeyType Type, string Key) obj) =>
            HashCode.Combine(obj.Type, MaterialKey.Comparer.GetHashCode(obj.Key));
    }
}
