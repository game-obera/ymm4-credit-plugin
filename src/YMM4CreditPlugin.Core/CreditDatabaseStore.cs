using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YMM4CreditPlugin.Core;

/// <summary>登録データの読み込みに失敗したことを表す。</summary>
public sealed class CreditDatabaseLoadException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>登録データ(JSON)の読み書き。</summary>
public static class CreditDatabaseStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // 日本語をエスケープせずに書き、手で読めるようにする
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>読み込む。ファイルが無ければ空のデータを返す。</summary>
    public static CreditDatabase Load(string path)
    {
        if (!File.Exists(path)) return new CreditDatabase();

        try
        {
            var db = JsonSerializer.Deserialize<CreditDatabase>(File.ReadAllText(path, Encoding.UTF8), Options)
                ?? throw new JsonException("中身が空です");
            db.Materials ??= [];
            db.Sources ??= [];
            return db;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new CreditDatabaseLoadException($"登録データを読み込めませんでした: {path}\n{ex.Message}", ex);
        }
    }

    /// <summary>一時ファイルに書いてから置き換える(書き込み途中で失敗しても元のファイルは壊れない)。</summary>
    public static void Save(string path, CreditDatabase db)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(db, Options), new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
